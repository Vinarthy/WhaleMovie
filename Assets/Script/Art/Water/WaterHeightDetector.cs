using System;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>Tracks the active player's head against the Ocean water surface.</summary>
[DisallowMultipleComponent]
public sealed class WaterHeightDetector : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Renderer waterRenderer;
    [SerializeField] private float fallbackWaterLevel;
    [SerializeField, Min(0f)] private float enterDepth = 0.08f;
    [SerializeField, Min(0f)] private float exitHeight = 0.08f;

    public event Action EnteredWater;
    public event Action ExitedWater;

    public Camera TargetCamera => targetCamera;
    public float WaterLevel => waterRenderer != null ? waterRenderer.bounds.center.y : fallbackWaterLevel;
    public bool IsUnderwater { get; private set; }

    private bool initialized;
    private float nextCameraLookup;

    private void OnEnable()
    {
        initialized = false;
        FindWaterRenderer();
        FindPlayerCamera();
    }

    private void LateUpdate()
    {
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            if (Time.unscaledTime < nextCameraLookup)
                return;
            nextCameraLookup = Time.unscaledTime + 0.5f;
            FindPlayerCamera();
            initialized = false;
        }

        if (targetCamera == null)
            return;

        float headY = targetCamera.transform.position.y;
        float level = WaterLevel;
        if (!initialized)
        {
            IsUnderwater = headY < level;
            initialized = true;
            return;
        }

        if (!IsUnderwater && headY <= level - enterDepth)
        {
            IsUnderwater = true;
            EnteredWater?.Invoke();
        }
        else if (IsUnderwater && headY >= level + exitHeight)
        {
            IsUnderwater = false;
            ExitedWater?.Invoke();
        }
    }

    private void FindPlayerCamera()
    {
        if (targetCamera != null && targetCamera.isActiveAndEnabled)
            return;

        targetCamera = null;
        foreach (XROrigin origin in FindObjectsOfType<XROrigin>())
        {
            if (origin.isActiveAndEnabled && origin.Camera != null && origin.Camera.isActiveAndEnabled)
            {
                targetCamera = origin.Camera;
                return;
            }
        }

        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
            targetCamera = main;
    }

    private void FindWaterRenderer()
    {
        if (waterRenderer != null || !gameObject.scene.IsValid())
            return;

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (Renderer candidate in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in candidate.sharedMaterials)
                {
                    if (material == null || material.shader == null ||
                        material.shader.name != "Custom/WaveWithReflection")
                        continue;

                    waterRenderer = candidate;
                    return;
                }
            }
        }
    }
}
