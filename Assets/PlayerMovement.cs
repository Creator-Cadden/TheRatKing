using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed     = 6f;
    public float sprintSpeed   = 10f;
    public float acceleration  = 8f;
    public float deceleration  = 10f;
    public float sprintDrag    = 2f;
    public float gravity       = -9.81f;
    public float jumpForce     = 1.5f;
    public float rotationSpeed = 10f;

    [Header("Cinemachine Cameras")]
    public CinemachineCamera freeLookCamera;
    public CinemachineCamera aimCamera;

    [Header("Aim Rig Transforms")]
    public Transform cameraPitch;
    public Transform shoulderPos;

    [Header("Aim Feel")]
    public float aimSensitivity = 0.15f;
    [Range(-60f, 0f)]
    public float pitchMin = -40f;
    [Range(0f, 80f)]
    public float pitchMax = 60f;

    [Header("Camera Priorities")]
    public int defaultPriority = 10;
    public int activePriority  = 20;

    // ── Private State ──
    private CharacterController _controller;
    private CinemachineInputAxisController _freeLookInput;

    private Vector2 _moveInput;
    private Vector3 _velocity;
    private Vector3 _currentMoveVelocity;
    private bool    _jumpPressed;
    private bool    _isGrounded;
    private bool    _isSprinting;

    private bool    _isAiming;
    private float   _aimYaw;
    private float   _aimPitch;
    private Vector2 _lookDelta;

    private Coroutine _suppressCoroutine;

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

    private void HandleMovement()
    {
        Vector3 camForward;
        Vector3 camRight;

        if (_isAiming)
        {
            camForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            camRight   = Vector3.ProjectOnPlane(transform.right,   Vector3.up).normalized;
        }
        else
        {
            camForward = Vector3.ProjectOnPlane(freeLookCamera.transform.forward, Vector3.up).normalized;
            camRight   = Vector3.ProjectOnPlane(freeLookCamera.transform.right,   Vector3.up).normalized;
        }

        Vector3 targetDirection = camForward * _moveInput.y + camRight * _moveInput.x;

        // Can't sprint while aiming
        float targetSpeed = (_isSprinting && !_isAiming && _moveInput.y > 0.1f)
            ? sprintSpeed
            : walkSpeed;

        Vector3 targetVelocity = targetDirection * targetSpeed;

        // Accelerate toward target, decelerate when no input
        float accelRate = targetDirection.sqrMagnitude > 0.01f ? acceleration : deceleration;

        // Apply extra drag when sprinting to prevent instant top speed
        if (_isSprinting && _currentMoveVelocity.magnitude > walkSpeed)
            accelRate -= sprintDrag;

        _currentMoveVelocity = Vector3.MoveTowards(
            _currentMoveVelocity,
            targetVelocity,
            accelRate * Time.deltaTime
        );

        _controller.Move(_currentMoveVelocity * Time.deltaTime);
    }

    private void HandleRotation()
    {
        if (!_isAiming)
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
            _velocity.y  = Mathf.Sqrt(jumpForce * -2f * gravity);
            _jumpPressed = false;
        }

        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    private void DriveAimLook()
    {
        _aimYaw   += _lookDelta.x * aimSensitivity * 60f * Time.deltaTime;
        _aimPitch -= _lookDelta.y * aimSensitivity * 60f * Time.deltaTime;
        _aimPitch  = Mathf.Clamp(_aimPitch, pitchMin, pitchMax);

        transform.rotation        = Quaternion.Euler(0f, _aimYaw, 0f);
        cameraPitch.localRotation = Quaternion.Euler(_aimPitch, 0f, 0f);
    }

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

    // ── Input Callbacks ──
    public void OnMove(InputValue value)  => _moveInput  = value.Get<Vector2>();
    public void OnJump(InputValue value)  { if (value.isPressed) _jumpPressed = true; }
    public void OnLook(InputValue value)  => _lookDelta  = value.Get<Vector2>();
    public void OnSprint(InputValue value) => _isSprinting = value.isPressed;

    public void OnAim(InputValue value)
    {
        _isAiming = value.isPressed;

        if (_isAiming)
        {
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