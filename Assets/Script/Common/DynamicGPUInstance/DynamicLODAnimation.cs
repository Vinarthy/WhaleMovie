using UnityEngine;

namespace WhaleMovie.Common
{
    /// <summary>Opt in to pausing the parent Animator from LOD2 onward.</summary>
    [DisallowMultipleComponent]
    public sealed class DynamicLODAnimation : MonoBehaviour
    {
        [SerializeField] private bool manageAnimation = true;

        private Animator animator;
        private bool paused;
        private bool previousKeepState;
        private bool previousWriteDefaults;

        public bool ManageAnimation => isActiveAndEnabled && manageAnimation;
        public bool IsPaused => paused && animator != null && !animator.enabled;
        internal Animator TargetAnimator => FindAnimator();

        internal void SetPaused(bool shouldPause)
        {
            if (!ManageAnimation)
                shouldPause = false;

            Animator target = FindAnimator();
            if (target == null)
                return;

            if (shouldPause)
            {
                if (paused || !target.enabled)
                    return;

                previousKeepState = target.keepAnimatorStateOnDisable;
                previousWriteDefaults = target.writeDefaultValuesOnDisable;
                target.keepAnimatorStateOnDisable = true;
                target.writeDefaultValuesOnDisable = false;
                target.enabled = false;
                paused = true;
            }
            else
            {
                Restore();
            }
        }

        internal void Restore()
        {
            if (!paused)
                return;

            if (animator != null)
            {
                animator.enabled = true;
                animator.keepAnimatorStateOnDisable = previousKeepState;
                animator.writeDefaultValuesOnDisable = previousWriteDefaults;
            }
            paused = false;
        }

        private Animator FindAnimator()
        {
            if (animator == null)
                animator = GetComponentInParent<Animator>();
            return animator;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
