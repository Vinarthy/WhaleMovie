using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.CreatureStage
{
    public enum StagePerformanceMode { Hold, Move }
    public enum StageGroundMode { InheritRoute, Ground, FreeSpace }

    public sealed class StagePerformanceClip : PlayableAsset, ITimelineClipAsset
    {
        public StagePerformanceMode mode;
        [Tooltip("片段独立设置：沿用路线默认、贴地或自由空间。") ]
        public StageGroundMode groundMode;
        public bool UsesGround(StageRoute route) => groundMode == StageGroundMode.Ground || groundMode == StageGroundMode.InheritRoute && route != null && route.snapToGround;
        public AnimationClip animation;
        public ExposedReference<StageRoute> route;
        public ExposedReference<Transform> station;
        [InspectorName("自动跟随上一段终点")]
        [Tooltip("停留向前寻找移动终点；前面没有移动时，连续停留以第一段停留点为基准。关闭后可独立摆放。") ]
        public bool followPrevious = true;
        public bool loop = true;
        [Min(0.01f)] public float playbackRate = 1;
        [Tooltip("横轴为片段归一化时间，纵轴为相对移动速度。积分归一化，保证按时到达终点。")]
        public AnimationCurve movementSpeed = AnimationCurve.Linear(0, 1, 1, 1);
        [Tooltip("横轴为片段归一化时间，纵轴乘以动画倍率。零表示暂停动画。")]
        public AnimationCurve animationSpeed = AnimationCurve.Linear(0, 1, 1, 1);
        [Min(0)] public float transitionDuration = 0.25f;
        [Tooltip("角色朝向相对路线切线或停留点朝向的附加角度。")]
        public float facingOffset;
        [Min(0)] public float groundTransitionDuration = 1;
        [Min(0)] public float facingTransitionDuration = 0.8f;
        public bool smoothFacingTransition = true;
        public bool animatedFacing;
        [Tooltip("横轴为片段内秒数，纵轴为相对路线方向的偏移角度。")]
        public AnimationCurve facingCurve = AnimationCurve.Linear(0, 0, 3, 0);
        public float FacingAt(double time) => animatedFacing && facingCurve != null && facingCurve.length > 0
            ? facingCurve.Evaluate((float)time) : facingOffset;
        [Tooltip("需先校准一个动画循环走多远；未勾选时使用手动动画倍率。")]
        public bool distanceSync;
        [Min(0.001f)] public float cycleDistance = 1;
        [TextArea] public string note;
        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => animation == null ? 3 : animation.length;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);

        public double MovementProgress(double localTime, double duration)
        {
            if (!StageMotionPlan.IsFinite(duration) || duration <= 0) throw new System.InvalidOperationException("片段时长无效。");
            var area = StageSpeedCurve.Integral(movementSpeed, 1);
            if (area <= 0.000001) throw new System.InvalidOperationException("移动速度曲线不能全为零。");
            return StageSpeedCurve.Integral(movementSpeed, localTime / duration) / area;
        }

        public double SourceTime(double localTime, double duration, double routeLength)
        {
            if (!StageMotionPlan.IsFinite(localTime) || !StageMotionPlan.IsFinite(duration) || duration <= 0)
                throw new System.InvalidOperationException("动画采样时间或片段时长无效。");
            if (animation == null || animation.length <= 0) throw new System.InvalidOperationException("片段缺少有效动画。");
            if (!StageMotionPlan.IsFinite(playbackRate) || playbackRate <= 0) throw new System.InvalidOperationException("动画倍率必须大于零。");
            var time = System.Math.Max(0, System.Math.Min(duration, localTime));
            var sample = duration * playbackRate * StageSpeedCurve.Integral(animationSpeed, time / duration);
            if (mode == StagePerformanceMode.Move && distanceSync)
            {
                if (!StageMotionPlan.IsFinite(cycleDistance) || cycleDistance <= 0 || duration <= 0)
                    throw new System.InvalidOperationException("距离同步需要有效的循环距离和片段时长。");
                sample = MovementProgress(time, duration) * routeLength / cycleDistance * animation.length;
            }
            return loop ? sample % animation.length : System.Math.Min(animation.length, sample);
        }
    }
}
