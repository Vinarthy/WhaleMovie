using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Captures the scene on both sides of the water plane for the VR water shader.
/// The capture cameras exclude only water renderers, so skybox, opaque and
/// transparent scene objects can appear in the water.
/// </summary>
public static class WaterSceneCapture
{
    private const int WaterCaptureLayer = 31;
    private const string WaterShaderName = "Custom/WaveWithReflection";

    private static readonly List<Renderer> WaterRenderers = new List<Renderer>();
    private static readonly RenderTexture[] Reflection = new RenderTexture[2];
    private static readonly RenderTexture[] Refraction = new RenderTexture[2];
    private static readonly int CaptureReady = Shader.PropertyToID("_WaterCaptureReady");
    private static readonly int CapturePass = Shader.PropertyToID("_WaterCapturePass");
    private static readonly int ReflectionLeft = Shader.PropertyToID("_WaterReflectionLeft");
    private static readonly int ReflectionRight = Shader.PropertyToID("_WaterReflectionRight");
    private static readonly int RefractionLeft = Shader.PropertyToID("_WaterRefractionLeft");
    private static readonly int RefractionRight = Shader.PropertyToID("_WaterRefractionRight");
    private static readonly int ReflectionVPLeft = Shader.PropertyToID("_WaterReflectionVPLeft");
    private static readonly int ReflectionVPRight = Shader.PropertyToID("_WaterReflectionVPRight");

    private static Camera captureCamera;
    private static float waterHeight;
    private static bool renderingCapture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        SceneManager.sceneLoaded -= SceneLoaded;
        Application.quitting -= Cleanup;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        SceneManager.sceneLoaded += SceneLoaded;
        Application.quitting += Cleanup;
        FindWater();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void InitializeEditor()
    {
        if (Application.isPlaying)
            return;

        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        EditorApplication.hierarchyChanged -= FindWater;
        EditorApplication.hierarchyChanged += FindWater;
        EditorSceneManager.sceneOpened -= EditorSceneOpened;
        EditorSceneManager.sceneOpened += EditorSceneOpened;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        FindWater();
    }

