using UnityEngine;
using UnityEngine.XR.Hands;

namespace WhaleMovie.GamePlay.G5
{
    /// <summary>Shows one animated jellyfish above each upward-facing tracked palm.</summary>
    [DisallowMultipleComponent]
    public sealed class JellyfishPalmSpawner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("掌心向上时生成的水母预制件。")]
        GameObject m_JellyfishPrefab;

        [SerializeField]
        [Tooltip("相对掌心关节的位置。局部 -Y 是掌心朝向。")]
        Vector3 m_PalmOffset = new Vector3(0f, -0.045f, 0f);

        [SerializeField]
        [Tooltip("相对掌心关节的旋转，使水母保持向上。")]
        Vector3 m_PalmRotation = new Vector3(0f, 0f, 180f);

        [SerializeField, Min(0.01f)]
        [Tooltip("在预制件原始尺寸基础上的缩放倍率。")]
        float m_SizeMultiplier = 0.5f;

        sealed class HandState
        {
            public bool palmUp;
            public Transform holder;
            public float nextSearchTime;
        }

        readonly HandState m_Left = new HandState();
        readonly HandState m_Right = new HandState();

        void Awake()
        {
            if (m_JellyfishPrefab == null)
                m_JellyfishPrefab = Resources.Load<GameObject>("Prefeb/Jellyfish3 Animated 03");

            if (m_JellyfishPrefab == null)
                Debug.LogError("找不到水母预制件 Jellyfish3 Animated 03。", this);
        }

        /// <summary>Connect to PalmUpDetector's On Palm Up Started event.</summary>
        public void OnPalmUpStarted(Handedness hand)
        {
            var state = GetState(hand);
            if (state == null)
                return;

            state.palmUp = true;
            TrySpawn(hand, state);
        }

        /// <summary>Connect to PalmUpDetector's On Palm Up Ended event.</summary>
        public void OnPalmUpEnded(Handedness hand)
        {
            var state = GetState(hand);
            if (state == null)
                return;

            state.palmUp = false;
            Remove(state);
        }

        void Update()
        {
            RetryIfNeeded(Handedness.Left, m_Left);
            RetryIfNeeded(Handedness.Right, m_Right);
        }

        void OnDisable()
        {
            m_Left.palmUp = false;
            m_Right.palmUp = false;
            Remove(m_Left);
            Remove(m_Right);
        }

        HandState GetState(Handedness hand)
        {
            switch (hand)
            {
                case Handedness.Left: return m_Left;
                case Handedness.Right: return m_Right;
                default: return null;
            }
        }

        void RetryIfNeeded(Handedness hand, HandState state)
        {
            if (!state.palmUp || state.holder != null || Time.unscaledTime < state.nextSearchTime)
                return;

            state.nextSearchTime = Time.unscaledTime + 0.25f;
            TrySpawn(hand, state);
        }

        bool TrySpawn(Handedness hand, HandState state)
        {
            if (m_JellyfishPrefab == null || state.holder != null)
                return false;

            var palm = FindTrackedPalm(hand);
            if (palm == null)
                return false;

            var holder = new GameObject($"{hand} Palm Jellyfish").transform;
            holder.SetParent(palm, false);
            holder.localPosition = m_PalmOffset;
            holder.localRotation = Quaternion.Euler(m_PalmRotation);

            var jellyfish = Instantiate(m_JellyfishPrefab, holder, false);
            // This prefab was saved with a non-zero scene position. It must not
            // become an offset from the tracked palm.
            jellyfish.transform.localPosition = Vector3.zero;
            jellyfish.transform.localScale = Vector3.Scale(
                m_JellyfishPrefab.transform.localScale,
                Vector3.one * Mathf.Max(0.01f, m_SizeMultiplier));
            CenterVisibleModelOverPalm(jellyfish, holder);

            state.holder = holder;
            return true;
        }

        static void CenterVisibleModelOverPalm(GameObject jellyfish, Transform holder)
        {
            var renderers = jellyfish.GetComponentsInChildren<Renderer>();
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return;

            var centerInPalm = holder.InverseTransformPoint(bounds.center);
            jellyfish.transform.localPosition = new Vector3(-centerInPalm.x, 0f, -centerInPalm.z);
        }

        static Transform FindTrackedPalm(Handedness hand)
        {
            var camera = Camera.main;
            Transform closestPalm = null;
            var closestDistance = float.PositiveInfinity;

            foreach (var driver in FindObjectsOfType<XRHandSkeletonDriver>(true))
            {
                if (!driver.isActiveAndEnabled || driver.handTrackingEvents == null ||
                    driver.handTrackingEvents.handedness != hand ||
                    !driver.handTrackingEvents.handIsTracked ||
                    driver.jointTransformReferences == null)
                    continue;

                foreach (var joint in driver.jointTransformReferences)
                {
                    if (joint.xrHandJointID != XRHandJointID.Palm || joint.jointTransform == null)
                        continue;

                    if (camera == null)
                        return joint.jointTransform;

                    var distance = (joint.jointTransform.position - camera.transform.position).sqrMagnitude;
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestPalm = joint.jointTransform;
                    }
                }
            }

            return closestPalm;
        }

        static void Remove(HandState state)
        {
            if (state.holder != null)
            {
                state.holder.gameObject.SetActive(false);
                Destroy(state.holder.gameObject);
            }

            state.holder = null;
        }
    }
}
