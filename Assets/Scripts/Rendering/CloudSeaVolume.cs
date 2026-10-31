using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Cloud Sea Volume")]
public sealed class CloudSeaVolume : MonoBehaviour
{
    static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    static readonly int DetailTex = Shader.PropertyToID("_DetailTex");
    static readonly int WeatherMap = Shader.PropertyToID("_WeatherMap");
    static readonly int Density = Shader.PropertyToID("_Density");
    static readonly int Coverage = Shader.PropertyToID("_Coverage");
    static readonly int ShapeTiling = Shader.PropertyToID("_ShapeTiling");
    static readonly int DetailTiling = Shader.PropertyToID("_DetailTiling");
    static readonly int ShapeSpeed = Shader.PropertyToID("_ShapeSpeed");
    static readonly int DetailSpeed = Shader.PropertyToID("_DetailSpeed");
    static readonly int WarpStrength = Shader.PropertyToID("_WarpStrength");
    static readonly int DensityOffset = Shader.PropertyToID("_DensityOffset");
    static readonly int DetailErodeWeight = Shader.PropertyToID("_DetailErodeWeight");
    static readonly int DetailExponent = Shader.PropertyToID("_DetailExponent");
    static readonly int EdgeFade = Shader.PropertyToID("_EdgeFade");
    static readonly int ViewAbsorption = Shader.PropertyToID("_ViewAbsorption");
    static readonly int LightAbsorption = Shader.PropertyToID("_LightAbsorption");
    static readonly int Steps = Shader.PropertyToID("_Steps");
    static readonly int LightSteps = Shader.PropertyToID("_LightSteps");
    static readonly int MobileQuality = Shader.PropertyToID("_MobileQuality");
    static readonly int ShapeWeights = Shader.PropertyToID("_ShapeWeights");
    static readonly int PhaseParams = Shader.PropertyToID("_PhaseParams");
    static readonly int LightColor = Shader.PropertyToID("_LightColor");
    static readonly int MidColor = Shader.PropertyToID("_MidColor");
    static readonly int ShadowColor = Shader.PropertyToID("_ShadowColor");
    static readonly int Brightness = Shader.PropertyToID("_Brightness");
    static readonly int AmbientFill = Shader.PropertyToID("_AmbientFill");

    Renderer cloudRenderer;
    MaterialPropertyBlock properties;

    [Header("Cloud Textures")]
    public Texture3D shapeNoise;
    public Texture3D detailNoise;
    public Texture2D weatherMap;

    [Header("Cloud Shape")]
    [Min(0f)] public float density = 1.35f;
    [Range(0f, 1f)] public float coverage = 0.47f;
    [Min(0.00001f)] public float shapeTiling = 0.002f;
    [Min(0.00001f)] public float detailTiling = 0.018f;
    public Vector4 shapeWeights = new Vector4(0.55f, 0.25f, 0.15f, 0.05f);
    [Range(-0.5f, 0.5f)] public float densityOffset = -0.08f;
    [Range(0f, 2f)] public float detailErodeWeight = 0.55f;
    [Range(0.25f, 4f)] public float detailExponent = 1.1f;
    [Range(0.005f, 0.25f)] public float edgeFade = 0.06f;

    [Header("Cloud Animation")]
    [Min(0f)] public float shapeSpeed = 0.012f;
    [Min(0f)] public float detailSpeed = 0.025f;
    [Range(0f, 0.5f)] public float warpStrength = 0.08f;

    [Header("Scattering and Thickness")]
    [Range(0.01f, 1f)] public float viewAbsorption = 0.075f;
    [Range(0.01f, 2f)] public float lightAbsorption = 0.18f;
    public Vector4 phaseParams = new Vector4(0.65f, 0.2f, 0.08f, 1.2f);
    public Color lightColor = new Color(1f, 0.82f, 0.62f, 1f);
    public Color midColor = new Color(0.72f, 0.76f, 0.8f, 1f);
    public Color shadowColor = new Color(0.34f, 0.43f, 0.54f, 1f);
    [Range(0.5f, 4f)] public float brightness = 1.6f;
    [Range(0f, 1f)] public float ambientFill = 0.35f;

    [Header("VR Performance")]
    [Range(16, 96)] public int steps = 48;
    [Range(1, 8)] public int lightSteps = 5;

    [Header("Mobile / Quest Quality")]
    [Tooltip("在移动头显上使用简化密度和光照，桌面端保留完整体积云效果。")]
    public bool useMobileQuality = true;
    [Range(8, 32)] public int mobileSteps = 16;
    [Tooltip("0 使用高度近似光照，避免每次视线采样再执行一轮体积光照采样。")]
    [Range(0, 2)] public int mobileLightSteps;

    void OnEnable() => ApplyProperties();
    void OnValidate() => ApplyProperties();

