using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

namespace WhaleMovie.Interact
{
    /// <summary>
    /// Automatically watches XRHandTrackingEvents in the loaded scene and reports
    /// transitions into and out of the configured fist hand shape.
    /// </summary>
    public sealed class FistGestureDetector : MonoBehaviour
    {
        [Serializable]
        public sealed class HandednessEvent : UnityEvent<Handedness> { }

        [SerializeField]
        [Tooltip("Assign Assets/Script/Interact/HandShape/Fist Hand Shape.asset.")]
        XRHandShape m_FistHandShape;

        [SerializeField, Min(0.1f)]
        [Tooltip("How often to search for hand tracking components, including hands created later at runtime.")]
        float m_HandSearchInterval = 1f;

        [SerializeField]
        HandednessEvent m_OnFistStarted = new HandednessEvent();

        [SerializeField]
        HandednessEvent m_OnFistEnded = new HandednessEvent();

        readonly List<HandBinding> m_Bindings = new List<HandBinding>();

        sealed class HandBinding
        {
            public XRHandTrackingEvents source;
            public Handedness handedness;
            public bool isFist;
            public UnityAction<XRHandJointsUpdatedEventArgs> jointsUpdated;
            public UnityAction trackingAcquired;
            public UnityAction trackingLost;
        }

        /// <summary>Raised once when a tracked hand starts matching the fist shape.</summary>
        public HandednessEvent onFistStarted => m_OnFistStarted;

        /// <summary>Raised once when a tracked hand stops matching the fist shape.</summary>
        public HandednessEvent onFistEnded => m_OnFistEnded;

        void OnEnable()
        {
            if (m_FistHandShape == null)
            {
                Debug.LogError(
                    "FistGestureDetector: 请在 Inspector 中将 Fist Hand Shape.asset 拖到 Fist Hand Shape 字段。",
                    this);
            }

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
                if (handEvents != null && !IsBound(handEvents))
                    Bind(handEvents);
            }

            for (var i = m_Bindings.Count - 1; i >= 0; --i)
            {
                var binding = m_Bindings[i];
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
            binding.trackingAcquired = () => OnTrackingAcquired(binding);
            binding.trackingLost = () => OnTrackingLost(binding);

            handEvents.jointsUpdated.AddListener(binding.jointsUpdated);
            handEvents.trackingAcquired.AddListener(binding.trackingAcquired);
            handEvents.trackingLost.AddListener(binding.trackingLost);
            m_Bindings.Add(binding);

            if (handEvents.handIsTracked)
                OnTrackingAcquired(binding);
        }

        void Unbind(HandBinding binding)
        {
            if (binding.source != null)
            {
                binding.source.jointsUpdated.RemoveListener(binding.jointsUpdated);
                binding.source.trackingAcquired.RemoveListener(binding.trackingAcquired);
                binding.source.trackingLost.RemoveListener(binding.trackingLost);
            }

            if (binding.isFist)
                SetFistState(binding, false, "追踪组件移除");
        }

        void OnJointsUpdated(HandBinding binding, XRHandJointsUpdatedEventArgs eventArgs)
        {
            if (m_FistHandShape == null || binding.source == null || !binding.source.handIsTracked)
                return;

            var detected = m_FistHandShape.CheckConditions(eventArgs);
            if (detected != binding.isFist)
                SetFistState(binding, detected, detected ? "手势成立" : "手指已松开");
        }

        void OnTrackingAcquired(HandBinding binding)
        {
            if (binding.source == null)
                return;

            Debug.Log($"{GetHandName(binding.source.handedness)}手追踪已获取。", binding.source);
        }

        void OnTrackingLost(HandBinding binding)
        {
            if (binding.source == null)
                return;

            if (binding.isFist)
                SetFistState(binding, false, "手部追踪丢失");

            Debug.Log($"{GetHandName(binding.source.handedness)}手追踪已丢失。", binding.source);
        }

        void SetFistState(HandBinding binding, bool isFist, string reason)
        {
            var wasFist = IsHandFist(binding.handedness);
            binding.isFist = isFist;
            var handedness = binding.handedness;
            var handIsFist = IsHandFist(handedness);
            if (wasFist == handIsFist)
                return;

            var handName = GetHandName(handedness);

            if (handIsFist)
            {
                Debug.Log($"检测到{handName}手握拳。", binding.source);
                m_OnFistStarted.Invoke(handedness);
            }
            else
            {
                Debug.Log($"{handName}手握拳状态结束（{reason}）。", binding.source);
                m_OnFistEnded.Invoke(handedness);
            }
        }

        bool IsHandFist(Handedness handedness)
        {
            foreach (var binding in m_Bindings)
            {
                if (binding.handedness == handedness && binding.isFist)
                    return true;
            }

            return false;
        }

        static string GetHandName(Handedness handedness)
        {
            switch (handedness)
            {
                case Handedness.Left: return "左";
                case Handedness.Right: return "右";
                default: return "未知";
            }
        }
    }
}
