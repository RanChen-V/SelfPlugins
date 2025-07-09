using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

[InitializeOnLoad]
public static class UIElementPickerInScene
{
    static Vector2 popupPos;
    static bool showPopup = false;
    static List<RectTransform> hitRects = new List<RectTransform>();
    static List<int> hitDepths = new List<int>();
    static RectTransform hoveredRect = null;

    static UIElementPickerInScene()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;

        // 右键按下
        if (e.type == EventType.MouseDown && e.button == 1)
        {
            hitRects.Clear();
            hitDepths.Clear();
            showPopup = false;
            hoveredRect = null;

            // 获取鼠标在Scene视图中的屏幕坐标
            Vector2 mousePos = e.mousePosition;
            // 转换为GUI坐标
            mousePos.y = sceneView.camera.pixelHeight - mousePos.y;

            // 使用HashSet存储命中及其父RectTransforms，避免重复
            HashSet<RectTransform> uniqueHitRects = new HashSet<RectTransform>();
            List<RectTransform> initialClickedRects = new List<RectTransform>();


            // 遍历所有RectTransform, 找出被点击的
            foreach (RectTransform rect in GameObject.FindObjectsOfType<RectTransform>())
            {
                if (!rect.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos, sceneView.camera)) continue;

                initialClickedRects.Add(rect);
                // 如果当前RectTransform被点击, 向上遍历父级并添加
                RectTransform current = rect;
                while(current != null && current.gameObject.activeInHierarchy)
                {
                    uniqueHitRects.Add(current);
                    // 向上移动到父级 RectTransform
                    if (current.parent != null) {
                        current = current.parent.GetComponent<RectTransform>();
                    } else {
                        current = null;
                    }
                }
            }

            // 将HashSet转换为List
            hitRects = new List<RectTransform>(uniqueHitRects);

            // 如果有命中的RectTransform，弹出EditorWindow弹窗
            if (hitRects.Count > 0)
            {
                 // 找出原始点击到的元素（不含自动添加的父级）中，层级最深的那一个
                 RectTransform deepestClickedRect = null;
                 int maxDepth = -1;
                 // 从 initialClickedRects 中找到最深的
                 foreach (RectTransform rect in initialClickedRects)
                 {
                     int depth = GetHierarchyDepth(rect.transform);
                     if (depth > maxDepth)
                     {
                         maxDepth = depth;
                         deepestClickedRect = rect;
                     }
                 }

                 // 将收集到的所有 RectTransform 以及最深点击元素传递给 ShowWindow
                UIElementPickerPopup.ShowWindow(e.mousePosition, initialClickedRects, deepestClickedRect, new List<RectTransform>(uniqueHitRects)); // 传递所有相关的RectTransforms
                e.Use();
            }
        }

        // Scene视图高亮显示悬浮的RectTransform
        RectTransform highlight = UIElementPickerPopup.hoveredRectInPopup;
        if (highlight != null)
        {
            DrawRectTransformOutline(highlight, Color.yellow, sceneView.camera);
        }
    }

    // 辅助函数：获取相对于根Canvas或顶层对象的层级深度
    public static int GetHierarchyDepth(Transform t)
    {
        int depth = 0;
        Transform current = t;
        while (current != null && current.GetComponent<Canvas>() == null && current.parent != null)
        {
            current = current.parent;
            depth++;
        }
        return depth;
    }

    static void DrawRectTransformOutline(RectTransform rect, Color color, Camera cam)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Handles.color = color;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p1 = corners[i];
            Vector3 p2 = corners[(i + 1) % 4];
            Handles.DrawLine(p1, p2);
        }
    }
}

// 新增EditorWindow弹窗类
public class UIElementPickerPopup : EditorWindow
{
    List<RectTransform> rects;
    List<int> displayDepths; // 存储计算后的显示深度（用于缩进）
    Vector2 mousePos;
    public static RectTransform hoveredRectInPopup = null; // 用于Scene高亮显示
    Rect[] optionRects; // 记录每个选项的Rect
    bool mouseInAnyOption = false;

