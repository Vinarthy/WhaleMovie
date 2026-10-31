using UnityEngine;

// Keeps the imported whale as a scene root while SceneStory drives a separate proxy.
[ExecuteAlways]
[DefaultExecutionOrder(10000)]
public sealed class WhaleRouteFollower : MonoBehaviour
{
    public Transform routeProxy;
    public Quaternion modelRotationOffset = Quaternion.identity;
    [Min(1f), InspectorName("水平最大转向速度（度/秒）")]
    public float maxYawDegreesPerSecond = 35f;
    [Min(0.01f), InspectorName("转向缓动时间（秒）")]
    public float yawSmoothTime = 0.45f;

    private Transform previousProxy;
    private float currentYaw;
    private float yawVelocity;
    private bool hasYaw;

#if UNITY_EDITOR
    private double lastEditorTime;
#endif

    private void OnEnable()
    {
        hasYaw = false;
#if UNITY_EDITOR
        lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update += EditorSync;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorSync;
#endif
    }

#if UNITY_EDITOR
    private void EditorSync()
    {
        if (Application.isPlaying) return;
        var now = UnityEditor.EditorApplication.timeSinceStartup;
        var deltaTime = (float)(now - lastEditorTime);
        lastEditorTime = now;
        Sync(Mathf.Clamp(deltaTime, 0f, 0.1f));
    }
#endif

    private void LateUpdate()
    {
        if (Application.isPlaying) Sync(Time.deltaTime);
    }

    private void Sync(float deltaTime)
    {
        if (routeProxy == null) return;
        var position = routeProxy.position;
        var targetYaw = routeProxy.rotation.eulerAngles.y;
        if (!hasYaw || previousProxy != routeProxy)
        {
            currentYaw = targetYaw;
            yawVelocity = 0f;
            hasYaw = true;
            previousProxy = routeProxy;
        }
        else if (deltaTime > 0f)
        {
            var speed = Mathf.Max(1f, maxYawDegreesPerSecond);
            var dampedYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity,
                Mathf.Max(0.01f, yawSmoothTime), speed, deltaTime);
            currentYaw = Mathf.MoveTowardsAngle(currentYaw, dampedYaw, speed * deltaTime);
        }

        // Smooth only the horizontal heading; keep any pitch/roll and the model's axis correction.
        var targetYawRotation = Quaternion.Euler(0f, targetYaw, 0f);
        var pitchAndRoll = Quaternion.Inverse(targetYawRotation) * routeProxy.rotation;
        var rotation = Quaternion.Euler(0f, currentYaw, 0f) * pitchAndRoll * modelRotationOffset;
        if ((transform.position - position).sqrMagnitude < 0.00000001f &&
            Quaternion.Angle(transform.rotation, rotation) < 0.001f) return;
        transform.SetPositionAndRotation(position, rotation);
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
#endif
    }
}
