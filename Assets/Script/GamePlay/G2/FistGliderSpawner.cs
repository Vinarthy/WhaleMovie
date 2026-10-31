using UnityEngine;
using UnityEngine.XR.Hands;

namespace WhaleMovie.GamePlay.G2
{
    /// <summary>
    /// Keeps one animated glider on the first fist. If that hand opens while the
    /// other is still a fist, the same glider moves to the remaining hand.
    /// Bind the FistGestureDetector's handedness events to the public methods.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FistGliderSpawner : MonoBehaviour
    {
        [SerializeField] GameObject m_GliderPrefab;
        [SerializeField] string m_GripRendererName = "抓手_防滑握持段";
        [SerializeField] Vector3 m_LeftPalmOffset;
        [SerializeField] Vector3 m_LeftPalmRotation;
        [SerializeField] Vector3 m_RightPalmOffset;
        [SerializeField] Vector3 m_RightPalmRotation;

        bool m_LeftFist;
        bool m_RightFist;
        Handedness m_Owner = Handedness.Invalid;
        Transform m_Holder;
        Renderer m_GripRenderer;
        float m_NextPalmSearch;

        void OnEnable()
        {
            if (m_GliderPrefab == null)
                m_GliderPrefab = Resources.Load<GameObject>("Prefeb/滑翔伞动画");

            if (m_GliderPrefab == null)
                Debug.LogError("找不到滑翔伞动画预制体。", this);
        }

        public void OnFistStarted(Handedness hand)
        {
            if (!SetFist(hand, true) || m_Holder != null)
                return;

            TryAttachTo(hand);
        }

        public void OnFistEnded(Handedness hand)
        {
            if (!SetFist(hand, false))
                return;

            if (m_Owner != hand)
                return;

            var other = hand == Handedness.Left ? Handedness.Right : Handedness.Left;
            if (IsFist(other) && TryAttachTo(other))
                return;

            DestroyHeldGlider();
        }

        void Update()
        {
            // The XR rig can arrive after this scene has loaded. Retry while a
            // fist is held, without creating a second glider.
            if (m_Holder != null || (!m_LeftFist && !m_RightFist) ||
                Time.unscaledTime < m_NextPalmSearch)
                return;

            m_NextPalmSearch = Time.unscaledTime + 0.25f;
            if (m_LeftFist && TryAttachTo(Handedness.Left))
                return;

            if (m_RightFist)
                TryAttachTo(Handedness.Right);
        }

        void OnDisable()
        {
            m_LeftFist = false;
            m_RightFist = false;
            DestroyHeldGlider();
        }

        bool SetFist(Handedness hand, bool value)
        {
            switch (hand)
            {
                case Handedness.Left:
                    m_LeftFist = value;
                    return true;
                case Handedness.Right:
                    m_RightFist = value;
                    return true;
                default:
                    return false;
            }
        }

        bool IsFist(Handedness hand) =>
            hand == Handedness.Left ? m_LeftFist : m_RightFist;

        bool TryAttachTo(Handedness hand)
        {
            if (m_GliderPrefab == null)
                return false;

            var palm = FindTrackedPalm(hand);
            if (palm == null)
                return false;

            if (m_Holder == null)
            {
                m_Holder = new GameObject("滑翔伞握持跟随").transform;
                m_Holder.SetParent(palm, false);
                var glider = Instantiate(m_GliderPrefab, m_Holder, false);
                m_GripRenderer = FindGripRenderer(glider);
                if (m_GripRenderer == null)
                {
                    Debug.LogError($"滑翔伞预制体中找不到握把网格：{m_GripRendererName}", this);
                    DestroyHeldGlider();
                    return false;
                }
            }
            else
            {
                m_Holder.SetParent(palm, false);
            }

            // The holder sits outside the prefab's Animator hierarchy, so the
            // imported animation cannot overwrite its hand-following pose.
            m_Holder.localPosition = Vector3.zero;
            m_Holder.localRotation = Quaternion.identity;
            var gripInHolder = m_Holder.InverseTransformPoint(m_GripRenderer.bounds.center);
            var rotation = Quaternion.Euler(hand == Handedness.Left
                ? m_LeftPalmRotation : m_RightPalmRotation);
            var offset = hand == Handedness.Left ? m_LeftPalmOffset : m_RightPalmOffset;
            m_Holder.localRotation = rotation;
            m_Holder.localPosition = offset - rotation * gripInHolder;
            m_Owner = hand;
            return true;
        }

        Renderer FindGripRenderer(GameObject glider)
        {
            foreach (var renderer in glider.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == m_GripRendererName)
                    return renderer;
            }

            return null;
        }

        static Transform FindTrackedPalm(Handedness hand)
        {
            var camera = Camera.main;
            Transform nearestPalm = null;
            var nearestDistance = float.PositiveInfinity;
            foreach (var driver in FindObjectsOfType<XRHandSkeletonDriver>(true))
            {
                if (!driver.isActiveAndEnabled || driver.handTrackingEvents == null ||
                    driver.handTrackingEvents.handedness != hand ||
                    !driver.handTrackingEvents.handIsTracked ||
                    driver.jointTransformReferences == null)
                    continue;

                foreach (var joint in driver.jointTransformReferences)
                {
                    if (joint.xrHandJointID == XRHandJointID.Palm && joint.jointTransform != null)
                    {
                        if (camera == null)
                            return joint.jointTransform;

                        var distance = (joint.jointTransform.position - camera.transform.position).sqrMagnitude;
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearestPalm = joint.jointTransform;
                        }
                    }
                }
            }

            return nearestPalm;
        }

        void DestroyHeldGlider()
        {
            if (m_Holder != null)
            {
                // Destroy is deferred until the end of the frame. Hide the old
                // instance immediately so another fist cannot show two gliders.
                m_Holder.gameObject.SetActive(false);
                Destroy(m_Holder.gameObject);
            }

            m_Holder = null;
            m_GripRenderer = null;
            m_Owner = Handedness.Invalid;
        }
    }
}
