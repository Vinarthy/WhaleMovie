using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.CreatureStage
{
    // 保留原生动画图、混合权重和偏移，只在图准备阶段改采样时间。
    public sealed class StageGaitAnimationAsset : AnimationPlayableAsset
    {
        public bool synchronizeDistance = true;
        [Min(.001f)] public float cycleDistance = 1;
        private readonly List<Playable> instances = new List<Playable>();

        public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
        {
            var result = base.CreatePlayable(graph, go);
            instances.RemoveAll(p => !p.IsValid());
            if (result.IsValid()) instances.Add(result);
            return result;
        }

        public double SampleTime(StagePerformanceTrack routeTrack, PlayableDirector director, TimelineClip animationClip, double time)
        {
            if (clip == null || clip.length <= 0 || !StageMotionPlan.IsFinite(cycleDistance) || cycleDistance <= 0)
                throw new InvalidOperationException("步态同步需要有效动画和大于零的循环距离。");
            var end = Math.Max(animationClip.start, Math.Min(animationClip.end, time));
            var origin = animationClip;
            // 同一个步态连续剪成多段时保持落脚相位，避免每段都重回第一帧。
            var ordered = animationClip.GetParentTrack().GetClips().OrderBy(c => c.start).ToArray();
            var index = Array.IndexOf(ordered, animationClip);
            while (index > 0)
            {
                var prior = ordered[index - 1];
                if (prior.end < origin.start - .000001 || !(prior.asset is StageGaitAnimationAsset gait) ||
                    !gait.synchronizeDistance || gait.clip != clip || !Mathf.Approximately(gait.cycleDistance, cycleDistance) ||
                    Math.Abs(prior.clipIn - animationClip.clipIn) > .000001) break;
                origin = prior; index--;
            }
            double distance = 0;
            foreach (var move in routeTrack.GetClips())
            {
                if (!(move.asset is StagePerformanceClip data) || data.mode != StagePerformanceMode.Move) continue;
                var from = Math.Max(move.start, origin.start);
                var to = Math.Min(move.end, end);
                if (to <= from) continue;
                var route = data.route.Resolve(director);
                if (route == null) throw new InvalidOperationException(move.displayName + "：缺少路线。");
                distance += route.LengthForGround(data.UsesGround(route)) *
                    (data.MovementProgress(to - move.start, move.duration) - data.MovementProgress(from - move.start, move.duration));
            }
            // clipIn 是手动设置的起步相位；同步时不再乘手动 timeScale。
            return animationClip.clipIn + distance / cycleDistance * clip.length;
        }

        public void Synchronize(PlayableGraph graph, StagePerformanceTrack track, PlayableDirector director, TimelineClip timelineClip)
        {
            if (!synchronizeDistance) return;
            var time = SampleTime(track, director, timelineClip, director.time);
            foreach (var instance in instances)
                if (instance.IsValid() && instance.GetGraph().Equals(graph)) SetTimeRecursive(instance, time);
        }

        private static void SetTimeRecursive(Playable playable, double time)
        {
            playable.SetTime(time);
            for (var i = 0; i < playable.GetInputCount(); i++)
            {
                var input = playable.GetInput(i);
                if (input.IsValid()) SetTimeRecursive(input, time);
            }
        }
    }
}
