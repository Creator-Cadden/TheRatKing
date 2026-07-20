using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Player locomotion: camera-relative walk/sprint, jump, dodge roll, gravity,
/// and stamina spend (costs from PlayerStatBlock via EntityStats).
/// Fires movement animator params on the rat body animator AND the active weapon's
/// animator (resolved through WeaponModelSwapper.ActiveWeaponAnimator).
/// </summary>
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
    [Tooltip("The rat BODY animator — drives running, jumping, attack pose, etc. " +
             "This is always fed every animator parameter PlayerMovement sets.")]
    [SerializeField] private Animator _primaryAnimator;

    // The currently-equipped weapon's animator is fetched dynamically from
    // WeaponModelSwapper.ActiveWeaponAnimator so it always matches whichever
    // weapon is in the player's hand right now. No serialized field needed.
    private WeaponModelSwapper _swapper;

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

    // Knockback (from boss attacks, contact damage, etc.)
    private Vector3 _knockbackDir;         // unit-length push direction
    private float   _knockbackForce;       // peak speed at t = 0 of the push
    private float   _knockbackTimer;       // remaining seconds of push
    private float   _knockbackInitialTimer;// total duration of this push

    private Coroutine _suppressCoroutine;

    // Animation state
    private bool jump;
    private bool fall;
    private bool contact;
    private bool ground;

    void Awake()
    {
        // Cache base speeds in Awake so they are set before ANY Start() runs.
        // EntityStats.Start() calls InitStats() which calls ApplySpeedBonus()
        // on this component — if we wait until our own Start() to cache these,
        // EntityStats.Start() may run first and ApplySpeedBonus multiplies from 0.
        _baseWalkSpeed   = walkSpeed;
        _baseSprintSpeed = sprintSpeed;

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
        _swapper       = GetComponent<WeaponModelSwapper>();

        // Cache the OrbitalFollow so we can write HorizontalAxis.Value on aim-exit
        _orbitalFollow = freeLookCamera.GetComponent<CinemachineOrbitalFollow>();
        if (_orbitalFollow == null)
            Debug.LogWarning("[PlayerMovement] CinemachineOrbitalFollow not found on freeLookCamera. " +
                             "Aim-exit yaw sync will not work.");

        freeLookCamera.Priority = activePriority;
        aimCamera.Priority      = defaultPriority;

        _aimYaw = transform.eulerAngles.y;

        // Auto-resolve the Enemy layer if the mask wasn't set in the Inspector.
        if (enemyLayers.value == 0)
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) enemyLayers = 1 << enemyLayer;
        }
    }

    void Update()
    {
        _isGrounded = _controller.isGrounded;

        CheckEnemyHeadSlide();   // no camping on enemy backs

        if (!_isRolling && !IsStaggered)
        {
            HandleMovement();
            HandleRotation();
        }

        HandleJumpAndGravity();
        HandleKnockback();

        if (_isAiming)
            DriveAimLook();

        SetFloat("Running", _currentMoveVelocity == Vector3.zero ? 0f : 1f);
    }

    // ── Speed stat integration ──

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

    // ── Knockback (called by boss attacks, hazards, etc.) ──

    /// <summary>
    /// Push the player along <paramref name="direction"/> at <paramref name="force"/>
    /// units/sec, decaying linearly to zero over <paramref name="duration"/> seconds.
    /// Stacks with normal input — the player can still steer a little during the push.
    /// </summary>
    public void TakeKnockback(Vector3 direction, float force, float duration)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || force <= 0f || duration <= 0f) return;

        _knockbackDir          = direction.normalized;
        _knockbackForce        = force;
        _knockbackTimer        = duration;
        _knockbackInitialTimer = duration;
    }

    // ── Hit reactions (Impact overhaul) ──
    // Basic enemy hits: small knockback + brief i-frames, NO control loss.
    // Decal hits: STAGGER — control lockout + bigger knockback + i-frames.

    [Header("Enemy Head Slide (no camping on enemies)")]
    [Tooltip("Layers counted as enemies. Leave empty to auto-resolve the " +
             "'Enemy' layer at startup.")]
    public LayerMask enemyLayers;

    [Tooltip("Slide-off speed while standing on top of an enemy — makes enemy " +
             "backs unstandable instead of a free safe spot. Must comfortably " +
             "beat walk speed or the player can fight the slide.")]
    public float enemyHeadSlideSpeed = 6f;

    private Vector3 _enemyHeadSlide;
    private bool    _standingOnEnemy;

    // OVERLAP-based probe (casts that start inside a collider silently miss —
    // the classic reason "detect what I'm standing on" fails). Every frame we
    // overlap a sphere at the player capsule's bottom against the Enemy layer;
    // if the contact point is BELOW our feet, we're on top of someone: shed off
    // their SIDE (never along a long body's back ridge) and deny ground status.
    private void CheckEnemyHeadSlide()
    {
        _standingOnEnemy = false;

        float   r      = _controller.radius;
        Vector3 bottom = transform.position + _controller.center
                       - Vector3.up * (_controller.height * 0.5f - r);
        float feetY = bottom.y - r;

        foreach (Collider col in Physics.OverlapSphere(
                     bottom + Vector3.down * 0.15f, r + 0.1f,
                     enemyLayers, QueryTriggerInteraction.Ignore))
        {
            // Where does this enemy touch us? Below the feet = we're ON it.
            Vector3 closest = col.ClosestPoint(transform.position);
            if (closest.y > feetY + 0.1f) continue;   // side contact = body-block, fine

            var ai = col.GetComponentInParent<EnemyAI>();
            Transform enemy = ai != null ? ai.transform : col.transform;

            Vector3 away = transform.position - enemy.position;
            away.y = 0f;

            Vector3 side = enemy.right; side.y = 0f;
            bool alongRidge = away.sqrMagnitude < 0.04f ||
                              (side.sqrMagnitude > 0.001f &&
                               Mathf.Abs(Vector3.Dot(away.normalized, enemy.forward.normalized)) > 0.8f);
            if (alongRidge && side.sqrMagnitude > 0.001f)
                away = side * (Vector3.Dot(away, side) >= 0f ? 1f : -1f);
            if (away.sqrMagnitude < 0.001f) away = transform.forward;

            _enemyHeadSlide  = away.normalized * enemyHeadSlideSpeed;
            _standingOnEnemy = true;   // enemy backs are NOT ground — no jumping off
            break;
        }
    }

    [Header("Hit Reactions")]
    [Tooltip("Knockback force from a basic enemy hit (no control loss).")]
    public float basicHitKnockbackForce = 4f;

    [Tooltip("I-frame duration after a basic hit.")]
    public float basicHitInvulnDuration = 0.3f;

    [Tooltip("Horizontal launch SPEED (units/sec) when STAGGERED by a decal hit. " +
             "Constant while airborne — momentum carries THROUGH the bounces, " +
             "losing energy only at each bump. ~9 = thrown well clear of the attack.")]
    public float staggerKnockbackForce = 9f;

    [Tooltip("Seconds of skid after the FINAL landing — the leftover momentum " +
             "sliding out before recovery.")]
    public float staggerKnockbackDuration = 0.3f;

    [Tooltip("UPWARD velocity injected on a decal hit — the vertical half of the " +
             "launch. ~ jump strength at 9-10; 12+ = sent flying. Uses the normal " +
             "gravity arc, so you fly up-and-back and come down for real.")]
    public float staggerLaunchVertical = 11f;

    [Tooltip("Seconds of recovery lockout AFTER the FINAL landing — reads as " +
             "picking yourself up once the bouncing stops.")]
    public float staggerRecoverDuration = 0.4f;

    [Tooltip("I-frame duration after being staggered — covers the whole flight + " +
             "bounces + getting up, so you can't be juggled mid-air.")]
    public float staggerInvulnDuration = 1.6f;

    [Header("Stagger Bounces (hit → land bump → land bump → stop)")]
    [Tooltip("How much fall speed survives each ground bounce.")]
    public float staggerBounciness = 0.45f;

    [Tooltip("Number of bumps after the first landing before you settle.")]
    public int staggerMaxBounces = 2;

    [Tooltip("Minimum fall speed for a bounce — slower than this just lands.")]
    public float staggerBounceMinFallSpeed = 4f;

    [Tooltip("Horizontal push kept after each bounce (the slide shortens per bump).")]
    public float staggerBounceHorizontalKeep = 0.55f;

    private float   _staggerUntil = -999f;
    private bool    _inStaggerFlight;
    private int     _staggerBouncesLeft;
    private float   _staggerFlightSafetyEnd;
    private Vector3 _staggerHorizVelocity;   // constant ballistic momentum during flight

    /// <summary>True while staggered — the ENTIRE hit → fly → bounce → bounce →
    /// settle → recover sequence. Movement, roll, jump, and attacks all blocked
    /// until it fully completes.</summary>
    public bool IsStaggered => _inStaggerFlight || Time.time < _staggerUntil;

    /// <summary>
    /// Called by enemy attacks after damage lands.
    /// stagger = true for DECAL hits: a big shove away from the attack, then a
    /// recovery lockout while you pick yourself up. false = basic hit (small
    /// push, no control loss).
    /// pushDirOverride: attacker-supplied shove direction (e.g. the tough's
    /// dash pushes ALONG the charge). Zero = default "away from attacker".
    /// </summary>
    public void ApplyHitReaction(Vector3 sourcePosition, bool stagger,
                                 Vector3 pushDirOverride = default)
    {
        Vector3 away = pushDirOverride.sqrMagnitude > 0.0001f
            ? pushDirOverride
            : transform.position - sourcePosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;

        if (stagger)
        {
            // LAUNCH: horizontal push + vertical velocity = real up-and-back arc
            // (Elden Ring boulder-hit treatment), then land-bump-land-bump-stop.
            // The lockout is STATE-driven: it holds through the entire flight +
            // bounces, then the recovery timer starts at the FINAL landing.
            BeginStaggerFlight();
            // Constant horizontal momentum — carries through the bounces,
            // losing energy only at each bump (no mid-air decay).
            _staggerHorizVelocity = away.normalized * staggerKnockbackForce;
            _velocity.y = staggerLaunchVertical;   // up we go — gravity brings us down
            _stats?.GrantInvulnerability(staggerInvulnDuration);
            SetTriggerIfPresent("Stagger");
        }
        else
        {
            TakeKnockback(away, basicHitKnockbackForce, 0.15f);
            _stats?.GrantInvulnerability(basicHitInvulnDuration);
            SetTriggerIfPresent("HitReact");
        }
    }

    /// <summary>Stagger without the horizontal shove (boss code applies its own
    /// tuned knockback) — but the vertical LAUNCH + bounce sequence still fires,
    /// plus i-frames. Boss hits send you flying too.</summary>
    public void ApplyStagger()
    {
        BeginStaggerFlight();
        _velocity.y = staggerLaunchVertical;
        _stats?.GrantInvulnerability(staggerInvulnDuration);
        SetTriggerIfPresent("Stagger");
    }

    private void BeginStaggerFlight()
    {
        _inStaggerFlight        = true;
        _staggerBouncesLeft     = staggerMaxBounces;
        _staggerFlightSafetyEnd = Time.time + 3f;   // never stuck airborne forever
        _staggerUntil           = Time.time;        // recovery timer starts at settle
    }

    private void EndStaggerFlight()
    {
        if (!_inStaggerFlight) return;
        _inStaggerFlight = false;
        _staggerUntil    = Time.time + staggerRecoverDuration;   // pick yourself up

        // Convert leftover momentum into a short decaying skid, then stop.
        if (_staggerHorizVelocity.sqrMagnitude > 0.01f)
            TakeKnockback(_staggerHorizVelocity.normalized,
                          _staggerHorizVelocity.magnitude,
                          staggerKnockbackDuration);
        _staggerHorizVelocity = Vector3.zero;
    }

    private void SetTriggerIfPresent(string trigger)
    {
        if (_primaryAnimator == null || string.IsNullOrEmpty(trigger)) return;
        foreach (var p in _primaryAnimator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == trigger)
            {
                _primaryAnimator.SetTrigger(trigger);
                return;
            }
        }
    }

    /// <summary>
    /// Applies and decays the current knockback impulse each frame.
    /// </summary>
    private void HandleKnockback()
    {
        if (_knockbackTimer <= 0f) return;

        // t = 1 at start, 0 at end → speed ramps from full force down to zero.
        float t     = _knockbackTimer / Mathf.Max(0.0001f, _knockbackInitialTimer);
        float speed = _knockbackForce * t;

        _controller.Move(_knockbackDir * speed * Time.deltaTime);

        _knockbackTimer -= Time.deltaTime;
        if (_knockbackTimer <= 0f)
            _knockbackTimer = 0f;
    }

    // ── Movement ──

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
        // Stagger flight safety: force-settle if somehow airborne too long
        // (launched onto a ledge lip, physics weirdness...).
        if (_inStaggerFlight && Time.time > _staggerFlightSafetyEnd)
            EndStaggerFlight();

        if (_isGrounded && _velocity.y < 0f)
        {
            if (_inStaggerFlight)
            {
                // Landing during a stagger: BUMP if there's fall speed left,
                // otherwise this is the final landing → recovery begins.
                float fallSpeed = -_velocity.y;
                if (_staggerBouncesLeft > 0 && fallSpeed > staggerBounceMinFallSpeed)
                {
                    _staggerBouncesLeft--;
                    _velocity.y            = fallSpeed * staggerBounciness;        // bump!
                    _staggerHorizVelocity *= staggerBounceHorizontalKeep;          // lose energy per bump
                    SetTriggerIfPresent("Bounce");   // squash anim hook (optional clip)
                }
                else
                {
                    EndStaggerFlight();
                    _velocity.y = -2f;
                }
            }
            else
            {
                _velocity.y = -2f;
            }
        }

        bool landedThisFrame = !ground && _velocity.y == -2f;
        bool falling         = !ground && _velocity.y < -0.1f;

        SetBool("Grounded", true);
        if (falling) SetBool("Grounded", false);
        ground = false;

        SetBool("Jump",    false);
        SetBool("Falling", true);
        SetBool("Contact", false);
        if (landedThisFrame) SetBool("Contact", true);

        if (_jumpPressed && _isGrounded && !IsStaggered && !_standingOnEnemy)
        {
            // Jumping is FREE — no stamina cost (playtest feedback: stamina-gated
            // jumps made platforming feel unfair). jumpStaminaCost on the stat
            // block is now unused.
            _velocity.y  = Mathf.Sqrt(jumpForce * -2f * gravity);
            _jumpPressed = false;
            SetBool("Jump", true);
        }

        _velocity.y += gravity * Time.deltaTime;

        // During stagger flight, the constant ballistic momentum rides along
        // with gravity in the same Move — full arc, no mid-air decay.
        Vector3 flightVelocity = _inStaggerFlight ? _staggerHorizVelocity : Vector3.zero;

        // Enemy-head slide: shed off any enemy we're standing on, decaying fast
        // once we're clear.
        if (_enemyHeadSlide.sqrMagnitude > 0.01f)
        {
            flightVelocity += _enemyHeadSlide;
            _enemyHeadSlide = Vector3.MoveTowards(_enemyHeadSlide, Vector3.zero,
                                                  12f * Time.deltaTime);
        }

        _controller.Move((_velocity + flightVelocity) * Time.deltaTime);
    }

    // ── Roll ──

    private void TryRoll()
    {
        if (_isRolling)   return;
        if (_isAiming)    return;
        if (IsStaggered)  return;   // no dodging out of a stagger — that's the punish
        if (Time.time < _lastRollTime + rollCooldown) return;

        // Roll works with ANY stamina left — even 1 point — draining whatever
        // remains (playtest feedback: being denied a last-sliver escape dodge
        // felt bad). Only a completely empty bar blocks the roll.
        int rollCost = _stats?.playerStatBlock?.rollStaminaCost ?? 12;
        if (_stats != null && !_stats.UseStaminaPartial(rollCost))
        {
            Debug.Log("[PlayerMovement] Out of stamina — can't roll");
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

    // ── Aim Look ──

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

    // ── Animator helpers ──

    private void SetBool(string param, bool value)
    {
        _primaryAnimator?.SetBool(param, value);
        // Fire on whichever weapon is currently equipped — auto-resolves via swapper.
        _swapper?.ActiveWeaponAnimator?.SetBool(param, value);
    }

    private void SetFloat(string param, float value)
    {
        _primaryAnimator?.SetFloat(param, value);
        _swapper?.ActiveWeaponAnimator?.SetFloat(param, value);
    }

    // ── Input Callbacks ──

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
