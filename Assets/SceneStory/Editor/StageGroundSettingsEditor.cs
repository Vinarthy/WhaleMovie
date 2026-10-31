using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIBuilder.EditorTools.CreatureStage
{
    [CustomEditor(typeof(StageGroundSettings))]
    public sealed class StageGroundSettingsEditor : Editor
    {
        private string result;

        [MenuItem("Tools/场景剧情/地面绑定设置")]
        public static void Open()
        {
            var settings = StageGroundRepair.GetSettings(SceneManager.GetActiveScene());
            if (settings == null) { Debug.LogWarning("请先打开带剧情路线的场景。每个场景只配置一份地面绑定设置。"); return; }
            Selection.activeGameObject = settings.gameObject;
            EditorGUIUtility.PingObject(settings);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("按场景对象名称匹配地面网格，忽略大小写；可限定查找根对象。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("groundRoot"), new GUIContent("地面查找根对象"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("groundName"), new GUIContent("地面对象名称"));
            var match = serializedObject.FindProperty("match");
            match.enumValueIndex = EditorGUILayout.Popup("匹配方式", match.enumValueIndex, new[] { "名称以此开头", "名称包含此文字", "名称完全一致" });
            if (serializedObject.ApplyModifiedProperties()) result = null;
            var settings = (StageGroundSettings)target;
            if (string.IsNullOrWhiteSpace(settings.groundName))
                EditorGUILayout.HelpBox("名称不能为空。未匹配到地面时会保留现有绑定。", MessageType.Warning);
            EditorGUILayout.HelpBox("例如填 Ground_，选择“名称以此开头”，会匹配 Ground_01、Ground_02。匹配的是 Hierarchy 对象名，不是 FBX 文件名。", MessageType.None);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("应用设置并重新绑定地面")) result = StageGroundRepair.Repair(settings.gameObject.scene);
            if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
        }
    }
}
