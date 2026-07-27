using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Combat router. Reads attack/aim input and dispatches to the equipped weapon's
/// controller: Blade → BladeCombat, Hammer → HammerCombat, Bow → BowController.
/// Also fires the shared Attk/AirAttk animator triggers on the rat body + active
/// weapon animator. To add a weapon: new controller + a branch in OnAttack.
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("Bow Rate-of-Fire Cooldown (also used by HUD when bow is equipped)")]
    [Tooltip("Cooldown at base Speed. Speed points reduce it toward the minimum.")]
    public float basicAttackCooldownBase = 1.0f;

    [Tooltip("Fastest possible cooldown regardless of Speed.")]
    public float basicAttackCooldownMin  = 0.3f;

    [Tooltip("UNUSED — replaced by the percentage version below (design doc: " +
             "−4% per Speed point). Kept so prefab data isn't lost.")]
    public float attackCooldownPerSpeed  = 0.1f;

    [Tooltip("Fractional cooldown reduction per Speed point above base — " +
             "0.04 = −4% per point (design doc). Multiplicative: cooldown = " +
             "base × (1 − this × points), clamped at the minimum.")]
    public float attackCooldownPercentPerSpeed = 0.04f;

    /// <summary>Current Speed-driven cooldown multiplier (1 = base Speed).
    /// Weapon controllers and the bow's draw time read this.</summary>
    public float SpeedCooldownMultiplier { get; private set; } = 1f;

    /// <summary>
    /// The ONE source of truth for attack cooldowns: per-weapon base from the
    /// stat block × the Speed multiplier, never below the per-weapon floor.
    /// </summary>
    public float GetAttackCooldown(EntityStats.WeaponType weapon)
    {
        var sb = _stats != null ? _stats.playerStatBlock : null;
        if (sb == null) return basicAttackCooldownBase;   // legacy fallback

        return weapon switch
        {
            EntityStats.WeaponType.Blade  => Mathf.Max(sb.bladeCooldownFloor,
                                                       sb.bladeAttackCooldown  * SpeedCooldownMultiplier),
            EntityStats.WeaponType.Hammer => Mathf.Max(sb.hammerCooldownFloor,
                                                       sb.hammerAttackCooldown * SpeedCooldownMultiplier),
            _                             => Mathf.Max(sb.bowCooldownFloor,
                                                       sb.bowAttackCooldown    * SpeedCooldownMultiplier),
        };
    }

    [Header("Jump Attack Cooldown (used when bow is equipped — blade/hammer have their own)")]
    public float jumpAttackCooldown = 1.2f;

    [Header("Shared Attack Origin (BladeCombat and HammerCombat auto-pull this)")]
    public Transform attackOrigin;

    [Header("Shared Enemy Layer (each weapon controller can override or auto-pull)")]
    [Tooltip("The layer mask for enemies. BladeCombat and HammerCombat default to " +
             "this if their own enemyLayer is empty. BowController has its own.")]
    public LayerMask enemyLayer;

    [Header("Animators")]
    [Tooltip("The rat body animator — drives running, jumping, attack pose.")]
    [SerializeField] private Animator _primaryAnimator;

    [Header("Bow Body Animation")]
    [Tooltip("Name of the bool parameter on the RAT BODY animator that is set TRUE " +
             "while the bow is equipped, a charge is building, AND the player is moving.\n" +
             "Plays the BowMove animation on the body so the rat doesn't snap back to " +
             "idle while strafing with a drawn bow.\n" +
             "Must exactly match the parameter name in the rat body Animator Controller.")]
    public string bowMoveAnimParam = "BowMove";

    // ── Cached components ──
    private CharacterController _controller;
    private EntityStats         _stats;
    private BladeCombat         _blade;
    private HammerCombat        _hammer;
    private BowController       _bow;
    private WeaponModelSwapper  _swapper;

    // ── Shared state ──
    private float _lastAttackTime;
    private float _lastJumpAttackTime;
    private bool  _hasJumpAttacked;
    private float _currentAttackCooldown;     // bow rate-of-fire
    private bool  _isAiming;                  // bow aim mode

    // Tracks the last value pushed to the body animator for BowMove so we
    // only call SetBool when it actually changes — same dirty-flag pattern
    // BowController uses for Hold / Moving on the bow's own animator.
    private bool _lastBowMoveSent;

    // ── Lifecycle ──

    private PlayerMovement _movement;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _stats      = GetComponent<EntityStats>();
        _blade      = GetComponent<BladeCombat>();
        _hammer     = GetComponent<HammerCombat>();
        _bow        = GetComponent<BowController>();
        _swapper    = GetComponent<WeaponModelSwapper>();
        _movement   = GetComponent<PlayerMovement>();
    }

    void Start()
    {
        if (_stats == null)
            Debug.LogError("[PlayerCombat] No EntityStats found on player!");

        if (_stats != null)
            _stats.onStatsChanged.AddListener(RecalculateAttackCooldown);

        RecalculateAttackCooldown();
    }

    void OnDestroy()
    {
        if (_stats != null)
            _stats.onStatsChanged.RemoveListener(RecalculateAttackCooldown);
    }

    void Update()
    {
        // Reset the "one jump attack per air time" flag when we land.
        if (_controller != null && _controller.isGrounded)
            _hasJumpAttacked = false;

        // Drive BowMove bool on the rat body animator.
        // Condition: bow equipped AND BowController reports charging + moving.
        // This keeps the body in its bow-draw-while-moving pose instead of
        // snapping back to idle when the player strafes with LMB held.
        PushBowMoveAnim();
    }

    // ── Cooldown recalc ──

    /// <summary>
    /// Called by EntityStats on every stat change (Speed leveled, weapon swap, reset).
    /// Recomputes the bow's speed-affected cooldown AND forwards to BladeCombat so
    /// its swing cooldown stays in sync with Speed too.
    /// </summary>
    public void RecalculateAttackCooldown()
    {
        // Bow rate-of-fire — speed-affected
        if (_stats == null)
        {
            _currentAttackCooldown = basicAttackCooldownBase;
        }
        else
        {
            int   baseSpd    = _stats.playerStatBlock != null ? _stats.playerStatBlock.baseSpeed : 5;
            int   speedBonus = Mathf.Max(0, _stats.Speed - baseSpd);
            // Design doc: −4% cooldown per Speed point, multiplicative, floored.
            SpeedCooldownMultiplier = Mathf.Max(0.1f, 1f - speedBonus * attackCooldownPercentPerSpeed);
            _currentAttackCooldown  = GetAttackCooldown(EntityStats.WeaponType.Bow);
        }

        // Forward to BladeCombat AND HammerCombat — both have their own
        // speed-affected cooldown logic that needs to re-derive from Speed.
        _blade?.RecalculateCooldown();
        _hammer?.RecalculateCooldown();
    }

    // ── Input — OnAttack routes per equipped weapon ──

    public void OnAttack(InputValue value)
    {
        bool isPressed  = value.isPressed;
        bool isGrounded = _controller.isGrounded;

        if (_stats == null) return;
        if (_movement != null && _movement.IsStaggered) return;   // staggered — no attacking
        var weapon = _stats.EquippedWeapon;

        // ── Bow path ───────────────────────────────────────────────
        if (weapon == EntityStats.WeaponType.Bow && _bow != null)
        {
            if (!isPressed) return;   // release is handled inside BowController

            if (!isGrounded && !_hasJumpAttacked)
            {
                _hasJumpAttacked    = true;
                _lastJumpAttackTime = Time.time;
                FireAttackAnims("AirAttk");
                _bow.JumpTripleShot();
                return;
            }

            // HOLD-TO-DRAW everywhere (design doc): every grounded press starts
            // a draw — aimed or free-look — and the shot fires on RELEASE
            // (handled inside BowController, which calls NotifyBowShotFired).
            if (Time.time < _lastAttackTime + _currentAttackCooldown) return;
            _bow.BeginCharge();
            return;
        }

        // ── Hammer path ────────────────────────────────────────────
        if (weapon == EntityStats.WeaponType.Hammer && _hammer != null)
        {
            if (!isPressed) return;

            if (!isGrounded && !_hasJumpAttacked)
            {
                if (_hammer.TryJumpSlam())
                {
                    _hasJumpAttacked    = true;
                    _lastJumpAttackTime = Time.time;
                    FireAttackAnims("AirAttk");
                }
                return;
            }

            if (isGrounded && _hammer.TryBasicSwing())
            {
                PushComboStepAnim(_hammer.LastComboStep);
                FireAttackAnims("Attk");
            }

            return;
        }

        // ── Blade path ─────────────────────────────────────────────
        if (_blade != null)
        {
            if (!isPressed) return;

            if (!isGrounded && !_hasJumpAttacked)
            {
                if (_blade.TryJumpAttack())
                {
                    _hasJumpAttacked    = true;
                    _lastJumpAttackTime = Time.time;
                    FireAttackAnims("AirAttk");
                }
                return;
            }

            if (isGrounded && _blade.TryBasicAttack())
            {
                PushComboStepAnim(_blade.LastComboStep);
                FireAttackAnims("Attk");
            }
        }
    }

    /// <summary>
    /// PlayerInput sends OnAim to ALL MonoBehaviours on this GameObject. This
    /// is our copy so we can route bow press -> BeginAimedShot vs FreeLookShot.
    /// </summary>
    public void OnAim(InputValue value) => _isAiming = value.isPressed;

    /// <summary>Current aim state — BowController reads this at release to pick
    /// camera-aimed vs free-look firing.</summary>
    public bool IsAiming => _isAiming;

    /// <summary>Called by BowController the moment an arrow actually fires —
    /// starts the rate-of-fire cooldown and plays the shot animations.</summary>
    public void NotifyBowShotFired()
    {
        _lastAttackTime = Time.time;
        _swapper?.ActiveWeaponAnimator?.ResetTrigger("BowAttk");
        FireAttackAnims("Attk", "BowAttk");
    }

    // ── Weapon swap shortcut helpers (still useful for menus / pickups) ──

    public void EquipBlade()  => _stats?.EquipWeapon(EntityStats.WeaponType.Blade);
    public void EquipHammer() => _stats?.EquipWeapon(EntityStats.WeaponType.Hammer);
    public void EquipBow()    => _stats?.EquipWeapon(EntityStats.WeaponType.Bow);

    // ── Animator helpers ──

    private void FireAttackAnims(string trigger, string secondTrigger = null)
    {
        _primaryAnimator?.SetTrigger(trigger);
        _swapper?.ActiveWeaponAnimator?.SetTrigger(trigger);

        if (secondTrigger != null)
        {
            _primaryAnimator?.SetTrigger(secondTrigger);
            _swapper?.ActiveWeaponAnimator?.SetTrigger(secondTrigger);
        }
    }

    /// <summary>Pushes the combo step (0-based) to the "ComboStep" int on both
    /// animators BEFORE the Attk trigger fires — the animator branches to
    /// combo1/combo2/finisher clips on it. Silently skipped until the
    /// parameter exists (same pattern as every other optional anim hook).</summary>
    private void PushComboStepAnim(int step)
    {
        SetIntIfPresent(_primaryAnimator, "ComboStep", step);
        SetIntIfPresent(_swapper?.ActiveWeaponAnimator, "ComboStep", step);
    }

    private static void SetIntIfPresent(Animator anim, string param, int value)
    {
        if (anim == null) return;
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Int && p.name == param)
            {
                anim.SetInteger(param, value);
                return;
            }
        }
    }

    /// <summary>
    /// Drives the "BowMove" bool on the rat body animator each frame.
    /// TRUE when: bow is equipped AND LMB is held AND the player is moving.
    /// </summary>
    private void PushBowMoveAnim()
    {
        if (_primaryAnimator == null) return;

        bool isMoving = _controller != null &&
                        new Vector3(_controller.velocity.x, 0f, _controller.velocity.z).sqrMagnitude > 0.01f;

        bool bowMove = _stats != null
                    && _stats.EquippedWeapon == EntityStats.WeaponType.Bow
                    && _isAiming
                    && isMoving;

        if (bowMove == _lastBowMoveSent) return;

        _primaryAnimator.SetBool(bowMoveAnimParam, bowMove);
        _lastBowMoveSent = bowMove;
    }

    // ── HUD accessors — forward to the active weapon's controller (kept as PlayerCombat properties so AttackCooldownHUD doesn't change) ──

    public float AttackCooldownProgress
    {
        get
        {
            if (_stats == null) return 1f;
            switch (_stats.EquippedWeapon)
            {
                case EntityStats.WeaponType.Hammer:
                    return _hammer != null ? _hammer.SwingCooldownProgress : 1f;
                case EntityStats.WeaponType.Bow:
                    return Mathf.Clamp01((Time.time - _lastAttackTime) /
                                         Mathf.Max(0.0001f, _currentAttackCooldown));
                default:
                    return _blade != null ? _blade.SwingCooldownProgress : 1f;
            }
        }
    }

    public float CurrentAttackCooldown
    {
        get
        {
            if (_stats == null) return basicAttackCooldownBase;
            switch (_stats.EquippedWeapon)
            {
                case EntityStats.WeaponType.Hammer:
                    return _hammer != null ? _hammer.CurrentSwingCooldown : basicAttackCooldownBase;
                case EntityStats.WeaponType.Bow:
                    return _currentAttackCooldown;
                default:
                    return _blade != null ? _blade.CurrentCooldown : basicAttackCooldownBase;
            }
        }
    }

    public float JumpAttackCooldownProgress
    {
        get
        {
            if (_stats == null) return 1f;
            switch (_stats.EquippedWeapon)
            {
                case EntityStats.WeaponType.Hammer:
                    return _hammer != null ? _hammer.SlamCooldownProgress : 1f;
                case EntityStats.WeaponType.Bow:
                    return Mathf.Clamp01((Time.time - _lastJumpAttackTime) /
                                         Mathf.Max(0.0001f, jumpAttackCooldown));
                default:
                    return _blade != null ? _blade.JumpCooldownProgress : 1f;
            }
        }
    }

    public float JumpAttackCooldownValue
    {
        get
        {
            if (_stats == null) return jumpAttackCooldown;
            switch (_stats.EquippedWeapon)
            {
                case EntityStats.WeaponType.Hammer:
                    return _hammer != null ? _hammer.slamCooldown : jumpAttackCooldown;
                case EntityStats.WeaponType.Bow:
                    return jumpAttackCooldown;
                default:
                    return _blade != null ? _blade.JumpCooldown : jumpAttackCooldown;
            }
        }
    }

    // Backwards-compatible alias — kept so any external script reading the old
    // JumpAttackCooldown property still compiles.
    public float JumpAttackCooldown => JumpAttackCooldownValue;
}
