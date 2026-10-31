using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.CreatureStage
{
    [AddComponentMenu("AIBuilder/角色剧情/场景路线")]
    public sealed class StageRoute : MonoBehaviour
    {
        [InspectorName("按 Timeline 顺序自动连接起点")] public bool autoConnectPrevious = true;
        public Transform startAnchor;
        public Transform endAnchor;
        public Vector3[] points = { Vector3.zero, Vector3.forward };
        [InspectorName("圆滑曲线")] public bool curved = true;
        [Range(0, 1), InspectorName("圆滑度")] public float smoothness = 0.5f;
        [InspectorName("圆滑连接相邻移动片段")] public bool smoothConnections = true;
        [Tooltip("自动使用场景的地面绑定设置。关闭后可使用手动指定地面或悬空路线。")]
        [InspectorName("自动绑定场景地面")] public bool autoBindGround = true;
        [Tooltip("开启时沿地面采样；关闭时直接使用节点的世界 XYZ 坐标，可编辑飞行路线。") ]
        public bool snapToGround = true;
        public Collider[] groundSurfaces = Array.Empty<Collider>();
        public bool limitGroundProjection;
        [Min(0)] public float groundStepUp = .5f;
        [Min(.1f)] public float groundDrop = 100;
        [Range(0, 90)] public float groundSlope = 60;
        private Collider[] projectionCandidates;
        [NonSerialized] public Vector3? groundFailurePosition;
        private Vector3[] samples;
        private Vector3[] tangents;
        private double[] lengths;
        private int signature;
        [NonSerialized] public bool? previewGroundMode;
        private bool? samplingGroundMode;
        private bool EffectiveGround => samplingGroundMode ?? previewGroundMode ?? snapToGround;
        private bool refreshing;
        private Vector3? refreshStart;
        public double Length { get { Refresh(); return lengths[lengths.Length - 1]; } }
        public int PointCount => points == null ? 0 : points.Length;
        // 路线可在合并后仍位于原 Director 下，按 Timeline 实际绑定查找角色。
        public Color DisplayColor
        {
            get
            {
                StageActorDriver fallback = null;
                foreach (var director in FindObjectsOfType<PlayableDirector>(true))
                {
                    if (director.gameObject.scene != gameObject.scene || !(director.playableAsset is TimelineAsset timeline)) continue;
                    foreach (var track in timeline.GetOutputTracks().OfType<StagePerformanceTrack>())
                    {
                        if (track.mutedInHierarchy || !(director.GetGenericBinding(track) is StageActorDriver actor)) continue;
                        if (!track.GetClips().Any(c => c.asset is StagePerformanceClip data && data.mode == StagePerformanceMode.Move && data.route.Resolve(director) == this)) continue;
                        if (director.isActiveAndEnabled) return actor.routeColor;
                        if (fallback == null) fallback = actor;
                    }
                }
                return fallback != null ? fallback.routeColor : new Color(0.2f, 0.9f, 0.7f);
            }
        }
        public bool IsUsedByStory()
        {
            var hasTimeline = false;
            foreach (var director in FindObjectsOfType<PlayableDirector>(true))
            {
                if (director.gameObject.scene != gameObject.scene || !(director.playableAsset is TimelineAsset timeline)) continue;
                hasTimeline = true;
                foreach (var track in timeline.GetOutputTracks())
                {
                    if (!(track is StagePerformanceTrack) || track.mutedInHierarchy) continue;
                    foreach (var clip in track.GetClips())
                        if (clip.asset is StagePerformanceClip data && data.mode == StagePerformanceMode.Move && data.route.Resolve(director) == this)
                            return true;
                }
            }
            // 未接入剧情的独立路线仍可编辑；剧情场景只绘制还被片段引用的路线。
            return !hasTimeline;
        }
        public void Invalidate() { samples = null; lengths = null; }
        private void OnValidate() { Invalidate(); }

        public Vector3 WorldPoint(int index)
        {
            if (index == 0)
            {
                if (refreshing && refreshStart.HasValue) return refreshStart.Value;
                if (!refreshing && TryGetLinkedStart(out var linked)) return linked;
            }
            if (index == 0 && startAnchor != null) return startAnchor.position;
            if (index == PointCount - 1 && endAnchor != null) return endAnchor.position;
            return transform.TransformPoint(points[index]);
        }

        // 只读取前段终点，不改写场景锚点，重排、撤销和删除都按当前顺序求值。
        private IEnumerable<PlayableDirector> ConnectionDirectors()
        {
            var usages = FindObjectsOfType<PlayableDirector>(true).Where(d => d.gameObject.scene == gameObject.scene &&
                d.playableAsset is TimelineAsset timeline && timeline.GetOutputTracks().OfType<StagePerformanceTrack>().Any(t =>
                    !t.mutedInHierarchy && t.GetClips().Any(c => c.asset is StagePerformanceClip data &&
                        data.mode == StagePerformanceMode.Move && data.route.Resolve(d) == this))).ToArray();
            var active = usages.Where(d => d.isActiveAndEnabled).ToArray();
            // 合并后来源仍保留路线引用，但停用来源不能干扰主剧情的新顺序。
            return active.Length > 0 ? active : usages;
        }

        public bool TryGetLinkedStart(out Vector3 position)
        {
            position = default;
            if (!autoConnectPrevious) return false;
            var usages = 0;
            var linked = false;
            foreach (var director in ConnectionDirectors())
            {
                if (director.gameObject.scene != gameObject.scene || !(director.playableAsset is TimelineAsset timeline)) continue;
                foreach (var track in timeline.GetOutputTracks().OfType<StagePerformanceTrack>())
                {
                    if (track.mutedInHierarchy) continue;
                    var ordered = track.GetClips().OrderBy(c => c.start).ToArray();
                    for (var index = 0; index < ordered.Length; index++)
                    {
                        var current = (StagePerformanceClip)ordered[index].asset;
                        if (current.mode != StagePerformanceMode.Move || current.route.Resolve(director) != this) continue;
                        // 共享路线无法同时拥有不同的起点，保留显式路线数据。
                        if (++usages > 1) return false;
                        if (index == 0) continue;
                        var source = StagePerformanceTrack.PlacementSource(ordered[index - 1]);
                        if (source == ordered[index]) continue; // 开头停留本来就跟随本路线起点。
                        var data = (StagePerformanceClip)source.asset;
                        if (data.mode == StagePerformanceMode.Move)
                        {
                            var previous = data.route.Resolve(director);
                            if (previous == null || previous == this || previous.PointCount < 2) continue;
                            position = previous.WorldPoint(previous.PointCount - 1);
                            try
                            {
                                if (previous.limitGroundProjection && data.UsesGround(previous) && !previous.refreshing)
                                    previous.SampleForGround(1, true, out position, out _);
                                else position = previous.ProjectToGround(position, data.UsesGround(previous));
                            }
                            catch (InvalidOperationException) { /* 地面失效时保留原始点，允许继续修复路线。 */ }
                        }
                        else
                        {
                            var station = data.station.Resolve(director);
                            if (station == null) continue;
                            position = station.position;
                        }
                        linked = true;
                    }
                }
            }
            return linked;
        }

        private Vector3? NeighborPoint(bool atStart)
        {
            if (!curved || !smoothConnections || smoothness <= 0) return null;
            Vector3? found = null;
            foreach (var director in ConnectionDirectors())
            {
                if (director.gameObject.scene != gameObject.scene || !(director.playableAsset is TimelineAsset timeline)) continue;
                foreach (var track in timeline.GetOutputTracks())
                {
                    if (!(track is StagePerformanceTrack) || track.mutedInHierarchy) continue;
                    var ordered = track.GetClips().OrderBy(c => c.start).ToArray();
                    for (var index = 0; index < ordered.Length; index++)
                    {
                        var clip = ordered[index];
                        if (!(clip.asset is StagePerformanceClip current) || current.mode != StagePerformanceMode.Move || current.route.Resolve(director) != this) continue;
                        var step = atStart ? -1 : 1;
                        var boundary = clip;
                        for (var cursor = index + step; cursor >= 0 && cursor < ordered.Length; cursor += step)
                        {
                            var other = ordered[cursor];
                            if (Math.Abs(atStart ? other.end - boundary.start : other.start - boundary.end) > 0.000001) break;
                            var data = (StagePerformanceClip)other.asset;
                            // 自动停留只暂停时间，不切断空间路径的贝塞尔切线。
                            if (data.mode == StagePerformanceMode.Hold)
                            {
                                if (!data.followPrevious) break;
                                boundary = other;
                                continue;
                            }
                            var neighbor = data.route.Resolve(director);
                            if (neighbor == null || neighbor == this || neighbor.PointCount < 2 || !neighbor.curved || !neighbor.smoothConnections || neighbor.smoothness <= 0) break;
                            var joint = WorldPoint(atStart ? 0 : PointCount - 1);
                            var endpoint = neighbor.WorldPoint(atStart ? neighbor.PointCount - 1 : 0);
                            var gap = endpoint - joint; if (EffectiveGround && data.UsesGround(neighbor)) gap.y = 0;
                            if (gap.sqrMagnitude > 0.000001f) break;
                            var candidate = neighbor.WorldPoint(atStart ? neighbor.PointCount - 2 : 1);
                            if (found.HasValue && (found.Value - candidate).sqrMagnitude > 0.000001f) return null;
                            found = candidate;
                            break;
                        }
                    }
                }
            }
            return found;
        }

        private Vector3 HandleOffset(int index, Vector3? previous, Vector3? next)
        {
            var point = WorldPoint(index);
            var a = index > 0 ? WorldPoint(index - 1) : previous ?? point;
            var b = index < PointCount - 1 ? WorldPoint(index + 1) : next ?? point;
            var before = Vector3.Distance(a, point);
            var after = Vector3.Distance(point, b);
            var span = before < 0.00001f ? after : after < 0.00001f ? before : Mathf.Min(before, after);
            return (b - a).normalized * span * Mathf.Clamp01(smoothness) * 0.5f;
        }

        private void Refresh()
        {
            refreshStart = TryGetLinkedStart(out var linked) ? linked : (Vector3?)null;
            refreshing = true;
            try { RefreshCore(); }
            finally { refreshing = false; }
        }

        private void RefreshCore()
        {
            if (PointCount < 2) throw new InvalidOperationException("路线至少需要两个点。");
            var hash = (curved ? 1 : 0) ^ (EffectiveGround ? 2 : 0);
            unchecked { hash = hash * 397 ^ limitGroundProjection.GetHashCode(); hash = hash * 397 ^ groundStepUp.GetHashCode(); hash = hash * 397 ^ groundDrop.GetHashCode(); hash = hash * 397 ^ groundSlope.GetHashCode(); }
            var previous = NeighborPoint(true);
            var next = NeighborPoint(false);
            unchecked { hash = hash * 397 ^ smoothness.GetHashCode(); hash = hash * 397 ^ previous.GetHashCode(); hash = hash * 397 ^ next.GetHashCode(); }
            unchecked { for (var i = 0; i < PointCount; i++) hash = hash * 397 ^ WorldPoint(i).GetHashCode(); }
            unchecked
            {
                if (EffectiveGround && groundSurfaces != null) foreach (var ground in groundSurfaces)
                {
                    hash = hash * 397 ^ (ground != null ? ground.GetInstanceID() : 0);
                    if (ground == null) continue;
                    hash = hash * 397 ^ ground.transform.localToWorldMatrix.GetHashCode();
                    hash = hash * 397 ^ ground.bounds.GetHashCode();
                    if (ground is MeshCollider mesh && mesh.sharedMesh != null)
                        hash = hash * 397 ^ mesh.sharedMesh.GetInstanceID();
                }
            }
            if (samples != null && signature == hash) return;
            var list = new List<Vector3>();
            var directions = new List<Vector3>();
            for (var i = 0; i < PointCount - 1; i++)
            {
                var a = WorldPoint(Math.Max(0, i - 1));
                var b = WorldPoint(i);
                var c = WorldPoint(i + 1);
                var d = WorldPoint(Math.Min(PointCount - 1, i + 2));
                foreach (var point in new[] { a, b, c, d })
                    if (!StageMotionPlan.IsFinite(point.x) || !StageMotionPlan.IsFinite(point.y) || !StageMotionPlan.IsFinite(point.z))
                        throw new InvalidOperationException("路线含无效坐标。");
                var control1 = b + HandleOffset(i, previous, next);
                var control2 = c - HandleOffset(i + 1, previous, next);
                var steps = EffectiveGround && limitGroundProjection
                    ? Mathf.Clamp(Mathf.CeilToInt((Vector3.Distance(b, control1) + Vector3.Distance(control1, control2) + Vector3.Distance(control2, c)) / .25f), 32, 2048)
                    : 32;
                for (var j = 0; j < steps; j++)
                {
                    var t = (float)j / steps;
                    var u = 1 - t;
                    var useCurve = curved && smoothness > 0;
                    list.Add(useCurve ? u * u * u * b + 3 * u * u * t * control1 + 3 * u * t * t * control2 + t * t * t * c : Vector3.Lerp(b, c, t));
                    var direction = useCurve ? 3 * u * u * (control1 - b) + 6 * u * t * (control2 - control1) + 3 * t * t * (c - control2) : c - b;
                    direction.y = 0;
                    directions.Add(direction.sqrMagnitude > 0.000001f ? direction.normalized : (c - b).normalized);
                }
            }
            list.Add(WorldPoint(PointCount - 1));
            var lastDirection = curved && smoothness > 0 ? HandleOffset(PointCount - 1, previous, next) : WorldPoint(PointCount - 1) - WorldPoint(PointCount - 2);
            lastDirection.y = 0;
            directions.Add(lastDirection.sqrMagnitude > 0.000001f ? lastDirection.normalized : directions[directions.Count - 1]);
            // 失败时不能继续复用上次地面的采样缓存。
            samples = null;
            var area = new Bounds(list[0], Vector3.zero);
            foreach (var point in list) area.Encapsulate(point);
            // 每次路线缓存重建只筛一次附近碰撞体，不让每个采样点遍历全场景 Mesh。
            projectionCandidates = groundSurfaces == null ? Array.Empty<Collider>() : groundSurfaces.Where(c => c != null &&
                c.bounds.max.x >= area.min.x && c.bounds.min.x <= area.max.x && c.bounds.max.z >= area.min.z && c.bounds.min.z <= area.max.z).ToArray();
            try
            {
                var projected = new Vector3[list.Count];
                for (var i = 0; i < list.Count; i++)
                {
                    var point = list[i];
                    if (i == 0 || !EffectiveGround || !limitGroundProjection)
                        projected[i] = ProjectToGround(point);
                    else
                    {
                        // 从本次完整采样的前一点地面高度续走，不能继续以空中直线高度限制整段坡道。
                        var distance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(projected[i - 1].x, projected[i - 1].z));
                        point.y = projected[i - 1].y;
                        var rise = distance * Mathf.Tan(Mathf.Min(groundSlope, 89) * Mathf.Deg2Rad) + .001f;
                        projected[i] = ProjectGround(point, true, Mathf.Max(groundStepUp, rise), projected[i - 1]);
                    }
                }
                samples = projected;
            }
            finally { projectionCandidates = null; }
            tangents = directions.ToArray();
            lengths = new double[samples.Length];
            for (var i = 1; i < samples.Length; i++) lengths[i] = lengths[i - 1] + Vector3.Distance(samples[i - 1], samples[i]);
            if (lengths[lengths.Length - 1] <= 0.00001) { samples = null; throw new InvalidOperationException("路线长度为零，请移动终点。"); }
            signature = hash;
        }

        public Vector3 ProjectToGround(Vector3 point) => ProjectToGround(point, EffectiveGround);
        public Vector3 ProjectToGround(Vector3 point, bool enabled)
            => ProjectGround(point, enabled, null, null);

        private Vector3 ProjectGround(Vector3 point, bool enabled, float? rise, Vector3? previousPoint)
        {
            if (!enabled || groundSurfaces == null || groundSurfaces.Length == 0) return point;
            var up = limitGroundProjection ? Mathf.Max(.001f, rise ?? groundStepUp) : 100;
            var down = limitGroundProjection ? Mathf.Max(.1f, groundDrop) : 100;
            var ray = new Ray(point + Vector3.up * up, Vector3.down);
            var nearest = float.PositiveInfinity;
            var foundBlockingRise = false;
            var result = point;
            foreach (var ground in projectionCandidates ?? groundSurfaces)
            {
                if (ground == null || !ground.enabled || !ground.gameObject.activeInHierarchy) continue;
                var bounds = ground.bounds;
                if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) continue;
                if (!ground.Raycast(ray, out var hit, up + down)) continue;
                if (limitGroundProjection && Vector3.Angle(hit.normal, Vector3.up) > groundSlope) continue;
                // 优先保持当前地面层。只有脚前方确实遇到该物体，才跨上它的表面；头顶平面不会挡住脚前方。
                var blockingRise = false;
                if (limitGroundProjection && previousPoint.HasValue && hit.point.y > point.y + .001f)
                {
                    var origin = previousPoint.Value + Vector3.up * .002f;
                    var step = new Vector3(point.x - origin.x, 0, point.z - origin.z);
                    if (step.sqrMagnitude > .00000001f)
                        blockingRise = ground.Raycast(new Ray(origin, step.normalized), out _, step.magnitude + .001f);
                }
                var score = limitGroundProjection ? Mathf.Abs(hit.point.y - point.y) : hit.distance;
                if (blockingRise && !foundBlockingRise || blockingRise == foundBlockingRise && score < nearest)
                { nearest = score; result = hit.point; foundBlockingRise = blockingRise; }
            }
            if (float.IsPositiveInfinity(nearest))
            {
                groundFailurePosition = point;
                throw new InvalidOperationException("路线「" + name + "」在 " + point.ToString("F3") + " 未命中有效地面：请检查地面绑定、节点高度、坡度及升降限制。");
            }
            groundFailurePosition = null;
            return result;
        }

        public bool HasFreeSpaceUsage()
        {
            foreach (var director in FindObjectsOfType<PlayableDirector>(true))
            {
                if (director.gameObject.scene != gameObject.scene || !(director.playableAsset is TimelineAsset timeline)) continue;
                foreach (var track in timeline.GetOutputTracks().OfType<StagePerformanceTrack>())
                foreach (var clip in track.GetClips())
                    if (clip.asset is StagePerformanceClip data && data.mode == StagePerformanceMode.Move && data.route.Resolve(director) == this && !data.UsesGround(this)) return true;
            }
            return false;
        }
        public double LengthForGround(bool ground)
        {
            var old = samplingGroundMode; samplingGroundMode = ground;
            try { return Length; }
            finally { samplingGroundMode = old; }
        }
        public void SampleForGround(double progress, bool ground, out Vector3 position, out Vector3 forward)
        {
            var old = samplingGroundMode; samplingGroundMode = ground;
            try { Sample(progress, out position, out forward); }
            finally { samplingGroundMode = old; }
        }

        public void SampleForClip(TimelineClip clip, double localTime, double progress, out Vector3 position, out Vector3 forward)
        {
            var old = samplingGroundMode;
            samplingGroundMode = ((StagePerformanceClip)clip.asset).UsesGround(this);
            try
            {
                Sample(progress, out position, out forward);
                position.y += LandingOffset(clip, localTime, LandingHeight());
            }
            finally { samplingGroundMode = old; }
        }

        private float LandingHeight() => EffectiveGround && refreshStart.HasValue
            ? refreshStart.Value.y - samples[0].y : 0;

        private static float LandingOffset(TimelineClip clip, double localTime, float height)
        {
            var duration = Math.Min(clip.duration, Math.Max(0, ((StagePerformanceClip)clip.asset).groundTransitionDuration));
            return duration > 0 ? height * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)(localTime / duration)))) : 0;
        }

        private TimelineClip DisplayClip()
        {
            TimelineClip result = null;
            foreach (var director in ConnectionDirectors())
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<StagePerformanceTrack>().Where(t => !t.muted))
            foreach (var clip in track.GetClips())
                if (clip.asset is StagePerformanceClip data && data.mode == StagePerformanceMode.Move && data.route.Resolve(director) == this)
                {
                    if (result != null) return null;
                    result = clip;
                }
            return result;
        }

        public void Sample(double normalizedDistance, out Vector3 position, out Vector3 forward)
        {
            if (!StageMotionPlan.IsFinite(normalizedDistance)) throw new ArgumentOutOfRangeException(nameof(normalizedDistance));
            Refresh();
            SampleCached(normalizedDistance, out position, out forward);
        }

        // 一条显示线只更新一次路线关系和地面缓存，避免每个显示点重复扫描全场景。
        public void SamplePolyline(Vector3[] positions)
        {
            if (positions == null || positions.Length < 2) throw new ArgumentException("路线显示至少需要两个采样点。", nameof(positions));
            var clip = DisplayClip();
            var old = samplingGroundMode;
            if (clip != null) samplingGroundMode = ((StagePerformanceClip)clip.asset).UsesGround(this);
            try
            {
                Refresh();
                var height = LandingHeight();
                double[] progressSamples = null;
                if (clip != null)
                {
                    progressSamples = new double[positions.Length];
                    StageSpeedCurve.FillProgress(((StagePerformanceClip)clip.asset).movementSpeed, progressSamples);
                }
                for (var i = 0; i < positions.Length; i++)
                {
                    var phase = i / (double)(positions.Length - 1);
                    var time = clip != null ? phase * clip.duration : 0;
                    var progress = progressSamples != null ? progressSamples[i] : phase;
                    SampleCached(progress, out positions[i], out _);
                    if (clip != null) positions[i].y += LandingOffset(clip, time, height);
                }
            }
            finally { samplingGroundMode = old; }
        }

        private void SampleCached(double normalizedDistance, out Vector3 position, out Vector3 forward)
        {
            var distance = Math.Max(0, Math.Min(1, normalizedDistance)) * lengths[lengths.Length - 1];
            var index = Array.BinarySearch(lengths, distance);
            index = Mathf.Clamp(index < 0 ? ~index : index + 1, 1, samples.Length - 1);
            while (index < samples.Length - 1 && lengths[index] - lengths[index - 1] <= 0.000001) index++;
            var segment = lengths[index] - lengths[index - 1];
            var blend = segment > 0 ? (float)((distance - lengths[index - 1]) / segment) : 0;
            position = Vector3.LerpUnclamped(samples[index - 1], samples[index], blend);
            forward = curved && smoothness > 0 ? Vector3.Lerp(tangents[index - 1], tangents[index], blend) : samples[index] - samples[index - 1];
            forward.y = 0;
            if (forward.sqrMagnitude < 0.000001f) forward = Vector3.forward;
            forward.Normalize();
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            // Editor 的 StageRouteOverlay 已统一绘制，避免每次重绘再采样一遍。
            return;
#else
            if (Application.isPlaying) return;
            if (PointCount < 2 || !IsUsedByStory()) return;
            try
            {
                var display = new Vector3[65];
                SamplePolyline(display);
                Gizmos.color = DisplayColor;
                for (var i = 1; i < display.Length; i++) Gizmos.DrawLine(display[i - 1], display[i]);
                Gizmos.DrawSphere(display[0], 0.055f);
                Gizmos.DrawSphere(display[display.Length - 1], 0.055f);
            }
            catch (InvalidOperationException) { }
#endif
        }
    }
}
