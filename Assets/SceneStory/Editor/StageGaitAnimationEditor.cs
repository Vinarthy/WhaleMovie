using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    [CustomEditor(typeof(StageGaitAnimationAsset))]
    public sealed class StageGaitAnimationEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor nativeEditor;
        private void OnEnable()
        {
            var type = typeof(TimelineEditor).Assembly.GetType("UnityEditor.Timeline.AnimationPlayableAssetInspector");
            if (type != null) nativeEditor = CreateEditor(target, type);
        }
        private void OnDisable() { if (nativeEditor != null) DestroyImmediate(nativeEditor); }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("synchronizeDistance"), new GUIContent("动画跟随移动距离"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cycleDistance"), new GUIContent("一循环前进距离（米）"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("走跑动画按路程推进，停留期间保持步态相位。重叠混合仍由 Timeline 控制；同步时忽略手动播放倍率。请使用循环动作。", MessageType.Info);
            if (nativeEditor != null) nativeEditor.OnInspectorGUI();
        }

        public static StageGaitAnimationAsset Enable(TimelineClip clip, PlayableDirector director)
        {
            var original = clip.asset as AnimationPlayableAsset;
            if (original == null) throw new System.InvalidOperationException("请选择动画片段。");
            if (original is StageGaitAnimationAsset existing) return existing;
            var track = clip.GetParentTrack();
            Undo.RegisterCompleteObjectUndo(track, "启用动画步态同步");
            var replacement = CreateInstance<StageGaitAnimationAsset>();
            var source = new SerializedObject(original);
            var destination = new SerializedObject(replacement);
            var property = source.GetIterator();
            if (property.Next(true))
                do
                {
                    if (property.propertyPath != "m_Script") destination.CopyFromSerializedProperty(property);
                } while (property.Next(false));
            destination.ApplyModifiedPropertiesWithoutUndo();
            replacement.name = original.name;
            replacement.synchronizeDistance = true;
            replacement.loop = AnimationPlayableAsset.LoopMode.On;
            if (AssetDatabase.Contains(track)) AssetDatabase.AddObjectToAsset(replacement, track);
            Undo.RegisterCreatedObjectUndo(replacement, "启用动画步态同步");
            clip.asset = replacement;
            Undo.DestroyObjectImmediate(original);
            EditorUtility.SetDirty(track);
            director.RebuildGraph();
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
            return replacement;
        }
    }
}
