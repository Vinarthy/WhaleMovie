using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AIBuilder.CreatureStage
{
    // 仅用于明确交出控制权的独立 Animator。调用方必须在退出、禁用和异常时 Dispose。
    public sealed class StagePoseSession : IDisposable
    {
        private static readonly HashSet<int> Owners = new HashSet<int>();
        private readonly int animatorId;
        private readonly Animator animator;
        private readonly Transform[] bones;
        private readonly Vector3[] positions;
        private readonly Quaternion[] rotations;
        private readonly Vector3[] scales;
        private readonly bool rootMotion;
        private readonly bool fireEvents;
        private readonly AnimatorCullingMode cullingMode;
        private readonly double length;
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private AnimationClipPlayable previousPlayable;
        private AnimationMixerPlayable mixer;
        private readonly double previousLength;
        private AnimationPlayableOutput output;
        private bool disposed;

        public StagePoseSession(Animator animator, AnimationClip clip, AnimationClip previousClip = null)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (!animator.enabled || !animator.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Animator 必须处于启用状态。");
            if (animator.runtimeAnimatorController != null || animator.hasBoundPlayables)
                throw new InvalidOperationException("Animator 仍有 Controller 或动画图控制，必须先显式交出控制权。");
            if (clip.legacy || clip.isHumanMotion || animator.isHuman || clip.length <= 0)
                throw new InvalidOperationException("当前原型只接受有效的 Generic 动画。");
            if (previousClip != null && (previousClip.legacy || previousClip.isHumanMotion || previousClip.length <= 0))
                throw new InvalidOperationException("过渡只接受有效的 Generic 动画。");
            animatorId = animator.GetInstanceID();
            if (!Owners.Add(animatorId)) throw new InvalidOperationException("Animator 已有剧情求值会话。");
            this.animator = animator;
            length = clip.length;
            previousLength = previousClip != null ? previousClip.length : 0;
            rootMotion = animator.applyRootMotion;
            fireEvents = animator.fireEvents;
            cullingMode = animator.cullingMode;
            bones = animator.GetComponentsInChildren<Transform>(true);
            positions = new Vector3[bones.Length];
            rotations = new Quaternion[bones.Length];
            scales = new Vector3[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                positions[i] = bones[i].localPosition;
                rotations[i] = bones[i].localRotation;
                scales[i] = bones[i].localScale;
            }
            try
            {
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("CreatureStage.AbsolutePose");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetSpeed(0);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                if (previousClip != null)
                {
                    previousPlayable = AnimationClipPlayable.Create(graph, previousClip);
                    previousPlayable.SetSpeed(0);
                    previousPlayable.SetApplyFootIK(false);
                    previousPlayable.SetApplyPlayableIK(false);
                    mixer = AnimationMixerPlayable.Create(graph, 2);
                    graph.Connect(previousPlayable, 0, mixer, 0);
                    graph.Connect(playable, 0, mixer, 1);
                    output.SetSourcePlayable(mixer);
                }
                else output.SetSourcePlayable(playable);
                graph.Play();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Evaluate(double sourceTime, double previousTime = 0, float weight = 1)
        {
            if (disposed) throw new ObjectDisposedException(nameof(StagePoseSession));
            if (!StageMotionPlan.IsFinite(sourceTime) || sourceTime < 0 || sourceTime > length)
                throw new ArgumentOutOfRangeException(nameof(sourceTime));
            if (animator == null || animator.runtimeAnimatorController != null)
                throw new InvalidOperationException("Animator 丢失或控制权发生变化。");
            // 未被动画写入的骨骼也回到基准，避免上一采样或外部修正影响 Seek。
            RestoreTransforms();
            playable.SetTime(sourceTime);
            if (mixer.IsValid())
            {
                if (!StageMotionPlan.IsFinite(previousTime) || previousTime < 0 || previousTime > previousLength ||
                    !StageMotionPlan.IsFinite(weight)) throw new ArgumentOutOfRangeException(nameof(previousTime));
                previousPlayable.SetTime(previousTime);
                mixer.SetInputWeight(0, 1 - Mathf.Clamp01(weight));
                mixer.SetInputWeight(1, Mathf.Clamp01(weight));
            }
            graph.Evaluate(0);
        }

        private void RestoreTransforms()
        {
            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].localPosition = positions[i];
                bones[i].localRotation = rotations[i];
                bones[i].localScale = scales[i];
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (graph.IsValid())
                {
                    if (output.IsOutputValid()) output.SetTarget(null);
                    graph.Destroy();
                }
                if (animator != null)
                {
                    animator.applyRootMotion = rootMotion;
                    animator.fireEvents = fireEvents;
                    animator.cullingMode = cullingMode;
                }
                RestoreTransforms();
            }
            finally { Owners.Remove(animatorId); }
        }
    }
}
