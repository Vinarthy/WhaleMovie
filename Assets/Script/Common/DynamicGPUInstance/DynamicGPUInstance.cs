using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WhaleMovie.Common
{
    /// <summary>
    /// 用时按现有静态管理器的层级摆放：管理器 → 分组 → 实例根 → LOD 子物体。
    /// 实例根需要 Animator 和 LODGroup；LOD2 要合批，需使用 MeshRenderer、共享网格与材质，材质开启 GPU Instancing，并把 LODGroup 的 Fade Mode 设为 None。
    /// VR 中把管理器的 Target Camera 指向头显相机。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DynamicGPUInstance : MonoBehaviour
    {
        private const int SafeMaxInstancesPerDraw = 256;

        [Header("Camera and culling")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool frustumCulling = true;
        [SerializeField, Range(1, SafeMaxInstancesPerDraw)] private int maxInstancesPerDraw = SafeMaxInstancesPerDraw;

        private readonly List<Instance> instances = new List<Instance>();
        private readonly Dictionary<BatchKey, DrawBatch> batches = new Dictionary<BatchKey, DrawBatch>();
        private readonly Plane[] frustumPlanes = new Plane[6];
        private readonly Matrix4x4[] drawBuffer = new Matrix4x4[SafeMaxInstancesPerDraw];

        public int ManagedInstanceCount => instances.Count;
        public int BatchableInstanceCount { get; private set; }

        private void OnEnable()
        {
            if (Application.isPlaying)
                Rebuild();
        }

        private void OnDisable() => RestoreAll();

        private void OnValidate()
        {
            maxInstancesPerDraw = Mathf.Clamp(maxInstancesPerDraw, 1, SafeMaxInstancesPerDraw);
        }

        /// <summary>Call after changing the hierarchy, LODGroup, meshes, or materials.</summary>
        public void Rebuild()
        {
            RestoreAll();
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;

            if (targetCamera == null)
                targetCamera = Camera.main;

            foreach (Transform groupRoot in transform)
            {
                if (!groupRoot.gameObject.activeInHierarchy)
                    continue;

                foreach (Transform instanceRoot in groupRoot)
                {
                    if (!instanceRoot.gameObject.activeInHierarchy)
                        continue;

                    if (TryCreateInstance(instanceRoot, out Instance instance))
                    {
                        instances.Add(instance);
                        if (instance.CanBatch)
                            BatchableInstanceCount++;
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
            if (targetCamera == null)
                return;

            if (frustumCulling)
                GeometryUtility.CalculateFrustumPlanes(targetCamera, frustumPlanes);

            foreach (DrawBatch batch in batches.Values)
                batch.Clear();

            bool canInstance = SystemInfo.supportsInstancing;
            for (int i = 0; i < instances.Count; i++)
            {
                Instance instance = instances[i];
                if (instance.Root == null || !instance.Root.gameObject.activeInHierarchy ||
                    instance.LodGroup == null || instance.Animation == null)
                {
                    StopBatch(instance);
                    if (instance.Animation != null)
                        instance.Animation.Restore();
                    continue;
                }

                int lod = SelectLod(instance, targetCamera);
                bool pauseAnimation = instance.Animation.ManageAnimation && (lod >= 2 || lod < 0);
                if (pauseAnimation)
                    instance.Animation.SetPaused(true);

                bool batchLod2 = canInstance && pauseAnimation && lod == 2 && instance.CanBatch &&
                                 instance.Animation.IsPaused &&
                                 (instance.Batching || instance.LodGroup.enabled);
                if (batchLod2)
                    StartBatch(instance);
                else
                    StopBatch(instance);

                if (!pauseAnimation)
                    instance.Animation.SetPaused(false);

                if (!instance.Batching)
                    continue;

                Bounds bounds = GetLod2Bounds(instance);
                if (frustumCulling && !GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                    continue;

                foreach (Part part in instance.Lod2Parts)
                {
                    Matrix4x4 matrix = part.Renderer.transform.localToWorldMatrix;
                    for (int submesh = 0; submesh < part.Mesh.subMeshCount; submesh++)
                    {
                        var key = new BatchKey(part, submesh);
                        if (!batches.TryGetValue(key, out DrawBatch batch))
                        {
                            batch = new DrawBatch(key);
                            batches.Add(key, batch);
                        }
                        batch.Add(matrix, part.Renderer.bounds);
                    }
                }
            }

            foreach (DrawBatch batch in batches.Values)
            {
                if (batch.Matrices.Count == 0)
                    continue;

                BatchKey key = batch.Key;
                var renderParams = new RenderParams(key.Material)
                {
                    camera = targetCamera,
                    worldBounds = batch.Bounds,
                    layer = key.Layer,
                    renderingLayerMask = key.RenderingLayerMask,
                    shadowCastingMode = key.ShadowCastingMode,
                    receiveShadows = key.ReceiveShadows,
                    lightProbeUsage = key.LightProbeUsage,
                    reflectionProbeUsage = key.ReflectionProbeUsage
                };

                try
                {
                    for (int start = 0; start < batch.Matrices.Count; start += maxInstancesPerDraw)
                    {
                        int count = Mathf.Min(maxInstancesPerDraw, batch.Matrices.Count - start);
                        batch.Matrices.CopyTo(start, drawBuffer, 0, count);
                        Graphics.RenderMeshInstanced(renderParams, key.Mesh, key.Submesh, drawBuffer, count);
                    }
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogError("Dynamic instanced draw failed; restoring native LOD renderers. " + exception.Message, this);
                    enabled = false;
                    return;
                }
            }
        }

        private static int SelectLod(Instance instance, Camera camera)
        {
            LODGroup lodGroup = instance.LodGroup;
            Vector3 scale = lodGroup.transform.lossyScale;
            float worldSize = lodGroup.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 referencePoint = lodGroup.transform.TransformPoint(lodGroup.localReferencePoint);
            float relativeHeight;
            if (camera.orthographic)
                relativeHeight = worldSize / (2f * camera.orthographicSize);
            else
            {
                float distance = Mathf.Max(Vector3.Distance(camera.transform.position, referencePoint), 0.001f);
                relativeHeight = worldSize / (2f * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            }
            relativeHeight *= QualitySettings.lodBias;

            for (int i = 0; i < instance.Thresholds.Length; i++)
            {
                if (relativeHeight >= instance.Thresholds[i])
                    return i;
            }
            return -1;
        }

        private static bool TryCreateInstance(Transform root, out Instance instance)
        {
            instance = null;
            DynamicLODAnimation[] animations = root.GetComponentsInChildren<DynamicLODAnimation>(true);
            LODGroup[] lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            if (animations.Length != 1 || lodGroups.Length != 1)
                return false;

            Animator animator = animations[0].TargetAnimator;
            if (animator == null || !animator.transform.IsChildOf(root))
                return false;

            LOD[] lods = lodGroups[0].GetLODs();
            if (lods.Length < 3)
                return false;

            instance = new Instance
            {
                Root = root,
                Animation = animations[0],
                LodGroup = lodGroups[0],
                Thresholds = new float[lods.Length]
            };
            var groupRenderers = new HashSet<Renderer>();
            for (int i = 0; i < lods.Length; i++)
            {
                instance.Thresholds[i] = lods[i].screenRelativeTransitionHeight;
                foreach (Renderer renderer in lods[i].renderers)
                {
                    if (renderer != null)
                        groupRenderers.Add(renderer);
                }
            }
            instance.GroupRenderers = new List<Renderer>(groupRenderers).ToArray();
            instance.PreviousRendererStates = new bool[instance.GroupRenderers.Length];
            instance.CanBatch = lodGroups[0].fadeMode == LODFadeMode.None &&
                                TryCreateLod2Parts(lods[2], out instance.Lod2Parts);
            return true;
        }

        private static bool TryCreateLod2Parts(LOD lod2, out Part[] parts)
        {
            parts = null;
            if (lod2.renderers == null || lod2.renderers.Length == 0)
                return false;

            var result = new List<Part>();
            var seen = new HashSet<Renderer>();
            foreach (Renderer renderer in lod2.renderers)
            {
                if (!(renderer is MeshRenderer meshRenderer) || !seen.Add(renderer) ||
                    !meshRenderer.gameObject.activeInHierarchy || meshRenderer.HasPropertyBlock() ||
                    meshRenderer.lightmapIndex >= 0 || meshRenderer.isPartOfStaticBatch)
                    return false;

                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material[] materials = meshRenderer.sharedMaterials;
                if (mesh == null || mesh.subMeshCount == 0 || materials.Length < mesh.subMeshCount)
                    return false;

                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (materials[i] == null || !materials[i].enableInstancing ||
                        materials[i].renderQueue >= (int)RenderQueue.Transparent)
                        return false;
                }
                result.Add(new Part(meshRenderer, mesh, materials));
            }
            parts = result.ToArray();
            return true;
        }

        private static Bounds GetLod2Bounds(Instance instance)
        {
            Bounds bounds = instance.Lod2Parts[0].Renderer.bounds;
            for (int i = 1; i < instance.Lod2Parts.Length; i++)
                bounds.Encapsulate(instance.Lod2Parts[i].Renderer.bounds);
            return bounds;
        }

        private static void StartBatch(Instance instance)
        {
            if (instance.Batching)
                return;

            for (int i = 0; i < instance.GroupRenderers.Length; i++)
            {
                Renderer renderer = instance.GroupRenderers[i];
                instance.PreviousRendererStates[i] = renderer != null && renderer.enabled;
                if (renderer != null)
                    renderer.enabled = false;
            }
            instance.LodGroup.enabled = false;
            instance.Batching = true;
        }

        private static void StopBatch(Instance instance)
        {
            if (!instance.Batching)
                return;

            for (int i = 0; i < instance.GroupRenderers.Length; i++)
            {
                Renderer renderer = instance.GroupRenderers[i];
                if (renderer != null)
                    renderer.enabled = instance.PreviousRendererStates[i];
            }
            if (instance.LodGroup != null)
                instance.LodGroup.enabled = true;
            instance.Batching = false;
        }

        private void RestoreAll()
        {
            foreach (Instance instance in instances)
            {
                StopBatch(instance);
                if (instance.Animation != null)
                    instance.Animation.Restore();
            }
            instances.Clear();
            batches.Clear();
            BatchableInstanceCount = 0;
        }

        private sealed class Instance
        {
            public Transform Root;
            public DynamicLODAnimation Animation;
            public LODGroup LodGroup;
            public float[] Thresholds;
            public Renderer[] GroupRenderers;
            public bool[] PreviousRendererStates;
            public Part[] Lod2Parts;
            public bool CanBatch;
            public bool Batching;
        }

        private sealed class Part
        {
            public readonly MeshRenderer Renderer;
            public readonly Mesh Mesh;
            public readonly Material[] Materials;

            public Part(MeshRenderer renderer, Mesh mesh, Material[] materials)
            {
                Renderer = renderer;
                Mesh = mesh;
                Materials = materials;
            }
        }

        private struct BatchKey : IEquatable<BatchKey>
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly int Submesh;
            public readonly int Layer;
            public readonly uint RenderingLayerMask;
            public readonly ShadowCastingMode ShadowCastingMode;
            public readonly bool ReceiveShadows;
            public readonly LightProbeUsage LightProbeUsage;
            public readonly ReflectionProbeUsage ReflectionProbeUsage;

            public BatchKey(Part part, int submesh)
            {
                MeshRenderer renderer = part.Renderer;
                Mesh = part.Mesh;
                Material = part.Materials[submesh];
                Submesh = submesh;
                Layer = renderer.gameObject.layer;
                RenderingLayerMask = renderer.renderingLayerMask;
                ShadowCastingMode = renderer.shadowCastingMode;
                ReceiveShadows = renderer.receiveShadows;
                LightProbeUsage = renderer.lightProbeUsage;
                ReflectionProbeUsage = renderer.reflectionProbeUsage;
            }

            public bool Equals(BatchKey other)
            {
                return Mesh == other.Mesh && Material == other.Material && Submesh == other.Submesh &&
                       Layer == other.Layer && RenderingLayerMask == other.RenderingLayerMask &&
                       ShadowCastingMode == other.ShadowCastingMode && ReceiveShadows == other.ReceiveShadows &&
                       LightProbeUsage == other.LightProbeUsage && ReflectionProbeUsage == other.ReflectionProbeUsage;
            }

            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Mesh != null ? Mesh.GetHashCode() : 0;
                    hash = (hash * 397) ^ (Material != null ? Material.GetHashCode() : 0);
                    hash = (hash * 397) ^ Submesh;
                    hash = (hash * 397) ^ Layer;
                    hash = (hash * 397) ^ RenderingLayerMask.GetHashCode();
                    hash = (hash * 397) ^ (int)ShadowCastingMode;
                    hash = (hash * 397) ^ (ReceiveShadows ? 1 : 0);
                    hash = (hash * 397) ^ (int)LightProbeUsage;
                    return (hash * 397) ^ (int)ReflectionProbeUsage;
                }
            }
        }

        private sealed class DrawBatch
        {
            public readonly BatchKey Key;
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
            public Bounds Bounds;
            private bool hasBounds;

            public DrawBatch(BatchKey key) => Key = key;

            public void Clear()
            {
                Matrices.Clear();
                hasBounds = false;
            }

            public void Add(Matrix4x4 matrix, Bounds bounds)
            {
                Matrices.Add(matrix);
                if (!hasBounds)
                {
                    Bounds = bounds;
                    hasBounds = true;
                }
                else
                    Bounds.Encapsulate(bounds);
            }
        }
    }
}
