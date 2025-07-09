using System;
using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;

/// <summary>
/// 为MonoBehaviour提供排序的Inspector视图
/// 提供按字母顺序排序的字段和属性显示
/// 支持可折叠的属性列表，使用彩虹色标识不同属性
/// </summary>
[CustomEditor(typeof(MonoBehaviour), true)]
public class GenericSortedInspectorEditor : Editor
{
    /// <summary>
    /// 需要从Inspector视图中排除的字段名称集合
    /// 主要包含Unity内置的一些常用属性，避免在自定义Inspector中重复显示
    /// </summary>
    private static readonly HashSet<string> ExcludedFields = new HashSet<string>
    {
        // Unity对象的基本属性，这些在Inspector的其他位置已有显示
        // "m_Script" 已从排除列表中移除，因为我们需要显示脚本引用
        "hideFlags","name","tag","transform","parent","gameObject","layer","activeSelf","activeInHierarchy","isStatic"
    };

    /// <summary>
    /// 缓存的按字母顺序排序的字段列表
    /// </summary>
    private List<FieldInfo> sortedFields;
    
    /// <summary>
    /// 缓存的按字母顺序排序的属性列表
    /// </summary>
    private List<PropertyInfo> sortedProperties;
    
    /// <summary>
    /// 缓存的序列化属性字典，用于快速查找字段对应的SerializedProperty
    /// 键为字段名，值为对应的SerializedProperty
    /// </summary>
    private Dictionary<string, SerializedProperty> cachedProperties;
    
    /// <summary>
    /// 属性列表的折叠状态，默认为折叠(false)
    /// </summary>
    private bool propertiesFoldout = false;
    
    /// <summary>
    /// 属性名称的GUI样式，用于自定义属性显示的颜色和字体
    /// </summary>
    private GUIStyle propertyNameStyle;

    /// <summary>
    /// 当Inspector被启用时调用，初始化所有必要的数据
    /// 在Unity编辑器加载脚本或选择对象时触发
    /// </summary>
    private void OnEnable()
    {
        try
        {
            // 初始化字段和属性列表
            InitializeFields();
        }
        catch (Exception ex)
        {
            // 捕获并记录任何初始化过程中的异常
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// 初始化字段列表、属性列表和缓存
    /// 获取目标对象的所有字段和属性，并按名称排序
    /// </summary>
    private void InitializeFields()
    {
        // 获取目标对象的所有实例字段（包括公共和非公共）
        var allFields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        // 获取目标对象的所有实例属性（包括公共和非公共）
        var allProperties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        // 过滤并按名称排序字段
        sortedFields = allFields
            .Where(ShouldIncludeField)  // 应用过滤条件
            .OrderBy(field => field.Name)  // 按名称字母顺序排序
            .ToList();

        // 过滤并按名称排序属性
        sortedProperties = allProperties
            .Where(ShouldIncludeProperty)  // 应用过滤条件
            .OrderBy(field => field.Name)  // 按名称字母顺序排序
            .ToList();
        
        // 初始化序列化属性缓存，提高访问效率
        cachedProperties = new Dictionary<string, SerializedProperty>();
        foreach (var field in sortedFields)
        {
            // 查找每个字段对应的序列化属性
            var property = serializedObject.FindProperty(field.Name);
            if (property != null)
            {
                // 将找到的序列化属性添加到缓存中
                cachedProperties[field.Name] = property;
            }
        }
    }

    /// <summary>
    /// 判断字段是否应该被包含在Inspector中
    /// </summary>
    /// <param name="field">要检查的字段信息</param>
    /// <returns>如果字段应该被包含则返回true，否则返回false</returns>
    private bool ShouldIncludeField(FieldInfo field)
    {
        // 排除在ExcludedFields中列出的字段
        // 排除标记了NonSerialized特性的字段（这些字段不会被Unity序列化）
        if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializableAttribute))) return false;
        // 排除不支持序列化的复杂泛型类型
        // Unity不能很好地序列化这些类型，所以在Inspector中显示它们没有意义
        var fieldType = field.FieldType;
        if (fieldType.IsGenericType)
        {
            var genericTypeDef = fieldType.GetGenericTypeDefinition();
            if (genericTypeDef == typeof(Dictionary<,>) ||  // 字典
                genericTypeDef == typeof(HashSet<>) ||      // 哈希集
                genericTypeDef == typeof(Queue<>) ||        // 队列
                genericTypeDef == typeof(Stack<>))          // 栈
            {
                return false;
            }

            if (genericTypeDef == typeof(List<>))
            {
                var value = field.GetValue(target);
                if (value != null)
                {
                    return true;
                }
            }
        }
        