    // 添加参数以接收点击的RectTransforms列表、最深的RectTransform和所有相关的RectTransforms
    public static void ShowWindow(Vector2 position, List<RectTransform> initialClickedRects, RectTransform deepestClickedRect, List<RectTransform> allRelevantRects)
    {
        hoveredRectInPopup = null;
        var window = CreateInstance<UIElementPickerPopup>();

        // 1. 构建有效的父子关系（只考虑 allRelevantRects 中的元素）
        Dictionary<RectTransform, RectTransform> effectiveParents = new Dictionary<RectTransform, RectTransform>();
        Dictionary<RectTransform, List<RectTransform>> effectiveChildren = new Dictionary<RectTransform, List<RectTransform>>();
        HashSet<RectTransform> allRelevantSet = new HashSet<RectTransform>(allRelevantRects); // 使用Set以加快查找速度

        foreach(var rect in allRelevantRects)
        {
            var canvas=rect.GetComponent<Canvas>();
            if(canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceCamera)
            {
                allRelevantSet.Remove(rect);
                continue;
            }
             RectTransform effectiveParent = null;
             Transform currentParent = rect.parent;
             while(currentParent != null)
             {
                  RectTransform parentRect = currentParent.GetComponent<RectTransform>();
                  // 只考虑也在相关集中的父级
                  if(parentRect != null && allRelevantSet.Contains(parentRect))
                  {
                       effectiveParent = parentRect;
                       break;
                  }
                  currentParent = currentParent.parent;
             }
             effectiveParents[rect] = effectiveParent;

             if(effectiveParent != null)
             {
                 if(!effectiveChildren.ContainsKey(effectiveParent))
                 {
                     effectiveChildren[effectiveParent] = new List<RectTransform>();
                 }
                 effectiveChildren[effectiveParent].Add(rect);
             }
        }

        // 2. 找出从最深点击的元素到最高相关祖先的主点击链
        List<RectTransform> mainClickedChain = new List<RectTransform>();
        RectTransform currentChainElement = deepestClickedRect;
        // 从最深点击的元素向上构建链
        while(currentChainElement != null && allRelevantSet.Contains(currentChainElement))
        {
            mainClickedChain.Add(currentChainElement);
            if (effectiveParents.ContainsKey(currentChainElement))
            {
                 currentChainElement = effectiveParents[currentChainElement];
            }
            else
            {
                 currentChainElement = null;
            }
        }
        mainClickedChain.Reverse(); // 从最高相关父级向下到最深点击的元素排序


        // 3. 找出有效的根元素（allRelevantSet 中有效父级不在 allRelevantSet 中的元素）
        List<RectTransform> effectiveRoots = allRelevantRects
            .Where(r => !effectiveParents.ContainsKey(r) || effectiveParents[r] == null || !allRelevantSet.Contains(effectiveParents[r]))
            .ToList();

        // 对这些根进行排序（例如，按名称）
         //effectiveRoots = effectiveRoots.OrderBy(r => r.gameObject.name).ToList();


        // 4. 使用自定义排序构建最终显示列表
        List<RectTransform> finalRects = new List<RectTransform>();
        List<int> finalDisplayDepths = new List<int>();
        HashSet<RectTransform> addedToFinal = new HashSet<RectTransform>();

        // 用于自定义排序深度优先遍历的递归帮助器
        Action<RectTransform, int> AddElementRecursively = null;
        AddElementRecursively = (currentRect, currentIndent) =>
        {
            if (currentRect == null || addedToFinal.Contains(currentRect) || !allRelevantSet.Contains(currentRect)) return;

            // 添加当前元素
            finalRects.Add(currentRect);
            finalDisplayDepths.Add(currentIndent);
            addedToFinal.Add(currentRect);

            // 查找并排序其有效子级
            if (effectiveChildren.ContainsKey(currentRect))
            {
                List<RectTransform> children = effectiveChildren[currentRect]
                    .Where(r => allRelevantSet.Contains(r)) // 只考虑相关集中的子级
                    .ToList();

                // 根据子级是否在主点击链中进行自定义排序
                // 不在链中的子级在前，链中的子级在后。两组都按 SiblingIndex 排序。
                children = children
                           .OrderBy(c => mainClickedChain.Contains(c) ? 1 : 0) // 0表示不在链中，1表示在链中
                           .ThenBy(c => c.transform.GetSiblingIndex()) // 在每个组内按SiblingIndex排序
                           .ToList();

                // 按排序后的顺序递归添加子级
                foreach (var child in children)
                {
                    AddElementRecursively(child, currentIndent + 1);
                }
            }
        };

        // 从每个有效根开始按排序后的顺序遍历
        foreach (var root in effectiveRoots)
        {
             AddElementRecursively(root, 0);
        }

        #region 调试代码

        // 最终检查：确保所有相关元素都已添加（用于调试）
        foreach (var relevantRect in allRelevantSet)
        {
            if (!addedToFinal.Contains(relevantRect))
            {
                Debug.LogWarning($"UIElementPickerPopup: Relevant RectTransform '{relevantRect.gameObject.name}' was not added to the final list. This indicates an issue in the traversal logic.");
                // 可选：在末尾添加遗漏的元素并使用高缩进或特定指示器进行调试
                // finalRects.Add(relevantRect); // 表示遗漏的元素
                // finalDisplayDepths.Add(99);
            }
        }
        #endregion
        
        // 5. 为 OnGUI 准备最终列表
        // 递归帮助器已按正确顺序和缩进构建了finalRects和finalDisplayDepths。
        window.rects = finalRects;
        window.displayDepths = finalDisplayDepths;

        // 如果列表为空，则不显示弹窗
        if (window.rects == null || window.rects.Count == 0)
        {
            // 如果列表为空，则不显示弹窗
            return;
        }


        window.minSize = new Vector2(300f,0);
        // 使用默认按钮样式计算窗口宽度（与之前的请求保持一致）
        float preferredWidth = window.GetPreferredWidth();

        window.mousePos = GUIUtility.GUIToScreenPoint(position);
        // Window height based on the number of items, width is adaptive, minimum 300
        // 窗口高度基于项目数量，宽度自适应，最小300
        window.position = new Rect(window.mousePos, new Vector2(Mathf.Max(preferredWidth, 300f), 19 * window.rects.Count + 5));

        window.ShowPopup();
        window.Focus();
    }

