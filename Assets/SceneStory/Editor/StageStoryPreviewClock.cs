using System.Linq;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    [InitializeOnLoad]
    public static class StageStoryPreviewClock
    {
        private static double nextCheck;
        static StageStoryPreviewClock() { EditorApplication.update += Update; }
        private static void Update()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + .5;
            foreach (var playback in Object.FindObjectsOfType<StageStoryPlayback>())
                EnsurePreviewClock(playback);
        }

        public static bool EnsurePreviewClock(StageStoryPlayback playback)
        {
            if (playback == null || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            var director = playback.director;
            if (director == null || !director.gameObject.scene.IsValid() || EditorUtility.IsPersistent(director) ||
                !(director.playableAsset is TimelineAsset timeline) || !timeline.GetOutputTracks().Any(t => t is StagePerformanceTrack) ||
                director.timeUpdateMode != DirectorUpdateMode.Manual) return false;
            // 只迁移本工具自己的运行驱动，其他自定义手动 Timeline 不受影响。
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            EditorUtility.SetDirty(director);
            PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            return true;
        }
    }
}
