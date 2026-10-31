using System;
using System.Collections.Generic;
using UnityEngine;

// Keep the existing class identity so scene references remain valid.
[DisallowMultipleComponent]
public class JellyfishMove : MonoBehaviour
{
    [Serializable]
    public class Aquarium
    {
        public Transform root;
        [Min(0.01f)] public float radius = 0.8f;
        [Min(0.01f)] public float height = 2.2f;
    }

    [Header("水族箱：子节点原点为缸体中心，尺寸为子节点局部单位")]
    [SerializeField] private Aquarium[] aquariums = new Aquarium[0];
    public Aquarium[] Aquariums => aquariums;
    public void Configure(Aquarium[] value) { aquariums = value; }

    [Header("边界余量（另自动计入水母模型大小）")]
    [Min(0f)] [SerializeField] private float wallMargin = 0.08f;
    [Min(0f)] [SerializeField] private float topBottomMargin = 0.1f;
    [Header("游动设置")]
    [SerializeField] private bool randomizeStartPositions = false;
    [Min(0.01f)] [SerializeField] private float minSpeed = 0.08f;
    [Min(0.01f)] [SerializeField] private float maxSpeed = 0.18f;
    [Min(0.01f)] [SerializeField] private float smoothTime = 1.5f;
    [Min(0.01f)] [SerializeField] private float reachDistance = 0.06f;
    [Min(0f)] [SerializeField] private float minTravelDistance = 0.3f;

    [Header("动画差异（仅用于水母数量较多的水族箱）")]
    [SerializeField] private bool staggerAnimations = true;
    [Min(2)] [SerializeField] private int staggerMinimumCount = 6;
    [Range(0f, 1f)] [SerializeField] private float animationPhaseSpread = 0.9f;
    [Range(0.1f, 2f)] [SerializeField] private float minAnimationSpeed = 0.94f;
    [Range(0.1f, 2f)] [SerializeField] private float maxAnimationSpeed = 1.06f;

    private class JellyfishState
    {
        public Transform root;
        public Aquarium tank;
        public Renderer[] renderers;
        public Vector3 target, velocity;
        public float speed, phase;
    }
    private readonly List<JellyfishState> jellyfish = new List<JellyfishState>();

    private void Start()
    {
        jellyfish.Clear();
        foreach (var tank in aquariums)
            if (tank != null && tank.root != null)
                foreach (Transform child in tank.root) RegisterJellyfish(child);
    }

    // Runtime additions must be direct children of one of the five tank roots.
    public void RegisterJellyfish(Transform root)
    {
        if (root == null || jellyfish.Exists(s => s.root == root)) return;
        Aquarium tank = Array.Find(aquariums, a => a != null && a.root == root.parent);
        if (tank == null) return;
        var state = new JellyfishState {
            root = root, tank = tank, renderers = root.GetComponentsInChildren<Renderer>(true),
            speed = UnityEngine.Random.Range(Mathf.Max(0.01f, Mathf.Min(minSpeed, maxSpeed)),
                Mathf.Max(0.01f, Mathf.Max(minSpeed, maxSpeed))),
            phase = UnityEngine.Random.value * Mathf.PI * 2f
        };
        StaggerAnimation(root, tank);
        Measure(state, out var center, out var extents);
        var start = randomizeStartPositions ? RandomPoint(state, extents) : KeepInside(state, center, extents);
        root.position += tank.root.TransformVector(start - center);
        PickTarget(state, start, extents);
        jellyfish.Add(state);
    }

