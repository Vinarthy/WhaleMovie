using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace WhaleMovie.Art.SC04
{
    [DisallowMultipleComponent]
    public sealed class BoatWakeEffects : MonoBehaviour
    {
        [SerializeField, InspectorName("船模型")] private Transform boatVisual;
        [SerializeField, InspectorName("场景时间轴")] private PlayableDirector director;
        [SerializeField, InspectorName("航行轨道")] private TrackAsset sailingTrack;
        [SerializeField, InspectorName("水花预制件")] private GameObject splashPrefab;
        [SerializeField, InspectorName("水面世界高度")] private float waterSurfaceY;
        [SerializeField, Min(0f), InspectorName("最低航速")]
        private float minimumSpeed = 0.08f;
        [SerializeField, Min(0.1f), InspectorName("单次水花保留时间")]
        private float effectLifetime = 2.4f;
        [SerializeField, Min(0.05f), InspectorName("水花尺寸倍率"),
         Tooltip("1 为当前大小；0.5 为一半；2 为两倍。")]
        private float effectSize = 1f;
        [SerializeField, Range(0.3f, 1f), InspectorName("船首前后位置")]
        private float bowPosition = 0.72f;
        [SerializeField, Range(0.3f, 1f), InspectorName("船尾前后位置")]
        private float sternPosition = 0.72f;
        [SerializeField, Range(0.4f, 0.8f), InspectorName("左右离船距离")]
        private float sidePosition = 0.54f;

        private readonly float[] nextBurst = new float[4];
        private readonly List<WakeInstance> instances = new List<WakeInstance>();
        private MeshFilter[] meshes;
        private SkinnedMeshRenderer[] skinnedMeshes;
        private Renderer[] fallbackRenderers;
        private Transform poolRoot;
        private Vector3 lastPosition;
        private Vector3 travelForward;
        private double lastDirectorTime;
        private bool sailing;

        private sealed class WakeInstance
        {
            public GameObject gameObject;
            public ParticleSystem[] particles;
            public float availableAt;
        }

        private void Awake()
        {
            if (boatVisual == null) boatVisual = transform;
            meshes = boatVisual.GetComponentsInChildren<MeshFilter>(true);
            skinnedMeshes = boatVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            fallbackRenderers = boatVisual.GetComponentsInChildren<Renderer>(true);
            lastPosition = transform.position;
            travelForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        }

        private void LateUpdate()
        {
            if (director == null || sailingTrack == null || splashPrefab == null ||
                director.state != PlayState.Playing || !IsInsideSailingClip(director.time))
            {
                sailing = false;
                RetireFinished();
                return;
            }

            double directorTime = director.time;
            Vector3 position = transform.position;
            if (!sailing || directorTime < lastDirectorTime - 0.05 ||
                directorTime - lastDirectorTime > 0.5)
            {
                for (int i = 0; i < nextBurst.Length; i++)
                    nextBurst[i] = Time.time + i * 0.18f;
                lastPosition = position;
                sailing = true;
            }

            Vector3 movement = Vector3.ProjectOnPlane(position - lastPosition, Vector3.up);
            float speed = movement.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            if (movement.sqrMagnitude > 0.000001f && movement.magnitude < 3f)
                travelForward = movement.normalized;
            lastPosition = position;
            lastDirectorTime = directorTime;

            RetireFinished();
            for (int lane = 0; lane < nextBurst.Length; lane++)
            {
                if (Time.time < nextBurst[lane]) continue;
                bool bow = lane < 2;
                nextBurst[lane] = Time.time + (bow ? 0.82f : 1.18f) +
                                  Random.Range(-0.15f, 0.19f);
                if (speed >= minimumSpeed) SpawnWake(lane, bow);
            }
        }

        private bool IsInsideSailingClip(double time)
        {
            foreach (TimelineClip clip in sailingTrack.GetClips())
                if (time >= clip.start && time < clip.end) return true;
            return false;
        }

        private void SpawnWake(int lane, bool bow)
        {
            Vector3 forward = travelForward.sqrMagnitude > 0.01f ? travelForward : transform.forward;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            if (!MeasureFootprint(forward, side, out float minForward, out float maxForward,
                    out float minSide, out float maxSide)) return;

            float width = maxSide - minSide;
            Vector3 point = WakePoint(lane, forward, side,
                minForward, maxForward, minSide, maxSide);
            point += forward * Random.Range(-0.13f, 0.09f) + side * Random.Range(-0.07f, 0.07f);

            WakeInstance instance = Acquire();
            if (instance == null) return;
            instance.gameObject.transform.SetPositionAndRotation(
                point, Quaternion.AngleAxis(Random.Range(-12f, 12f), Vector3.up) *
                       Quaternion.LookRotation(forward, Vector3.up));
            float scale = Mathf.Clamp(width * (bow ? 0.18f : 0.16f), 0.24f, 0.7f);
            instance.gameObject.transform.localScale = Vector3.one * scale * effectSize *
                                                        Random.Range(0.85f, 1.13f);
            instance.gameObject.SetActive(true);
            foreach (ParticleSystem particles in instance.particles)
            {
                particles.Clear(false);
                // The bow throws spray; the stern leaves low foam and expanding rings.
                if (!bow && (particles.name.Contains("Crest") || particles.name.Contains("Droplets")))
                    continue;
                particles.Play(false);
            }
            instance.availableAt = Time.time + effectLifetime;
        }

        private Vector3 WakePoint(int lane, Vector3 forward, Vector3 side,
            float minForward, float maxForward, float minSide, float maxSide)
        {
            bool bow = lane < 2;
            float centerForward = (minForward + maxForward) * 0.5f;
            float centerSide = (minSide + maxSide) * 0.5f;
            float longitudinal = centerForward + (bow ? 1f : -1f) *
                (maxForward - minForward) * (bow ? bowPosition : sternPosition) * 0.5f;
            float lateral = centerSide + (lane % 2 == 0 ? -1f : 1f) *
                ((maxSide - minSide) * sidePosition * 0.5f + 0.12f);
            Vector3 point = transform.position + forward * longitudinal + side * lateral;
            point.y = waterSurfaceY + 0.03f;
            return point;
        }

        private WakeInstance Acquire()
        {
            foreach (WakeInstance instance in instances)
                if (Time.time >= instance.availableAt) return instance;
            if (instances.Count >= 16) return null;

            if (poolRoot == null)
            {
                GameObject holder = new GameObject("boat 航行水花（运行时）");
                poolRoot = holder.transform;
            }
            GameObject splash = Instantiate(splashPrefab, poolRoot);
            splash.SetActive(false);
            var created = new WakeInstance
            {
                gameObject = splash,
                particles = splash.GetComponentsInChildren<ParticleSystem>(true),
                availableAt = 0f
            };
            instances.Add(created);
            return created;
        }

        private void RetireFinished()
        {
            foreach (WakeInstance instance in instances)
                if (instance.gameObject.activeSelf && Time.time >= instance.availableAt)
                    instance.gameObject.SetActive(false);
        }

        private bool MeasureFootprint(Vector3 forward, Vector3 side,
            out float minForward, out float maxForward, out float minSide, out float maxSide)
        {
            minForward = minSide = float.PositiveInfinity;
            maxForward = maxSide = float.NegativeInfinity;
            bool found = false;
            foreach (MeshFilter mesh in meshes)
            {
                if (mesh == null || mesh.sharedMesh == null || !mesh.gameObject.activeInHierarchy) continue;
                AddBounds(mesh.sharedMesh.bounds, mesh.transform, forward, side,
                    ref minForward, ref maxForward, ref minSide, ref maxSide);
                found = true;
            }
            foreach (SkinnedMeshRenderer mesh in skinnedMeshes)
            {
                if (mesh == null || !mesh.gameObject.activeInHierarchy) continue;
                AddBounds(mesh.localBounds, mesh.transform, forward, side,
                    ref minForward, ref maxForward, ref minSide, ref maxSide);
                found = true;
            }
            if (!found)
            {
                foreach (Renderer renderer in fallbackRenderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    Bounds bounds = renderer.bounds;
                    AddBounds(bounds, null, forward, side,
                        ref minForward, ref maxForward, ref minSide, ref maxSide);
                    found = true;
                }
            }
            return found && maxForward - minForward > 0.1f && maxSide - minSide > 0.1f;
        }

        private void AddBounds(Bounds bounds, Transform owner, Vector3 forward, Vector3 side,
            ref float minForward, ref float maxForward, ref float minSide, ref float maxSide)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                if (owner != null) corner = owner.TransformPoint(corner);
                Vector3 offset = corner - transform.position;
                float f = Vector3.Dot(offset, forward);
                float s = Vector3.Dot(offset, side);
                minForward = Mathf.Min(minForward, f);
                maxForward = Mathf.Max(maxForward, f);
                minSide = Mathf.Min(minSide, s);
                maxSide = Mathf.Max(maxSide, s);
            }
        }

        private void OnDestroy()
        {
            if (poolRoot != null) Destroy(poolRoot.gameObject);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (boatVisual == null) return;
            if (meshes == null) meshes = boatVisual.GetComponentsInChildren<MeshFilter>(true);
            if (skinnedMeshes == null)
                skinnedMeshes = boatVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (fallbackRenderers == null)
                fallbackRenderers = boatVisual.GetComponentsInChildren<Renderer>(true);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) return;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            if (!MeasureFootprint(forward, side, out float minForward, out float maxForward,
                    out float minSide, out float maxSide)) return;
            float radius = Mathf.Clamp((maxSide - minSide) * 0.18f, 0.24f, 0.7f) * effectSize;
            for (int lane = 0; lane < 4; lane++)
            {
                Gizmos.color = lane < 2 ? new Color(0.45f, 0.95f, 1f) :
                                          new Color(0.7f, 0.85f, 1f);
                Gizmos.DrawWireSphere(WakePoint(lane, forward, side,
                    minForward, maxForward, minSide, maxSide), radius);
            }
        }
#endif
    }
}
