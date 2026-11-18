using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace WhaleMovie.Interact
{
    /// <summary>
    /// Ports the museum's reverse-hand-stroke swimming to XR Hands and XR Origin.
    /// Enable this scene-owned component through Timeline; it never moves the tracked camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSwimController : MonoBehaviour
    {
        [Header("玩家（留空时自动查找常驻 XR Origin）")]
        [SerializeField] private XROrigin xrOrigin;

        [Header("划水")]
        [SerializeField, Min(0f)] private float swimSpeedMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float maxSwimSpeed = 3f;
        [SerializeField, Min(0f)] private float handVelocityThreshold = 0.1f;
        [SerializeField, Min(0f)] private float handTrackingMultiplier = 1.5f;
        [Tooltip("没有裸手追踪时，按住对应手柄的扳机划动也可以游泳。")]
        [SerializeField] private bool allowControllers = true;
        [SerializeField, Min(0f)] private float handMovementMultiplier = 2f;

        [Header("惯性")]
        [SerializeField] private bool enableInertia = true;
        [SerializeField, Min(0f)] private float waterDragFactor = 1f;
        [SerializeField, Min(0f)] private float minSpeedThreshold = 0.05f;

        [Header("自动移动优先")]
        [Tooltip("这些组件启用时暂停游泳；用于让 DivingPoint 的 XRRigMove 优先完成移动。")]
        [SerializeField] private Behaviour[] scriptedMovements = new Behaviour[0];

        private readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        private XRHandSubsystem handSubsystem;
        private Transform player;
        private Transform trackingSpace;
        private CharacterController characterController;
        private InputDevice leftController;
        private InputDevice rightController;
        private HandSample leftSample;
        private HandSample rightSample;
        private Vector3 momentum;
        private float nextLookup;

        private struct HandSample
        {
            public bool valid;
            public bool fromHand;
            public Vector3 position;
        }

        private void OnEnable()
        {
            nextLookup = 0f;
            ResetMotion();
        }

        private void OnDisable() => ResetMotion();

        private void Update()
        {
            if (Time.unscaledTime >= nextLookup)
            {
                FindTracking();
                nextLookup = Time.unscaledTime + 0.5f;
            }

            if (player == null || trackingSpace == null || xrOrigin == null ||
                !xrOrigin.isActiveAndEnabled || ScriptedMoveActive() || Time.deltaTime <= 0f)
            {
                ResetMotion();
                return;
            }

            bool leftTracked = TryReadPalm(true, out Vector3 left, out bool leftIsHand);
            bool rightTracked = TryReadPalm(false, out Vector3 right, out bool rightIsHand);
            Vector3 stroke = SampleVelocity(ref leftSample, leftTracked, leftIsHand, left, Time.unscaledDeltaTime);
            stroke += SampleVelocity(ref rightSample, rightTracked, rightIsHand, right, Time.unscaledDeltaTime);
            if (!leftTracked && !rightTracked)
            {
                ResetMotion();
                return;
            }

            // Tracking-local differences exclude our own locomotion, avoiding positive feedback.
            stroke = trackingSpace.TransformDirection(-stroke);
            Vector3 velocity = Integrate(stroke, Time.deltaTime);
            Vector3 displacement = velocity * Time.deltaTime;
            if (characterController != null && characterController.enabled)
                characterController.Move(displacement);
            else
                player.position += displacement;
        }

        private void FindTracking()
        {
            if (xrOrigin == null || !xrOrigin.isActiveAndEnabled)
            {
                XROrigin found = Camera.main != null ? Camera.main.GetComponentInParent<XROrigin>() : null;
                if (found == null)
                    foreach (XROrigin candidate in FindObjectsOfType<XROrigin>())
                        if (candidate.isActiveAndEnabled) { found = candidate; break; }
                if (xrOrigin != found) ResetMotion();
                xrOrigin = found;
            }

            if (xrOrigin != null)
            {
                player = xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;
                trackingSpace = xrOrigin.CameraFloorOffsetObject != null
                    ? xrOrigin.CameraFloorOffsetObject.transform : player;
                characterController = player.GetComponent<CharacterController>();
            }

            if (handSubsystem == null || !handSubsystem.running)
            {
                handSubsystem = null;
                SubsystemManager.GetSubsystems(subsystems);
                foreach (XRHandSubsystem candidate in subsystems)
                    if (candidate.running) { handSubsystem = candidate; break; }
            }
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        private bool TryReadPalm(bool left, out Vector3 position, out bool fromHand)
        {
            fromHand = false;
            position = default;
            if (handSubsystem != null && handSubsystem.running)
            {
                XRHand hand = left ? handSubsystem.leftHand : handSubsystem.rightHand;
                if (hand.isTracked && hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose pose))
                {
                    position = pose.position;
                    fromHand = true;
                    return true;
                }
            }

            InputDevice device = left ? leftController : rightController;
            return allowControllers && device.isValid &&
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed &&
                device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
        }

        private Vector3 SampleVelocity(ref HandSample sample, bool tracked, bool fromHand,
            Vector3 position, float deltaTime)
        {
            bool continuous = tracked && sample.valid && sample.fromHand == fromHand;
            Vector3 delta = position - sample.position;
            sample.valid = tracked;
            sample.fromHand = fromHand;
            sample.position = position;
            // Seed after acquisition/source switches, and reject tracking jumps or long stalls.
            if (!continuous || deltaTime <= 0f || deltaTime > 0.2f) return Vector3.zero;
            Vector3 velocity = delta / deltaTime;
            float speed = velocity.magnitude;
            if (speed < handVelocityThreshold || speed > 6f) return Vector3.zero;
            return velocity * (fromHand ? handTrackingMultiplier : handMovementMultiplier);
        }

        private Vector3 Integrate(Vector3 stroke, float deltaTime)
        {
            stroke = Vector3.ClampMagnitude(stroke, 2f) * swimSpeedMultiplier;
            if (!enableInertia) return Vector3.ClampMagnitude(stroke, maxSwimSpeed);
            // Integrate by elapsed time instead of adding a full velocity every frame.
            momentum = Vector3.ClampMagnitude(momentum + stroke * deltaTime, maxSwimSpeed);
            momentum *= Mathf.Exp(-waterDragFactor * deltaTime);
            if (stroke.sqrMagnitude == 0f && momentum.magnitude < minSpeedThreshold)
                momentum = Vector3.zero;
            return momentum;
        }

        private bool ScriptedMoveActive()
        {
            foreach (Behaviour movement in scriptedMovements)
                if (movement != null && movement.isActiveAndEnabled) return true;
            return false;
        }

        private void ResetMotion()
        {
            momentum = Vector3.zero;
            leftSample = default;
            rightSample = default;
        }
    }
}
