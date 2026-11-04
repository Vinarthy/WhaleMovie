using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Fills the distant horizon beyond the finite water tiles.</summary>
[DisallowMultipleComponent]
public sealed class WaterHorizonExtension : MonoBehaviour
{
    [SerializeField] private Material sourceMaterial;
    [SerializeField, Min(100f)] private float halfExtent = 500f;
    [SerializeField, Min(1f)] private float innerExtent = 80f;
    [SerializeField, Min(0.5f)] private float innerStep = 10f;
    [SerializeField, Range(1, 64)] private int outerRings = 7;

    private GameObject extensionObject;
    private Mesh extensionMesh;
    private Material extensionMaterial;

    private void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        if (sourceMaterial == null)
            sourceMaterial = Resources.Load<Material>("Texture/seawave");
        if (sourceMaterial == null)
            return;

        bool foundWater = false;
        Bounds waterBounds = default;
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.sharedMaterial != sourceMaterial)
                continue;

            if (foundWater)
                waterBounds.Encapsulate(renderer.bounds);
            else
            {
                waterBounds = renderer.bounds;
                foundWater = true;
            }
        }
        if (!foundWater)
            return;

        extensionMesh = BuildMesh();
        extensionMaterial = new Material(sourceMaterial)
        {
            name = "Water horizon extension (runtime)",
            renderQueue = sourceMaterial.renderQueue - 1
        };

        extensionObject = new GameObject("Water horizon extension");
        extensionObject.layer = 31; // WaterSceneCapture excludes this layer.
        extensionObject.transform.position = waterBounds.center;
        extensionObject.AddComponent<MeshFilter>().sharedMesh = extensionMesh;
        MeshRenderer meshRenderer = extensionObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = extensionMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private Mesh BuildMesh()
    {
        float[] axis = BuildAxis();
        int count = axis.Length;
        Vector3[] vertices = new Vector3[count * count];
        int[] triangles = new int[(count - 1) * (count - 1) * 6];

        for (int z = 0; z < count; z++)
            for (int x = 0; x < count; x++)
                vertices[z * count + x] = new Vector3(axis[x], 0f, axis[z]);

        int index = 0;
        for (int z = 0; z < count - 1; z++)
        {
            for (int x = 0; x < count - 1; x++)
            {
                int bottomLeft = z * count + x;
                triangles[index++] = bottomLeft;
                triangles[index++] = bottomLeft + count;
                triangles[index++] = bottomLeft + 1;
                triangles[index++] = bottomLeft + 1;
                triangles[index++] = bottomLeft + count;
                triangles[index++] = bottomLeft + count + 1;
            }
        }

        Mesh mesh = new Mesh { name = "Water horizon grid" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private float[] BuildAxis()
    {
        float near = Mathf.Min(innerExtent, halfExtent * 0.5f);
        int innerIntervals = Mathf.CeilToInt(near / innerStep);
        List<float> values = new List<float>(2 * outerRings + 2 * innerIntervals + 1);

        for (int i = outerRings; i >= 1; i--)
            values.Add(-near - (halfExtent - near) * Mathf.Pow((float)i / outerRings, 1.6f));
        for (int i = -innerIntervals; i <= innerIntervals; i++)
            values.Add(near * i / innerIntervals);
        for (int i = 1; i <= outerRings; i++)
            values.Add(near + (halfExtent - near) * Mathf.Pow((float)i / outerRings, 1.6f));

        return values.ToArray();
    }

    private void OnDisable()
    {
        if (extensionObject != null)
            Destroy(extensionObject);
        if (extensionMesh != null)
            Destroy(extensionMesh);
        if (extensionMaterial != null)
            Destroy(extensionMaterial);
    }
}
