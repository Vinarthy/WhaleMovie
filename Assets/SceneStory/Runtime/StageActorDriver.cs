using System;
using UnityEngine;

namespace AIBuilder.CreatureStage
{
    [AddComponentMenu("AIBuilder/角色剧情/剧情角色")]
    public sealed class StageActorDriver : MonoBehaviour
    {
        public Animator animator;
        public StageActorProfile profile;
        [InspectorName("路径线颜色"), ColorUsage(false)]
        public Color routeColor = new Color(0.2f, 0.9f, 0.7f);
        private object owner;
        private StagePoseSession session;
        private AnimationClip activeClip;
        private AnimationClip activePrevious;
        private Vector3 initialPosition;
        private Quaternion initialRotation;

        public void Acquire(object claimant)
        {
            if (owner != null && !ReferenceEquals(owner, claimant)) throw new InvalidOperationException("同一角色不能被两条表演轨同时控制。");
            if (owner != null) return;
            if (animator == null || animator.transform == transform || !animator.transform.IsChildOf(transform))
                throw new InvalidOperationException("Animator 必须在独立 ActorRoot 的子节点中。");
            var scale = transform.lossyScale;
            if ((scale - Vector3.one).sqrMagnitude > 0.00001)
                throw new InvalidOperationException("ActorRoot 世界比例必须为 1。");
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            owner = claimant;
        }

        public void Evaluate(object claimant, AnimationClip clip, double sourceTime, Vector3 position, Quaternion rotation,
            AnimationClip previous = null, double previousTime = 0, float weight = 1)
        {
            Acquire(claimant);
            try
            {
                if (activeClip != clip || activePrevious != previous || session == null)
                {
                    session?.Dispose();
                    session = null;
                    activeClip = null;
                    session = new StagePoseSession(animator, clip, previous);
                    activeClip = clip;
                    activePrevious = previous;
                }
                transform.SetPositionAndRotation(position, rotation);
                session.Evaluate(sourceTime, previousTime, weight);
            }
            catch { Release(claimant); throw; }
        }

        public void EvaluateMovement(object claimant, Vector3 position, Quaternion rotation)
        {
            Acquire(claimant);
            // 原生 Animation Track 独占子节点 Animator，路线轨仅写角色根节点。
            if (session != null)
            {
                session.Dispose(); session = null; activeClip = null; activePrevious = null;
            }
            transform.SetPositionAndRotation(position, rotation);
        }

        public void Release(object claimant)
        {
            if (!ReferenceEquals(owner, claimant) || owner == null) return;
            try { session?.Dispose(); }
            finally
            {
                session = null;
                activeClip = null;
                activePrevious = null;
                transform.SetPositionAndRotation(initialPosition, initialRotation);
                owner = null;
            }
        }

        private void OnDisable() { if (owner != null) Release(owner); }
    }
}
