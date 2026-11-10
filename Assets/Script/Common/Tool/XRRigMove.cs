using System.Collections;
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

        [Header("移动")]
        [Tooltip("目标物体启用后抵达终点的时间，单位为秒。")]
        [Min(0.01f)]
        [SerializeField] private float moveDuration = 4f;

        private void OnEnable()
        {
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
        }

        private IEnumerator MoveToTarget()
        {
            // Let Timeline deactivate an initially active target before moving the rig.
            yield return null;

            Vector3 startPosition = xrRig.position;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, moveDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                xrRig.position = Vector3.Lerp(startPosition, targetPosition.position, progress);
                yield return null;
            }

            xrRig.position = targetPosition.position;
        }
    }
}
