using System.Collections;
using UnityEngine;

namespace WhaleMovie.Art.SC01
{
    [DisallowMultipleComponent]
    public sealed class SC01XRRigMove : MonoBehaviour
    {
        [Header("场景对象")]
        [Tooltip("整个 XR Rig 的根对象；留空时按名称查找 XR Interaction Hands Setup。")]
        [SerializeField] private Transform xrRig;

        [Tooltip("移动终点；留空时按名称查找 TargetPosition。")]
        [SerializeField] private Transform targetPosition;

        [Header("移动")]
        [Tooltip("从游戏开始到抵达目标的时间，单位为秒。")]
        [Min(0.01f)]
        [SerializeField] private float moveDuration = 4f;

        private void Start()
        {
            if (xrRig == null)
            {
                GameObject rigObject = GameObject.Find("XR Interaction Hands Setup");
                if (rigObject != null)
                    xrRig = rigObject.transform;
            }

            if (targetPosition == null)
            {
                GameObject targetObject = GameObject.Find("TargetPosition");
                if (targetObject != null)
                    targetPosition = targetObject.transform;
            }

            if (xrRig == null || targetPosition == null)
            {
                Debug.LogError("SC01XRRigMove：找不到 XR Interaction Hands Setup 或 TargetPosition，请在 Inspector 中指定。", this);
                return;
            }

            StartCoroutine(MoveToTarget());
        }

        private IEnumerator MoveToTarget()
        {
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
