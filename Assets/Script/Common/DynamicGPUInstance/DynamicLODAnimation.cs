using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhaleMovie.Common
{
    /// <summary>Pause the root Animator or material vertex animation from LOD2 onward.</summary>
    [DisallowMultipleComponent]
    public sealed class DynamicLODAnimation : MonoBehaviour
    {
        [SerializeField] private bool manageAnimation = true;
        [Tooltip("Use the material float property 'timeScale' instead of Animator.")]
        [SerializeField] private bool vertexAnimation = false;

        private static readonly int TimeScaleId = Shader.PropertyToID("timeScale");
        private static readonly Dictionary<Material, PausedMaterial> pausedMaterials = new();
        private readonly List<MaterialBinding> materialBindings = new();
        private Animator animator;
        private bool paused;
        private bool vertexPaused;
        private bool previousKeepState;
        private bool previousWriteDefaults;

        public bool ManageAnimation => isActiveAndEnabled && manageAnimation;
        public bool IsVertexAnimation => vertexAnimation;
        public bool IsPaused => paused && (vertexPaused || animator != null && !animator.enabled);
        internal bool NeedsRestore => paused;
        internal Animator TargetAnimator => animator != null ? animator : animator = GetComponent<Animator>();
        internal event Action PauseStateChanged;

        internal void SetPaused(bool shouldPause)
        {
            if (!shouldPause || !ManageAnimation)
            {
                Restore();
                return;
            }
            if (paused)
                return;

            if (vertexAnimation)
            {
                CacheVertexMaterials();
                if (materialBindings.Count == 0)
                    return;
                SetVertexMaterials(true);
                vertexPaused = true;
            }
            else
            {
                Animator target = TargetAnimator;
                if (target == null || !target.enabled)
                    return;
                previousKeepState = target.keepAnimatorStateOnDisable;
                previousWriteDefaults = target.writeDefaultValuesOnDisable;
                target.keepAnimatorStateOnDisable = true;
                target.writeDefaultValuesOnDisable = false;
                target.enabled = false;
            }
            paused = true;
            PauseStateChanged?.Invoke();
        }

        internal void Restore()
        {
            if (!paused)
                return;

            if (vertexPaused)
                SetVertexMaterials(false);
            else if (animator != null)
            {
                animator.enabled = true;
                animator.keepAnimatorStateOnDisable = previousKeepState;
                animator.writeDefaultValuesOnDisable = previousWriteDefaults;
            }
            paused = vertexPaused = false;
            PauseStateChanged?.Invoke();
        }

        private void CacheVertexMaterials()
        {
            if (materialBindings.Count > 0)
                return;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Material[] original = renderer.sharedMaterials;
                Material[] frozen = (Material[])original.Clone();
                bool changed = false;
                for (int i = 0; i < original.Length; i++)
                {
                    Material source = original[i];
                    if (source == null || !source.HasProperty(TimeScaleId))
                        continue;
                    if (!pausedMaterials.TryGetValue(source, out PausedMaterial entry))
                    {
                        entry = new PausedMaterial
                        {
                            Material = new Material(source)
                            {
                                name = source.name + " (LOD paused)",
                                hideFlags = HideFlags.DontSave
                            }
                        };
                        entry.Material.SetFloat(TimeScaleId, 0f);
                        pausedMaterials.Add(source, entry);
                    }
                    entry.Users++;
                    frozen[i] = entry.Material;
                    changed = true;
                }
                if (changed)
                    materialBindings.Add(new MaterialBinding(renderer, original, frozen));
            }
        }

        private void SetVertexMaterials(bool freeze)
        {
            // Keep originals untouched: their saved timeScale is restored with the material references.
            foreach (MaterialBinding binding in materialBindings)
                if (binding.Renderer != null)
                    binding.Renderer.sharedMaterials = freeze ? binding.Frozen : binding.Original;
        }

        private void ReleaseVertexMaterials()
        {
            foreach (MaterialBinding binding in materialBindings)
            {
                for (int i = 0; i < binding.Original.Length; i++)
                {
                    Material source = binding.Original[i];
                    if (ReferenceEquals(source, binding.Frozen[i]) ||
                        !pausedMaterials.TryGetValue(source, out PausedMaterial entry) || --entry.Users > 0)
                        continue;
                    pausedMaterials.Remove(source);
                    if (Application.isPlaying)
                        Destroy(entry.Material);
                    else
                        DestroyImmediate(entry.Material);
                }
            }
            materialBindings.Clear();
        }

        private void OnDisable()
        {
            Restore();
            ReleaseVertexMaterials();
        }

        private void OnDestroy() => OnDisable();

        private sealed class PausedMaterial
        {
            public Material Material;
            public int Users;
        }

        private sealed class MaterialBinding
        {
            public readonly Renderer Renderer;
            public readonly Material[] Original;
            public readonly Material[] Frozen;

            public MaterialBinding(Renderer renderer, Material[] original, Material[] frozen)
            {
                Renderer = renderer;
                Original = original;
                Frozen = frozen;
            }
        }
    }
}
