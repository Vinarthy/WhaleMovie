using System.Collections.Generic;
using UnityEngine;

namespace WhaleMovie.Art
{
    /// <summary>运行时让鲸鱼材质的 Rim Strength 从 0 渐变到目标值。</summary>
    [DisallowMultipleComponent]
    public sealed class WhaleRimStrengthFade : MonoBehaviour
    {
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");

        [Header("目标")]
        [Tooltip("可指定鲸鱼的渲染器；留空时自动查找当前物体及其子物体中带 Rim Strength 属性的渲染器。")]
        [SerializeField] private Renderer targetRenderer;

        [Header("渐变")]
        [Tooltip("开启后鲸鱼启用时立即开始；关闭后保持 0，等待 Timeline 信号调用 Play()。")]
        [SerializeField] private bool playOnEnable;

        [Tooltip("从 0 渐变到目标值所用的秒数。")]
        [Min(0f)]
        [SerializeField] private float duration = 2f;

        [Tooltip("渐变结束时的 Rim Strength。")]
        [Range(0f, 5f)]
        [SerializeField] private float targetStrength = 2.71f;

        private readonly List<TargetMaterial> targets = new List<TargetMaterial>();
        private MaterialPropertyBlock propertyBlock;
        private float elapsed;
        private bool isPlaying;

        private struct TargetMaterial
        {
            public Renderer renderer;
            public int materialIndex;

            public TargetMaterial(Renderer renderer, int materialIndex)
            {
                this.renderer = renderer;
                this.materialIndex = materialIndex;
            }
        }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            FindTargets();
        }

        private void OnEnable()
        {
            if (playOnEnable)
            {
                Play();
            }
            else if (Application.isPlaying)
            {
                // 等待 Timeline 信号时，先把鲸鱼的边缘光保持在 0。
                if (propertyBlock == null)
                    propertyBlock = new MaterialPropertyBlock();
                if (targets.Count == 0)
                    FindTargets();
                ApplyStrength(0f);
            }
        }

        private void OnDisable()
        {
            isPlaying = false;
        }

        private void Update()
        {
            if (!isPlaying)
                return;

            elapsed += Time.deltaTime;
            float progress = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            ApplyStrength(Mathf.Lerp(0f, targetStrength, easedProgress));

            if (progress >= 1f)
                isPlaying = false;
        }

        /// <summary>从 0 重新播放渐变，也可由 Timeline 事件或其他脚本调用。</summary>
        public void Play()
        {
            if (!Application.isPlaying)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();
            if (targets.Count == 0)
                FindTargets();

            if (targets.Count == 0)
            {
                Debug.LogWarning("未找到使用 _RimStrength 的鲸鱼渲染器。", this);
                return;
            }

            elapsed = 0f;
            ApplyStrength(0f);
            isPlaying = true;
        }

        private void FindTargets()
        {
            targets.Clear();
            Renderer[] renderers = targetRenderer != null
                ? new[] { targetRenderer }
                : GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    if (materials[index] != null && materials[index].HasProperty(RimStrengthId))
                        targets.Add(new TargetMaterial(renderer, index));
                }
            }
        }

        private void ApplyStrength(float value)
        {
            foreach (TargetMaterial target in targets)
            {
                if (target.renderer == null)
                    continue;

                // 读取原属性块，保留其他脚本已设置的渲染参数。
                target.renderer.GetPropertyBlock(propertyBlock, target.materialIndex);
                propertyBlock.SetFloat(RimStrengthId, value);
                target.renderer.SetPropertyBlock(propertyBlock, target.materialIndex);
            }
        }
    }
}
