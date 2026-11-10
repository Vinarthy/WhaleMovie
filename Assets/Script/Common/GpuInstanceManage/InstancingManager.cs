using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WhaleMovie.Common
{
    /// <summary>
    /// 用于统一放进GPU实例化管理器中进行管理
    /// Hierarchy: Manager -> spatial/type group -> instance root -> mesh children.
    /// LODGroup supplies the mesh levels and screen-size thresholds for instanced rendering.
    /// Add one manager per independently culled area (or per object type).
    /// 作为一个总管理器放在Group上面，并给自己的子物体开启合批和lod
    /// 在每个实例根物体上加 LODGroup，把 LOD0、LOD1、LOD2 各自的 MeshRenderer 填进去。
    /// 各级网格的材质开启 GPU Instancing。VR 场景建议在 Manager 的 Target Camera 指定主相机，并确认 Cull Distance 不会过早把远处实例剔除。
    ///这个目前用于管理静态物体（主要为石头等静态自然物）
    ///行数有点多，不够精简，我需要将它精简
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InstancingManager : MonoBehaviour
    {
        private const int SafeMaxInstancesPerDraw = 256;

        [Header("Camera and culling")]
        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(0.01f)] private float cullDistance = 80f;
        [SerializeField] private bool frustumCulling = true;

        [Header("Groups")]
        [Tooltip("Expand group bounds to allow for shader-driven movement such as grass sway.")]
        [SerializeField, Min(0f)] private float boundsPadding = 1f;
        [Tooltip("Enable if instance transforms change at runtime. Static rocks/grass can leave this off.")]
        [SerializeField] private bool updateBoundsEveryFrame;
        [SerializeField, Range(1, SafeMaxInstancesPerDraw)] private int maxInstancesPerDraw = SafeMaxInstancesPerDraw;

        private readonly List<InstanceGroup> groups = new List<InstanceGroup>();
        private readonly Dictionary<BatchKey, DrawBatch> batches = new Dictionary<BatchKey, DrawBatch>();
        private readonly List<MeshRenderer> hiddenRenderers = new List<MeshRenderer>();
        private readonly List<LODGroup> disabledLodGroups = new List<LODGroup>();
        private readonly Plane[] frustumPlanes = new Plane[6];
        private readonly Matrix4x4[] drawBuffer = new Matrix4x4[SafeMaxInstancesPerDraw];

        public int ManagedInstanceCount { get; private set; }

        private void OnEnable()
        {
            if (Application.isPlaying)
                Rebuild();
        }

        private void OnDisable()
        {
            RestoreSourceRenderers();
        }

        private void OnValidate()
        {
            cullDistance = Mathf.Max(0.01f, cullDistance);
            boundsPadding = Mathf.Max(0f, boundsPadding);
            maxInstancesPerDraw = Mathf.Clamp(maxInstancesPerDraw, 1, SafeMaxInstancesPerDraw);
        }

        /// <summary>
        /// Call after adding/removing instance children, changing meshes/materials, or editing LODGroups.
        /// Moving existing instances only requires updateBoundsEveryFrame.
        /// </summary>
        public void Rebuild()
        {
            RestoreSourceRenderers();
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;

            if (!SystemInfo.supportsInstancing)
            {
                Debug.LogWarning("GPU instancing is not supported on this device. Source renderers remain active.", this);
                return;
            }

            if (targetCamera == null)
                targetCamera = Camera.main;
            if (targetCamera == null)
                return;

            int skippedCount = 0;
            string firstSkipReason = null;

            // Each direct child is one spatial/type group; its children are individual instances.
            foreach (Transform groupRoot in transform)
            {
                if (!groupRoot.gameObject.activeInHierarchy)
                    continue;

                var group = new InstanceGroup();
                foreach (Transform instanceRoot in groupRoot)
                {
                    if (!instanceRoot.gameObject.activeInHierarchy)
                        continue;

                    if (TryCreateInstance(instanceRoot, out InstanceRecord instance, out string reason))
                    {
                        group.Instances.Add(instance);
                        if (group.Instances.Count == 1)
                            group.Bounds = instance.Bounds;
                        else
                            group.Bounds.Encapsulate(instance.Bounds);
                    }
                    else if (reason != null)
                    {
                        skippedCount++;
                        if (firstSkipReason == null)
                            firstSkipReason = instanceRoot.name + ": " + reason;
                    }
                }

                if (group.Instances.Count == 0)
                    continue;

                group.Bounds.Expand(boundsPadding * 2f);
                groups.Add(group);
                foreach (InstanceRecord instance in group.Instances)
                {
                    ManagedInstanceCount++;
                    if (instance.LodGroup != null)
                    {
                        instance.LodGroup.enabled = false;
                        disabledLodGroups.Add(instance.LodGroup);
                    }

                    foreach (MeshRenderer renderer in instance.Renderers)
                    {
                        renderer.enabled = false;
                        hiddenRenderers.Add(renderer);
                    }
                }
            }

            if (skippedCount > 0)
                Debug.LogWarning($"InstancingManager left {skippedCount} instance(s) on their original renderers. First: {firstSkipReason}", this);
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera != null)
                    Rebuild();
                return;
            }

            if (groups.Count == 0)
                return;

            if (frustumCulling)
                GeometryUtility.CalculateFrustumPlanes(targetCamera, frustumPlanes);

            foreach (DrawBatch batch in batches.Values)
                batch.Clear();

            Vector3 cameraPosition = targetCamera.transform.position;
            float maxDistanceSquared = cullDistance * cullDistance;

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                InstanceGroup group = groups[groupIndex];
                if (updateBoundsEveryFrame)
                    UpdateGroupBounds(group);

                if (group.Bounds.SqrDistance(cameraPosition) > maxDistanceSquared ||
                    (frustumCulling && !GeometryUtility.TestPlanesAABB(frustumPlanes, group.Bounds)))
                    continue;

                foreach (InstanceRecord instance in group.Instances)
                {
                    if (instance.Root == null || !instance.Root.gameObject.activeInHierarchy)
                        continue;

                    if (instance.Bounds.SqrDistance(cameraPosition) > maxDistanceSquared ||
                        (frustumCulling && !GeometryUtility.TestPlanesAABB(frustumPlanes, instance.Bounds)))
                        continue;

                    int lodIndex = SelectLod(instance, cameraPosition);
                    if (lodIndex < 0)
                        continue;
                    foreach (SourcePart part in instance.LodParts[lodIndex])
                    {
                        Matrix4x4 matrix = part.Transform.localToWorldMatrix;
                        for (int submesh = 0; submesh < part.Mesh.subMeshCount; submesh++)
                        {
                            var key = new BatchKey(part, submesh);
                            if (!batches.TryGetValue(key, out DrawBatch batch))
                            {
                                batch = new DrawBatch(key);
                                batches.Add(key, batch);
                            }
                            batch.Add(matrix, instance.Bounds);
                        }
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
                    Debug.LogError("Instanced draw failed; restoring original renderers. " + exception.Message, this);
                    enabled = false;
                    return;
                }
            }
        }

        private int SelectLod(InstanceRecord instance, Vector3 cameraPosition)
        {
            if (instance.LodGroup == null)
                return 0;

            LODGroup lodGroup = instance.LodGroup;
            Vector3 scale = lodGroup.transform.lossyScale;
            float worldSize = lodGroup.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 referencePoint = lodGroup.transform.TransformPoint(lodGroup.localReferencePoint);
            float relativeHeight;
            if (targetCamera.orthographic)
                relativeHeight = worldSize / (2f * targetCamera.orthographicSize);
            else
            {
                float distance = Mathf.Max(Vector3.Distance(cameraPosition, referencePoint), 0.001f);
                relativeHeight = worldSize / (2f * distance * Mathf.Tan(targetCamera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            }
            relativeHeight *= QualitySettings.lodBias;

            for (int i = 0; i < instance.LodThresholds.Length; i++)
            {
                if (relativeHeight >= instance.LodThresholds[i])
                    return i;
            }
            return -1; // LODGroup's culled range.
        }

        private void UpdateGroupBounds(InstanceGroup group)
        {
            bool hasBounds = false;
            foreach (InstanceRecord instance in group.Instances)
            {
                if (instance.Root == null)
                    continue;

                bool hasInstanceBounds = false;
                Bounds instanceBounds = default;
                foreach (MeshRenderer renderer in instance.Renderers)
                {
                    if (renderer == null)
                        continue;
                    if (!hasInstanceBounds)
                    {
                        instanceBounds = renderer.bounds;
                        hasInstanceBounds = true;
                    }
                    else
                        instanceBounds.Encapsulate(renderer.bounds);
                }
                if (!hasInstanceBounds)
                    continue;

                instance.Bounds = instanceBounds;
                if (!hasBounds)
                {
                    group.Bounds = instanceBounds;
                    hasBounds = true;
                }
                else
                    group.Bounds.Encapsulate(instanceBounds);
            }
            if (hasBounds)
                group.Bounds.Expand(boundsPadding * 2f);
        }

        private bool TryCreateInstance(Transform root, out InstanceRecord instance, out string reason)
        {
            instance = null;
            reason = null;
            Renderer[] allRenderers = root.GetComponentsInChildren<Renderer>(true);
            var partsByRenderer = new Dictionary<MeshRenderer, SourcePart>();

            foreach (Renderer renderer in allRenderers)
            {
                if (!renderer.gameObject.activeInHierarchy || !renderer.enabled)
                    continue;
                if (!(renderer is MeshRenderer meshRenderer))
                {
                    reason = "contains a non-MeshRenderer (for example SkinnedMeshRenderer)";
                    return false;
                }
                if (meshRenderer.HasPropertyBlock() || meshRenderer.lightmapIndex >= 0 || meshRenderer.isPartOfStaticBatch)
                {
                    reason = "uses a property block, baked lightmap, or static batch";
                    return false;
                }

                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material[] materials = meshRenderer.sharedMaterials;
                if (mesh == null || materials.Length < mesh.subMeshCount)
                {
                    reason = "missing mesh or submesh material";
                    return false;
                }
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (materials[i] == null || !materials[i].enableInstancing || materials[i].renderQueue >= (int)RenderQueue.Transparent)
                    {
                        reason = "material is missing, transparent, or has GPU Instancing disabled";
                        return false;
                    }
                }
                partsByRenderer.Add(meshRenderer, new SourcePart(meshRenderer, mesh, materials));
            }

            if (partsByRenderer.Count == 0)
                return false;

            LODGroup[] lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            if (lodGroups.Length > 1)
            {
                reason = "contains multiple LODGroups; split it into separate instance roots";
                return false;
            }

            LODGroup lodGroup = lodGroups.Length == 1 ? lodGroups[0] : null;
            SourcePart[][] lodParts;
            float[] lodThresholds = null;
            if (lodGroup == null)
            {
                lodParts = new[] { new List<SourcePart>(partsByRenderer.Values).ToArray() };
            }
            else
            {
                if (!lodGroup.enabled || lodGroup.fadeMode != LODFadeMode.None)
                {
                    reason = "LODGroup must be enabled with Fade Mode set to None";
                    return false;
                }
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length == 0)
                {
                    reason = "LODGroup has no LOD levels";
                    return false;
                }

                lodParts = new SourcePart[lods.Length][];
                lodThresholds = new float[lods.Length];
                var assigned = new HashSet<MeshRenderer>();
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    lodThresholds[lod] = lods[lod].screenRelativeTransitionHeight;
                    var levelParts = new List<SourcePart>();
                    foreach (Renderer lodRenderer in lods[lod].renderers)
                    {
                        if (lodRenderer is MeshRenderer meshRenderer && partsByRenderer.TryGetValue(meshRenderer, out SourcePart part))
                        {
                            levelParts.Add(part);
                            assigned.Add(meshRenderer);
                        }
                    }
                    lodParts[lod] = levelParts.ToArray();
                }
                if (assigned.Count != partsByRenderer.Count)
                {
                    reason = "an active MeshRenderer is not listed in its LODGroup";
                    return false;
                }
            }

            instance = new InstanceRecord
            {
                Root = root,
                LodGroup = lodGroup,
                LodParts = lodParts,
                LodThresholds = lodThresholds,
                Renderers = new List<MeshRenderer>(partsByRenderer.Keys)
            };
            bool first = true;
            foreach (MeshRenderer renderer in instance.Renderers)
            {
                if (first)
                {
                    instance.Bounds = renderer.bounds;
                    first = false;
                }
                else
                    instance.Bounds.Encapsulate(renderer.bounds);
            }
            instance.Bounds.Expand(boundsPadding * 2f);
            return true;
        }

        private void RestoreSourceRenderers()
        {
            foreach (MeshRenderer renderer in hiddenRenderers)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }
            foreach (LODGroup lodGroup in disabledLodGroups)
            {
                if (lodGroup != null)
                    lodGroup.enabled = true;
            }
            hiddenRenderers.Clear();
            disabledLodGroups.Clear();
            groups.Clear();
            batches.Clear();
            ManagedInstanceCount = 0;
        }

        private sealed class InstanceGroup
        {
            public readonly List<InstanceRecord> Instances = new List<InstanceRecord>();
            public Bounds Bounds;
        }

        private sealed class InstanceRecord
        {
            public Transform Root;
            public LODGroup LodGroup;
            public SourcePart[][] LodParts;
            public float[] LodThresholds;
            public List<MeshRenderer> Renderers;
            public Bounds Bounds;
        }

        private sealed class SourcePart
        {
            public readonly MeshRenderer Renderer;
            public readonly Mesh Mesh;
            public readonly Material[] Materials;
            public readonly Transform Transform;

            public SourcePart(MeshRenderer renderer, Mesh mesh, Material[] materials)
            {
                Renderer = renderer;
                Mesh = mesh;
                Materials = materials;
                Transform = renderer.transform;
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

            public BatchKey(SourcePart part, int submesh)
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
                return Mesh == other.Mesh && Material == other.Material &&
                       Submesh == other.Submesh && Layer == other.Layer && RenderingLayerMask == other.RenderingLayerMask &&
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
