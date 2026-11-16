using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WhaleMovie.Common
{
    /// <summary>
    /// 用时按现有静态管理器的层级摆放：管理器 → 分组 → 实例根 → LOD 子物体。
    /// 实例根需要 Animator 和 LODGroup；LOD2 要合批，需使用 MeshRenderer、共享网格与材质，材质开启 GPU Instancing，并把 LODGroup 的 Fade Mode 设为 None。
    ///LOD0、LOD1时保持原有animation动画，不合批
    ///一般用于少量分散的含动画的物体如海草摆动和非群聚小动物
    /// Animator、LODGroup 和 DynamicLODAnimation 均挂在实例根物体上。
    /// VR 中把管理器的 Target Camera 指向头显相机。
    ///后续可以使用顶点动画进行管理，在动画管理脚本进行顶点动画的暂停与播放
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DynamicGPUInstance : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 256;

        [Header("Camera")]
        [SerializeField] private Camera targetCamera;

        private readonly List<Instance> instances = new List<Instance>();
        private readonly Dictionary<
            (Mesh Mesh, Material Material, int Submesh, int Layer, uint RenderingLayerMask,
             ShadowCastingMode ShadowCastingMode, bool ReceiveShadows,
             LightProbeUsage LightProbeUsage, ReflectionProbeUsage ReflectionProbeUsage), DrawBatch> batches = new();
        private readonly Plane[] frustumPlanes = new Plane[6];

        public int ManagedInstanceCount => instances.Count;
        public int BatchableInstanceCount { get; private set; }

        private void OnEnable() => Rebuild();

        private void OnDisable() => RestoreAll();

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

            GeometryUtility.CalculateFrustumPlanes(targetCamera, frustumPlanes);

            foreach (DrawBatch batch in batches.Values)
                batch.Matrices.Clear();

            bool canInstance = SystemInfo.supportsInstancing;
            foreach (Instance instance in instances)
            {
                if (instance.LodGroup == null || !instance.LodGroup.gameObject.activeInHierarchy ||
                    instance.Animation == null)
                {
                    SetBatching(instance, false);
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
                SetBatching(instance, batchLod2);

                if (!pauseAnimation)
                    instance.Animation.SetPaused(false);

                if (!instance.Batching)
                    continue;

                Bounds bounds = GetLod2Bounds(instance);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                    continue;

                foreach (Part part in instance.Lod2Parts)
                {
                    Matrix4x4 matrix = part.Renderer.transform.localToWorldMatrix;
                    for (int submesh = 0; submesh < part.Mesh.subMeshCount; submesh++)
                    {
                        MeshRenderer renderer = part.Renderer;
                        var key = (part.Mesh, part.Materials[submesh], submesh, renderer.gameObject.layer,
                            renderer.renderingLayerMask, renderer.shadowCastingMode, renderer.receiveShadows,
                            renderer.lightProbeUsage, renderer.reflectionProbeUsage);
                        if (!batches.TryGetValue(key, out DrawBatch batch))
                        {
                            batch = new DrawBatch();
                            batches.Add(key, batch);
                        }
                        batch.Add(matrix, part.Renderer.bounds);
                    }
                }
            }

            foreach (var entry in batches)
            {
                DrawBatch batch = entry.Value;
                if (batch.Matrices.Count == 0)
                    continue;

                var key = entry.Key;
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
                    for (int start = 0; start < batch.Matrices.Count; start += MaxInstancesPerDraw)
                    {
                        int count = Mathf.Min(MaxInstancesPerDraw, batch.Matrices.Count - start);
                        Graphics.RenderMeshInstanced(renderParams, key.Mesh, key.Submesh, batch.Matrices, count, start);
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
            if (!root.TryGetComponent(out DynamicLODAnimation animation) ||
                !root.TryGetComponent(out LODGroup lodGroup) || animation.TargetAnimator == null)
                return false;

            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length < 3)
                return false;

            instance = new Instance
            {
                Animation = animation,
                LodGroup = lodGroup,
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
            instance.CanBatch = lodGroup.fadeMode == LODFadeMode.None &&
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

        private static void SetBatching(Instance instance, bool batching)
        {
            if (instance.Batching == batching)
                return;

            for (int i = 0; i < instance.GroupRenderers.Length; i++)
            {
                Renderer renderer = instance.GroupRenderers[i];
                if (batching)
                    instance.PreviousRendererStates[i] = renderer != null && renderer.enabled;
                if (renderer != null)
                    renderer.enabled = !batching && instance.PreviousRendererStates[i];
            }
            if (instance.LodGroup != null)
                instance.LodGroup.enabled = !batching;
            instance.Batching = batching;
        }

        private void RestoreAll()
        {
            foreach (Instance instance in instances)
            {
                SetBatching(instance, false);
                if (instance.Animation != null)
                    instance.Animation.Restore();
            }
            instances.Clear();
            batches.Clear();
            BatchableInstanceCount = 0;
        }

        private sealed class Instance
        {
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

        private sealed class DrawBatch
        {
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
            public Bounds Bounds;

            public void Add(Matrix4x4 matrix, Bounds bounds)
            {
                if (Matrices.Count == 0)
                    Bounds = bounds;
                else
                    Bounds.Encapsulate(bounds);
                Matrices.Add(matrix);
            }
        }
    }
}
