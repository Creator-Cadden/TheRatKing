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
    public float acceleration  = 20f;
    public float deceleration  = 25f;
    public float gravity       = -9.81f;
    public float jumpForce     = 1.5f;
    public float rotationSpeed = 10f;

    [Header("Roll")]
    public float rollSpeed    = 12f;
    public float rollDuration = 0.35f;
    public float rollCooldown = 0.8f;

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

    // Animation
    private Animator animator;
    private bool jump;
    private bool fall;
    private bool contact;
    private bool ground;

    // ── Private State ──
    private CharacterController          _controller;
    private CinemachineInputAxisController _freeLookInput;
    private EntityStats                  _stats;

    private Vector2 _moveInput;
    private Vector3 _velocity;
    private Vector3 _currentMoveVelocity;
    private bool    _jumpPressed;
    private bool    _isGrounded;

    // Sprint is a held state set directly from input callbacks
    private bool    _sprintHeld;

    private bool    _isAiming;
    private float   _aimYaw;
    private float   _aimPitch;
    private Vector2 _lookDelta;

    // Roll
    private bool    _isRolling;
    private float   _lastRollTime = -999f;
    private Vector3 _rollDirection;

    private Coroutine _suppressCoroutine;

    void Start()
    {
        _controller    = GetComponent<CharacterController>();
        _freeLookInput = freeLookCamera.GetComponent<CinemachineInputAxisController>();
        _stats         = GetComponent<EntityStats>();

        freeLookCamera.Priority = activePriority;
        aimCamera.Priority      = defaultPriority;

        _aimYaw  = transform.eulerAngles.y;
        animator = GetComponentInChildren<Animator>();
        Debug.Log("Animator found: " + (animator != null ? animator.gameObject.name : "NULL"));
    }

    void Update()
    {
        _isGrounded = _controller.isGrounded;

        // Skip normal movement while rolling — roll coroutine drives position
        if (!_isRolling)
        {
            HandleMovement();
            HandleRotation();
        }

        HandleJumpAndGravity();

        if (_isAiming)
            DriveAimLook();

        // Animation
        if (_currentMoveVelocity == Vector3.zero)
            animator.SetFloat("Running", 0);
        else
            animator.SetFloat("Running", 1);
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

        // Sprint: held button + moving + not aiming + has stamina
        bool canSprint = _sprintHeld && !_isAiming && _moveInput.sqrMagnitude > 0.01f;

        if (canSprint && _stats != null)
        {
            // UseStaminaPerSecond returns false when stamina hits 0 — stop sprinting
            canSprint = _stats.UseStaminaPerSecond(_stats.playerStatBlock.sprintStaminaPerSecond);
        }

        float targetSpeed = canSprint ? sprintSpeed : walkSpeed;

        Vector3 targetVelocity = targetDirection * targetSpeed;

        float accelRate = targetDirection.sqrMagnitude > 0.01f ? acceleration : deceleration;

        float dot = Vector3.Dot(_currentMoveVelocity.normalized, targetVelocity.normalized);
        if (dot < 0.5f)
            _currentMoveVelocity = Vector3.Lerp(_currentMoveVelocity, targetVelocity, 15f * Time.deltaTime);
        else
            _currentMoveVelocity = Vector3.Lerp(_currentMoveVelocity, targetVelocity, accelRate * Time.deltaTime);

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

        bool contact = !ground && _velocity.y == -2f;
        bool falling = !ground && _velocity.y < -0.1f;

        animator.SetBool("Grounded", true);

        if (falling)
            animator.SetBool("Grounded", false);
        ground = false;

        animator.SetBool("Jump", false);
        jump = false;

        animator.SetBool("Falling", true);
        fall = false;

        animator.SetBool("Contact", false);

        if (contact)
            animator.SetBool("Contact", true);

        if (_jumpPressed && _isGrounded)
        {
            // Check stamina before allowing jump
            int jumpCost = _stats?.playerStatBlock?.jumpStaminaCost ?? 5;
            if (_stats != null && !_stats.UseStamina(jumpCost))
            {
                // Not enough stamina — cancel jump
                _jumpPressed = false;
            }
            else
            {
                _velocity.y  = Mathf.Sqrt(jumpForce * -2f * gravity);
                _jumpPressed = false;
                animator.SetBool("Jump", true);
            }
        }

        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    // ── Roll ──────────────────────────────────────────────────

    private void TryRoll()
    {
        if (_isRolling) return;
        if (_isAiming) return;
        if (Time.time < _lastRollTime + rollCooldown) return;

        // Check stamina before allowing roll
        int rollCost = _stats?.playerStatBlock?.rollStaminaCost ?? 12;
        if (_stats != null && !_stats.UseStamina(rollCost))
        {
            Debug.Log("[PlayerMovement] Not enough stamina to roll");
            return;
        }

        // Roll in the current WASD direction relative to camera;
        // if no input, roll forward relative to the character
        Vector3 camForward = Vector3.ProjectOnPlane(freeLookCamera.transform.forward, Vector3.up).normalized;
        Vector3 camRight   = Vector3.ProjectOnPlane(freeLookCamera.transform.right,   Vector3.up).normalized;

        Vector3 inputDir = camForward * _moveInput.y + camRight * _moveInput.x;

        _rollDirection = inputDir.sqrMagnitude > 0.01f
            ? inputDir.normalized
            : transform.forward;

        _lastRollTime = Time.time;
        StartCoroutine(RollCoroutine());
    }

    private IEnumerator RollCoroutine()
    {
        _isRolling = true;

        // Snap rotation to roll direction immediately so the animation looks right
        if (_rollDirection.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_rollDirection);

        // Optional: trigger a roll animation if you have one
        // animator.SetTrigger("Roll");

        float elapsed = 0f;
        while (elapsed < rollDuration)
        {
            float t     = elapsed / rollDuration;
            float speed = Mathf.Lerp(rollSpeed, 0f, t);

            _controller.Move(_rollDirection * speed * Time.deltaTime);

            elapsed += Time.deltaTime;
            yield return null;
        }

        _isRolling = false;
    }

    // ── Aim Look ──────────────────────────────────────────────

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

    // ── Input Callbacks ──────────────────────────────────────

    public void OnMove(InputValue value)   => _moveInput  = value.Get<Vector2>();
    public void OnJump(InputValue value)   { if (value.isPressed) _jumpPressed = true; }
    public void OnLook(InputValue value)   => _lookDelta  = value.Get<Vector2>();

    // Sprint: read as a held button — true while held, false when released
    public void OnSprint(InputValue value) => _sprintHeld = value.isPressed;

    // Roll: bind to your "Roll" action in the Input Action asset (e.g. Left Ctrl)
    public void OnRoll(InputValue value)
    {
        if (value.isPressed)
            TryRoll();
    }

    public void OnAim(InputValue value)
    {
        _isAiming = value.isPressed;

        if (_isAiming)
        {
            _aimYaw   = freeLookCamera.transform.eulerAngles.y;
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