using UnityEngine;

namespace WhaleMovie.Art
{
    [DisallowMultipleComponent]
    public sealed class ArtObjectShake : MonoBehaviour
    {
        [Header("播放")]
        [Tooltip("一次完整抖动的总时长（秒），包含逐渐增强和最后衰减停止的阶段。")]
        [Min(0.1f)]
        public float duration = 5f;

        [Tooltip("进入 Play 模式后是否在 Start 时自动开始抖动。关闭后仍可通过 Play() 手动开始。")]
        public bool playOnStart = true;

        [Header("幅度变化（归一化时间比例）")]
        [Tooltip("慢慢增强阶段的结束位置，占总时长的比例。0.55 表示在总时长的 55% 处进入快速增强阶段。")]
        [Range(0.1f, 0.85f)]
        public float slowBuildEnd = 0.55f;

        [Tooltip("快速增强阶段的结束位置，占总时长的比例。之后抖动幅度会缓慢衰减到零。应大于 Slow Build End。")]
        [Range(0.2f, 0.98f)]
        public float fastBuildEnd = 0.68f;

        [Tooltip("刚开始时的抖动强度，占最大强度的比例。0.025 表示最大抖动强度的 2.5%。")]
        [Range(0f, 0.2f)]
        public float startingIntensity = 0.025f;

        [Tooltip("慢慢增强阶段结束、快速增强开始时的强度，占最大强度的比例。")]
        [Range(0f, 0.8f)]
        public float intensityBeforeSurge = 0.18f;

        [Header("最大抖动")]
        [Tooltip("达到最大强度时的局部位置偏移，X/Y/Z 分别以 Unity 单位表示。")]
        public Vector3 peakPositionAmplitude = new Vector3(0.06f, 0.06f, 0.02f);

        [Tooltip("达到最大强度时的局部旋转偏移，X/Y/Z 分别以角度表示。")]
        public Vector3 peakRotationAmplitude = new Vector3(3f, 3f, 2f);

        [Tooltip("抖动刚开始时的振动频率（次/秒）；数值越低，抖动越慢。")]
        [Min(0.1f)]
        public float startingFrequency = 2f;

        [Tooltip("抖动增强到最大强度附近时的振动频率（次/秒）；数值越高，抖动越快。")]
        [Min(0.1f)]
        public float peakFrequency = 18f;

        [Tooltip("用于确定各方向振动的相位偏移。相同种子会产生相同的抖动节奏。")]
        public int phaseSeed = 17;

        // 开始抖动前记录的局部位置和旋转，结束或停止时恢复到这里。
        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;

        // 标记基准姿势是否已记录，避免在首次使用前读取未初始化的状态。
        private bool hasBaseline;

        // 当前是否正在播放抖动，以及从开始播放后经过的时间（秒）。
        private bool isShaking;
        private float elapsed;

        private void Awake()
        {
            CaptureBaseline();
        }

        private void Start()
        {
            if (playOnStart)
                Play();
        }

        private void Update()
        {
            if (!Application.isPlaying || !isShaking)
                return;

            elapsed += Time.deltaTime;
            EvaluateAt(elapsed);

            if (elapsed >= duration)
                isShaking = false;
        }

        public void Play()
        {
            EnsureBaseline();
            elapsed = 0f;
            isShaking = true;
            EvaluateAt(0f);
        }

        public void StopShake()
        {
            isShaking = false;
            elapsed = 0f;
            RestoreBaseline();
        }

        /// <summary>
        /// Sets an absolute pose for a given time. The editor baker calls this
        /// directly, so baking does not require Play Mode or Time.deltaTime.
        /// </summary>
        public void EvaluateAt(float time)
        {
            EnsureBaseline();

            if (duration <= 0f || time >= duration)
            {
                RestoreBaseline();
                return;
            }

            time = Mathf.Max(0f, time);
            float normalizedTime = Mathf.Clamp01(time / duration);
            float intensity = EvaluateIntensity(normalizedTime);
            float frequency = Mathf.Lerp(startingFrequency, peakFrequency, intensity);
            float phase = time * frequency * Mathf.PI * 2f + phaseSeed * 0.731f;

            Vector3 positionWave = new Vector3(
                Mathf.Sin(phase),
                Mathf.Sin(phase * 1.13f + 1.7f),
                Mathf.Sin(phase * 0.87f + 3.1f));

            Vector3 rotationWave = new Vector3(
                Mathf.Sin(phase * 1.07f + 0.4f),
                Mathf.Sin(phase * 0.91f + 2.2f),
                Mathf.Sin(phase * 1.19f + 4.1f));

            transform.localPosition =
                baseLocalPosition + Vector3.Scale(positionWave, peakPositionAmplitude) * intensity;

            Vector3 rotationOffset =
                Vector3.Scale(rotationWave, peakRotationAmplitude) * intensity;
            transform.localRotation = baseLocalRotation * Quaternion.Euler(rotationOffset);
        }

        public void CaptureBaseline()
        {
            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
            hasBaseline = true;
        }

        private float EvaluateIntensity(float normalizedTime)
        {
            float slowEnd = Mathf.Clamp(slowBuildEnd, 0.05f, 0.9f);
            float surgeEnd = Mathf.Clamp(fastBuildEnd, slowEnd + 0.05f, 0.99f);

            if (normalizedTime < slowEnd)
            {
                float progress = normalizedTime / slowEnd;
                return Mathf.Lerp(
                    startingIntensity,
                    intensityBeforeSurge,
                    Mathf.SmoothStep(0f, 1f, progress));
            }

            if (normalizedTime < surgeEnd)
            {
                float progress = (normalizedTime - slowEnd) / (surgeEnd - slowEnd);
                return Mathf.Lerp(
                    intensityBeforeSurge,
                    1f,
                    Mathf.SmoothStep(0f, 1f, progress));
            }

            float decayProgress = (normalizedTime - surgeEnd) / (1f - surgeEnd);
            return 1f - Mathf.SmoothStep(0f, 1f, decayProgress);
        }

        private void EnsureBaseline()
        {
            if (!hasBaseline)
                CaptureBaseline();
        }

        private void RestoreBaseline()
        {
            EnsureBaseline();
            transform.localPosition = baseLocalPosition;
            transform.localRotation = baseLocalRotation;
        }
    }
}