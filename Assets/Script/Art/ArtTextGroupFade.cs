using TMPro;
using UnityEngine;

namespace WhaleMovie.Art
{
    /// <summary>按 1、3、5、7……个可见字符分组，让文字从左到右快速渐显。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class ArtTextGroupFade : MonoBehaviour
    {
        [Header("文字")]
        [Tooltip("要渐显的 TextMeshPro 文本；留空时使用当前物体上的 TMP 文本组件。")]
        [SerializeField] private TMP_Text targetText;

        [Header("分组与速度")]
        [Tooltip("第一组包含几个可见字符。默认 1。")]
        [Min(1)] public int firstGroupSize = 1;

        [Tooltip("后一组比前一组多几个字符。默认 2，得到 1、3、5、7……。")]
        [Min(0)] public int groupSizeIncrease = 2;

        [Tooltip("相邻两组开始渐显的时间差，单位为秒。")]
        [Min(0f)] public float groupInterval = 0.12f;

        [Tooltip("每组从透明到不透明所用的秒数。")]
        [Min(0.01f)] public float fadeDuration = 0.18f;

        [Tooltip("开始显示第一组前等待的秒数。")]
        [Min(0f)] public float startDelay;

        [Header("播放")]
        public bool playOnEnable = true;

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
            if (targetText == null) return;

            targetText.OnPreRenderText += ApplyFade;
            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            if (targetText == null) return;
            targetText.OnPreRenderText -= ApplyFade;
            isPlaying = false;
            if (targetText.isActiveAndEnabled) targetText.ForceMeshUpdate();
        }

        /// <summary>从透明状态重新播放整句渐显，可由按钮、信号或其他脚本调用。</summary>
        public void Play()
        {
            elapsed = 0f;
            isPlaying = true;
            if (targetText != null && targetText.isActiveAndEnabled)
                targetText.ForceMeshUpdate();
        }

        /// <summary>结束效果，立即显示整句话。</summary>
        public void ShowAll()
        {
            isPlaying = false;
            if (targetText != null && targetText.isActiveAndEnabled)
                targetText.ForceMeshUpdate();
        }

        private void LateUpdate()
        {
            if (!isPlaying || targetText == null || !targetText.isActiveAndEnabled) return;

            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            targetText.ForceMeshUpdate();

            int visibleCount = CountVisibleCharacters(targetText.textInfo);
            int remaining = visibleCount;
            int groupSize = Mathf.Max(1, firstGroupSize);
            int increase = Mathf.Max(0, groupSizeIncrease);
            int groupCount = 0;
            while (remaining > 0)
            {
                remaining -= groupSize;
                groupCount++;
                groupSize += increase;
            }

            float endTime = Mathf.Max(0f, startDelay) +
                            Mathf.Max(0, groupCount - 1) * Mathf.Max(0f, groupInterval) +
                            Mathf.Max(0.01f, fadeDuration);
            if (elapsed >= endTime) ShowAll();
        }

        private void ApplyFade(TMP_TextInfo textInfo)
        {
            if (!isPlaying) return;

            int groupIndex = 0;
            int remainingInGroup = Mathf.Max(1, firstGroupSize);
            int increase = Mathf.Max(0, groupSizeIncrease);
            float interval = Mathf.Max(0f, groupInterval);
            float duration = Mathf.Max(0.01f, fadeDuration);
            float delay = Mathf.Max(0f, startDelay);

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[i];
                if (!character.isVisible) continue; // 空格和换行不占一组的名额。

                float progress = Mathf.Clamp01((elapsed - delay - groupIndex * interval) / duration);
                float opacity = Mathf.SmoothStep(0f, 1f, progress);
                Color32[] colors = textInfo.meshInfo[character.materialReferenceIndex].colors32;
                for (int corner = 0; corner < 4; corner++)
                {
                    int vertex = character.vertexIndex + corner;
                    Color32 color = colors[vertex];
                    color.a = (byte)Mathf.RoundToInt(color.a * opacity);
                    colors[vertex] = color;
                }

                remainingInGroup--;
                if (remainingInGroup != 0) continue;
                groupIndex++;
                remainingInGroup = Mathf.Max(1, firstGroupSize) + groupIndex * increase;
            }
        }

        private static int CountVisibleCharacters(TMP_TextInfo textInfo)
        {
            int count = 0;
            for (int i = 0; i < textInfo.characterCount; i++)
                if (textInfo.characterInfo[i].isVisible) count++;
            return count;
        }
    }
}
