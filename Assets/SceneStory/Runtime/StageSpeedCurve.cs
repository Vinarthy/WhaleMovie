using System;
using UnityEngine;

namespace AIBuilder.CreatureStage
{
    public static class StageSpeedCurve
    {
        // 显示一整条路线时只积分一次；网格及末段公式与 Integral 完全相同。
        public static void FillProgress(AnimationCurve curve, double[] progress)
        {
            if (progress == null || progress.Length < 2) throw new ArgumentException("至少需要两个进度点。");
            const int steps = 256;
            var values = new double[steps + 1];
            var areas = new double[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                var value = curve == null || curve.length == 0 ? 1 : curve.Evaluate(i / (float)steps);
                if (!StageMotionPlan.IsFinite(value)) throw new InvalidOperationException("速度曲线含无效数值。");
                values[i] = Math.Max(0, value);
                if (i > 0) areas[i] = areas[i - 1] + (values[i - 1] + values[i]) * .5 / steps;
            }
            if (areas[steps] <= .000001) throw new InvalidOperationException("移动速度曲线不能全为零。");
            for (var i = 0; i < progress.Length; i++)
            {
                var time = i / (double)(progress.Length - 1);
                var cell = Math.Min(steps, (int)(time * steps));
                var area = areas[cell];
                if (cell < steps && time > cell / (double)steps)
                {
                    var value = curve == null || curve.length == 0 ? 1 : curve.Evaluate((float)time);
                    if (!StageMotionPlan.IsFinite(value)) throw new InvalidOperationException("速度曲线含无效数值。");
                    area += (values[cell] + Math.Max(0, value)) * .5 * (time - cell / (double)steps);
                }
                progress[i] = area / areas[steps];
            }
        }

        // 固定时间格积分，与播放帧率和 Seek 历史无关。负速度按零处理。
        public static double Integral(AnimationCurve curve, double until)
        {
            until = Math.Max(0, Math.Min(1, until));
            if (curve == null || curve.length == 0) return until;
            const int steps = 256;
            double sum = 0;
            for (var i = 0; i < steps && i / (double)steps < until; i++)
            {
                var a = i / (double)steps;
                var b = Math.Min(until, (i + 1) / (double)steps);
                var va = curve.Evaluate((float)a);
                var vb = curve.Evaluate((float)b);
                if (!StageMotionPlan.IsFinite(va) || !StageMotionPlan.IsFinite(vb))
                    throw new InvalidOperationException("速度曲线含无效数值。");
                sum += (Math.Max(0, va) + Math.Max(0, vb)) * 0.5 * (b - a);
            }
            return sum;
        }
    }
}
