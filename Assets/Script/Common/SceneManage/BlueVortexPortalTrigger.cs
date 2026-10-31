using UnityEngine;

namespace WhaleMovie.SceneManage
{
    /// <summary>Transfers the XR player from Aquarium to Jellyfish through this portal.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BlueVortexPortalTrigger : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("头显进入传送门中心区域时触发的半径（传送门局部坐标）。")]
        float m_HeadTriggerRadius = 0.75f;

        [SerializeField, Min(0.01f)]
        [Tooltip("头显距传送门平面的最大深度（传送门局部坐标）。")]
        float m_HeadTriggerHalfDepth = 0.15f;

        bool m_RequestedTransition;
        BoxCollider m_Trigger;

        void Awake()
        {
            m_Trigger = GetComponent<BoxCollider>();
            m_Trigger.isTrigger = true;
        }

        void Update()
        {
            // Some XR setups move the tracked headset without moving a physics
            // collider. The trigger remains visible/editable, while this check
            // makes passing through it reliable in both VR and the simulator.
            var camera = Camera.main;
            if (camera == null || !camera.isActiveAndEnabled)
                return;

            var local = transform.InverseTransformPoint(camera.transform.position);
            if (Mathf.Abs(local.z) > m_HeadTriggerHalfDepth)
                return;

            var radiusSquared = m_HeadTriggerRadius * m_HeadTriggerRadius;
            if (local.x * local.x + local.y * local.y <= radiusSquared)
                TryTransition();
        }

        void OnTriggerEnter(Collider other)
        {
            var camera = Camera.main;
            if (camera == null || !camera.isActiveAndEnabled)
                return;

            // Accept the XR body collider or a collider attached to the headset.
            // Hand and unrelated scene colliders should not send the player away.
            var hit = other.transform;
            if (camera.transform.IsChildOf(hit) || hit.IsChildOf(camera.transform))
                TryTransition();
        }

        void TryTransition()
        {
            if (m_RequestedTransition)
                return;

            var flow = ContentFlowManager.Instance;
            if (flow == null || flow.IsTransitioning || flow.CurrentContentId != ContentId.Aquarium)
                return;

            m_RequestedTransition = true;
            flow.SwitchTo(ContentId.Jellyfish);
        }
    }
}
