using AIBuilder.CreatureStage;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// Editor-only scene controls for the route's serialized middle points.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class WhaleRouteControlHandles : MonoBehaviour
{
    [InspectorName("鲸鱼路线")] public StageRoute route;
    [InspectorName("中间点控制物体")] public Transform[] middleHandles;

#if UNITY_EDITOR
    private Vector3[] lastPositions;

    private void OnEnable()
    {
        InitializeFromRoute();
        EditorApplication.update += Sync;
    }

    private void OnDisable() => EditorApplication.update -= Sync;

    private void InitializeFromRoute()
    {
        if (route == null || middleHandles == null) return;
        lastPositions = new Vector3[middleHandles.Length];
        for (var i = 0; i < middleHandles.Length && i + 1 < route.PointCount - 1; i++)
        {
            var handle = middleHandles[i];
            if (handle == null) continue;
            handle.position = route.transform.TransformPoint(route.points[i + 1]);
            lastPositions[i] = handle.localPosition;
        }
    }

    private void Sync()
    {
        if (Application.isPlaying || route == null || middleHandles == null) return;
        EnsureHandleCount();
        if (lastPositions == null || lastPositions.Length != middleHandles.Length) InitializeFromRoute();
        if (lastPositions == null) return;

        var changed = false;
        for (var i = 0; i < middleHandles.Length && i + 1 < route.PointCount - 1; i++)
        {
            var handle = middleHandles[i];
            if (handle == null) continue;
            var point = route.transform.InverseTransformPoint(handle.position);
            if ((handle.localPosition - lastPositions[i]).sqrMagnitude > 0.00000001f)
            {
                Undo.RecordObject(route, "移动鲸鱼路线中间点");
                route.points[i + 1] = point;
                route.Invalidate();
                EditorUtility.SetDirty(route);
                lastPositions[i] = handle.localPosition;
                changed = true;
            }
            else if ((route.points[i + 1] - point).sqrMagnitude > 0.00000001f)
            {
                handle.position = route.transform.TransformPoint(route.points[i + 1]);
                lastPositions[i] = handle.localPosition;
                changed = true;
            }
        }
        if (changed) SceneView.RepaintAll();
    }

    private void EnsureHandleCount()
    {
        var wanted = Mathf.Max(0, route.PointCount - 2);
        if (middleHandles.Length == wanted && System.Array.TrueForAll(middleHandles, handle => handle != null)) return;

        Undo.RecordObject(this, "同步鲸鱼路线中间点");
        var previous = middleHandles;
        var updated = new Transform[wanted];
        var retained = Mathf.Min(previous.Length, wanted);
        for (var i = 0; i < retained; i++) updated[i] = previous[i];
        for (var i = 0; i < wanted; i++)
        {
            if (updated[i] != null) continue;
            var point = new GameObject("鲸鱼路线中间点 " + (i + 1));
            point.tag = "EditorOnly";
            Undo.RegisterCreatedObjectUndo(point, "增加鲸鱼路线中间点控制物体");
            point.transform.SetParent(route.transform, false);
            updated[i] = point.transform;
        }
        for (var i = wanted; i < previous.Length; i++)
            if (previous[i] != null) Undo.DestroyObjectImmediate(previous[i].gameObject);

        middleHandles = updated;
        EditorUtility.SetDirty(this);
        InitializeFromRoute();
    }
#endif
}
