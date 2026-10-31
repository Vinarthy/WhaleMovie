using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.CreatureStage
{
    [TrackColor(0.12f, 0.65f, 0.47f)]
    [TrackClipType(typeof(StagePerformanceClip))]
    [TrackBindingType(typeof(StageActorDriver))]
    public sealed class StagePerformanceTrack : TrackAsset
    {
        public AnimationTrack animationTrack;
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var playable = ScriptPlayable<StagePerformanceMixer>.Create(graph, inputCount);
            var mixer = playable.GetBehaviour();
            mixer.director = graph.GetResolver() as PlayableDirector;
            mixer.track = this;
            mixer.clips = GetClips().OrderBy(c => c.start).ToArray();
            if (mixer.director != null) mixer.director.stopped += mixer.OnDirectorStopped;
            return playable;
        }

        // 每次按时间顺序解析，移动节点、删除或重排片段后无需复制停留点坐标。
        public static TimelineClip PlacementSource(TimelineClip clip)
        {
            var ordered = clip.GetParentTrack().GetClips().OrderBy(c => c.start).ToArray();
            var index = Array.IndexOf(ordered, clip);
            while (index > 0 && ordered[index].asset is StagePerformanceClip hold &&
                   hold.mode == StagePerformanceMode.Hold && hold.followPrevious) index--;
            return index >= 0 ? ordered[index] : clip;
        }

        public static void SamplePlacement(TimelineClip clip, PlayableDirector director, double progress,
            out Vector3 position, out Quaternion rotation, double localTime = -1)
        {
            var source = PlacementSource(clip);
            var data = (StagePerformanceClip)source.asset;
            if (data.mode == StagePerformanceMode.Move)
            {
                var route = data.route.Resolve(director);
                if (route == null) throw new InvalidOperationException(source.displayName + "：缺少路线。");
                route.SampleForClip(source, source == clip ? (localTime >= 0 ? localTime : progress * source.duration) : source.duration,
                    source == clip ? progress : 1, out position, out var forward);
                rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            else
            {
                var point = data.station.Resolve(director);
                if (point == null) throw new InvalidOperationException(source.displayName + "：缺少停留点。");
                position = point.position;
                rotation = point.rotation;
            }
        }

        public static Quaternion SampleFacing(TimelineClip clip, PlayableDirector director, double local)
            => BlendFacing(clip, director, local, RawFacing(clip, director, local));

        internal static Quaternion BlendFacing(TimelineClip clip, PlayableDirector director, double local, Quaternion rotation)
        {
            var data = (StagePerformanceClip)clip.asset;
            var fade = Math.Min(Math.Max(0, data.facingTransitionDuration), clip.duration);
            if (fade <= 0 || local >= fade) return rotation;
            var ordered = clip.GetParentTrack().GetClips().OrderBy(c => c.start).ToArray();
            var index = Array.IndexOf(ordered, clip);
            if (index <= 0 || Math.Abs(ordered[index - 1].end - clip.start) > .000001) return rotation;
            var prior = ordered[index - 1];
            var weight = Mathf.Clamp01((float)(local / fade));
            if (data.smoothFacingTransition) weight = Mathf.SmoothStep(0, 1, weight);
            return Quaternion.Slerp(RawFacing(prior, director, prior.duration), rotation, weight);
        }

        private static Quaternion RawFacing(TimelineClip clip, PlayableDirector director, double local)
        {
            var data = (StagePerformanceClip)clip.asset;
            var progress = data.mode == StagePerformanceMode.Move ? data.MovementProgress(local, clip.duration) : 1;
            SamplePlacement(clip, director, progress, out _, out var rotation, local);
            return rotation * Quaternion.Euler(0, data.FacingAt(local), 0);
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector collector)
        {
            var actor = director.GetGenericBinding(this) as StageActorDriver;
            if (actor == null) return;
            foreach (var item in actor.GetComponentsInChildren<Transform>(true))
            {
                foreach (var field in new[] { "m_LocalPosition", "m_LocalRotation", "m_LocalScale" })
                    foreach (var axis in new[] { ".x", ".y", ".z" }) collector.AddFromName<Transform>(item.gameObject, field + axis);
                collector.AddFromName<Transform>(item.gameObject, "m_LocalRotation.w");
            }
            base.GatherProperties(director, collector);
        }
    }

    public sealed class StagePerformanceMixer : PlayableBehaviour
    {
        public PlayableDirector director;
        public TimelineClip[] clips;
        public StagePerformanceTrack track;
        private StageActorDriver actor;
        private string lastError;

        public override void PrepareFrame(Playable playable, FrameData info)
        {
            if (track == null || track.animationTrack == null || director == null) return;
            try
            {
                foreach (var clip in track.animationTrack.GetClips())
                    if (clip.asset is StageGaitAnimationAsset gait)
                        gait.Synchronize(playable.GetGraph(), track, director, clip);
            }
            catch (Exception error)
            {
                if (lastError != error.Message) Debug.LogError("角色剧情：" + error.Message, director);
                lastError = error.Message;
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // 撤销或原生 Timeline 重排可能先于图重建，始终读取当前片段顺序。
            if (track != null) clips = track.GetClips().OrderBy(c => c.start).ToArray();
            var bound = playerData as StageActorDriver;
            if (actor != bound) { if (actor != null) actor.Release(this); actor = bound; }
            if (actor == null || director == null || clips == null || clips.Length == 0) return;
            try
            {
                for (var i = 0; i < clips.Length; i++)
                {
                    if (clips[i].duration <= 0 || !StageMotionPlan.IsFinite(clips[i].duration)) throw new InvalidOperationException("片段时长无效。");
                    if (i > 0 && clips[i].start < clips[i - 1].end - 0.000001) throw new InvalidOperationException("基础表演片段不能重叠，请先错开时间。");
                }
                var index = 0;
                for (var i = 1; i < clips.Length; i++) if (director.time >= clips[i].start) index = i;
                var clip = clips[index];
                var data = (StagePerformanceClip)clip.asset;
                var local = Math.Max(0, Math.Min(clip.duration, director.time - clip.start));
                Sample(data, clip, local, out var position, out var rotation, out var sourceTime);
                AnimationClip previous = null;
                double previousTime = 0;
                float weight = 1;
                var fade = Math.Min(Math.Max(0, data.transitionDuration), clip.duration * 0.5);
                if (index > 0 && Math.Abs(clips[index - 1].end - clip.start) < 0.000001 && fade > 0 && local < fade)
                {
                    var priorClip = clips[index - 1];
                    var prior = (StagePerformanceClip)priorClip.asset;
                    // 循环动作在边界的模运算会回到首帧，过渡应接住刚刚播放的末姿态。
                    var priorTime = prior.loop ? Math.Max(0, priorClip.duration - 0.000001) : priorClip.duration;
                    Sample(prior, priorClip, priorTime, out _, out var priorRotation, out previousTime);
                    previous = prior.animation;
                    weight = Mathf.SmoothStep(0, 1, (float)(local / fade));
                }
                if (track != null && track.animationTrack != null) actor.EvaluateMovement(this, position, rotation);
                else actor.Evaluate(this, data.animation, sourceTime, position, rotation, previous, previousTime, weight);
                lastError = null;
            }
            catch (Exception error)
            {
                actor.Release(this);
                if (lastError != error.Message) Debug.LogError("角色剧情：" + error.Message, actor);
                lastError = error.Message;
            }
        }

        private void Sample(StagePerformanceClip data, TimelineClip clip, double local,
            out Vector3 position, out Quaternion rotation, out double sourceTime)
        {
            double length = 0;
            var progress = data.mode == StagePerformanceMode.Move ? data.MovementProgress(local, clip.duration) : 1;
            StagePerformanceTrack.SamplePlacement(clip, director, progress, out position, out rotation, local);
            if (data.mode == StagePerformanceMode.Move) { var route = data.route.Resolve(director); length = route.LengthForGround(data.UsesGround(route)); }
            rotation = StagePerformanceTrack.BlendFacing(clip, director, local, rotation * Quaternion.Euler(0, data.FacingAt(local), 0));
            sourceTime = track != null && track.animationTrack != null ? 0 : data.SourceTime(local, clip.duration, length);
        }
        public void OnDirectorStopped(PlayableDirector source) { if (actor != null) actor.Release(this); }
        public override void OnGraphStop(Playable playable) { if (actor != null) actor.Release(this); }
        public override void OnPlayableDestroy(Playable playable)
        {
            if (actor != null) actor.Release(this);
            if (director != null) director.stopped -= OnDirectorStopped;
        }
    }
}
