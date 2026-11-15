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
    /// Each direct child groups static instances; culling is performed per instance.
    /// 作为一个总管理器放在Group上面，并给自己的子物体开启合批和lod
    /// 在每个实例根物体上加 LODGroup，把 LOD0、LOD1、LOD2 各自的 MeshRenderer 填进去。
    ///这个目前用于管理静态物体（主要为石头等静态自然物）
    ///行数有点多，不够精简，我需要将它精简
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InstancingManager : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 256;

        [Header("Camera")]
        [SerializeField] private Camera targetCamera;

        private readonly List<InstanceRecord> instances = new List<InstanceRecord>();
        private readonly Dictionary<(Mesh mesh, Material material, int submesh), List<Matrix4x4>> batches =
            new Dictionary<(Mesh mesh, Material material, int submesh), List<Matrix4x4>>();
        private readonly Plane[] frustumPlanes = new Plane[6];

        private void OnEnable() => Build();

        private void OnDisable() => RestoreSourceRenderers();

        private void Build()
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

            // Each direct child groups instances; only the instance roots contain meshes.
            foreach (Transform groupRoot in transform)
            {
                foreach (Transform instanceRoot in groupRoot)
                {
                    if (!TryCreateInstance(instanceRoot, out InstanceRecord instance))
                        continue;

                    instances.Add(instance);
                    instance.LodGroup.enabled = false;

                    foreach (MeshRenderer renderer in instance.Renderers)
                    {
                        renderer.enabled = false;
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera != null)
                    Build();
                return;
            }

            if (instances.Count == 0)
                return;

            GeometryUtility.CalculateFrustumPlanes(targetCamera, frustumPlanes);

            foreach (List<Matrix4x4> matrices in batches.Values)
                matrices.Clear();

            Vector3 cameraPosition = targetCamera.transform.position;

            foreach (InstanceRecord instance in instances)
            {
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, instance.Bounds))
                    continue;

                int lodIndex = SelectLod(instance, cameraPosition);
                if (lodIndex < 0)
                    continue;

                foreach (SourcePart part in instance.LodParts[lodIndex])
                {
                    for (int submesh = 0; submesh < part.Mesh.subMeshCount; submesh++)
                    {
                        var key = (part.Mesh, part.Materials[submesh], submesh);
                        if (!batches.TryGetValue(key, out List<Matrix4x4> matrices))
                        {
                            matrices = new List<Matrix4x4>();
                            batches.Add(key, matrices);
                        }
                        matrices.Add(part.Matrix);
                    }
                }
            }

            foreach (var batch in batches)
            {
                List<Matrix4x4> matrices = batch.Value;
                if (matrices.Count == 0)
                    continue;

                var key = batch.Key;
                var renderParams = new RenderParams(key.material)
                {
                    camera = targetCamera,
                    layer = gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true
                };

                try
                {
                    for (int start = 0; start < matrices.Count; start += MaxInstancesPerDraw)
                    {
                        int count = Mathf.Min(MaxInstancesPerDraw, matrices.Count - start);
                        Graphics.RenderMeshInstanced(renderParams, key.mesh, key.submesh, matrices, count, start);
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

        private int SelectLod(InstanceRecord instance, Vector3 cameraPosition)//计算lod，这个的含义是视锥剔除
        {
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

        private bool TryCreateInstance(Transform root, out InstanceRecord instance)
        {
            instance = null;
            Renderer[] allRenderers = root.GetComponentsInChildren<Renderer>();
            var partsByRenderer = new Dictionary<MeshRenderer, SourcePart>();

            foreach (Renderer renderer in allRenderers)
            {
                if (!renderer.enabled)
                    continue;
                if (!(renderer is MeshRenderer meshRenderer))
                    return false;
                if (meshRenderer.HasPropertyBlock() || meshRenderer.lightmapIndex >= 0 || meshRenderer.isPartOfStaticBatch)
                    return false;

                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material[] materials = meshRenderer.sharedMaterials;
                if (mesh == null || materials.Length < mesh.subMeshCount)
                    return false;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (materials[i] == null || !materials[i].enableInstancing || materials[i].renderQueue >= (int)RenderQueue.Transparent)
                        return false;
                }
                partsByRenderer.Add(meshRenderer, new SourcePart(meshRenderer, mesh, materials));
            }

            if (partsByRenderer.Count == 0)
                return false;

            LODGroup lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup == null || !lodGroup.enabled || lodGroup.fadeMode != LODFadeMode.None)
                return false;

            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length == 0)
                return false;

            SourcePart[][] lodParts = new SourcePart[lods.Length][];
            float[] lodThresholds = new float[lods.Length];
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
                return false;

            instance = new InstanceRecord
            {
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
            return true;
        }

        private void RestoreSourceRenderers()
        {
            foreach (InstanceRecord instance in instances)
            {
                foreach (MeshRenderer renderer in instance.Renderers)
                {
                    if (renderer != null)
                        renderer.enabled = true;
                }
                if (instance.LodGroup != null)
                    instance.LodGroup.enabled = true;
            }
            instances.Clear();
            batches.Clear();
        }

        private sealed class InstanceRecord
        {
            public LODGroup LodGroup;
            public SourcePart[][] LodParts;
            public float[] LodThresholds;
            public List<MeshRenderer> Renderers;
            public Bounds Bounds;
        }

        private sealed class SourcePart
        {
            public readonly Mesh Mesh;
            public readonly Material[] Materials;
            public readonly Matrix4x4 Matrix;

            public SourcePart(MeshRenderer renderer, Mesh mesh, Material[] materials)
            {
                Mesh = mesh;
                Materials = materials;
                Matrix = renderer.transform.localToWorldMatrix;
            }
        }
    }
}
