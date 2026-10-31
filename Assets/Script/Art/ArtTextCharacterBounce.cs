using TMPro;
using UnityEngine;

namespace WhaleMovie.Art
{
    /// <summary>让 TextMeshPro 文本中的可见字符依次跳起，整句结束后可循环播放。</summary>
    [DisallowMultipleComponent]
    public sealed class ArtTextCharacterBounce : MonoBehaviour
    {
        [Header("文字")]
        [Tooltip("要跳动的 TextMeshPro 文本；留空时使用当前物体上的 TMP 文本组件。")]
        [SerializeField] private TMP_Text targetText;

        [Header("跳动节奏")]
        [Tooltip("跳起高度相对于字符高度的比例。0.35 表示约三分之一个字高。")]
        [Range(0f, 2f)] public float jumpHeight = 0.35f;

        [Tooltip("每个字符从起跳到落下所用的秒数。")]
        [Min(0.05f)] public float jumpDuration = 0.35f;

        [Tooltip("相邻两个可见字符开始起跳的时间差，单位为秒。")]
        [Min(0f)] public float characterInterval = 0.1f;

        [Tooltip("最后一个字符落下后，到下一轮开始前的停顿秒数。")]
        [Min(0f)] public float pauseBetweenWaves = 0.5f;

        [Header("播放")]
        public bool playOnEnable = true;
        public bool loop = true;
        [Tooltip("开启后即使 Time.timeScale 为 0，也会按真实时间播放。")]
        public bool useUnscaledTime;

        private float elapsed;
        private bool isPlaying;

        private void Reset()
        {
            targetText = GetComponent<TMP_Text>();
        }

        private void Awake()
        {
            if (targetText == null) targetText = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            if (targetText == null) targetText = GetComponent<TMP_Text>();
            elapsed = 0f;
            isPlaying = playOnEnable;
        }

        private void OnDisable()
        {
            isPlaying = false;
            RestoreTextMesh();
        }

        /// <summary>从第一个字符重新开始播放。</summary>
        public void Play()
        {
            elapsed = 0f;
            isPlaying = true;
        }

        /// <summary>停止并恢复文字原来的顶点位置。</summary>
        public void Stop()
        {
            isPlaying = false;
            elapsed = 0f;
            RestoreTextMesh();
        }

        private void LateUpdate()
        {
            if (!isPlaying || targetText == null || !targetText.isActiveAndEnabled) return;

            // 每帧从 TMP 原始排版重新取得顶点，避免位移逐帧累加；改字、换行和换字体也能立即生效。
            targetText.ForceMeshUpdate();
            TMP_TextInfo textInfo = targetText.textInfo;
            int visibleCount = 0;
            for (int i = 0; i < textInfo.characterCount; i++)
                if (textInfo.characterInfo[i].isVisible) visibleCount++;

            if (visibleCount == 0) return;

            float duration = Mathf.Max(0.05f, jumpDuration);
            float interval = Mathf.Max(0f, characterInterval);
            float cycleDuration = (visibleCount - 1) * interval + duration + Mathf.Max(0f, pauseBetweenWaves);
            if (!loop && elapsed >= cycleDuration)
            {
                Stop();
                return;
            }

            float waveTime = loop ? Mathf.Repeat(elapsed, cycleDuration) : elapsed;
            int visibleIndex = 0;
            bool changed = false;
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[i];
                if (!character.isVisible) continue;

                float localTime = waveTime - visibleIndex * interval;
                visibleIndex++;
                if (localTime <= 0f || localTime >= duration) continue;

                float characterHeight = character.ascender - character.descender;
                float offsetY = characterHeight * Mathf.Max(0f, jumpHeight) *
                                Mathf.Sin(Mathf.PI * localTime / duration);
                Vector3[] vertices = textInfo.meshInfo[character.materialReferenceIndex].vertices;
                for (int corner = 0; corner < 4; corner++)
                    vertices[character.vertexIndex + corner].y += offsetY;
                changed = true;
            }

            if (changed) targetText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        private void RestoreTextMesh()
        {
            if (targetText != null && targetText.isActiveAndEnabled)
                targetText.ForceMeshUpdate();
        }
    }
}
