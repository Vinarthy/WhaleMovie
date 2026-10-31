using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

namespace WhaleMovie.Interact
{
    /// <summary>Detects when either tracked XR hand has its palm facing world up.</summary>
    public sealed class PalmUpDetector : MonoBehaviour
    {
        [Serializable]
        public sealed class HandednessEvent : UnityEvent<Handedness> { }

        [SerializeField, Range(0f, 89f)]
        [Tooltip("手掌朝上进入判定的最大偏角（度）。")]
        float m_EnterAngle = 50f;

        [SerializeField, Range(0f, 89f)]
        [Tooltip("已判定朝上后，超过此偏角才结束；应大于进入角度，避免抖动。")]
        float m_ExitAngle = 65f;

        [SerializeField, Min(0.1f)]
        [Tooltip("自动搜索场景中 XRHandTrackingEvents 的间隔（秒）。")]
        float m_HandSearchInterval = 1f;

        [SerializeField]
        HandednessEvent m_OnPalmUpStarted = new HandednessEvent();

        [SerializeField]
        HandednessEvent m_OnPalmUpEnded = new HandednessEvent();

        readonly List<HandBinding> m_Bindings = new List<HandBinding>();

        sealed class HandBinding
        {
            public XRHandTrackingEvents source;
            public Handedness handedness;
            public bool isPalmUp;
            public UnityAction<XRHandJointsUpdatedEventArgs> jointsUpdated;
            public UnityAction trackingLost;
        }

        public HandednessEvent onPalmUpStarted => m_OnPalmUpStarted;
        public HandednessEvent onPalmUpEnded => m_OnPalmUpEnded;

        /// <summary>Current state for a left or right hand.</summary>
        public bool IsPalmUp(Handedness handedness)
        {
            foreach (var binding in m_Bindings)
            {
                if (binding.handedness == handedness && binding.isPalmUp)
                    return true;
            }

            return false;
        }

        void OnValidate()
        {
            m_ExitAngle = Mathf.Max(m_EnterAngle, m_ExitAngle);
            m_HandSearchInterval = Mathf.Max(0.1f, m_HandSearchInterval);
        }

        void OnEnable()
        {
            StartCoroutine(FindHandsRoutine());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            foreach (var binding in m_Bindings)
                Unbind(binding);
            m_Bindings.Clear();
        }

        IEnumerator FindHandsRoutine()
        {
            var wait = new WaitForSeconds(Mathf.Max(0.1f, m_HandSearchInterval));
            while (true)
            {
                FindAndBindHands();
                yield return wait;
            }
        }

        void FindAndBindHands()
        {
            var foundHands = FindObjectsOfType<XRHandTrackingEvents>(true);
            foreach (var handEvents in foundHands)
            {
                if (handEvents != null &&
                    (handEvents.handedness == Handedness.Left || handEvents.handedness == Handedness.Right) &&
                    !IsBound(handEvents))
                    Bind(handEvents);
            }

            for (var i = m_Bindings.Count - 1; i >= 0; --i)
            {
                var binding = m_Bindings[i];
                if (binding.source == null || !binding.source.isActiveAndEnabled ||
                    !Array.Exists(foundHands, hand => hand == binding.source))
                    SetPalmUpState(binding, false, "手部追踪不可用");

                if (binding.source == null || !Array.Exists(foundHands, hand => hand == binding.source))
                {
                    Unbind(binding);
                    m_Bindings.RemoveAt(i);
                }
            }
        }

        bool IsBound(XRHandTrackingEvents handEvents)
        {
            foreach (var binding in m_Bindings)
            {
                if (binding.source == handEvents)
                    return true;
            }

            return false;
        }

        void Bind(XRHandTrackingEvents handEvents)
        {
            var binding = new HandBinding
            {
                source = handEvents,
                handedness = handEvents.handedness
            };
            binding.jointsUpdated = args => OnJointsUpdated(binding, args);
            binding.trackingLost = () => SetPalmUpState(binding, false, "手部追踪丢失");
            handEvents.jointsUpdated.AddListener(binding.jointsUpdated);
            handEvents.trackingLost.AddListener(binding.trackingLost);
            m_Bindings.Add(binding);
        }

        void Unbind(HandBinding binding)
        {
            if (binding.source != null)
            {
                binding.source.jointsUpdated.RemoveListener(binding.jointsUpdated);
                binding.source.trackingLost.RemoveListener(binding.trackingLost);
            }

            SetPalmUpState(binding, false, "追踪组件移除");
        }

        void OnJointsUpdated(HandBinding binding, XRHandJointsUpdatedEventArgs args)
        {
            if (binding.source == null || !binding.source.isActiveAndEnabled ||
                !binding.source.handIsTracked || !args.hand.isTracked)
            {
                SetPalmUpState(binding, false, "手部追踪不可用");
                return;
            }

            // XR Hands defines the hand's palm direction as the wrist/root pose's local -Y.
            var palmNormal = args.hand.rootPose.rotation * Vector3.down;
            var dot = Vector3.Dot(palmNormal, Vector3.up);
            var angle = binding.isPalmUp ? m_ExitAngle : m_EnterAngle;
            var isPalmUp = dot >= Mathf.Cos(angle * Mathf.Deg2Rad);
            SetPalmUpState(binding, isPalmUp, "手掌已转离上方");
        }

        void SetPalmUpState(HandBinding binding, bool isPalmUp, string endReason)
        {
            if (binding.isPalmUp == isPalmUp)
                return;

            var wasPalmUp = IsPalmUp(binding.handedness);
            binding.isPalmUp = isPalmUp;
            var nowPalmUp = IsPalmUp(binding.handedness);
            if (wasPalmUp == nowPalmUp)
                return;

            var handName = binding.handedness == Handedness.Left ? "左" : "右";
            if (nowPalmUp)
            {
                Debug.Log($"检测到{handName}手手掌朝上。", this);
                m_OnPalmUpStarted.Invoke(binding.handedness);
            }
            else
            {
                Debug.Log($"{handName}手手掌朝上状态结束（{endReason}）。", this);
                m_OnPalmUpEnded.Invoke(binding.handedness);
            }
        }
    }
}