        // 如果是class类型，只保留MonoBehaviour的子类
        if (fieldType.IsClass)
        {
            // 保留string类型的显示（string虽然是class但是基础类型）
            if (fieldType == typeof(string))
            {
                return !Attribute.IsDefined(field, typeof(NonSerializedAttribute));
            }
            
            // 保留Unity的Object类型（因为这包含了所有Unity资源类型）
            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
            {
                return !Attribute.IsDefined(field, typeof(NonSerializedAttribute));
            }
            
            // 其他所有class类型都排除
            return false;
        }
        return !ExcludedFields.Contains(field.Name) && 
               !Attribute.IsDefined(field, typeof(NonSerializedAttribute));
    }

    /// <summary>
    /// 判断属性是否应该被包含在Inspector中
    /// 过滤掉不适合在Inspector中显示的属性类型
    /// </summary>
    /// <param name="property">要检查的属性信息</param>
    /// <returns>如果属性应该被包含则返回true，否则返回false</returns>
    private bool ShouldIncludeProperty(PropertyInfo property)
    {
        // 排除在ExcludedFields中列出的属性
        // 排除标记了Obsolete特性的属性（已过时的属性）
        if (ExcludedFields.Contains(property.Name) || 
            Attribute.IsDefined(property, typeof(ObsoleteAttribute)))
        {
            return false;
        }
        
        // 排除Unity基类中定义的属性，避免显示Unity内置的属性
        var declaringType = property.DeclaringType;
        if (declaringType == typeof(MonoBehaviour) || 
            declaringType == typeof(Component) || 
            declaringType == typeof(Behaviour))
        {
            return false;
        }

        var propertyType = property.PropertyType;
        
        // 排除不支持序列化的复杂泛型类型
        // Unity不能很好地序列化这些类型，所以在Inspector中显示它们没有意义
        if (propertyType.IsGenericType)
        {
            var genericTypeDef = propertyType.GetGenericTypeDefinition();
            if (genericTypeDef == typeof(Dictionary<,>) ||  // 字典
                genericTypeDef == typeof(HashSet<>) ||      // 哈希集
                genericTypeDef == typeof(Queue<>) ||        // 队列
                genericTypeDef == typeof(Stack<>))          // 栈
            {
                return false;
            }
        }
        
        // 排除委托和事件类型，这些不适合在Inspector中显示
        if (typeof(Delegate).IsAssignableFrom(propertyType))
        {
            return false;
        }

        // 如果是class类型，只保留MonoBehaviour的子类
        if (propertyType.IsClass)
        {
            // 保留string类型的显示（string虽然是class但是基础类型）
            if (propertyType == typeof(string))
            {
                return !Attribute.IsDefined(property, typeof(NonSerializedAttribute));
            }
            
            // 保留Unity的Object类型（因为这包含了所有Unity资源类型）
            if (typeof(UnityEngine.Object).IsAssignableFrom(propertyType))
            {
                return !Attribute.IsDefined(property, typeof(NonSerializedAttribute));
            }
            
            // 其他所有class类型都排除
            return false;
        }
        
        // 排除标记了NonSerialized特性的属性
        return !Attribute.IsDefined(property, typeof(NonSerializedAttribute));
    }

    /// <summary>
    /// Unity编辑器的Inspector GUI绘制入口点
    /// 重写基类方法以提供自定义的Inspector界面
    /// 包含异常处理机制，在出错时回退到默认Inspector
    /// </summary>
    public override void OnInspectorGUI()
    {
        try
        {
            DrawInspectorGUI();
        }
        catch (Exception ex)
        {
            // Unity的GUI系统有时需要重新抛出特定异常
            if (ExitGUIUtility.ShouldRethrowException(ex))
            {
                throw;
            }
            Debug.LogException(ex);
            // 发生错误时回退到默认Inspector，确保用户仍然可以编辑组件
            DrawDefaultInspector();
        }
    }

    /// <summary>
    /// 绘制自定义Inspector界面
    /// 按以下顺序显示内容：
    /// 1. 脚本引用字段（只读）
    /// 2. 所有序列化字段（按字母顺序）
    /// 3. 可折叠的属性列表（使用彩虹色标识）
    /// </summary>
    private void DrawInspectorGUI()
    {
        serializedObject.Update();

        // 绘制脚本引用字段（禁用状态）
        using (new EditorGUI.DisabledScope(true))
        {
            var scriptProperty = serializedObject.FindProperty("m_Script");
            if (scriptProperty != null)
            {
                EditorGUILayout.PropertyField(scriptProperty);
            }
        }

        if (sortedFields == null || sortedFields.Count <= 0)
        {
            InitializeFields();
            
        }

        // 绘制所有排序后的字段
        foreach (var field in sortedFields)
        {
            if (cachedProperties.TryGetValue(field.Name, out var property))
            {
                // 检查是否为对象引用类型
                bool isObjectReference = property.propertyType == SerializedPropertyType.ObjectReference;
        
                // 使用正确的方式绘制属性
                EditorGUILayout.PropertyField(property, new GUIContent(ObjectNames.NicifyVariableName(field.Name)), true);
        
                // 如果是对象引用且值发生变化，确保更改被应用
                if (isObjectReference && GUI.changed)
                {
                    serializedObject.ApplyModifiedProperties();
                }
            }
            else
            {
                // 对于无法获取序列化属性的字段，尝试直接通过反射绘制
                DrawFieldFallback(field);
            }
        }

        if (sortedProperties == null || sortedProperties.Count <= 0) return;
        EditorGUILayout.Space(20);
        
        // 使用Foldout控件创建可折叠的属性列表
        var style = new GUIStyle(EditorStyles.foldout);
        style.normal.textColor = Color.green;
        style.onNormal.textColor = Color.white;
        style.fontStyle = FontStyle.Bold;
        
        propertiesFoldout = EditorGUILayout.Foldout(propertiesFoldout, "属性列表(仅展示)", true, style);

        if (propertiesFoldout)
        {
            // 确保样式已初始化
            propertyNameStyle ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Color.yellow },
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            EditorGUI.indentLevel++;
            // 由于在ShouldIncludeProperty中已经过滤了NonSerializedAttribute，这里不需要再次检查
            for (var i = 0; i < sortedProperties.Count; i++)
            {
                var color = GetPropertyColor(i);
                propertyNameStyle.normal.textColor = color;
                var property = sortedProperties[i];
                DrawProperty(property);
            }

            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// 根据索引返回七种彩虹颜色（红橙黄绿青蓝紫）
    /// 用于在属性列表中为不同属性提供视觉区分
    /// 使用模运算确保索引总是在0-6范围内
    /// </summary>
    /// <param name="index">颜色索引（任意整数）</param>
    /// <returns>对应的Unity Color对象</returns>
    private Color GetPropertyColor(int index)
    {
        // 使用模运算确保索引在0-6范围内循环
        index %= 7;
        
        // 使用C# 8.0的switch表达式语法返回对应颜色
        return index switch
        {
            0 => Color.red,                                  // 红色 - 使用Unity内置颜色
            1 => new Color(1.0f, 0.5f, 0.0f),                // 橙色 - 自定义RGB值(255,127,0)
            2 => Color.yellow,                               // 黄色 - 使用Unity内置颜色
            3 => Color.green,                                // 绿色 - 使用Unity内置颜色
            4 => Color.cyan,                                 // 青色 - 使用Unity内置颜色
            5 => new Color(0.4f, 0.4f, 1.0f),                // 蓝色 - 调亮的蓝色(102,102,255)
            6 => new Color(0.7f, 0.3f, 1.0f),                // 紫色 - 调亮的紫色(178,76,255)
            _ => Color.white                                 // 默认返回白色（理论上不会执行到这里）
        };
    }

    /// <summary>
    /// 为无法获取序列化属性的字段提供回退绘制方法
    /// 通过反射直接处理字段值的显示和编辑
    /// </summary>
    /// <param name="field">要绘制的字段信息</param>
    private void DrawFieldFallback(FieldInfo field)
    {
        // 获取字段当前值
        var value = field.GetValue(target);

        // 创建字段标签
        GUIContent label = new GUIContent(ObjectNames.NicifyVariableName(field.Name));

        // 开始水平布局
        EditorGUILayout.BeginHorizontal();

        // 根据字段类型选择适当的绘制方式
        if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
        {
            // Unity对象引用类型
            UnityEngine.Object objValue = (UnityEngine.Object)value;
            UnityEngine.Object newValue = EditorGUILayout.ObjectField(
                label,
                objValue,
                field.FieldType,
                true // 允许场景引用
            );

            // 如果值发生变化，更新字段
            if (newValue != objValue)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType == typeof(string))
        {
            // 字符串类型
            string strValue = (string)value;
            string newValue = EditorGUILayout.TextField(label, strValue ?? "");

            if (newValue != strValue)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType == typeof(int))
        {
            // 整数类型
            int intValue = value != null ? (int)value : 0;
            int newValue = EditorGUILayout.IntField(label, intValue);

            if (newValue != intValue)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType == typeof(float))
        {
            // 浮点数类型
            float floatValue = value != null ? (float)value : 0f;
            float newValue = EditorGUILayout.FloatField(label, floatValue);

            if (newValue != floatValue)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType == typeof(bool))
        {
            // 布尔类型
            bool boolValue = value != null && (bool)value;
            bool newValue = EditorGUILayout.Toggle(label, boolValue);

            if (newValue != boolValue)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType == typeof(Vector2))
        {
            // Vector2类型
            Vector2 vec2Value = value != null ? (Vector2)value : Vector2.zero;
            Vector2 newValue = EditorGUILayout.Vector2Field(label, vec2Value);

            if (newValue != vec2Value)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType == typeof(Vector3))
        {
            // Vector3类型
            Vector3 vec3Value = value != null ? (Vector3)value : Vector3.zero;
            Vector3 newValue = EditorGUILayout.Vector3Field(label, vec3Value);

            if (newValue != vec3Value)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType == typeof(Color))
        {
            // 颜色类型
            Color colorValue = value != null ? (Color)value : Color.white;
            Color newValue = EditorGUILayout.ColorField(label, colorValue);

            if (newValue != colorValue)
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType.IsEnum)
        {
            // 枚举类型
            Enum enumValue = value != null ? (Enum)value : (Enum)Enum.GetValues(field.FieldType).GetValue(0);
            Enum newValue = EditorGUILayout.EnumPopup(label, enumValue);

            if (!newValue.Equals(enumValue))
            {
                field.SetValue(target, newValue);
                EditorUtility.SetDirty(target);
            }
        }
        else if (field.FieldType.IsArray)
        {
            // 数组类型
            Array array = (Array)value;
            if (array != null)
            {
                EditorGUILayout.LabelField(label, $"Array[{array.Length}]");

                // 为数组提供一个基本的长度编辑功能
                EditorGUI.indentLevel++;
                int newLength = EditorGUILayout.IntField("Size", array.Length);
                if (newLength != array.Length)
                {
                    Array newArray = Array.CreateInstance(field.FieldType.GetElementType(), newLength);
                    Array.Copy(array, newArray, Math.Min(array.Length, newLength));
                    field.SetValue(target, newArray);
                    EditorUtility.SetDirty(target);
                }

                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.LabelField(label, "null");
            }
        }
        else
        {
            // 其他类型使用只读标签显示
            string valueStr = value != null ? value.ToString() : "null";
            EditorGUILayout.LabelField(label, valueStr);
        }

        // 结束水平布局
        EditorGUILayout.EndHorizontal();

        // 如果GUI发生改变，确保重绘
        if (GUI.changed)
        {
            EditorUtility.SetDirty(target);
            // 请求重绘，确保值的更改立即显示
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// 绘制单个属性的UI
    /// 根据属性类型选择适当的显示方式
    /// 支持基本类型、向量和Unity对象引用的显示
    /// </summary>
    /// <param name="property">要绘制的属性信息</param>
    private void DrawProperty(PropertyInfo property)
    {
        var type = property.PropertyType;
        var value = property.GetValue(target);
        
        // 创建带颜色的属性名称标签
        GUIContent propertyLabel = new GUIContent(property.Name);
        
        // 开始水平布局，用于并排显示属性名和值
        EditorGUILayout.BeginHorizontal();
        
        // 使用自定义样式显示属性名
        EditorGUILayout.LabelField(propertyLabel, propertyNameStyle, GUILayout.Width(EditorGUIUtility.labelWidth));
        
        // 获取值的字符串表示，处理null情况
        string valueStr = value == null ? "null" : value.ToString();
        
        // 根据属性类型选择适当的显示方式
        if (type == typeof(int))
        {
            // 整数类型显示
            EditorGUILayout.LabelField(valueStr, propertyNameStyle);
        }
        else if (type == typeof(float))
        {
            // 浮点数类型显示
            EditorGUILayout.LabelField(valueStr, propertyNameStyle);
        }
        else if (type == typeof(bool))
        {
            // 布尔类型显示
            EditorGUILayout.LabelField(valueStr, propertyNameStyle);
        }
        else if (type == typeof(string))
        {
            // 字符串类型显示，处理null情况
            EditorGUILayout.LabelField(value == null ? "null" : (string)value, propertyNameStyle);
        }
        else if (type == typeof(Vector2))
        {
            // Vector2类型显示，格式化为(x, y)
            if (value != null)
            {
                Vector2 vec2 = (Vector2)value;
                EditorGUILayout.LabelField($"({vec2.x}, {vec2.y})", propertyNameStyle);
            }
        }
        else if (type == typeof(Vector3))
        {
            // Vector3类型显示，格式化为(x, y, z)
            if (value != null)
            {
                Vector3 vec3 = (Vector3)value;
                EditorGUILayout.LabelField($"({vec3.x}, {vec3.y}, {vec3.z})", propertyNameStyle);
            }
        }
        else if (type == typeof(Object) || (type.BaseType != null && type.BaseType == typeof(Object)))
        {
            // Unity对象引用类型显示，使用ObjectField但禁用编辑
            Object obj = (Object)value;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(obj, type, true);
            }
            // 注释掉的替代显示方式
            // EditorGUILayout.LabelField(obj == null ? "null" : obj.name, propertyNameStyle);
        }
        else
        {
            // 其他类型使用默认的ToString()显示
            EditorGUILayout.LabelField(valueStr, propertyNameStyle);
        }
        
        // 结束水平布局
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 当Inspector被禁用时调用，清理所有缓存的资源
    /// 在Unity编辑器中切换选择对象或关闭Inspector时触发
    /// 通过释放引用和清空集合来防止内存泄漏
    /// </summary>
    private void OnDisable()
    {
        // 清空并释放字段列表
        sortedFields?.Clear();
        sortedFields = null;
        
        // 清空并释放属性列表
        sortedProperties?.Clear();
        sortedProperties = null;
        
        // 清空并释放序列化属性缓存
        cachedProperties?.Clear();
        cachedProperties = null;
        
        // 释放GUI样式引用
        propertyNameStyle = null;
    }
}
