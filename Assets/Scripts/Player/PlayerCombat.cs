using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Combat router. Reads input from the Input System and dispatches attacks
/// to whichever per-weapon controller is appropriate for the equipped weapon:
///
///   Blade  → <see cref="BladeCombat"/>
///   Hammer → <see cref="HammerCombat"/>
///   Bow    → <see cref="BowController"/>
///
/// PlayerCombat itself no longer holds per-weapon stats. Each weapon owns its
/// own reach, cooldown, gizmos, and hit scan code. Adding a 4th weapon later
/// is just: create a new controller, add a branch in <see cref="OnAttack"/>.
///
/// What stays here:
///   • Input callbacks (OnAttack, OnAim).
///   • Animator triggers fired on the rat body + active weapon's animator.
///   • _hasJumpAttacked flag — shared across all weapons (one jump = one air attack).
///   • _isAiming forwarded from PlayerInput's OnAim (BowController also reads OnAim
///     via SendMessages from PlayerInput so it has its own copy too).
///   • HUD accessors (AttackCooldownProgress / JumpAttackCooldownProgress) that
///     forward to the active weapon controller. AttackCooldownHUD reads these.
///   • Bow rate-of-fire cooldown — bow still uses _currentAttackCooldown / _lastAttackTime
///     since it doesn't have its own controller for that yet.
///   • RecalculateAttackCooldown() — recalculates speed-affected cooldown for the
///     bow and forwards to BladeCombat. Called by EntityStats on stat changes.
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("Bow Rate-of-Fire Cooldown (also used by HUD when bow is equipped)")]
    [Tooltip("Cooldown at base Speed. Speed points reduce it toward the minimum.")]
    public float basicAttackCooldownBase = 1.0f;

    [Tooltip("Fastest possible cooldown regardless of Speed.")]
    public float basicAttackCooldownMin  = 0.3f;

    [Tooltip("Seconds shaved off the cooldown per Speed point above base.")]
    public float attackCooldownPerSpeed  = 0.1f;

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

    // ─────────────────────────────────────────
    // Cached components
    // ─────────────────────────────────────────
    private CharacterController _controller;
    private EntityStats         _stats;
    private BladeCombat         _blade;
    private HammerCombat        _hammer;
    private BowController       _bow;
    private WeaponModelSwapper  _swapper;

    // ─────────────────────────────────────────
    // Shared state
    // ─────────────────────────────────────────
    private float _lastAttackTime;
    private float _lastJumpAttackTime;
    private bool  _hasJumpAttacked;
    private float _currentAttackCooldown;     // bow rate-of-fire
    private bool  _isAiming;                  // bow aim mode

    // ═════════════════════════════════════════════════════════════
    // Lifecycle
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _stats      = GetComponent<EntityStats>();
        _blade      = GetComponent<BladeCombat>();
        _hammer     = GetComponent<HammerCombat>();
        _bow        = GetComponent<BowController>();
        _swapper    = GetComponent<WeaponModelSwapper>();
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
    }

    // ═════════════════════════════════════════════════════════════
    // Cooldown recalc
    // ═════════════════════════════════════════════════════════════

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
            int   baseSpd     = _stats.playerStatBlock != null ? _stats.playerStatBlock.baseSpeed : 5;
            int   speedBonus  = Mathf.Max(0, _stats.Speed - baseSpd);
            float reduction   = speedBonus * attackCooldownPerSpeed;
            _currentAttackCooldown = Mathf.Max(basicAttackCooldownMin,
                                               basicAttackCooldownBase - reduction);
        }

        // Forward to BladeCombat AND HammerCombat — both have their own
        // speed-affected cooldown logic that needs to re-derive from Speed.
        _blade?.RecalculateCooldown();
        _hammer?.RecalculateCooldown();
    }

    // ═════════════════════════════════════════════════════════════
    // Input — OnAttack routes per equipped weapon
    // ═════════════════════════════════════════════════════════════

    public void OnAttack(InputValue value)
    {
        bool isPressed   = value.isPressed;
        bool isGrounded  = _controller.isGrounded;

        if (_stats == null) return;
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

            if (Time.time < _lastAttackTime + _currentAttackCooldown) return;
            _lastAttackTime = Time.time;
            _swapper?.ActiveWeaponAnimator?.ResetTrigger("BowAttk");
            FireAttackAnims("Attk", "BowAttk");


            if (_isAiming) _bow.BeginAimedShot();
            else           _bow.FreeLookShot();
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
                 FireAttackAnims("Attk");
                //FireAttackAnims("HamAttk");
           
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
                FireAttackAnims("Attk");
        }
    }

    /// <summary>
    /// PlayerInput sends OnAim to ALL MonoBehaviours on this GameObject. This
    /// is our copy so we can route bow press → BeginAimedShot vs FreeLookShot.
    /// </summary>
    public void OnAim(InputValue value) => _isAiming = value.isPressed;

    // ─────────────────────────────────────────
    // Weapon swap shortcut helpers (still useful for menus / pickups)
    // ─────────────────────────────────────────

    public void EquipBlade()  => _stats?.EquipWeapon(EntityStats.WeaponType.Blade);
    public void EquipHammer() => _stats?.EquipWeapon(EntityStats.WeaponType.Hammer);
    public void EquipBow()    => _stats?.EquipWeapon(EntityStats.WeaponType.Bow);

    // ═════════════════════════════════════════════════════════════
    // Animator helper
    // ═════════════════════════════════════════════════════════════

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

    // ═════════════════════════════════════════════════════════════
    // HUD accessors — forward to the active weapon's controller
    // (kept as PlayerCombat properties so AttackCooldownHUD doesn't change)
    // ═════════════════════════════════════════════════════════════

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
