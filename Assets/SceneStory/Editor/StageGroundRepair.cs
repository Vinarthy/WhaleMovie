using System;
using System.IO;
using System.Linq;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIBuilder.EditorTools.CreatureStage
{
    [InitializeOnLoad]
    public static class StageGroundRepair
    {
        private const string Request = "Library/CreatureStage/ground-repair.request.txt";
        private static bool pending = true;
        private static double next;
        private const string PlayTransitionKey = "SceneStory.GroundRepair.PlayTransition";
        private static bool CanEdit => !Application.isPlaying && !EditorApplication.isPlaying &&
            !EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool(PlayTransitionKey, false);
        static StageGroundRepair()
        {
            EditorApplication.update += Poll;
            EditorSceneManager.sceneOpened += (scene, mode) => Schedule();
            EditorApplication.playModeStateChanged += state =>
            {
                // 跨域重载保留过渡状态，直到编辑场景完成恢复才允许修改。
                SessionState.SetBool(PlayTransitionKey, state != PlayModeStateChange.EnteredEditMode);
                if (state == PlayModeStateChange.EnteredEditMode) next = EditorApplication.timeSinceStartup + 2;
            };
        }
        internal static void Schedule() { pending = true; }
        public static void RepairActive() { Debug.Log(Repair(SceneManager.GetActiveScene())); }
        public static StageGroundSettings GetSettings(Scene scene)
        {
            if (!CanEdit) return null;
            if (!scene.IsValid() || !scene.isLoaded) return null;
            var settings = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<StageGroundSettings>(true)).ToArray();
            if (settings.Length > 0) return settings.Length == 1 ? settings[0] : null;
            var route = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<StageRoute>(true)).FirstOrDefault();
            var owner = route != null ? route.transform.root.gameObject : scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true))
                .FirstOrDefault(d => d.playableAsset is UnityEngine.Timeline.TimelineAsset timeline && timeline.GetOutputTracks().OfType<StagePerformanceTrack>().Any())?.gameObject;
            if (owner == null) return null;
            var created = Undo.AddComponent<StageGroundSettings>(owner);
            EditorSceneManager.MarkSceneDirty(scene);
            return created;
        }
        private static void Poll()
        {
            if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || !CanEdit || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            var requested = File.Exists(Request);
            if (!pending && !requested) return;
            pending = false;
            try
            {
                var result = "";
                for (var i = 0; i < SceneManager.sceneCount; i++) result += Repair(SceneManager.GetSceneAt(i)) + "\n";
                if (requested) File.WriteAllText("Library/CreatureStage/ground-repair.result.txt", result);
            }
            catch (Exception e)
            {
                if (requested) File.WriteAllText("Library/CreatureStage/ground-repair.result.txt", "FAILED " + e);
                Debug.LogException(e);
            }
            finally { if (requested) File.Delete(Request); }
        }
        public static MeshFilter[] Scan(StageGroundSettings settings)
        {
            if (!StageMotionPlan.IsFinite(settings.maxStepUp) || settings.maxStepUp < 0 ||
                !StageMotionPlan.IsFinite(settings.maxDrop) || settings.maxDrop <= 0 ||
                !StageMotionPlan.IsFinite(settings.maxSlope) || settings.maxSlope < 0 || settings.maxSlope > 90)
                throw new InvalidOperationException("吸附范围和坡度无效：上升至少 0 米，下降大于 0 米，坡度为 0–90 度。");
            var scene = settings.gameObject.scene;
            if (settings.groundRoot != null && settings.groundRoot.gameObject.scene != scene) throw new InvalidOperationException("地面根对象必须属于当前场景");
            var roots = settings.groundRoot != null ? new[] { settings.groundRoot } : scene.GetRootGameObjects().Select(g => g.transform).ToArray();
            return roots.Concat(settings.includedRoots ?? Array.Empty<Transform>()).Where(r => r != null && r.gameObject.scene == scene)
                .SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)).Distinct().ToArray();
        }

        public static string Repair(Scene scene, bool applyStatic = false)
        {
            if (!CanEdit) return "运行中不修改场景地面绑定，请退出 Play 后修复。";
            if (!scene.IsValid() || !scene.isLoaded) return "场景未加载";
            var routes = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<StageRoute>(true)).ToArray();
            if (routes.Length == 0 && !applyStatic) return scene.name + "：无剧情路线";
            var settings = GetSettings(scene);
            if (settings == null) return scene.name + "：地面绑定设置不唯一，请每个场景只保留一份";
            if (settings.scanMode == StageGroundScanMode.StaticMeshes && !applyStatic) return "静态 Mesh 模式请在地面绑定面板扫描预览后应用；保留当前绑定。";
            if (settings.groundRoot != null && settings.groundRoot.gameObject.scene != scene) return "地面根对象必须属于当前场景";
            var meshes = Scan(settings).Where(f => settings.Includes(f, out _)).ToArray();
            if (meshes.Length == 0) return scene.name + "：没有符合筛选规则的地面，保留现有绑定和路线点";
            StageStoryWindow.StopAllPreviews();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("更新剧情地面绑定");
            try
            {
            var surfaces = meshes.Select(f =>
            {
                var c = settings.scanMode == StageGroundScanMode.StaticMeshes
                    ? f.GetComponents<MeshCollider>().FirstOrDefault(v => v.enabled && !v.isTrigger && !v.convex && v.sharedMesh == f.sharedMesh)
                    : f.GetComponent<MeshCollider>();
                if (c == null) c = Undo.AddComponent<MeshCollider>(f.gameObject);
                if (c.sharedMesh != f.sharedMesh)
                {
                    Undo.RecordObject(c, "更新白模碰撞网格"); c.sharedMesh = f.sharedMesh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                }
                return (Collider)c;
            }).ToArray();
            Undo.RecordObject(settings, "记录已应用地面");
            settings.boundSurfaces = surfaces;
            EditorUtility.SetDirty(settings);
            PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
            Physics.SyncTransforms();
            var count = 0;
            var snapped = 0;
            var problems = new System.Collections.Generic.List<string>();
            foreach (var route in routes)
            {
                // 自动模式包括新建路线的空绑定；手动/悬空路线通过明确开关保留。
                if (!route.autoBindGround) continue;
                if (settings.scanMode == StageGroundScanMode.StaticMeshes || route.limitGroundProjection)
                {
                    Undo.RecordObject(route, "更新贴地范围");
                    route.limitGroundProjection = settings.scanMode == StageGroundScanMode.StaticMeshes; route.groundStepUp = settings.maxStepUp;
                    route.groundDrop = settings.maxDrop; route.groundSlope = settings.maxSlope;
                    EditorUtility.SetDirty(route);
                }
                if (route.groundSurfaces == null || !route.groundSurfaces.SequenceEqual(surfaces))
                {
                    Undo.RecordObject(route, "重新绑定路线地面"); route.groundSurfaces = surfaces;
                    EditorUtility.SetDirty(route); count++;
                }
                route.Invalidate();
                // 新模式不改写创作节点高度，仅在采样时投射，避免重新绑定改变空中/多层路线。
                if (settings.scanMode == StageGroundScanMode.StaticMeshes)
                {
                    try { route.SampleForGround(.5, true, out _, out _); }
                    catch (InvalidOperationException e) { problems.Add(e.Message); }
                    continue;
                }
                // 共享路线只要有飞行片段，就保留原始高度；地面片段在采样时单独投射。
                if (!route.snapToGround || route.HasFreeSpaceUsage()) continue;
                for (var i = 0; i < route.PointCount; i++)
                {
                    if (i == 0 && route.TryGetLinkedStart(out _)) continue;
                    var point = route.WorldPoint(i);
                    Vector3 projected;
                    try { projected = route.ProjectToGround(point, true); }
                    catch (InvalidOperationException) { continue; }
                    if (Mathf.Abs(projected.y - point.y) < 0.0001f) continue;
                    point.y = projected.y;
                    var anchor = i == 0 ? route.startAnchor : i == route.PointCount - 1 ? route.endAnchor : null;
                    if (anchor != null)
                    {
                        Undo.RecordObject(anchor, "路线节点贴地"); anchor.position = point;
                        EditorUtility.SetDirty(anchor);
                    }
                    else
                    {
                        Undo.RecordObject(route, "路线控制点贴地"); route.points[i] = route.transform.InverseTransformPoint(point);
                        EditorUtility.SetDirty(route);
                    }
                    snapped++;
                }
                route.Invalidate();
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate();
            return scene.name + "：地面 " + surfaces.Length + " 块，重绑路线 " + count + " 条，贴地节点 " + snapped + " 个。请保存场景。" +
                (problems.Count == 0 ? "" : "\n有 " + problems.Count + " 条路线需检查（Scene 中标红）：\n" + string.Join("\n", problems.Take(8)));
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
    }

    public sealed class StageGroundImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(moved).Any(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))) StageGroundRepair.Schedule();
        }
    }
}
