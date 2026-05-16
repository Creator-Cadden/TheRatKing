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

    [Header("Animators")]
    [SerializeField] private Animator _primaryAnimator;
    [SerializeField] private Animator _secondaryAnimator;

    // ── Private State ──
    private CharacterController            _controller;
    private CinemachineInputAxisController _freeLookInput;
    private CinemachineOrbitalFollow       _orbitalFollow;   // <-- NEW: direct ref to sync yaw on aim-exit
    private EntityStats                    _stats;

    private float _baseWalkSpeed;
    private float _baseSprintSpeed;

    private Vector2 _moveInput;
    private Vector3 _velocity;
    private Vector3 _currentMoveVelocity;
    private bool    _jumpPressed;
    private bool    _isGrounded;
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

    // Animation state
    private bool jump;
    private bool fall;
    private bool contact;
    private bool ground;

    // ─────────────────────────────────────────
    void Awake()
    {
        // Cache base speeds in Awake so they are set before ANY Start() runs.
        // EntityStats.Start() calls InitStats() which calls ApplySpeedBonus()
        // on this component — if we wait until our own Start() to cache these,
        // EntityStats.Start() may run first and ApplySpeedBonus multiplies from 0.
        _baseWalkSpeed   = walkSpeed;
        _baseSprintSpeed = sprintSpeed;

        Debug.Log($"[PlayerMovement] Awake — base speeds cached: walk:{_baseWalkSpeed}  sprint:{_baseSprintSpeed}");

        if (_baseWalkSpeed <= 0f)
        {
            Debug.LogWarning("[PlayerMovement] walkSpeed is 0 in Awake — using safe defaults. " +
                             "Make sure walkSpeed is set in the Inspector.");
            _baseWalkSpeed   = 6f;
            _baseSprintSpeed = 10f;
            walkSpeed        = _baseWalkSpeed;
            sprintSpeed      = _baseSprintSpeed;
        }
    }

    void Start()
    {
        _controller    = GetComponent<CharacterController>();
        _freeLookInput = freeLookCamera.GetComponent<CinemachineInputAxisController>();
        _stats         = GetComponent<EntityStats>();

        // Cache the OrbitalFollow so we can write HorizontalAxis.Value on aim-exit
        _orbitalFollow = freeLookCamera.GetComponent<CinemachineOrbitalFollow>();
        if (_orbitalFollow == null)
            Debug.LogWarning("[PlayerMovement] CinemachineOrbitalFollow not found on freeLookCamera. " +
                             "Aim-exit yaw sync will not work.");

        freeLookCamera.Priority = activePriority;
        aimCamera.Priority      = defaultPriority;

        _aimYaw = transform.eulerAngles.y;
    }

    void Update()
    {
        _isGrounded = _controller.isGrounded;

        if (!_isRolling)
        {
            HandleMovement();
            HandleRotation();
        }

        HandleJumpAndGravity();

        if (_isAiming)
            DriveAimLook();

        SetFloat("Running", _currentMoveVelocity == Vector3.zero ? 0f : 1f);
    }

    // ─────────────────────────────────────────
    // Speed stat integration
    // ─────────────────────────────────────────

    /// <summary>
    /// Called by EntityStats on init, stat spend, and weapon swap.
    /// hammerFraction = 1.0 normally, 0.667 while hammer is equipped.
    /// </summary>
    public void ApplySpeedBonus(int bonusPoints, float bonusPerPoint, float weaponFraction = 1f)
    {
        float walkBonus   = bonusPoints * bonusPerPoint;
        float sprintBonus = bonusPoints * bonusPerPoint * 1.33f;

        walkSpeed   = (_baseWalkSpeed   + walkBonus) * weaponFraction;
        sprintSpeed = (_baseSprintSpeed + sprintBonus) * weaponFraction;

        Debug.Log($"[PlayerMovement] Speed updated — walk:{walkSpeed:F1}  sprint:{sprintSpeed:F1}  weaponFraction:{weaponFraction:F2}");
    }

    // ─────────────────────────────────────────
    // Movement
    // ─────────────────────────────────────────

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

        bool canSprint = _sprintHeld && !_isAiming && _moveInput.sqrMagnitude > 0.01f;
        if (canSprint && _stats != null)
            canSprint = _stats.UseStaminaPerSecond(_stats.playerStatBlock.sprintStaminaPerSecond);

        // Bow aim penalty — reduce move speed by bowAimMoveSpeedFraction while aiming
        float weaponAimFraction = 1f;
        if (_isAiming && _stats != null &&
            _stats.EquippedWeapon == EntityStats.WeaponType.Bow &&
            _stats.playerStatBlock != null)
        {
            weaponAimFraction = _stats.playerStatBlock.bowAimMoveSpeedFraction;
        }

        float targetSpeed      = (canSprint ? sprintSpeed : walkSpeed) * weaponAimFraction;
        Vector3 targetVelocity = targetDirection * targetSpeed;

        // Temporary debug — remove once movement is confirmed working
        if (_moveInput.sqrMagnitude > 0.01f)
            Debug.Log($"[PlayerMovement] moveInput:{_moveInput}  targetSpeed:{targetSpeed:F2}  " +
                      $"walk:{walkSpeed:F2}  targetVel:{targetVelocity}");
        float accelRate        = targetDirection.sqrMagnitude > 0.01f ? acceleration : deceleration;
        float dot              = Vector3.Dot(_currentMoveVelocity.normalized, targetVelocity.normalized);
        float lerpRate         = dot < 0.5f ? 15f : accelRate;

        _currentMoveVelocity = Vector3.Lerp(_currentMoveVelocity, targetVelocity, lerpRate * Time.deltaTime);
        _controller.Move(_currentMoveVelocity * Time.deltaTime);
    }

    private void HandleRotation()
    {
        if (_isAiming) return;

        Vector3 camForward = Vector3.ProjectOnPlane(freeLookCamera.transform.forward, Vector3.up).normalized;
        Vector3 camRight   = Vector3.ProjectOnPlane(freeLookCamera.transform.right,   Vector3.up).normalized;
        Vector3 move       = (camForward * _moveInput.y + camRight * _moveInput.x);
        move.y = 0f;

        if (move.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(move);
            transform.rotation   = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private void HandleJumpAndGravity()
    {
        if (_isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;

        bool landedThisFrame = !ground && _velocity.y == -2f;
        bool falling         = !ground && _velocity.y < -0.1f;

        SetBool("Grounded", true);
        if (falling) SetBool("Grounded", false);
        ground = false;

        SetBool("Jump",    false);
        SetBool("Falling", true);
        SetBool("Contact", false);
        if (landedThisFrame) SetBool("Contact", true);

        if (_jumpPressed && _isGrounded)
        {
            int jumpCost = _stats?.playerStatBlock?.jumpStaminaCost ?? 5;
            if (_stats != null && !_stats.UseStamina(jumpCost))
            {
                _jumpPressed = false;
            }
            else
            {
                _velocity.y  = Mathf.Sqrt(jumpForce * -2f * gravity);
                _jumpPressed = false;
                SetBool("Jump", true);
            }
        }

        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    // ─────────────────────────────────────────
    // Roll
    // ─────────────────────────────────────────

    private void TryRoll()
    {
        if (_isRolling) return;
        if (_isAiming)  return;
        if (Time.time < _lastRollTime + rollCooldown) return;

        int rollCost = _stats?.playerStatBlock?.rollStaminaCost ?? 12;
        if (_stats != null && !_stats.UseStamina(rollCost))
        {
            Debug.Log("[PlayerMovement] Not enough stamina to roll");
            return;
        }

        Vector3 camForward = Vector3.ProjectOnPlane(freeLookCamera.transform.forward, Vector3.up).normalized;
        Vector3 camRight   = Vector3.ProjectOnPlane(freeLookCamera.transform.right,   Vector3.up).normalized;
        Vector3 inputDir   = camForward * _moveInput.y + camRight * _moveInput.x;

        _rollDirection = inputDir.sqrMagnitude > 0.01f ? inputDir.normalized : transform.forward;
        _lastRollTime  = Time.time;
        StartCoroutine(RollCoroutine());
    }

    private IEnumerator RollCoroutine()
    {
        _isRolling = true;

        if (_rollDirection.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_rollDirection);

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

    // ─────────────────────────────────────────
    // Aim Look
    // ─────────────────────────────────────────

    private void DriveAimLook()
    {
        _aimYaw   += _lookDelta.x * aimSensitivity * 60f * Time.deltaTime;
        _aimPitch -= _lookDelta.y * aimSensitivity * 60f * Time.deltaTime;
        _aimPitch  = Mathf.Clamp(_aimPitch, pitchMin, pitchMax);

        transform.rotation        = Quaternion.Euler(0f, _aimYaw, 0f);
        cameraPitch.localRotation = Quaternion.Euler(_aimPitch, 0f, 0f);
    }

    /// <summary>
    /// Writes the current aim yaw back into the OrbitalFollow's horizontal axis
    /// so the free look camera resumes from exactly where aim left off.
    /// Without this, the orbit snaps back to wherever it was when you entered aim.
    /// </summary>
    private void SyncFreeLookYawToAim()
    {
        if (_orbitalFollow == null) return;

        // HorizontalAxis.Value is the orbit angle in degrees.
        // Setting it here repositions the camera instantly on the same frame
        // that freeLookCamera regains priority, preventing any visible snap.
        _orbitalFollow.HorizontalAxis.Value = _aimYaw;
    }

    private void SuppressFreeLookInput(bool suppress)
    {
        if (_freeLookInput == null) return;
        if (_suppressCoroutine != null) StopCoroutine(_suppressCoroutine);
        _suppressCoroutine = StartCoroutine(SetFreeLookEnabledNextFrame(!suppress));
    }

    private IEnumerator SetFreeLookEnabledNextFrame(bool enabled)
    {
        yield return null;
        if (_freeLookInput != null)
            _freeLookInput.enabled = enabled;
    }

    // ─────────────────────────────────────────
    // Animator helpers
    // ─────────────────────────────────────────

    private void SetBool(string param, bool value)
    {
        _primaryAnimator?.SetBool(param, value);
        _secondaryAnimator?.SetBool(param, value);
    }

    private void SetFloat(string param, float value)
    {
        _primaryAnimator?.SetFloat(param, value);
        _secondaryAnimator?.SetFloat(param, value);
    }

    // ─────────────────────────────────────────
    // Input Callbacks
    // ─────────────────────────────────────────

    public void OnMove(InputValue value)   => _moveInput  = value.Get<Vector2>();
    public void OnJump(InputValue value)   { if (value.isPressed) _jumpPressed = true; }
    public void OnLook(InputValue value)   => _lookDelta  = value.Get<Vector2>();
    public void OnSprint(InputValue value) => _sprintHeld = value.isPressed;

    public void OnRoll(InputValue value)
    {
        if (value.isPressed) TryRoll();
    }

    public void OnAim(InputValue value)
    {
        _isAiming = value.isPressed;

        if (_isAiming)
        {
            // Entering aim — read the free look camera's current yaw so aim
            // starts from exactly where the orbit camera is pointing
            _aimYaw   = freeLookCamera.transform.eulerAngles.y;
            _aimPitch = cameraPitch.localEulerAngles.x;
            if (_aimPitch > 180f) _aimPitch -= 360f;

            aimCamera.Priority      = activePriority;
            freeLookCamera.Priority = defaultPriority;
            SuppressFreeLookInput(true);
        }
        else
        {
            // Exiting aim — sync the orbit back to aim's final yaw BEFORE
            // restoring priority so there is zero visible snap on the transition
            SyncFreeLookYawToAim();

            freeLookCamera.Priority = activePriority;
            aimCamera.Priority      = defaultPriority;
            SuppressFreeLookInput(false);
        }
    }
}