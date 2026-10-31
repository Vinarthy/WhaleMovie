using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    [InitializeOnLoad]
    public static class StageStoryStart
    {
        private const string Request = "Library/CreatureStage/align-start.request.txt";
        static StageStoryStart() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Delete(Request);
            Apply();
        }
        [MenuItem("Tools/场景剧情/维护/剧情移到零秒")]
        public static void Apply()
        {
            StageStoryWindow.StopAllPreviews();
            foreach (var director in Object.FindObjectsOfType<PlayableDirector>())
            {
                if (!(director.playableAsset is TimelineAsset timeline) || !timeline.GetOutputTracks().Any(t => t is AIBuilder.CreatureStage.StagePerformanceTrack)) continue;
                var moved = Align(director);
                File.WriteAllText("Library/CreatureStage/align-start.result.txt", "已整体前移 " + moved + " 秒，保留片段相对时间与过渡。");
            }
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
        }
        public static double Align(PlayableDirector director)
        {
            if (!(director.playableAsset is TimelineAsset timeline)) return 0;
            var tracks = timeline.GetOutputTracks().ToArray();
            var clips = tracks.SelectMany(t => t.GetClips()).ToArray();
            if (clips.Length == 0) return 0;
            var offset = clips.Min(c => c.start);
            if (offset <= .000001) return 0;
            director.Stop();
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(tracks.Cast<Object>().Concat(new Object[] { director }).ToArray(), "剧情整体移到零秒");
            foreach (var clip in clips) clip.start -= offset;
            foreach (var track in tracks) EditorUtility.SetDirty(track);
            director.initialTime = 0; director.time = 0;
            EditorUtility.SetDirty(director);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            director.RebuildGraph();
            Undo.FlushUndoRecordObjects();
            return offset;
        }
    }
}
