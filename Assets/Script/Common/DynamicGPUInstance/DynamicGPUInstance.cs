using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WhaleMovie.Common
{
    /// <summary>
    /// Shader vertex animation batches at every valid LOD; skeletal animation batches only at paused LOD2.
    /// 实例根需要 LODGroup 和 DynamicLODAnimation；骨骼动画另需 Animator；LOD2 要合批，需使用 MeshRenderer、共享网格与材质，材质开启 GPU Instancing，并把 LODGroup 的 Fade Mode 设为 None。
    ///LOD0、LOD1时保持原有animation动画，不合批
    ///一般用于少量分散的含动画的物体如海草摆动和非群聚小动物
    /// LODGroup、DynamicLODAnimation 及可选的 Animator 均挂在实例根物体上。
    /// VR 中把管理器的 Target Camera 指向头显相机。
    ///关于对顶点动画的管理：直接修改着色器
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

            foreach (DynamicLODAnimation animation in GetComponentsInChildren<DynamicLODAnimation>())
            {
                if (TryCreateInstance(animation.transform, out Instance instance))
                {
                    instances.Add(instance);
                    if (instance.CanBatch)
                        BatchableInstanceCount++;
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
                if (pauseAnimation && !instance.Animation.IsPaused)
                    instance.Animation.SetPaused(true);

                if (!pauseAnimation && instance.Animation.NeedsRestore)
                    instance.Animation.SetPaused(false);

                Part[] parts = lod >= 0 ? instance.LodParts[lod] : null;
                bool batchCurrentLod = canInstance && parts != null &&
                    (instance.Animation.IsVertexAnimation ||
                     (lod == 2 && pauseAnimation && instance.Animation.IsPaused)) &&
                    (instance.Batching || instance.LodGroup.enabled);
                SetBatching(instance, batchCurrentLod);

                if (!instance.Batching)
                    continue;

                Bounds bounds = GetPartsBounds(parts);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                    continue;

                foreach (Part part in parts)
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
                !root.TryGetComponent(out LODGroup lodGroup) ||
                (!animation.IsVertexAnimation && animation.TargetAnimator == null))
                return false;

            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length < 3)
                return false;

            instance = new Instance
            {
                Animation = animation,
                LodGroup = lodGroup,
                Thresholds = new float[lods.Length],
                LodParts = new Part[lods.Length][]
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
            if (lodGroup.fadeMode == LODFadeMode.None)
            {
                for (int i = 0; i < lods.Length; i++)
                {
                    if ((animation.IsVertexAnimation || i == 2) &&
                        TryCreateParts(lods[i], out instance.LodParts[i]))
                        instance.CanBatch = true;
                }
            }
            if (instance.CanBatch)
                animation.PauseStateChanged += instance.RefreshMaterials;
            return true;
        }

        private static bool TryCreateParts(LOD lod, out Part[] parts)
        {
            parts = null;
            if (lod.renderers == null || lod.renderers.Length == 0)
                return false;

            var result = new List<Part>();
            var seen = new HashSet<Renderer>();
            foreach (Renderer renderer in lod.renderers)
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

        private static Bounds GetPartsBounds(Part[] parts)
        {
            Bounds bounds = parts[0].Renderer.bounds;
            for (int i = 1; i < parts.Length; i++)
                bounds.Encapsulate(parts[i].Renderer.bounds);
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
                {
                    instance.Animation.PauseStateChanged -= instance.RefreshMaterials;
                    instance.Animation.Restore();
                }
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
            public Part[][] LodParts;
            public bool CanBatch;
            public bool Batching;

            public void RefreshMaterials()
            {
                foreach (Part[] parts in LodParts)
                {
                    if (parts == null)
                        continue;
                    foreach (Part part in parts)
                        if (part.Renderer != null)
                            part.Materials = part.Renderer.sharedMaterials;
                }
            }
        }

        private sealed class Part
        {
            public readonly MeshRenderer Renderer;
            public readonly Mesh Mesh;
            public Material[] Materials;

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
