using System;
using System.Linq;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIBuilder.EditorTools.CreatureStage
{
    public sealed class StageGroundBindingWindow : EditorWindow
    {
        [SerializeField] private StageGroundSettings settings;
        private MeshFilter[] meshes;
        private bool[] included;
        private string[] reasons;
        private Vector2 scroll;
        private string result;
        private bool highlight;

        public static void Open(Scene scene)
        {
            var window = GetWindow<StageGroundBindingWindow>("地面绑定");
            window.settings = StageGroundRepair.GetSettings(scene);
            window.meshes = null;
            window.minSize = new Vector2(460, 480);
            window.Show();
        }
        private void OnEnable()
        {
            SceneView.duringSceneGui += DrawScene;
            EditorApplication.hierarchyChanged += ClearPreview;
            EditorApplication.projectChanged += ClearPreview;
            Undo.undoRedoPerformed += ClearPreview;
        }
        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            EditorApplication.hierarchyChanged -= ClearPreview;
            EditorApplication.projectChanged -= ClearPreview;
            Undo.undoRedoPerformed -= ClearPreview;
            SceneView.RepaintAll();
        }
        private void ClearPreview() { meshes = null; Repaint(); }
        private void Scan()
        {
            try
            {
                meshes = StageGroundRepair.Scan(settings);
                included = new bool[meshes.Length]; reasons = new string[meshes.Length];
                for (var i = 0; i < meshes.Length; i++) included[i] = settings.Includes(meshes[i], out reasons[i]);
                result = $"加入 {included.Count(v => v)} 个，排除 {included.Count(v => !v)} 个。扫描未修改碰撞体或路线绑定。";
                SceneView.RepaintAll();
            }
            catch (Exception e) { meshes = null; result = e.Message; }
        }
        private void OnGUI()
        {
            if (settings == null) { EditorGUILayout.HelpBox("当前场景没有唯一的地面设置。请先创建剧情路线，并确认场景只有一份地面绑定设置。", MessageType.Info); return; }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.LabelField("地面绑定", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("先扫描预览，再应用。绿色框为加入，红色框为排除。明确排除优先于强制包含；父节点规则作用于全部子物体。", MessageType.None);
                var serialized = new SerializedObject(settings);
                serialized.Update();
                var mode = serialized.FindProperty("scanMode");
                mode.enumValueIndex = EditorGUILayout.Popup("筛选方式", mode.enumValueIndex, new[] { "旧版名称匹配", "静态 Mesh（推荐）" });
                EditorGUILayout.PropertyField(serialized.FindProperty("groundRoot"), new GUIContent("环境根节点（空为全场景）"));
                if (mode.enumValueIndex == 0)
                {
                    EditorGUILayout.PropertyField(serialized.FindProperty("groundName"), new GUIContent("地面名称"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("match"), new GUIContent("名称匹配方式"));
                }
                else
                {
                    EditorGUILayout.PropertyField(serialized.FindProperty("excludedKeywords"), new GUIContent("排除关键词（逗号分隔）"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("maxStepUp"), new GUIContent("允许向上吸附（米）"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("maxDrop"), new GUIContent("允许向下寻找（米）"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("maxSlope"), new GUIContent("最大可走坡度（度）"));
                    EditorGUILayout.HelpBox("按节点高度附近选择地面，避免吸到头顶桥面。石头或斜坡高出路线时，先调整节点高度或向上吸附范围。同一 Mesh 内的草与地面需使用独立行走面。", MessageType.None);
                }
                EditorGUILayout.PropertyField(serialized.FindProperty("excludedRoots"), new GUIContent("排除对象 / 植被父节点"), true);
                EditorGUILayout.PropertyField(serialized.FindProperty("includedRoots"), new GUIContent("强制包含 / 石头与坡道"), true);
                if (serialized.ApplyModifiedProperties())
                {
                    meshes = null;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
                    EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("扫描预览 / 重新扫描", GUILayout.Height(30))) Scan();
                    using (new EditorGUI.DisabledScope(meshes == null || included == null || !included.Any(v => v)))
                        if (GUILayout.Button("应用绑定", GUILayout.Height(30)))
                        {
                            try
                            {
                                var current = StageGroundRepair.Scan(settings);
                                if (!current.SequenceEqual(meshes) || current.Where((m, i) => settings.Includes(m, out _) != included[i]).Any())
                                { Scan(); result += "\n场景筛选结果已变化，请检查新结果后再应用。"; }
                                else result = StageGroundRepair.Repair(settings.gameObject.scene, true);
                            }
                            catch (Exception e) { result = "绑定失败：" + e.Message; }
                        }
                }
                EditorGUILayout.HelpBox("应用会复用有效 MeshCollider，缺少时添加供地面检测使用的 MeshCollider；排除对象的已有碰撞组件不变。支持 Ctrl+Z 撤销。", MessageType.None);
                if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
                var nextHighlight = EditorGUILayout.Toggle("Scene 高亮扫描结果", highlight);
                if (nextHighlight != highlight) { highlight = nextHighlight; SceneView.RepaintAll(); }
                if (meshes != null)
                    for (var i = 0; i < meshes.Length; i++)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(included[i] ? "加入" : "排除", meshes[i], typeof(MeshFilter), true);
                        GUILayout.Label(reasons[i], GUILayout.Width(150));
                    }
                EditorGUILayout.EndScrollView();
            }
        }
        private void DrawScene(SceneView view)
        {
            if (!highlight || meshes == null || Event.current.type != EventType.Repaint) return;
            for (var i = 0; i < meshes.Length; i++)
            {
                var mesh = meshes[i];
                if (mesh == null || mesh.sharedMesh == null) continue;
                using (new Handles.DrawingScope(included[i] ? Color.green : Color.red, mesh.transform.localToWorldMatrix))
                    Handles.DrawWireCube(mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.size);
            }
        }
    }
}