    void OnDisable()
    {
        if (cloudRenderer) cloudRenderer.enabled = false;
    }

    // Animation uses shader _Time; properties only need uploading when settings change.
    public void ApplyProperties()
    {
        if (!isActiveAndEnabled) return;
        if (!cloudRenderer || cloudRenderer.transform.parent != transform) cloudRenderer = CreateCloudRenderer();
        if (!cloudRenderer) return;
        cloudRenderer.enabled = true;
        if (!shapeNoise) shapeNoise = Resources.Load<Texture3D>("Cloud/RayMarchingCloud/ExampleNoise13D");
        if (!detailNoise) detailNoise = Resources.Load<Texture3D>("Cloud/RayMarchingCloud/cloudDetailTexture");
        if (!weatherMap) weatherMap = Resources.Load<Texture2D>("Cloud/RayMarchingCloud/weather3");
        if (!weatherMap) weatherMap = Resources.Load<Texture2D>("Cloud/RayMarchingCloud/weather5");

        if (properties == null) properties = new MaterialPropertyBlock();
        cloudRenderer.GetPropertyBlock(properties);
        if (shapeNoise) properties.SetTexture(NoiseTex, shapeNoise);
        if (detailNoise) properties.SetTexture(DetailTex, detailNoise);
        if (weatherMap) properties.SetTexture(WeatherMap, weatherMap);
        properties.SetFloat(Density, density);
        properties.SetFloat(Coverage, coverage);
        properties.SetFloat(ShapeTiling, shapeTiling);
        properties.SetFloat(DetailTiling, detailTiling);
        properties.SetFloat(ShapeSpeed, shapeSpeed);
        properties.SetFloat(DetailSpeed, detailSpeed);
        properties.SetFloat(WarpStrength, warpStrength);
        properties.SetFloat(DensityOffset, densityOffset);
        properties.SetFloat(DetailErodeWeight, detailErodeWeight);
        properties.SetFloat(DetailExponent, detailExponent);
        properties.SetFloat(EdgeFade, edgeFade);
        properties.SetFloat(ViewAbsorption, viewAbsorption);
        properties.SetFloat(LightAbsorption, lightAbsorption);
        bool mobile = useMobileQuality && Application.isMobilePlatform;
        properties.SetFloat(Steps, mobile ? Mathf.Clamp(mobileSteps, 8, 32) : steps);
        properties.SetFloat(LightSteps, mobile ? Mathf.Clamp(mobileLightSteps, 0, 2) : lightSteps);
        properties.SetFloat(MobileQuality, mobile ? 1f : 0f);
        properties.SetVector(ShapeWeights, shapeWeights);
        properties.SetVector(PhaseParams, phaseParams);
        properties.SetColor(LightColor, lightColor);
        properties.SetColor(MidColor, midColor);
        properties.SetColor(ShadowColor, shadowColor);
        properties.SetFloat(Brightness, brightness);
        properties.SetFloat(AmbientFill, ambientFill);
        cloudRenderer.SetPropertyBlock(properties);
    }

    Renderer CreateCloudRenderer()
    {
        Transform existing = transform.Find("CloudSeaRenderer");
        GameObject rendererObject = existing ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
        if (!existing)
        {
            rendererObject.name = "CloudSeaRenderer";
            rendererObject.transform.SetParent(transform, false);
            Collider collider = rendererObject.GetComponent<Collider>();
            if (collider)
            {
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            }
        }
        rendererObject.SetActive(true);
        rendererObject.hideFlags = HideFlags.None;
        rendererObject.transform.localPosition = Vector3.zero;
        rendererObject.transform.localRotation = Quaternion.identity;
        rendererObject.transform.localScale = Vector3.one;
        EnsureCubeMesh(rendererObject);
        Renderer parentRenderer = GetComponent<Renderer>();
        if (parentRenderer && parentRenderer.gameObject == gameObject) parentRenderer.enabled = false;
        Renderer renderer = rendererObject.GetComponent<Renderer>();
        if (!renderer) renderer = rendererObject.AddComponent<MeshRenderer>();
        renderer.enabled = true;
        renderer.forceRenderingOff = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        Material material = Resources.Load<Material>("Cloud/CloudSeaObject");
        if (renderer && material) renderer.sharedMaterial = material;
        return renderer;
    }

    static void EnsureCubeMesh(GameObject rendererObject)
    {
        MeshFilter meshFilter = rendererObject.GetComponent<MeshFilter>();
        if (!meshFilter || (meshFilter.sharedMesh && meshFilter.sharedMesh.name == "Cube")) return;

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
        if (!meshFilter) meshFilter = rendererObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = cubeMesh;

        Collider collider = rendererObject.GetComponent<Collider>();
        if (collider)
        {
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }
        if (Application.isPlaying) Destroy(cube); else DestroyImmediate(cube);
    }
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.45f, 0.8f, 1f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
