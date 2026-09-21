using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudHarvest.EditorTools
{
    // Editor-only: placed blocks remain ordinary, linked prefab instances.
    public static class CubePlacement
    {
        public const string Prefix = "CH Block ";
        public static bool TryBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var b = filter.sharedMesh.bounds;
                var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return found;
        }

        public static bool IsUnitCube(GameObject prefab, out Bounds bounds)
        {
            return TryBounds(prefab, out bounds) && (bounds.size - Vector3.one).sqrMagnitude < 0.003f;
        }

        public static Vector3Int CellAt(Vector3 localPoint, int layer)
            => new Vector3Int(Mathf.FloorToInt(localPoint.x), layer, Mathf.FloorToInt(localPoint.z));

        public static Dictionary<Vector3Int, GameObject> Index(Transform root)
        {
            var result = new Dictionary<Vector3Int, GameObject>();
            if (!root) return result;
            foreach (Transform child in root)
            {
                if (!child.name.StartsWith(Prefix, StringComparison.Ordinal) || !TryBounds(child.gameObject, out var b)) continue;
                var center = root.InverseTransformPoint(child.TransformPoint(b.center));
                var cell = Vector3Int.FloorToInt(center + Vector3.one * 0.0001f);
                result[cell] = child.gameObject;
            }
            return result;
        }

        public static bool Apply(Transform root, Dictionary<Vector3Int, GameObject> index, Vector3Int cell,
            GameObject prefab, Bounds bounds, int turns, bool erase, bool replace, bool collider, bool undo = true)
        {
            index.TryGetValue(cell, out var previous);
            if (erase)
            {
                if (!previous) return false;
                if (undo) Undo.DestroyObjectImmediate(previous); else UnityEngine.Object.DestroyImmediate(previous);
                index.Remove(cell);
                return true;
            }
            if (!prefab || (previous && !replace)) return false;
            if (previous && PrefabUtility.GetCorrespondingObjectFromSource(previous) == prefab &&
                Quaternion.Angle(previous.transform.localRotation, Quaternion.Euler(0, turns * 90, 0)) < 0.01f) return false;
            if (previous)
            {
                if (undo) Undo.DestroyObjectImmediate(previous); else UnityEngine.Object.DestroyImmediate(previous);
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
            instance.transform.SetParent(root, false);
            instance.transform.localScale = Vector3.one;
            instance.transform.localRotation = Quaternion.Euler(0, turns * 90, 0);
            instance.transform.localPosition = (Vector3)cell + Vector3.one * 0.5f - instance.transform.localRotation * bounds.center;
            instance.name = $"{Prefix}[{cell.x},{cell.y},{cell.z}] {prefab.name}";
            if (collider && instance.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                var box = instance.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = Vector3.one;
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            if (undo) Undo.RegisterCreatedObjectUndo(instance, "Paint cube");
            index[cell] = instance;
            return true;
        }
    }

    public class CloudHarvestCubePalette : EditorWindow
    {
        public const string SourceFolder = "Assets/KUBIKOS - World/URP Support/Prefabs URP/Cubes";
        [SerializeField] Transform target;
        [SerializeField] GameObject selected;
        [SerializeField] int layer, turns, mode;
        [SerializeField] int brushSize = 1;
        [SerializeField] bool replace, addCollider = true;
        [SerializeField] string search = "";
        Vector2 scroll;
        bool painting;
        List<GameObject> cubes = new List<GameObject>();
        Dictionary<Vector3Int, GameObject> index;
        readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        Vector3Int? lastCell;
        int strokeGroup = -1, control;
        Bounds selectedBounds;

        [MenuItem("Tools/Cloud Harvest/Cube Palette")]
        public static void Open()
        {
            var window = GetWindow<CloudHarvestCubePalette>();
            window.titleContent = new GUIContent("Cube Palette");
            window.minSize = new Vector2(360, 480);
            window.Show();
        }

        public static List<GameObject> LoadCubes() => AssetDatabase.FindAssets("t:Prefab", new[] { SourceFolder })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(p => p && CubePlacement.IsUnitCube(p, out _))
            .OrderBy(p => p.name == "Cube_Grass" ? 0 : 1).ThenBy(p => p.name, StringComparer.Ordinal).ToList();

        void OnEnable()
        {
            Reload();
            SceneView.duringSceneGui += DuringScene;
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }
        void OnDisable()
        {
            EndStroke();
            SceneView.duringSceneGui -= DuringScene;
            Undo.undoRedoPerformed -= Invalidate;
            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            SceneView.RepaintAll();
        }
        void PlayModeChanged(PlayModeStateChange change) { painting = false; EndStroke(); Repaint(); }
        void Invalidate() { index = null; Repaint(); SceneView.RepaintAll(); }
        void Reload()
        {
            cubes = LoadCubes();
            if (!selected || !cubes.Contains(selected)) selected = cubes.FirstOrDefault();
            if (selected) CubePlacement.TryBounds(selected, out selectedBounds);
            Repaint();
        }
        bool ValidTarget() => target && !EditorUtility.IsPersistent(target) && target.gameObject.scene.IsValid()
            && target.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(target.gameObject.scene)
            && Quaternion.Angle(target.rotation, Quaternion.identity) < 0.01f
            && (target.lossyScale - Vector3.one).sqrMagnitude < 0.0001f;

        void OnGUI()
        {
            EditorGUILayout.LabelField("CLOUD HARVEST · KÜP PALETİ", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("KUBIKOS URP  •  1 × 1 × 1 birim  •  XZ düzlemi", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null))
            {
                EditorGUI.BeginChangeCheck();
                target = (Transform)EditorGUILayout.ObjectField("Blok grubu", target, typeof(Transform), true);
                if (EditorGUI.EndChangeCheck()) { EndStroke(); index = null; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Yeni blok grubu"))
                    {
                        EndStroke();
                        var go = new GameObject("Cloud Harvest Blocks");
                        Undo.RegisterCreatedObjectUndo(go, "Create block group");
                        target = go.transform;
                        index = null;
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
                    using (new EditorGUI.DisabledScope(!target))
                        if (GUILayout.Button("Gruba odaklan"))
                        {
                            Selection.activeGameObject = target.gameObject;
                            SceneView.lastActiveSceneView?.LookAt(target.position + new Vector3(4, layer, 4), Quaternion.Euler(55, -45, 0), 14);
                        }
                }
                EditorGUI.BeginChangeCheck();
                layer = EditorGUILayout.IntField("Y katmanı", layer);
                brushSize = EditorGUILayout.IntSlider("Fırça (küp)", brushSize, 1, 5);
                turns = EditorGUILayout.Popup("Döndürme", turns, new[] { "0°", "90°", "180°", "270°" });
                mode = GUILayout.Toolbar(mode, new[] { "Boya", "Sil" });
                replace = EditorGUILayout.Toggle("Dolu hücreyi değiştir", replace);
                addCollider = EditorGUILayout.Toggle("Box Collider ekle", addCollider);
                if (EditorGUI.EndChangeCheck()) { EndStroke(); SceneView.RepaintAll(); }
                if (!ValidTarget()) EditorGUILayout.HelpBox("Yeni blok grubu oluştur veya bir sahne grubu seç. Grubun dönüşü 0, ölçeği 1 olmalı.", MessageType.Info);
                using (new EditorGUI.DisabledScope(!ValidTarget() || !selected))
                {
                    bool active = GUILayout.Toggle(painting, painting ? "BOYAMA AÇIK — Esc ile kapat" : "Sahnede boyamayı aç", "Button", GUILayout.Height(30));
                    if (active != painting) { painting = active; EndStroke(); SceneView.RepaintAll(); }
                }
            }
            EditorGUILayout.HelpBox("Scene: sol tık / sürükle = uygula · Shift = sil\nR = döndür · Page Up/Down = katman · Ctrl+Z = geri al\nAlt + fare: normal sahne gezintisi. Katman 0: Y=0…1.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("Yenile", GUILayout.Width(58))) Reload();
            }
            EditorGUILayout.LabelField(selected ? "Seçili: " + Label(selected) : "Küp bulunamadı", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var visible = cubes.Where(c => c && c.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            int columns = Mathf.Max(2, Mathf.FloorToInt((position.width - 24) / 90));
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            for (int row = 0; row < visible.Count; row += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                    for (int col = 0; col < columns; col++)
                    {
                        int i = row + col;
                        if (i >= visible.Count) { GUILayout.FlexibleSpace(); continue; }
                        var prefab = visible[i];
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(84)))
                        {
                            var preview = AssetPreview.GetAssetPreview(prefab);
                            var old = GUI.backgroundColor;
                            if (prefab == selected) GUI.backgroundColor = new Color(0.35f, 0.85f, 0.65f);
                            if (GUILayout.Button(new GUIContent(preview ? preview : AssetPreview.GetMiniThumbnail(prefab), prefab.name), GUILayout.Width(82), GUILayout.Height(64)))
                            { selected = prefab; CubePlacement.TryBounds(selected, out selectedBounds); SceneView.RepaintAll(); }
                            GUI.backgroundColor = old;
                            GUILayout.Label(Label(prefab), labelStyle, GUILayout.Width(82), GUILayout.Height(30));
                            if (!preview && AssetPreview.IsLoadingAssetPreview(prefab.GetInstanceID())) Repaint();
                        }
                    }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"{cubes.Count} tam küp · Prefab bağlantıları korunur", EditorStyles.miniLabel);
        }
        static string Label(GameObject prefab) => prefab.name.Replace("Cube_", "").Replace("_", " ");

        void EndStroke()
        {
            if (strokeGroup >= 0) Undo.CollapseUndoOperations(strokeGroup);
            if (control != 0 && GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            strokeGroup = -1;
            visited.Clear();
            lastCell = null;
        }
        void DuringScene(SceneView view)
        {
            if (!painting || !ValidTarget() || !selected || EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
            var e = Event.current;
            control = GUIUtility.GetControlID("CloudHarvestCubePalette".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.KeyDown && !e.alt && !e.control && !e.command)
            {
                if (e.keyCode == KeyCode.Escape) { painting = false; EndStroke(); e.Use(); Repaint(); view.Repaint(); return; }
                if (e.keyCode == KeyCode.R) turns = (turns + 1) % 4;
                else if (e.keyCode == KeyCode.PageUp) layer++;
                else if (e.keyCode == KeyCode.PageDown) layer--;
                else return;
                EndStroke(); e.Use(); Repaint(); view.Repaint(); return;
            }
            if (e.rawType == EventType.MouseUp && e.button == 0 && strokeGroup >= 0) { EndStroke(); e.Use(); return; }
            if (e.alt || e.control || e.command || Tools.current == Tool.View) return;
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var plane = new Plane(Vector3.up, target.position + Vector3.up * layer);
            if (!plane.Raycast(ray, out float distance) || distance > 10000) return;
            var cell = CubePlacement.CellAt(ray.GetPoint(distance) - target.position, layer);
            bool erase = mode == 1 || e.shift;
            int offset = (brushSize - 1) / 2;
            if (e.type == EventType.Repaint)
            {
                using (new Handles.DrawingScope(new Color(1, 1, 1, 0.18f), Matrix4x4.Translate(target.position)))
                    for (int n = -10; n <= 10; n++)
                    {
                        Handles.DrawLine(new Vector3(cell.x + n, layer, cell.z - 10), new Vector3(cell.x + n, layer, cell.z + 10));
                        Handles.DrawLine(new Vector3(cell.x - 10, layer, cell.z + n), new Vector3(cell.x + 10, layer, cell.z + n));
                    }
                using (new Handles.DrawingScope(erase ? new Color(1, 0.3f, 0.25f) : new Color(0.3f, 1, 0.65f)))
                {
                    Vector3 center = target.position + (Vector3)cell + new Vector3(brushSize / 2f - offset, 0.5f, brushSize / 2f - offset);
                    Handles.DrawWireCube(center, new Vector3(brushSize, 1, brushSize));
                    Handles.Label(center + Vector3.up, $"{(erase ? "Sil" : Label(selected))}  [{cell.x}, {layer}, {cell.z}]");
                }
            }
            if (e.type == EventType.MouseMove) view.Repaint();
            if (e.button != 0) return;
            if (e.type == EventType.MouseDown && HandleUtility.nearestControl == control)
            {
                GUIUtility.hotControl = control;
                Undo.IncrementCurrentGroup(); strokeGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(erase ? "Erase cubes" : "Paint cubes");
                index = CubePlacement.Index(target);
            }
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && GUIUtility.hotControl == control && strokeGroup >= 0)
            {
                if (index == null) index = CubePlacement.Index(target);
                var from = lastCell ?? cell;
                int steps = Mathf.Max(Mathf.Abs(cell.x - from.x), Mathf.Abs(cell.z - from.z));
                for (int step = 0; step <= steps; step++)
                {
                    var at = Vector3Int.RoundToInt(Vector3.Lerp(from, cell, steps == 0 ? 1 : step / (float)steps));
                    for (int x = 0; x < brushSize; x++) for (int z = 0; z < brushSize; z++)
                    {
                        var p = at + new Vector3Int(x - offset, 0, z - offset);
                        if (visited.Add(p) && CubePlacement.Apply(target, index, p, selected, selectedBounds, turns, erase, replace, addCollider))
                            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                    }
                }
                lastCell = cell;
                e.Use(); view.Repaint();
            }
        }
    }
}

