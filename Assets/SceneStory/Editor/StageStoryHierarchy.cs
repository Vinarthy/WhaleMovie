using System.Collections.Generic;
using System.Linq;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    // 只整理工具生成对象的 Hierarchy 可见性，保留引用和撤销所需对象。
    [InitializeOnLoad]
    public static class StageStoryHierarchy
    {
        private const string Menu = "Tools/场景剧情/维护/显示未使用剧情对象";
        private const string Preference = "AIBuilder.CreatureStage.ShowUnusedObjects";
        private static double nextRefresh;
        static StageStoryHierarchy()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += () => nextRefresh = 0;
        }
        [MenuItem(Menu)]
        private static void Toggle()
        {
            EditorPrefs.SetBool(Preference, !EditorPrefs.GetBool(Preference, false));
            nextRefresh = 0;
        }
        [MenuItem(Menu, true)]
        private static bool ValidateMenu()
        {
            UnityEditor.Menu.SetChecked(Menu, EditorPrefs.GetBool(Preference, false));
            return true;
        }
        private static void Tick()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + .5;
            Refresh(EditorPrefs.GetBool(Preference, false));
        }
        public static void Refresh(bool showUnused = false)
        {
            var candidates = new HashSet<GameObject>();
            var used = new HashSet<GameObject>();
            foreach (var director in Object.FindObjectsOfType<PlayableDirector>(true))
            {
                if (!director.gameObject.scene.IsValid() || !(director.playableAsset is TimelineAsset timeline)) continue;
                var tracks = timeline.GetOutputTracks().OfType<StagePerformanceTrack>().ToArray();
                if (tracks.Length == 0) continue;
                foreach (Transform child in director.transform)
                {
                    var route = child.GetComponent<StageRoute>();
                    if (route != null)
                    {
                        candidates.Add(child.gameObject);
                        AddOwnedPoint(candidates, route.startAnchor, director.transform);
                        AddOwnedPoint(candidates, route.endAnchor, director.transform);
                    }
                    else if (IsGeneratedPoint(child)) candidates.Add(child.gameObject);
                }
                foreach (var track in tracks)
                foreach (var clip in track.GetClips())
                {
                    var data = (StagePerformanceClip)clip.asset;
                    if (data.mode == StagePerformanceMode.Move)
                    {
                        var route = data.route.Resolve(director);
                        if (route == null) continue;
                        Keep(used, route.transform);
                        if (!route.TryGetLinkedStart(out _)) Keep(used, route.startAnchor);
                        Keep(used, route.endAnchor);
                    }
                    else
                    {
                        var source = StagePerformanceTrack.PlacementSource(clip);
                        var placement = (StagePerformanceClip)source.asset;
                        if (placement.mode == StagePerformanceMode.Hold) Keep(used, placement.station.Resolve(director));
                    }
                }
            }
            var changed = false;
            foreach (var obj in candidates)
            {
                var flags = showUnused || used.Contains(obj) ? obj.hideFlags & ~HideFlags.HideInHierarchy : obj.hideFlags | HideFlags.HideInHierarchy;
                if (obj.hideFlags == flags) continue;
                obj.hideFlags = flags;
                changed = true;
            }
            if (changed) EditorApplication.RepaintHierarchyWindow();
        }
        private static void Keep(HashSet<GameObject> used, Transform point)
        {
            for (; point != null; point = point.parent) used.Add(point.gameObject);
        }
        private static void AddOwnedPoint(HashSet<GameObject> candidates, Transform point, Transform root)
        {
            if (point != null && point.parent == root && point.GetComponents<Component>().Length == 1)
                candidates.Add(point.gameObject);
        }
        private static bool IsGeneratedPoint(Transform point)
        {
            if (point.GetComponents<Component>().Length != 1 || point.childCount != 0) return false;
            return point.name.StartsWith("剧情点 ") || point.name.StartsWith("到达点 ") ||
                new[] { "A 起始位置", "B 倒木前觅食", "C 草丛藏身", "D 再次觅食", "E 离场位置" }.Contains(point.name);
        }
    }
}
