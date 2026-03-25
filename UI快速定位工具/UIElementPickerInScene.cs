using System;
using UnityEngine;
using UnityEditor;
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
            Vector2 mousePos = e.mousePosition; // 直接用Scene视图GUI坐标
            mousePos.y = sceneView.camera.pixelHeight - mousePos.y;
            HashSet<RectTransform> uniqueHitRects = new HashSet<RectTransform>();
            List<RectTransform> initialClickedRects = new List<RectTransform>();
            // 更兼容编辑器：不过滤activeInHierarchy，只过滤hideFlags和scene
            foreach (RectTransform rect in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                //if ((rect.hideFlags & HideFlags.NotEditable) != 0 || rect.hideFlags == HideFlags.HideAndDontSave) continue;
                //if (rect.gameObject.scene.name == null) continue; // 只处理场景中的对象
                if (!rect.gameObject.activeInHierarchy) continue; // 只处理激活对象
                //if (rect.GetComponentInParent<Canvas>() == null) continue; // 只处理Canvas下对象
                if(!RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos, sceneView.camera)) continue;

                initialClickedRects.Add(rect);
                RectTransform current = rect;
                while (current != null && current.gameObject.activeInHierarchy)
                {
                    uniqueHitRects.Add(current);
                    if (current.parent != null)
                    {
                        current = current.parent.GetComponent<RectTransform>();
                    }
                    else
                    {
                        current = null;
                    }
                }
            }
            // 优化点击检测：按Canvas下渲染顺序遍历所有RectTransform
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (!canvas.gameObject.activeInHierarchy) continue;
                var rectsInCanvas = canvas.GetComponentsInChildren<RectTransform>(true);
                for (int i = rectsInCanvas.Length - 1; i >= 0; i--) // 从顶层到底层
                {
                    var rect = rectsInCanvas[i];
                    if (!rect.gameObject.activeInHierarchy) continue;
                    //if ((rect.hideFlags & HideFlags.NotEditable) != 0 || rect.hideFlags == HideFlags.HideAndDontSave) continue;
                    if (rect.gameObject.scene.name == null) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos, sceneView.camera)) continue;
                    initialClickedRects.Add(rect);
                    RectTransform current = rect;
                    while(current != null)
                    {
                        uniqueHitRects.Add(current);
                        if (current.parent != null) {
                            current = current.parent.GetComponent<RectTransform>();
                        } else {
                            current = null;
                        }
                    }
                }
            }
            hitRects = new List<RectTransform>(uniqueHitRects);
            if (hitRects.Count > 0)
            {
                // 1. 收集所有点击对象的祖先链
                var canvasChains = new Dictionary<Canvas, List<List<RectTransform>>>();
                foreach (var clickedRect in initialClickedRects)
                {
                    List<RectTransform> chain = new List<RectTransform>();
                    RectTransform current = clickedRect;
                    Canvas canvas = null;
                    while (current != null)
                    {
                        chain.Insert(0, current);
                        if (canvas == null)
                        {
                            canvas = GetRootCanvas(current);
                            if (canvas != null)
                            {
                                if (!chain.Contains(canvas.GetComponent<RectTransform>())) chain.Insert(0, canvas.GetComponent<RectTransform>());
                            }
                        }

                        if (current.parent != null)
                        {
                            var parentNode = current.parent.GetComponent<RectTransform>();
                            current = !parentNode ? current.parent.GetComponentInParent<RectTransform>() : parentNode;
                        }
                        else
                            current = null;
                    }
                    if (canvas != null)
                    {
                        if (!canvasChains.ContainsKey(canvas))
                            canvasChains[canvas] = new List<List<RectTransform>>();
                        canvasChains[canvas].Add(chain);
                    }
                }
                // 2. 按Canvas分组，链内按Hierarchy顺序排序
                var sortedChains = new List<List<RectTransform>>();
                foreach (var kvp in canvasChains)
                {
                    var canvas = kvp.Key;
                    var chains = kvp.Value;
                    // 按链中最底层对象在Canvas下的Hierarchy顺序排序
                    chains.Sort((a, b) =>
                    {
                        var aObj = a.Last();
                        var bObj = b.Last();
                        var siblings = canvas.GetComponentsInChildren<RectTransform>(true);
                        int aIdx = Array.IndexOf(siblings, aObj);
                        int bIdx = Array.IndexOf(siblings, bObj);
                        return aIdx.CompareTo(bIdx);
                    });
                    sortedChains.AddRange(chains);
                }
                UIElementPickerPopup.ShowWindow(e.mousePosition, sortedChains);
                e.Use();
            }
        }
        RectTransform highlight = UIElementPickerPopup.hoveredRectInPopup;
        if (highlight != null)
        {
            DrawRectTransformOutline(highlight, Color.yellow, sceneView.camera);
        }
    }

    private static Canvas GetRootCanvas(Transform rect)
    {
        Canvas canvas = null;
        if (rect == null || rect.parent == null) return null;
        var node = rect.parent;
        while (node != null)
        {
            var parentCanvas = node.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                canvas = parentCanvas;
            }
            node = node.parent;
        }
        return canvas;
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

    // 新增EditorWindow弹窗类
    public class UIElementPickerPopup : EditorWindow
    {
        List<RectTransform> rects;
        List<int> displayDepths;
        Vector2 mousePos;
        public static RectTransform hoveredRectInPopup = null;
        Rect[] optionRects;
        bool mouseInAnyOption = false;
        // Node树结构
        public class Node {
            public RectTransform rect;
            public List<Node> children = new List<Node>();
            public bool isClicked;
            public Node(RectTransform rect, bool isClicked = false) {
                this.rect = rect;
                this.isClicked = isClicked;
            }
        }
        public List<Node> rootNodes;
        public Dictionary<int, bool> foldoutStates = new Dictionary<int, bool>();
        Canvas targetCanvas;
        List<RectTransform> visibleRects = new List<RectTransform>(); // 新增：当前可见项
        Vector2 scrollPos = Vector2.zero; // 新增：滚动条位置

        // 新增链数据结构
        public List<List<RectTransform>> chains;

        // 按照Canvas下的渲染层级显示，并支持折叠/展开
        public static void ShowWindow(Vector2 position, List<List<RectTransform>> sortedChains)
        {
            hoveredRectInPopup = null;
            var window = CreateInstance<UIElementPickerPopup>();
            window.rootNodes = BuildTree(sortedChains);
            window.foldoutStates = new Dictionary<int, bool>();
            foreach (var root in window.rootNodes) InitFoldout(root, window.foldoutStates);
            window.minSize = new Vector2(300f,0);
            float preferredWidth = window.GetPreferredWidth();
            window.mousePos = GUIUtility.GUIToScreenPoint(position);
            int totalRows = 0;
            foreach (var root in window.rootNodes) totalRows += CountRows(root, window.foldoutStates, 0);
            window.position = new Rect(window.mousePos, new Vector2(Mathf.Max(preferredWidth, 300f), 19 * totalRows + 5));
            window.ShowPopup();
            window.Focus();
        }
        // 初始化折叠状态
        static void InitFoldout(Node node, Dictionary<int, bool> foldoutStates) {
            if (node == null) return;
            foldoutStates[node.rect.GetInstanceID()] = true;
            foreach (var child in node.children) InitFoldout(child, foldoutStates);
        }
        // 统计显示行数
        static int CountRows(Node node, Dictionary<int, bool> foldoutStates, int indent) {
            if (node == null) return 0;
            int count = 1;
            bool fold = !foldoutStates.ContainsKey(node.rect.GetInstanceID()) || foldoutStates[node.rect.GetInstanceID()];
            if (node.children.Count > 0 && fold) {
                foreach (var child in node.children) count += CountRows(child, foldoutStates, indent + 1);
            }
            return count;
        }
        // GetPreferredWidth遍历树
        private float GetPreferredWidth() {
            float preferredWidth = 400f;
            GUIStyle tempStyle = GetButtonStyle();
            tempStyle.alignment = TextAnchor.MiddleLeft;
            void Traverse(Node node, int indent) {
                if (node == null) return;
                float textWidth = tempStyle.CalcSize(new GUIContent(node.rect.gameObject.name)).x;
                float itemWidth = textWidth + indent * 16 + tempStyle.padding.horizontal + tempStyle.margin.horizontal + 20;
                if (itemWidth > preferredWidth) preferredWidth = itemWidth;
                foreach (var child in node.children) Traverse(child, indent + 1);
            }
            if (rootNodes != null) foreach (var root in rootNodes) Traverse(root, 0);
            return Mathf.Max(preferredWidth, minSize.x);
        }
        // 单行渲染
        void DrawRectSingle(RectTransform rect, int indent, ref int yIndex, GUIStyle style, ref float contentHeight)
        {
            if (rect == null) return;
            int rectId = rect.GetInstanceID();
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent * 16);
            GUILayout.Space(18); // 占位折叠图标
            Rect lastRect = GUILayoutUtility.GetRect(new GUIContent(rect.gameObject.name), style, GUILayout.ExpandWidth(true), GUILayout.Height(18));
            if (optionRects != null && yIndex < optionRects.Length)
                optionRects[yIndex] = lastRect;
            visibleRects.Add(rect);
            bool isHover = lastRect.Contains(Event.current.mousePosition);
            if (isHover && (Event.current.type == EventType.MouseMove || Event.current.type == EventType.Repaint))
            {
                hoveredRectInPopup = rect;
                mouseInAnyOption = true;
            }
            if (GUI.Button(lastRect, rect.gameObject.name, style))
            {
                Selection.activeGameObject = rect.gameObject;
                Close();
            }
            GUILayout.EndHorizontal();
            contentHeight += 18;
            yIndex++;
        }
        // OnGUI递归渲染所有根节点（递归安全）
        void OnGUI() {
            try {
                hoveredRectInPopup = null;
                mouseInAnyOption = false;
                visibleRects.Clear();
                if (rootNodes == null || rootNodes.Count == 0) return;
                GUIStyle normalStyle = GetButtonStyle();
                int yIndex = 0;
                float contentHeight = 0f;
                foreach (var root in rootNodes) DrawNodeRecursive(root, 0, ref yIndex, normalStyle, ref contentHeight, 0);
                if (optionRects == null || optionRects.Length != visibleRects.Count)
                    optionRects = new Rect[visibleRects.Count];
                float windowHeight = position.height;
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(position.width), GUILayout.Height(windowHeight));
                EditorGUILayout.EndScrollView();
                if (hoveredRectInPopup != null) Selection.activeGameObject = hoveredRectInPopup.gameObject;
                if (mouseInAnyOption || Event.current.type == EventType.Repaint) Repaint();
            } catch (Exception ex) {
                Debug.LogError("UIElementPickerPopup OnGUI error: " + ex);
            }
        }
        // 递归渲染Node树，支持折叠（递归安全）
        void DrawNodeRecursive(Node node, int indent, ref int yIndex, GUIStyle style, ref float contentHeight, int depth = 0) {
            if (node == null || depth > 100) return; // 限制最大递归深度100
            int rectId = node.rect.GetInstanceID();
            bool hasChildren = node.children.Count > 0;
            bool fold = foldoutStates.ContainsKey(rectId) ? foldoutStates[rectId] : true;
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent * 16);
            GUIContent foldIcon = hasChildren ? (fold ? EditorGUIUtility.IconContent("IN Foldout on") : EditorGUIUtility.IconContent("IN Foldout")) : null;
            if (hasChildren) {
                if (GUILayout.Button(foldIcon, GUIStyle.none, GUILayout.Width(18), GUILayout.Height(18))) {
                    foldoutStates[rectId] = !fold;
                    fold = !fold;
                }
            } else {
                GUILayout.Space(18);
            }
            Color origBg = GUI.backgroundColor;
            if (node.isClicked) GUI.backgroundColor = new Color(1f, 1f, 0.6f, 1f);
            Rect lastRect = GUILayoutUtility.GetRect(new GUIContent(node.rect.gameObject.name), style, GUILayout.ExpandWidth(true), GUILayout.Height(18));
            if (optionRects != null && yIndex < optionRects.Length)
                optionRects[yIndex] = lastRect;
            visibleRects.Add(node.rect);
            bool isHover = lastRect.Contains(Event.current.mousePosition);
            if (isHover && (Event.current.type == EventType.MouseMove || Event.current.type == EventType.Repaint)) {
                hoveredRectInPopup = node.rect;
                mouseInAnyOption = true;
            }
            GUIStyle labelStyle = new GUIStyle(style);
            labelStyle.normal.textColor = Color.white;
            labelStyle.fontStyle = node.isClicked ? FontStyle.Bold : FontStyle.Normal;
            if (GUI.Button(lastRect, node.rect.gameObject.name, labelStyle)) {
                Selection.activeGameObject = node.rect.gameObject;
                Close();
            }
            GUI.backgroundColor = origBg;
            GUILayout.EndHorizontal();
            contentHeight += 18;
            yIndex++;
            if (hasChildren && fold) {
                foreach (var child in node.children) DrawNodeRecursive(child, indent + 1, ref yIndex, style, ref contentHeight, depth + 1);
            }
        }

        // 构建Node树，根节点为链的最顶层父物体，Canvas只是其中一个节点（递归安全，链条插入Canvas节点优化）
        public static List<Node> BuildTree(List<List<RectTransform>> chains) {
            Dictionary<RectTransform, Node> nodeMap = new Dictionary<RectTransform, Node>();
            HashSet<Node> rootCandidates = new HashSet<Node>();
            foreach (var chain in chains) {
                Node parent = null;
                HashSet<RectTransform> inserted = new HashSet<RectTransform>();
                for (int i = 0; i < chain.Count && i < 100; i++) { // 限制最大递归深度100
                    var rect = chain[i];
                    if (inserted.Contains(rect)) continue; // 防止重复插入
                    inserted.Add(rect);
                    bool isClicked = (i == chain.Count - 1);
                    if (!nodeMap.TryGetValue(rect, out var node)) {
                        node = new Node(rect, isClicked);
                        nodeMap[rect] = node;
                        if (parent != null && !parent.children.Contains(node)) parent.children.Add(node);
                        if (i == 0) rootCandidates.Add(node);
                    } else {
                        if (isClicked) node.isClicked = true;
                        if (parent != null && !parent.children.Contains(node)) parent.children.Add(node);
                    }
                    parent = node;
                }
            }
            // 剔除没有子节点的Canvas（仅当Canvas是唯一节点且无children时）
            var result = new List<Node>();
            foreach (var root in rootCandidates)
            {
                var canvas = root.rect.GetComponent<Canvas>();
                if (canvas != null && root.children.Count == 0) continue;
                result.Add(root);
            }
            return result;
        }

        private void Update()
        {
            // 保证Hierarchy持续选中
            if (hoveredRectInPopup != null)
            {
                Selection.activeGameObject = hoveredRectInPopup.gameObject;
            }
            // 恢复失焦自动关闭弹窗
            if (EditorWindow.focusedWindow != this)
            {
                Close();
            }
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
    }

    static bool PointInQuad(Vector2 p, Vector2[] quad)
    {
        // 射线法：统计与四边形边的交点数
        int count = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = quad[i];
            Vector2 b = quad[(i + 1) % 4];
            if (((a.y > p.y) != (b.y > p.y)) &&
                (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x))
            {
                count++;
            }
        }
        return (count % 2) == 1;
    }
    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
        float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
        if ((s < 0) != (t < 0)) return false;
        float A = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
        return A < 0 ? (s <= 0 && s + t >= A) : (s >= 0 && s + t <= A);
    }
    // 新增辅助方法
    static bool IsMouseOverRectTransform(RectTransform rect, SceneView sceneView)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2[] screenCorners = new Vector2[4];
        Camera cam = SceneView.lastActiveSceneView.camera;
        float screenHeight = (float)Handles.GetMainGameViewSize().y;
        for (int i = 0; i < 4; i++)
        {
            screenCorners[i] = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            screenCorners[i].y = screenHeight - screenCorners[i].y;
        }
        Vector2 mousePos = Event.current.mousePosition;
        return PointInQuad(mousePos, screenCorners);
    }
    static bool PointInQuad3D(Vector3 p, Vector3[] quad)
    {
        return PointInTriangle3D(p, quad[0], quad[1], quad[2]) || PointInTriangle3D(p, quad[0], quad[2], quad[3]);
    }
    static bool PointInTriangle3D(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        float areaABC = Vector3.Cross(b - a, c - a).magnitude;
        float areaPAB = Vector3.Cross(a - p, b - p).magnitude;
        float areaPBC = Vector3.Cross(b - p, c - p).magnitude;
        float areaPCA = Vector3.Cross(c - p, a - p).magnitude;
        float sum = areaPAB + areaPBC + areaPCA;
        return Mathf.Abs(sum - areaABC) < 1e-3f;
    }
}
