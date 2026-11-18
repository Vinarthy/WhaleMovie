using Unity.XR.CoreUtils;
using UnityEngine;

public class BoatChild : MonoBehaviour
{
    [SerializeField] private Transform parentPoint;

    private Transform xrRig;
    private Transform originalParent;

    private void OnEnable()
    {
        var origin = FindFirstObjectByType<XROrigin>();
        if (origin == null)
            return;

        xrRig = origin.transform;
        originalParent = xrRig.parent;
        xrRig.SetParent(parentPoint != null ? parentPoint : transform, true);
    }

    private void OnDisable()
    {
        if (xrRig != null && xrRig.parent == (parentPoint != null ? parentPoint : transform))
            xrRig.SetParent(originalParent, true);
    }
}
