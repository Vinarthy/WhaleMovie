using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace WhaleMovie.Art.SC01
{
    [DisallowMultipleComponent]
    public sealed class XRRigMove : MonoBehaviour
    {
        [Header("场景对象")]
        [Tooltip("XR Origin (XR Rig) 或其父物体 XR Interaction Hands Setup；留空时自动查找。只会移动 XR Origin。")]
        [SerializeField] private Transform xrRig;

        [Tooltip("移动终点；留空时按名称查找 TargetPosition。")]
        [SerializeField] private Transform targetPosition;

        [Tooltip("按移动顺序设置过渡点；留空时直线移动。")]
        [SerializeField] private Transform[] waypoints;

        [Header("移动")]
        [Tooltip("目标物体启用后抵达终点的时间，单位为秒。")]
        [Min(0.01f)]
        [SerializeField] private float moveDuration = 4f;

        [Tooltip("抵达终点后启用的组件。")]
        [SerializeField] private Behaviour enableOnArrival;

        [Tooltip("抵达后跟随终点的位置变化。")]
        [SerializeField] private bool followTargetOnArrival;

        private bool arrived;
        private Vector3 previousTargetPosition;

        private void OnEnable()
        {
            if (enableOnArrival != null)
                enableOnArrival.enabled = false;

            if (xrRig == null)
            {
                GameObject rigObject = GameObject.Find("XR Interaction Hands Setup");
                if (rigObject != null)
                    xrRig = rigObject.transform;
            }

            if (xrRig != null)
            {
                XROrigin origin = xrRig.GetComponentInChildren<XROrigin>();
                xrRig = origin != null ? origin.transform : null;
            }

            if (targetPosition == null)
            {
                GameObject targetObject = GameObject.Find("TargetPosition");
                if (targetObject != null)
                    targetPosition = targetObject.transform;
            }

            if (xrRig == null || targetPosition == null)
            {
                Debug.LogError("XRRigMove：找不到活动的 XR Origin (XR Rig) 或 TargetPosition，请在 Inspector 中指定。", this);
                return;
            }

            StartCoroutine(MoveToTarget());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            arrived = false;
            if (enableOnArrival != null)
                enableOnArrival.enabled = false;
        }

        private void LateUpdate()
        {
            if (!arrived || !followTargetOnArrival || xrRig == null || targetPosition == null)
                return;

            Vector3 position = targetPosition.position;
            xrRig.position += position - previousTargetPosition;
            previousTargetPosition = position;
        }

        private IEnumerator MoveToTarget()
        {
            // Let Timeline deactivate an initially active target before moving the rig.
            yield return null;

            Vector3 startPosition = xrRig.position;
            var path = new List<Vector3> { startPosition };
            if (waypoints != null)
            {
                foreach (Transform waypoint in waypoints)
                {
                    if (waypoint != null)
                        path.Add(waypoint.position);
                }
            }
            path.Add(targetPosition.position);

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, moveDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                path[path.Count - 1] = targetPosition.position;
                xrRig.position = path.Count == 2
                    ? Vector3.Lerp(startPosition, targetPosition.position, progress)
                    : SamplePath(path, Mathf.SmoothStep(0f, 1f, progress));
                yield return null;
            }

            previousTargetPosition = targetPosition.position;
            xrRig.position = previousTargetPosition;
            arrived = true;
            if (enableOnArrival != null)
                enableOnArrival.enabled = true;
        }

        private static Vector3 SamplePath(List<Vector3> path, float progress)
        {
            int last = path.Count - 1;
            float position = progress * last;
            int segment = Mathf.Min(Mathf.FloorToInt(position), last - 1);
            float t = position - segment;

            Vector3 start = path[segment];
            Vector3 end = path[segment + 1];
            Vector3 startTangent = (end - path[Mathf.Max(0, segment - 1)]) * 0.5f;
            Vector3 endTangent = (path[Mathf.Min(last, segment + 2)] - start) * 0.5f;

            // Catmull–Rom interpolation keeps the tangent continuous at each waypoint.
            float tSquared = t * t;
            float blend = tSquared * (3f - 2f * t);
            return Vector3.LerpUnclamped(start, end, blend)
                + (tSquared * (t - 2f) + t) * startTangent
                + tSquared * (t - 1f) * endTangent;
        }
    }
}
