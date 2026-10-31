using UnityEngine;

namespace WhaleMovie.Art.SC04
{
    [DisallowMultipleComponent]
    public sealed class BoatGrowOnStart : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("从 0 增长到编辑模式下尺寸所需的秒数。")]
        private float growDuration = 5f;

        private Vector3 finalScale;
        private float elapsed;

        private void Start()
        {
            finalScale = transform.localScale;
            elapsed = 0f;
            transform.localScale = growDuration > 0f ? Vector3.zero : finalScale;
        }

        private void Update()
        {
            if (elapsed >= growDuration) return;

            elapsed = Mathf.Min(elapsed + Time.deltaTime, growDuration);
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / growDuration);
            transform.localScale = Vector3.LerpUnclamped(Vector3.zero, finalScale, progress);
        }
    }
}