    private static void EditorSceneOpened(Scene scene, OpenSceneMode mode)
    {
        FindWater();
    }
#endif

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Shader.SetGlobalFloat(CaptureReady, 0f);
        FindWater();
    }

    private static void FindWater()
    {
        WaterRenderers.Clear();
        var renderers = Object.FindObjectsOfType<Renderer>();
        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null || material.shader.name != WaterShaderName)
                    continue;

                WaterRenderers.Add(renderer);
                if (Application.isPlaying)
                    renderer.gameObject.layer = WaterCaptureLayer;
                if (WaterRenderers.Count == 1)
                    waterHeight = renderer.bounds.center.y;
                break;
            }
        }
    }

    private static void BeginCamera(ScriptableRenderContext context, Camera source)
    {
        if (renderingCapture || source == null || source == captureCamera ||
            (source.cameraType != CameraType.Game && source.cameraType != CameraType.SceneView))
            return;

        Shader.SetGlobalFloat(CaptureReady, 0f);
        if (WaterRenderers.Count == 0 && source.cameraType == CameraType.SceneView)
            FindWater();
        if (WaterRenderers.Count == 0)
            return;
        if (WaterRenderers[0] != null)
            waterHeight = WaterRenderers[0].bounds.center.y;

        if (captureCamera == null)
        {
            var go = new GameObject("Water scene capture camera");
            go.hideFlags = HideFlags.HideAndDontSave;
            captureCamera = go.AddComponent<Camera>();
            captureCamera.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
        }

        int width = source.stereoEnabled ? XRSettings.eyeTextureWidth : source.pixelWidth;
        int height = source.stereoEnabled ? XRSettings.eyeTextureHeight : source.pixelHeight;
        width = Mathf.Clamp(width, 256, 2048);
        height = Mathf.Clamp(height, 256, 2048);
        int eyes = source.stereoEnabled ? 2 : 1;
        EnsureTextures(width, height, eyes);

        if (Application.isPlaying)
            source.cullingMask |= 1 << WaterCaptureLayer;
        renderingCapture = true;
        Shader.SetGlobalFloat(CapturePass, 1f);
        try
        {
            for (int eye = 0; eye < eyes; eye++)
            {
                Camera.StereoscopicEye stereoEye = eye == 0 ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right;
                Matrix4x4 view = source.stereoEnabled ? source.GetStereoViewMatrix(stereoEye) : source.worldToCameraMatrix;
                Matrix4x4 projection = source.stereoEnabled ? source.GetStereoProjectionMatrix(stereoEye) : source.projectionMatrix;

                RenderEye(context, source, view, projection, Refraction[eye], false, out _);
                RenderEye(context, source, view, projection, Reflection[eye], true, out Matrix4x4 reflectionVP);
                Shader.SetGlobalMatrix(eye == 0 ? ReflectionVPLeft : ReflectionVPRight, reflectionVP);
            }

            Shader.SetGlobalTexture(ReflectionLeft, Reflection[0]);
            Shader.SetGlobalTexture(ReflectionRight, Reflection[eyes - 1]);
            Shader.SetGlobalTexture(RefractionLeft, Refraction[0]);
            Shader.SetGlobalTexture(RefractionRight, Refraction[eyes - 1]);
            Shader.SetGlobalFloat(CaptureReady, 1f);
        }
        finally
        {
            Shader.SetGlobalFloat(CapturePass, 0f);
            renderingCapture = false;
        }
    }

    private static void RenderEye(ScriptableRenderContext context, Camera source, Matrix4x4 view,
        Matrix4x4 projection, RenderTexture target, bool mirrored, out Matrix4x4 viewProjection)
    {
        captureCamera.CopyFrom(source);
        captureCamera.enabled = false;
        captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
        captureCamera.targetTexture = target;
        captureCamera.cullingMask = source.cullingMask & ~(1 << WaterCaptureLayer);
        captureCamera.allowMSAA = false;
        captureCamera.depthTextureMode = DepthTextureMode.None;

        var sourceSkybox = source.GetComponent<Skybox>();
        var captureSkybox = captureCamera.GetComponent<Skybox>();
        if (sourceSkybox != null && sourceSkybox.material != null)
        {
            if (captureSkybox == null)
                captureSkybox = captureCamera.gameObject.AddComponent<Skybox>();
            captureSkybox.material = sourceSkybox.material;
        }
        else if (captureSkybox != null)
        {
            captureSkybox.enabled = false;
        }

        if (mirrored)
        {
            Matrix4x4 reflect = Matrix4x4.identity;
            reflect.m11 = -1f;
            reflect.m13 = 2f * waterHeight;
            view = view * reflect;
        }

        captureCamera.worldToCameraMatrix = view;
        captureCamera.projectionMatrix = projection;
        captureCamera.transform.position = view.inverse.GetColumn(3);
        viewProjection = GL.GetGPUProjectionMatrix(projection, true) * view;

        bool previousCulling = GL.invertCulling;
        GL.invertCulling = mirrored;
        try
        {
            UniversalRenderPipeline.RenderSingleCamera(context, captureCamera);
        }
        finally
        {
            GL.invertCulling = previousCulling;
        }
    }

    private static void EnsureTextures(int width, int height, int eyes)
    {
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
        for (int eye = 0; eye < eyes; eye++)
        {
            EnsureTexture(ref Reflection[eye], width, height, format, "Water reflection " + eye);
            EnsureTexture(ref Refraction[eye], width, height, format, "Water refraction " + eye);
        }
    }

    private static void EnsureTexture(ref RenderTexture texture, int width, int height,
        RenderTextureFormat format, string name)
    {
        if (texture != null && texture.width == width && texture.height == height && texture.format == format)
            return;

        if (texture != null)
        {
            texture.Release();
            DestroyCaptureObject(texture);
        }

        texture = new RenderTexture(width, height, 24, format)
        {
            name = name,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear
        };
        texture.Create();
    }

    private static void Cleanup()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        SceneManager.sceneLoaded -= SceneLoaded;
        Application.quitting -= Cleanup;
#if UNITY_EDITOR
        EditorApplication.hierarchyChanged -= FindWater;
        EditorSceneManager.sceneOpened -= EditorSceneOpened;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
#endif
        for (int eye = 0; eye < 2; eye++)
        {
            if (Reflection[eye] != null) { Reflection[eye].Release(); DestroyCaptureObject(Reflection[eye]); }
            if (Refraction[eye] != null) { Refraction[eye].Release(); DestroyCaptureObject(Refraction[eye]); }
        }
        if (captureCamera != null)
            DestroyCaptureObject(captureCamera.gameObject);
        Shader.SetGlobalFloat(CaptureReady, 0f);
        Shader.SetGlobalFloat(CapturePass, 0f);
    }

    private static void DestroyCaptureObject(Object value)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Object.DestroyImmediate(value);
            return;
        }
#endif
        Object.Destroy(value);
    }
}
