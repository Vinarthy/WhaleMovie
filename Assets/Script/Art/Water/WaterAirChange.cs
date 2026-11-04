using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>Displays the waterline and plays bubbles when the detector reports entry.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(WaterHeightDetector))]
[DefaultExecutionOrder(100)]
public sealed class WaterAirChange : MonoBehaviour
{
    private const int WaterCaptureLayer = 31;

    [SerializeField] private WaterHeightDetector heightDetector;
    [SerializeField] private Material waterlineMaterial;
    [SerializeField] private GameObject entryBubblesPrefab;
    [FormerlySerializedAs("overlayDistance")]
    [SerializeField, Min(0.1f)] private float overlayRadius = 0.4f;
    [SerializeField, Min(0f)] private float bubbleSpawnBelowWater;
    [SerializeField, Min(0.1f)] private float bubbleCleanupSeconds = 10f;

    private static readonly int WaterLevelId = Shader.PropertyToID("_WaterLevel");
    private static readonly int BlendWidthId = Shader.PropertyToID("_BlendWidth");

    private Camera boundCamera;
    private GameObject overlayObject;
    private Mesh overlayMesh;
    private MeshRenderer overlayRenderer;
    private MaterialPropertyBlock propertyBlock;
    private float appliedWaterLevel = float.NaN;

    private void OnEnable()
    {
        if (heightDetector == null)
            heightDetector = GetComponent<WaterHeightDetector>();
        if (waterlineMaterial == null)
            waterlineMaterial = Resources.Load<Material>("Prefeb/Coral/UnderwaterWaterline");
        if (entryBubblesPrefab == null)
            entryBubblesPrefab = Resources.Load<GameObject>("Prefeb/Coral/DiveEntryBubbles");

        heightDetector.EnteredWater += SpawnEntryBubbles;
    }

    private void LateUpdate()
    {
        Camera activeCamera = heightDetector != null ? heightDetector.TargetCamera : null;
        if (activeCamera == null || waterlineMaterial == null)
        {
            if (overlayRenderer != null)
                overlayRenderer.enabled = false;
            return;
        }

        if (activeCamera != boundCamera || overlayRenderer == null)
            CreateOverlay(activeCamera);

        float level = heightDetector.WaterLevel;
        if (!Mathf.Approximately(level, appliedWaterLevel))
        {
            propertyBlock.SetFloat(WaterLevelId, level);
            overlayRenderer.SetPropertyBlock(propertyBlock);
            appliedWaterLevel = level;
        }

        // The shell follows the headset, so its bottom is the lowest visible point.
        float lowestY = overlayRenderer.bounds.min.y;
        overlayRenderer.enabled = lowestY <= level + waterlineMaterial.GetFloat(BlendWidthId);
    }

    private void SpawnEntryBubbles()
    {
        if (entryBubblesPrefab == null || heightDetector == null || heightDetector.TargetCamera == null)
            return;

        Vector3 position = heightDetector.TargetCamera.transform.position;
        position.y = heightDetector.WaterLevel - bubbleSpawnBelowWater;
        GameObject instance = Instantiate(entryBubblesPrefab, position, Quaternion.identity, transform);
        Destroy(instance, bubbleCleanupSeconds);
    }

    private void CreateOverlay(Camera camera)
    {
        RemoveOverlay();
        boundCamera = camera;

        float radius = Mathf.Max(overlayRadius, camera.nearClipPlane + 0.05f);
        overlayMesh = CreateSphereMesh(radius);

        overlayObject = new GameObject("Underwater waterline shell");
        overlayObject.hideFlags = HideFlags.HideInHierarchy;
        // WaterSceneCapture excludes this layer from its reflection/refraction cameras.
        overlayObject.layer = WaterCaptureLayer;
        overlayObject.transform.SetParent(camera.transform, false);
        overlayObject.transform.localPosition = Vector3.zero;

        MeshFilter filter = overlayObject.AddComponent<MeshFilter>();
        filter.sharedMesh = overlayMesh;
        overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = waterlineMaterial;
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
        overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        overlayRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        overlayRenderer.sortingOrder = 32767;
        overlayRenderer.enabled = false;

        propertyBlock = new MaterialPropertyBlock();
        appliedWaterLevel = float.NaN;
    }

    private static Mesh CreateSphereMesh(float radius)
    {
        const int longitudeCount = 48;
        const int latitudeCount = 24;
        int rowLength = longitudeCount + 1;
        Vector3[] vertices = new Vector3[rowLength * (latitudeCount + 1)];
        int[] triangles = new int[longitudeCount * latitudeCount * 6];

        for (int latitude = 0; latitude <= latitudeCount; latitude++)
        {
            float polar = Mathf.PI * latitude / latitudeCount;
            float ringRadius = Mathf.Sin(polar) * radius;
            float y = Mathf.Cos(polar) * radius;
            for (int longitude = 0; longitude <= longitudeCount; longitude++)
            {
                float azimuth = 2f * Mathf.PI * longitude / longitudeCount;
                vertices[latitude * rowLength + longitude] = new Vector3(
                    Mathf.Cos(azimuth) * ringRadius, y, Mathf.Sin(azimuth) * ringRadius);
            }
        }

        int index = 0;
        for (int latitude = 0; latitude < latitudeCount; latitude++)
        {
            for (int longitude = 0; longitude < longitudeCount; longitude++)
            {
                int upperLeft = latitude * rowLength + longitude;
                int lowerLeft = upperLeft + rowLength;
                triangles[index++] = upperLeft;
                triangles[index++] = upperLeft + 1;
                triangles[index++] = lowerLeft;
                triangles[index++] = upperLeft + 1;
                triangles[index++] = lowerLeft + 1;
                triangles[index++] = lowerLeft;
            }
        }

        Mesh mesh = new Mesh { name = "Underwater waterline sphere" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDisable()
    {
        if (heightDetector != null)
            heightDetector.EnteredWater -= SpawnEntryBubbles;
        RemoveOverlay();
    }

    private void RemoveOverlay()
    {
        if (overlayRenderer != null)
            overlayRenderer.enabled = false;
        if (overlayObject != null)
            Destroy(overlayObject);
        if (overlayMesh != null)
            Destroy(overlayMesh);

        overlayObject = null;
        overlayMesh = null;
        overlayRenderer = null;
        boundCamera = null;
    }
}
