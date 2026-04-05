using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR FIELDS
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Movement")]
    public float speed         = 6f;
    public float gravity       = -9.81f;
    public float jumpForce     = 1.5f;
    public float rotationSpeed = 10f;

    [Header("Cinemachine Cameras")]
    public CinemachineCamera freeLookCamera;
    public CinemachineCamera aimCamera;

    [Header("Aim Rig Transforms")]
    // Hierarchy under Player:
    //   CameraRoot (child of Player, stays at 0,0,0)
    //     CameraPitch (child of CameraRoot, Y = ~1.6 head height)
    //       ShoulderPos (child of CameraPitch, X = 0.6, Z = -2.2)
    //
    // Aim CinemachineCamera:
    //   Tracking Target  = ShoulderPos
    //   Position Control = None
    //   Rotation Control = None
    public Transform cameraPitch;    // the node that rotates on X (up/down)
    public Transform shoulderPos;    // the aim cam's Tracking Target

    [Header("Aim Feel")]
    public float aimSensitivity = 0.15f;
    [Range(-60f, 0f)]
    public float pitchMin = -40f;
    [Range(0f, 80f)]
    public float pitchMax = 60f;

    [Header("Camera Priorities")]
    public int defaultPriority = 10;
    public int activePriority  = 20;

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE STATE
    // ─────────────────────────────────────────────────────────────────────────

    private CharacterController _controller;
    private CinemachineInputAxisController _freeLookInput;

    // movement
    private Vector2 _moveInput;
    private Vector3 _velocity;
    private bool    _jumpPressed;
    private bool    _isGrounded;

    // aim
    private bool    _isAiming;
    private float   _aimYaw;      // drives player body Y rotation
    private float   _aimPitch;    // drives cameraPitch X rotation
    private Vector2 _lookDelta;

    private Coroutine _suppressCoroutine;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        _controller    = GetComponent<CharacterController>();
        _freeLookInput = freeLookCamera.GetComponent<CinemachineInputAxisController>();

        freeLookCamera.Priority = activePriority;
        aimCamera.Priority      = defaultPriority;

        _aimYaw = transform.eulerAngles.y;
    }

    void Update()
    {
        _isGrounded = _controller.isGrounded;

        HandleMovement();
        HandleRotation();
        HandleJumpAndGravity();

        if (_isAiming)
            DriveAimLook();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MOVEMENT
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        Vector3 camForward;
        Vector3 camRight;

        if (_isAiming)
        {
            // Move relative to player facing during aim so shoulder
            // offset doesn't cause drift when strafing
            camForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            camRight   = Vector3.ProjectOnPlane(transform.right,   Vector3.up).normalized;
        }
        else
        {
            camForward = Vector3.ProjectOnPlane(freeLookCamera.transform.forward, Vector3.up).normalized;
            camRight   = Vector3.ProjectOnPlane(freeLookCamera.transform.right,   Vector3.up).normalized;
        }

        Vector3 move = camForward * _moveInput.y + camRight * _moveInput.x;
        _controller.Move(move * speed * Time.deltaTime);
    }

    private void HandleRotation()
    {
        if (_isAiming)
        {
            // DriveAimLook() handles this — nothing needed here
        }
        else
        {
            Vector3 camForward = Vector3.ProjectOnPlane(freeLookCamera.transform.forward, Vector3.up).normalized;
            Vector3 camRight   = Vector3.ProjectOnPlane(freeLookCamera.transform.right,   Vector3.up).normalized;

            Vector3 move = camForward * _moveInput.y + camRight * _moveInput.x;
            move.y = 0f;

            if (move.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(move);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }
    }

    private void HandleJumpAndGravity()
    {
        if (_isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;

        if (_jumpPressed && _isGrounded)
        {
            _velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
            _jumpPressed = false;
        }

        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AIM LOOK
    // The rig works like this every frame:
    //   Mouse X → _aimYaw  → rotates the Player root (Y axis only)
    //                         CameraRoot inherits this — it's a child
    //   Mouse Y → _aimPitch → rotates CameraPitch (X axis only)
    //                         ShoulderPos inherits this — it's a child of CameraPitch
    //   Aim cam tracks ShoulderPos with Position=None, Rotation=None
    //   so it sits exactly at ShoulderPos in world space, no extra math
    // ─────────────────────────────────────────────────────────────────────────

    private void DriveAimLook()
    {
        _aimYaw   += _lookDelta.x * aimSensitivity * 60f * Time.deltaTime;
        _aimPitch -= _lookDelta.y * aimSensitivity * 60f * Time.deltaTime;
        _aimPitch  = Mathf.Clamp(_aimPitch, pitchMin, pitchMax);

        // Rotate the whole player on Y — camera rig is a child so it
        // orbits with the player, keeping the shoulder offset locked
        transform.rotation = Quaternion.Euler(0f, _aimYaw, 0f);

        // Rotate only the pitch node on X — ShoulderPos tilts with it,
        // so the camera looks up and down from the shoulder position
        cameraPitch.localRotation = Quaternion.Euler(_aimPitch, 0f, 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FREELOOK INPUT SUPPRESSION
    // ─────────────────────────────────────────────────────────────────────────

    private void SuppressFreeLookInput(bool suppress)
    {
        if (_freeLookInput == null) return;

        if (_suppressCoroutine != null)
            StopCoroutine(_suppressCoroutine);

        _suppressCoroutine = StartCoroutine(SetFreeLookEnabledNextFrame(!suppress));
    }

    private IEnumerator SetFreeLookEnabledNextFrame(bool enabled)
    {
        yield return null;
        if (_freeLookInput != null)
            _freeLookInput.enabled = enabled;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INPUT CALLBACKS
    // ─────────────────────────────────────────────────────────────────────────

    public void OnMove(InputValue value)
    {
        _moveInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (value.isPressed)
            _jumpPressed = true;
    }

    public void OnLook(InputValue value)
    {
        _lookDelta = value.Get<Vector2>();
    }

    public void OnAim(InputValue value)
    {
        _isAiming = value.isPressed;

        if (_isAiming)
        {
            // Seed from current player facing so nothing snaps on blend-in
            _aimYaw   = transform.eulerAngles.y;
            _aimPitch = cameraPitch.localEulerAngles.x;
            if (_aimPitch > 180f) _aimPitch -= 360f;

            aimCamera.Priority      = activePriority;
            freeLookCamera.Priority = defaultPriority;

            SuppressFreeLookInput(true);
        }
        else
        {
            freeLookCamera.Priority = activePriority;
            aimCamera.Priority      = defaultPriority;

            SuppressFreeLookInput(false);
        }
    }
}