    private GUIStyle GetButtonStyle()
    {
        GUIStyle normalStyle = new GUIStyle(GUI.skin.button);
        normalStyle.alignment = TextAnchor.MiddleLeft;
        normalStyle.normal.textColor = Color.white;
        normalStyle.normal.background = Texture2D.blackTexture;
        normalStyle.active.background = Texture2D.blackTexture;
        normalStyle.hover.background = Texture2D.blackTexture;
        normalStyle.hover.textColor = Color.yellow;
        normalStyle.focused.background = Texture2D.blackTexture;
        normalStyle.onNormal.background = Texture2D.blackTexture;
        normalStyle.onHover.background = Texture2D.blackTexture;
        normalStyle.onActive.background = Texture2D.blackTexture;
        normalStyle.onFocused.background = Texture2D.blackTexture;
        normalStyle.onHover.textColor = Color.yellow;
        normalStyle.border = new RectOffset(0, 0, 0, 0);
        normalStyle.margin = new RectOffset(0, 0, 0, 0);
        normalStyle.padding = new RectOffset(4, 4, 2, 2);
        return normalStyle;
    }

    private float GetPreferredWidth()
    {
        // 使用默认按钮样式计算窗口宽度（与之前的请求保持一致）
        float preferredWidth = 400f; // 最小宽度
        GUIStyle tempStyle = GetButtonStyle(); // 使用默认按钮样式进行计算
        // 确保文本对齐方式与 OnGUI 一致
        tempStyle.alignment = TextAnchor.MiddleLeft;

        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            int indent = displayDepths[i]; // 使用计算后的显示深度进行缩进
            // 计算文本宽度 + 缩进空间 + 额外内边距/外边距
            float textWidth = tempStyle.CalcSize(new GUIContent(rect.gameObject.name)).x;
            // 添加默认样式中的内边距和外边距
            float itemWidth = textWidth + indent * 16 + tempStyle.padding.horizontal + tempStyle.margin.horizontal + 20; // 添加额外空间
            if (itemWidth > preferredWidth)
            {
                preferredWidth = itemWidth;
            }
        }

        return Mathf.Max(preferredWidth, minSize.x);
    }

    void OnGUI()
    {
        hoveredRectInPopup = null;
        mouseInAnyOption = false;
        if (rects == null)
        {
            // Should not happen if ShowWindow is called correctly and list is not empty
            // 如果正确调用 ShowWindow 且列表不为空，不应发生这种情况
            return;
        }

        if (optionRects == null || optionRects.Length != rects.Count)
            optionRects = new Rect[rects.Count];

        // 自定义选项样式（保持之前请求的自定义样式）
        GUIStyle normalStyle = GetButtonStyle();

        var removeIndexList = new List<int>();
        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            if (rect == null)
            {
                removeIndexList.Add(i);
                continue;
            }
            int indent = displayDepths[i]; // 使用计算后的显示深度进行缩进
            GUILayout.BeginHorizontal();
            // 根据显示深度应用缩进
            GUILayout.Space(indent * 16);
            Rect lastRect = GUILayoutUtility.GetRect(new GUIContent(rect.gameObject.name), normalStyle, GUILayout.ExpandWidth(true));
            bool isHover = lastRect.Contains(Event.current.mousePosition);
            optionRects[i] = lastRect;
            GUIStyle style = normalStyle;

            // 只在鼠标悬停时高亮显示
            if (isHover && (Event.current.type == EventType.MouseMove || Event.current.type == EventType.Repaint))
            {
                hoveredRectInPopup = rect;
                Selection.activeGameObject = rect.gameObject;
                mouseInAnyOption = true;
            }

            if (GUI.Button(lastRect, rect.gameObject.name, style))
            {
                Selection.activeGameObject = rect.gameObject;
                Close();
            }

            GUILayout.EndHorizontal();
        }

        int count = rects.RemoveAll(x => !x);//移除所有为空的对象
        if (count > 0) //如果移除的数量大于0 深度列表同步移除数据并更新窗口大小
        {
            for (int i = removeIndexList.Count - 1; i >= 0; i--)
            {
                displayDepths.RemoveAt(removeIndexList[i]);
            }

            position = new Rect(position.x, position.y, GetPreferredWidth(), 19 * rects.Count + 5);
        }
        // if (GUILayout.Button("Close"))
        // {
        //     Close();
        // }

        // 当鼠标在窗口内时强制重绘（始终重绘以实现敏感高亮显示）
        if (mouseInAnyOption || Event.current.type == EventType.Repaint)
        {
            Repaint();
        }
    }

    private void Update()
    {
        if (EditorWindow.focusedWindow != this)
        {
            // 失去焦点时关闭
            Close();
        }
    }
}