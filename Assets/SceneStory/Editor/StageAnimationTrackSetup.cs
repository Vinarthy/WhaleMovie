using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    [InitializeOnLoad]
    public static class StageAnimationTrackSetup
    {
        private const string Request = "Library/CreatureStage/animation-track.request.txt";
        static StageAnimationTrackSetup() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Delete(Request);
            try { File.WriteAllText("Library/CreatureStage/animation-track.result.txt", ApplyScene()); }
            catch (Exception e) { File.WriteAllText("Library/CreatureStage/animation-track.result.txt", e.ToString()); }
        }
        public static void ApplyMenu() { Debug.Log(ApplyScene()); }
        private static string ApplyScene()
        {
            StageStoryWindow.StopAllPreviews();
            if (AnimationMode.InAnimationMode()) throw new InvalidOperationException("请先关闭 Timeline Preview 再拆分轨道。");
            var count = 0;
            foreach (var director in UnityEngine.Object.FindObjectsOfType<PlayableDirector>())
            {
                if (!(director.playableAsset is TimelineAsset timeline)) continue;
                foreach (var route in timeline.GetOutputTracks().OfType<StagePerformanceTrack>().ToArray())
                {
                    if (route.animationTrack != null) continue;
                    director.Stop();
                    Create(director, route);
                    director.RebuildGraph();
                    count++;
                }
            }
            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            return "已拆分 " + count + " 条动画轨道；保留路线与原动画设置。请保存场景及 Timeline。";
        }
        public static AnimationTrack Create(PlayableDirector director, StagePerformanceTrack route)
        {
            if (route.animationTrack != null) return route.animationTrack;
            var timeline = director.playableAsset as TimelineAsset;
            var actor = director.GetGenericBinding(route) as StageActorDriver;
            if (timeline == null || actor == null || actor.animator == null) throw new InvalidOperationException("剧情缺少角色或 Animator 绑定。");
            var originals = route.GetClips().OrderBy(c => c.start).ToArray();
            foreach (var item in originals)
            {
                var data = (StagePerformanceClip)item.asset;
                if (data.animation == null || data.animation.legacy || data.animation.isHumanMotion) throw new InvalidOperationException(item.displayName + " 缺少有效 Generic 动画，未拆分。");
            }
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { timeline, route, director }, "拆分动画轨道");
            var animationTrack = timeline.CreateTrack<AnimationTrack>(null, actor.name + " · 动画（重叠片段做过渡）");
            Undo.RegisterCreatedObjectUndo(animationTrack, "拆分动画轨道");
            animationTrack.trackOffset = TrackOffset.ApplySceneOffsets;
            for (var i = 0; i < originals.Length; i++)
            {
                var source = originals[i];
                var data = (StagePerformanceClip)source.asset;
                var fade = i + 1 < originals.Length && Math.Abs(originals[i + 1].start - source.end) < .000001
                    ? Math.Min(source.duration * .5, Math.Min(originals[i + 1].duration * .5, Math.Max(0, ((StagePerformanceClip)originals[i + 1].asset).transitionDuration))) : 0;
                var clip = animationTrack.CreateClip<AnimationPlayableAsset>();
                var asset = (AnimationPlayableAsset)clip.asset;
                Undo.RegisterCreatedObjectUndo(asset, "拆分动画轨道");
                asset.clip = data.animation;
                asset.loop = data.loop ? AnimationPlayableAsset.LoopMode.On : AnimationPlayableAsset.LoopMode.Off;
                asset.applyFootIK = false;
                asset.removeStartOffset = false;
                clip.start = source.start; clip.duration = source.duration + fade;
                clip.displayName = data.animation.name;
                clip.timeScale = data.playbackRate;
                if (data.distanceSync || !UnitCurve(data.animationSpeed))
                {
                    asset.clip = Bake(data, source.duration, clip.duration, data.mode == StagePerformanceMode.Move ? data.route.Resolve(director).LengthForGround(data.UsesGround(data.route.Resolve(director))) : 0);
                    if (AssetDatabase.Contains(timeline)) AssetDatabase.AddObjectToAsset(asset.clip, timeline);
                    Undo.RegisterCreatedObjectUndo(asset.clip, "保留动画速度曲线");
                    clip.timeScale = 1;
                    asset.loop = AnimationPlayableAsset.LoopMode.Off;
                }
                EditorUtility.SetDirty(asset);
            }
            var created = animationTrack.GetClips().OrderBy(c => c.start).ToArray();
            for (var i = 1; i < created.Length; i++)
            {
                var overlap = Math.Max(0, created[i - 1].end - created[i].start);
                created[i - 1].blendOutDuration = overlap;
                created[i].blendInDuration = overlap;
            }
            route.animationTrack = animationTrack;
            route.name = actor.name + " · 路线与停留";
            director.SetGenericBinding(animationTrack, actor.animator);
            EditorUtility.SetDirty(animationTrack); EditorUtility.SetDirty(route);
            EditorUtility.SetDirty(timeline); EditorUtility.SetDirty(director);
            if (director.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            return animationTrack;
        }
        private static bool UnitCurve(AnimationCurve curve)
        {
            return curve != null && curve.keys.Length > 0 && curve.keys.All(k => Mathf.Approximately(k.value, 1) && Mathf.Approximately(k.inTangent, 0) && Mathf.Approximately(k.outTangent, 0));
        }
        private static AnimationClip Bake(StagePerformanceClip data, double duration, double outputDuration, double length)
        {
            var result = new AnimationClip { name = data.animation.name + "（保留速度曲线）", frameRate = 60 };
            var steps = Math.Max(1, (int)Math.Ceiling(outputDuration * 60));
            foreach (var binding in AnimationUtility.GetCurveBindings(data.animation))
            {
                var source = AnimationUtility.GetEditorCurve(data.animation, binding);
                var keys = new Keyframe[steps + 1];
                for (var i = 0; i <= steps; i++)
                {
                    var time = outputDuration * i / steps;
                    var sample = data.SourceTime(Math.Min(time, duration - .000001), duration, length);
                    keys[i] = new Keyframe((float)time, source.Evaluate((float)sample));
                }
                var curve = new AnimationCurve(keys);
                for (var i = 0; i < keys.Length; i++)
                { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear); }
                AnimationUtility.SetEditorCurve(result, binding, curve);
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(data.animation))
            {
                var source = AnimationUtility.GetObjectReferenceCurve(data.animation, binding);
                var keys = new List<ObjectReferenceKeyframe>();
                for (var i = 0; i <= steps; i++)
                {
                    var time = outputDuration * i / steps;
                    var sample = data.SourceTime(Math.Min(time, duration - .000001), duration, length);
                    var frame = source.LastOrDefault(k => k.time <= sample);
                    if (keys.Count == 0 || keys[keys.Count - 1].value != frame.value) keys.Add(new ObjectReferenceKeyframe { time = (float)time, value = frame.value });
                }
                AnimationUtility.SetObjectReferenceCurve(result, binding, keys.ToArray());
            }
            result.EnsureQuaternionContinuity();
            return result;
        }
    }
}
