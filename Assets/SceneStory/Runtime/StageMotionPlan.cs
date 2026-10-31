using System;
using UnityEngine;

namespace AIBuilder.CreatureStage
{
    public readonly struct StageMotionFrame
    {
        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly double Distance;
        public readonly double UnwrappedPhase;
        public readonly double ClipTime;

        public StageMotionFrame(Vector3 position, Vector3 forward, double distance, double phase, double clipTime)
        {
            Position = position;
            Forward = forward;
            Distance = distance;
            UnwrappedPhase = phase;
            ClipTime = clipTime;
        }
    }

    // G1 基础实验：世界折线路径、匀速、线性周期距离。不宣称已实现起停、地形或脚锁。
    public sealed class StageMotionPlan
    {
        private readonly Vector3[] points;
        private readonly double[] lengths;
        private readonly double cycleDistance;
        private readonly double cycleDuration;
        private readonly double initialPhase;
        private readonly double clipIn;
        public double StartTime { get; }
        public double Duration { get; }
        public double Length => lengths[lengths.Length - 1];
        public double PlaybackRate => Length / Duration / cycleDistance * cycleDuration;

        private StageMotionPlan(Vector3[] points, double[] lengths, double startTime, double duration,
            double cycleDistance, double cycleDuration, double initialPhase, double clipIn)
        {
            this.points = points;
            this.lengths = lengths;
            StartTime = startTime;
            Duration = duration;
            this.cycleDistance = cycleDistance;
            this.cycleDuration = cycleDuration;
            this.initialPhase = initialPhase;
            this.clipIn = clipIn;
        }

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static bool TryCreate(Vector3[] worldPoints, double startTime, double duration,
            double cycleDistance, double cycleDuration, double minRate, double maxRate,
            double initialPhase, double clipIn, out StageMotionPlan plan, out string error)
        {
            plan = null;
            error = null;
            if (!IsFinite(startTime) || startTime < 0 || !IsFinite(duration) || duration <= 0 ||
                !IsFinite(startTime + duration) || startTime + duration <= startTime)
                error = "TIME_INVALID：开始时间非负，时长必须大于零，结束时间必须可表示。";
            else if (!IsFinite(cycleDistance) || cycleDistance <= 0 || !IsFinite(cycleDuration) || cycleDuration <= 0)
                error = "GAIT_INVALID：周期距离和周期时长必须大于零。";
            else if (!IsFinite(minRate) || !IsFinite(maxRate) || minRate <= 0 || maxRate < minRate)
                error = "RATE_INVALID：允许倍率范围无效。";
            else if (!IsFinite(initialPhase) || initialPhase < 0 || initialPhase >= 1 || !IsFinite(clipIn) || clipIn < 0)
                error = "PHASE_INVALID：初相位必须在 [0,1)，素材起点必须非负。";
            else if (worldPoints == null || worldPoints.Length < 2)
                error = "PATH_MISSING：移动实验至少需要两个世界路径点。";
            if (error != null) return false;

            var copy = (Vector3[])worldPoints.Clone();
            var arcLengths = new double[copy.Length];
            for (var i = 0; i < copy.Length; i++)
            {
                if (!IsFinite(copy[i].x) || !IsFinite(copy[i].y) || !IsFinite(copy[i].z))
                {
                    error = "PATH_INVALID：路径含有无效坐标。";
                    return false;
                }
                if (i == 0) continue;
                var segment = Vector3.Distance(copy[i - 1], copy[i]);
                if (!IsFinite(segment) || segment <= 0.000001f)
                {
                    error = "PATH_SEGMENT：相邻路径点重合或距离无效。";
                    return false;
                }
                arcLengths[i] = arcLengths[i - 1] + segment;
            }
            var length = arcLengths[arcLengths.Length - 1];
            var phaseEnd = initialPhase + length / cycleDistance;
            var rate = length / duration / cycleDistance * cycleDuration;
            if (!IsFinite(phaseEnd) || phaseEnd >= 4503599627370496d || !IsFinite(clipIn + cycleDuration))
            {
                error = "PRECISION_LIMIT：数据超出可稳定表示的相位或采样时间范围。";
                return false;
            }
            if (!IsFinite(rate) || rate < minRate - 1e-9 || rate > maxRate + 1e-9)
            {
                error = $"RATE_OUT_OF_RANGE：所需倍率 {rate:F3}，允许 {minRate:F3}–{maxRate:F3}。";
                return false;
            }
            plan = new StageMotionPlan(copy, arcLengths, startTime, duration, cycleDistance, cycleDuration, initialPhase, clipIn);
            return true;
        }

        public StageMotionFrame Evaluate(double absoluteTime)
        {
            if (!IsFinite(absoluteTime)) throw new ArgumentOutOfRangeException(nameof(absoluteTime), "时间必须有限。");
            var progress = Math.Max(0, Math.Min(1, (absoluteTime - StartTime) / Duration));
            var distance = Length * progress;
            var index = Array.BinarySearch(lengths, distance);
            index = index < 0 ? ~index : index + 1;
            index = Math.Max(1, Math.Min(points.Length - 1, index));
            var weight = (float)((distance - lengths[index - 1]) / (lengths[index] - lengths[index - 1]));
            var position = Vector3.LerpUnclamped(points[index - 1], points[index], weight);
            var forward = (points[index] - points[index - 1]).normalized;
            var phase = initialPhase + distance / cycleDistance;
            var clipTime = clipIn + (phase - Math.Floor(phase)) * cycleDuration;
            return new StageMotionFrame(position, forward, distance, phase, clipTime);
        }
    }
}
