using UnityEditor;
using UnityEngine;
namespace AIBuilder.EditorTools.CreatureStage
{
    public sealed class StageTutorial : EditorWindow
    {
        private Vector2 scroll;
        [MenuItem("Tools/场景剧情/使用教程", false, 100)]
        public static void Open() { var window = GetWindow<StageTutorial>("场景剧情 · 使用教程"); window.minSize = new Vector2(600, 450); }
        private void OnGUI()
        {
            if (GUILayout.Button("创建一个基础几何体示例")) StageBasicExample.CreateExample();
            var tutorial = Resources.Load<TextAsset>("SceneStoryTutorial");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var style = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 14, richText = false, padding = new RectOffset(12,12,10,10) };
            GUILayout.Label(tutorial != null ? tutorial.text : "未找到教程资源，请检查包是否完整导入。", style);
            EditorGUILayout.EndScrollView();
        }
    }
}
