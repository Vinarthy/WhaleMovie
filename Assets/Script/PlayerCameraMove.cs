using UnityEngine;

//负责控制玩家移动和视角旋转逻辑
public class PlayerCameraMove : MonoBehaviour
{
    public Transform cameraTransform;
    public float moveSpeed = 4f;
    public float gravity = -9.81f;
    public float mouseSensitivity = 2.5f;
    public float minPitch = -70f;
    public float maxPitch = 75f;
    public Vector3 cameraLocalPosition = new Vector3(0f, 1.6f, -0.1f);
    public bool inputEnabled = true;

    private CharacterController controller;
    private float verticalVelocity;
    private float pitch;

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
            verticalVelocity = 0f;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform != null && cameraTransform.parent != transform)
            cameraTransform.SetParent(transform, false);

        if (cameraTransform != null)
        {
            cameraTransform.localPosition = cameraLocalPosition;
            cameraTransform.localRotation = Quaternion.identity;
        }
    }

    private void Update()
    {
        if (!inputEnabled)
            return;

        RotateView();
        MovePlayer();
    }

    private void RotateView()
    {
        if (!Input.GetMouseButton(0) || cameraTransform == null)
            return;

        float yaw = Input.GetAxis("Mouse X") * mouseSensitivity;
        float pitchDelta = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up, yaw, Space.World);
        pitch = Mathf.Clamp(pitch - pitchDelta, minPitch, maxPitch);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void MovePlayer()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 movement = transform.right * horizontal + transform.forward * vertical;

        if (movement.sqrMagnitude > 1f)
            movement.Normalize();

        if (controller != null && controller.enabled)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -1f;

            verticalVelocity += gravity * Time.deltaTime;
            movement = movement * moveSpeed + Vector3.up * verticalVelocity;
            controller.Move(movement * Time.deltaTime);
        }
        else
        {
            transform.position += movement * moveSpeed * Time.deltaTime;
        }
    }
}
