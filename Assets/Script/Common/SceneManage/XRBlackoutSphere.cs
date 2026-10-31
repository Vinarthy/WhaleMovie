using System.Collections;
using UnityEngine;

namespace WhaleMovie.SceneManage
{
    [DisallowMultipleComponent]
    public sealed class XRBlackoutSphere : MonoBehaviour
    {
        private const string ShaderResourcePath = "SceneManage/XRBlackoutSphere";

        [Min(0.1f)] [SerializeField] private float sphereRadius = 1f;

        private Camera targetCamera;
        private MeshRenderer sphereRenderer;
        private Material material;
        private float alpha = 1f;

        public float Alpha => alpha;

        private void Awake()
        {
            SetAlphaImmediate(1f);
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
                BindToCamera(Camera.main != null ? Camera.main : FindObjectOfType<Camera>());
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }

        public void BindToCamera(Camera camera)
        {
            if (camera == null)
                return;

            targetCamera = camera;
            EnsureSphere();
            Transform sphere = sphereRenderer.transform;
            sphere.SetParent(targetCamera.transform, false);
            sphere.localPosition = Vector3.zero;
            sphere.localRotation = Quaternion.identity;

            float radius = Mathf.Max(sphereRadius, targetCamera.nearClipPlane * 4f);
            sphere.localScale = Vector3.one * radius * 2f;
            sphere.gameObject.layer = targetCamera.gameObject.layer;
        }

        public void SetAlphaImmediate(float value)
        {
            alpha = Mathf.Clamp01(value);
            EnsureSphere();
            ApplyAlpha();
        }

        public IEnumerator FadeTo(float targetAlpha, float duration)
        {
            targetAlpha = Mathf.Clamp01(targetAlpha);
            EnsureSphere();

            float start = alpha;
            if (duration <= 0f)
            {
                SetAlphaImmediate(targetAlpha);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                alpha = Mathf.Lerp(start, targetAlpha, t);
                ApplyAlpha();
                yield return null;
            }

            SetAlphaImmediate(targetAlpha);
        }

        private void EnsureSphere()
        {
            if (sphereRenderer != null)
                return;

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "XR Blackout Sphere";
            sphere.hideFlags = HideFlags.DontSave;

            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            sphereRenderer = sphere.GetComponent<MeshRenderer>();
            sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;
            sphereRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            sphereRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                Debug.LogError("XR blackout shader could not be found.", this);
                return;
            }

            material = new Material(shader) { name = "XR Blackout Runtime Material" };
            sphereRenderer.sharedMaterial = material;

            if (targetCamera != null)
                BindToCamera(targetCamera);

            ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            if (sphereRenderer == null || material == null)
                return;

            material.SetColor("_Color", new Color(0f, 0f, 0f, alpha));
            sphereRenderer.enabled = alpha > 0.001f;
        }
    }
}