    private void StaggerAnimation(Transform root, Aquarium tank)
    {
        if (!staggerAnimations || tank.root.childCount < Mathf.Max(2, staggerMinimumCount))
            return;

        int siblingIndex = root.GetSiblingIndex();
        int siblingCount = tank.root.childCount;
        float normalizedTime = Mathf.Repeat(
            siblingIndex / (float)siblingCount * Mathf.Clamp01(animationPhaseSpread), 1f);

        // A stable per-jellyfish speed variation prevents the animations from
        // gradually falling back into a visibly synchronized rhythm.
        uint hash = unchecked((uint)root.GetInstanceID() * 747796405u
            + (uint)siblingIndex * 2891336453u);
        float variation = (hash & 0x00ffffff) / 16777215f;
        float animationSpeed = Mathf.Lerp(
            Mathf.Min(minAnimationSpeed, maxAnimationSpeed),
            Mathf.Max(minAnimationSpeed, maxAnimationSpeed),
            variation);

        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
        {
            if (animator.runtimeAnimatorController == null || animator.layerCount == 0)
                continue;

            animator.Update(0f);
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != 0)
                animator.Play(state.fullPathHash, 0, normalizedTime);
            animator.speed = animationSpeed;
            animator.Update(0f);
        }
    }

    // Run after Animator so its bell/tentacle animation is preserved.
    private void LateUpdate() { Tick(Time.deltaTime); }
    private void Tick(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        for (int i = jellyfish.Count - 1; i >= 0; i--)
        {
            var s = jellyfish[i];
            if (s.root == null || s.tank.root == null || s.root.parent != s.tank.root)
            { jellyfish.RemoveAt(i); continue; }
            if (!s.root.gameObject.activeInHierarchy) continue;
            Measure(s, out var center, out var extents);
            s.target = KeepInside(s, s.target, extents);
            if (Vector3.Distance(center, s.target) <= Mathf.Max(0.01f, reachDistance))
                PickTarget(s, center, extents);
            s.phase += deltaTime * 1.6f;
            float speed = s.speed * (0.8f + 0.2f * Mathf.Sin(s.phase));
            var next = Vector3.SmoothDamp(center, s.target, ref s.velocity,
                Mathf.Max(0.01f, smoothTime), speed, deltaTime);
            var constrained = KeepInside(s, next, extents);
            if ((constrained - next).sqrMagnitude > 0.000001f) s.velocity = Vector3.zero;
            s.root.position += s.tank.root.TransformVector(constrained - center);
        }
    }

    private static void Measure(JellyfishState s, out Vector3 center, out Vector3 extents)
    {
        // Bounds include off-center model pivots and animated skinned meshes.
        Bounds bounds = new Bounds(s.tank.root.InverseTransformPoint(s.root.position), Vector3.zero);
        bool found = false;
        foreach (var renderer in s.renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Bounds b = renderer.bounds;
            for (int n = 0; n < 8; n++)
            {
                var corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((n & 1) == 0 ? -1 : 1, (n & 2) == 0 ? -1 : 1, (n & 4) == 0 ? -1 : 1));
                var p = s.tank.root.InverseTransformPoint(corner);
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                else bounds.Encapsulate(p);
            }
        }
        center = bounds.center;
        extents = bounds.extents;
    }

    private void Limits(JellyfishState s, Vector3 extents, out float radius, out float halfHeight)
    {
        radius = Mathf.Max(0f, s.tank.radius - Mathf.Max(0f, wallMargin) - new Vector2(extents.x, extents.z).magnitude);
        halfHeight = Mathf.Max(0f, s.tank.height * 0.5f - Mathf.Max(0f, topBottomMargin) - extents.y);
    }
    private Vector3 KeepInside(JellyfishState s, Vector3 p, Vector3 extents)
    {
        Limits(s, extents, out float radius, out float halfHeight);
        var disk = Vector2.ClampMagnitude(new Vector2(p.x, p.z), radius);
        return new Vector3(disk.x, Mathf.Clamp(p.y, -halfHeight, halfHeight), disk.y);
    }
    private Vector3 RandomPoint(JellyfishState s, Vector3 extents)
    {
        Limits(s, extents, out float radius, out float halfHeight);
        var disk = UnityEngine.Random.insideUnitCircle * radius;
        return new Vector3(disk.x, UnityEngine.Random.Range(-halfHeight, halfHeight), disk.y);
    }
    private void PickTarget(JellyfishState s, Vector3 center, Vector3 extents)
    {
        for (int i = 0; i < 10; i++)
        {
            s.target = RandomPoint(s, extents);
            if (Vector3.Distance(center, s.target) >= minTravelDistance) break;
        }
    }
    private void OnDrawGizmosSelected()
    {
        if (aquariums == null) return;
        var previous = Gizmos.matrix;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        foreach (var tank in aquariums)
        {
            if (tank == null || tank.root == null) continue;
            Gizmos.matrix = tank.root.localToWorldMatrix;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f, b = (i + 1) * Mathf.PI * 2f / 48f;
                var p = new Vector3(Mathf.Cos(a) * tank.radius, 0, Mathf.Sin(a) * tank.radius);
                var q = new Vector3(Mathf.Cos(b) * tank.radius, 0, Mathf.Sin(b) * tank.radius);
                var up = Vector3.up * tank.height * 0.5f;
                Gizmos.DrawLine(p + up, q + up); Gizmos.DrawLine(p - up, q - up);
                if (i % 12 == 0) Gizmos.DrawLine(p - up, p + up);
            }
            Gizmos.DrawSphere(Vector3.zero, 0.035f);
        }
        Gizmos.matrix = previous;
    }
}