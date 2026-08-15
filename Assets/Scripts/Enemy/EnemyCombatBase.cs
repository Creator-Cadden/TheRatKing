using UnityEngine;

/// <summary>
/// Abstract base for all per-enemy combat scripts (GruntCombat, EnemyCombat legacy,
/// future ToughCombat / CaptainCombat rework / BalloonCombat). EnemyAI drives any
/// enemy through this contract — it never needs to know which enemy type it's moving.
/// Provides the shared plumbing: stat block / player refs, cooldown, facing lock,
/// animation-event receivers, and hit-check + damage helpers.
/// </summary>
public abstract class EnemyCombatBase : MonoBehaviour
{
    [Header("Combat Setup")]
    public LayerMask playerLayer;

    [Tooltip("Fallback origin for hit checks (enemy chest/center). Subclasses may " +
             "use their own more specific attack point and ignore this.")]
    public Transform attackOrigin;

    [Header("Decal Grounding")]
    [Tooltip("Layers treated as ground when decals raycast down to find the real " +
             "floor (the NavMesh often bakes slightly above the visual floor). " +
             "Exclude the Enemy and Player layers. Unused by enemies without decals.")]
    public LayerMask groundLayers = ~0;

    [Header("Debug")]
    public bool verboseAttackLog = false;

    // ── Shared references (resolved in Awake / ConfigureRuntime) ──
    protected EntityStats    _selfStats;
    protected EntityStats    _playerStats;
    protected EnemyStatBlock _sb;
    protected Animator       _animator;
    protected Transform      _player;

    // ── Shared state ──
    protected float      _lastAttackTime = -999f;
    protected Quaternion _lockedRotation;

    // ── Contract EnemyAI depends on ──

    /// <summary>True while mid-windup or mid-attack — EnemyAI won't steer or re-attack.</summary>
    public abstract bool IsBusy { get; }

    /// <summary>Distance at which this enemy's current attack can connect. EnemyAI
    /// closes to this range before calling TryStartAttack.</summary>
    public abstract float CurrentAttackReach { get; }

    /// <summary>While true, EnemyAI holds the rotation locked at windup direction.</summary>
    public virtual bool IsRotationLocked => IsBusy;

    /// <summary>Called every frame by EnemyAI while alive and not knocked back.</summary>
    public virtual void Tick() { }

    /// <summary>Called by EnemyAI when the player is within CurrentAttackReach.</summary>
    public abstract void TryStartAttack(float distToPlayer);

    /// <summary>Animation event receiver — the attack clip's impact frame.</summary>
    public abstract void OnAttackHitFrame();

    /// <summary>Animation event receiver — the attack clip finished.</summary>
    public abstract void OnAttackEnd();

    /// <summary>Abort a windup that hasn't committed yet (player left range).</summary>
    public virtual void CancelWindup() { }

    /// <summary>Hard-cancel everything (knockback, stagger, death).</summary>
    public virtual void CancelAttackState() { }

    // ── Impact system hooks ──

    /// <summary>True while winding up OR executing a DECAL (Tier 2) attack.
    /// Decal actions can never be cancelled — staggers only delay the windup.</summary>
    public virtual bool IsInDecalAction => false;

    /// <summary>True while winding up a BASIC (Tier 1) attack — flinches delay it.</summary>
    public virtual bool IsInBasicWindup => false;

    [Tooltip("Max total seconds a single windup can be pushed back by flinches/" +
             "staggers. Stops fast weapons from chain-delaying a decal forever " +
             "(which would be a stealth interrupt).")]
    public float maxWindupDelay = 1f;

    protected float _windupDelayAccrued;   // reset by each script at windup start

    /// <summary>How much of the requested delay is still allowed this windup.</summary>
    protected float AccrueWindupDelay(float requested)
    {
        float allowed = Mathf.Min(requested, maxWindupDelay - _windupDelayAccrued);
        if (allowed <= 0f) return 0f;
        _windupDelayAccrued += allowed;
        return allowed;
    }

    /// <summary>Push the current windup back by up to <paramref name="seconds"/>
    /// (capped per windup). No-op outside windups — committed attacks can't be slowed.</summary>
    public virtual void DelayCurrentWindup(float seconds) { }

    /// <summary>Public animator-trigger access for EnemyAI (Flinch/Stagger cues).</summary>
    public void FireAnimTrigger(string trigger) => SetTriggerIfPresent(trigger);

    public Vector3 HitOriginPosition => HitOrigin.position;
    protected Transform HitOrigin => attackOrigin != null ? attackOrigin : transform;

    // ── Debug state reporting (read by EnemyStateDebugBalls) ──

    public enum CombatDebugState { None, Windup, Strike, Recover, Cooldown }

    /// <summary>State of this enemy's BASIC (Tier 1) attack, for debug visuals.</summary>
    public virtual CombatDebugState BasicDebugState => CombatDebugState.None;

    /// <summary>State of this enemy's DECAL (Tier 2) attack, for debug visuals.</summary>
    public virtual CombatDebugState DecalDebugState => CombatDebugState.None;

    protected virtual void Awake()
    {
        _selfStats = GetComponent<EntityStats>();
        _animator  = GetComponentInChildren<Animator>();
        _sb        = _selfStats != null ? _selfStats.enemyStatBlock : null;
    }

    /// <summary>Called by EnemyAI in Start to hand over the player refs + fallbacks.</summary>
    public virtual void ConfigureRuntime(Transform player, EntityStats playerStats,
        Transform fallbackOrigin, LayerMask fallbackLayer, bool verbose)
    {
        _player          = player;
        _playerStats     = playerStats;
        verboseAttackLog = verbose;

        if (attackOrigin      == null && fallbackOrigin != null)   attackOrigin = fallbackOrigin;
        if (playerLayer.value == 0    && fallbackLayer.value != 0) playerLayer  = fallbackLayer;

        if (_selfStats == null) _selfStats = GetComponent<EntityStats>();
        if (_animator  == null) _animator  = GetComponentInChildren<Animator>();
        if (_sb == null && _selfStats != null) _sb = _selfStats.enemyStatBlock;
    }

    // ── Shared helpers for subclasses ──

    // ── Windup tracking (natural turn-to-attack, hard lock for the dodge) ──

    [Header("Windup Tracking")]
    [Tooltip("Degrees/sec the enemy turns toward the player DURING a windup — " +
             "fast but visible rotation, never a snap. Also the anti-circling " +
             "cap: it out-turns an orbiting player at close range.")]
    public float windupTurnSpeed = 240f;

    [Range(0f, 1f)]
    [Tooltip("Fraction of the windup spent tracking. After this point the facing " +
             "HARD LOCKS — sidestepping during the locked portion is the earned " +
             "dodge. 0.6 = tracks the first 60%.")]
    public float windupLockFraction = 0.6f;

    /// <summary>Begin an attack's facing: no snap — just adopt the current
    /// rotation as the initial lock. TrackOrHold turns us during the windup.</summary>
    protected void FaceAndLockOntoPlayer()
    {
        _lockedRotation = transform.rotation;
    }

    /// <summary>Call every Tick during a WINDUP with 0-1 progress: tracks the
    /// player at windupTurnSpeed until the lock fraction, then holds — the
    /// telegraph's direction promise.</summary>
    protected void TrackOrHold(float windupProgress01)
    {
        if (windupProgress01 < windupLockFraction && _player != null)
        {
            Vector3 look = _player.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(look);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target, windupTurnSpeed * Time.deltaTime);
            }
            _lockedRotation = transform.rotation;
        }
        else
        {
            transform.rotation = _lockedRotation;
        }
    }

    /// <summary>Call from Tick while EXECUTING an attack — holds the locked
    /// facing so root motion / NavMesh drift can't turn the enemy mid-strike.</summary>
    protected void HoldLockedRotation()
    {
        if (IsRotationLocked)
            transform.rotation = _lockedRotation;
    }

    /// <summary>True if the player overlaps a sphere at <paramref name="center"/>.
    /// Uses playerLayer, verifies via EntityStats so props on the layer can't eat hits.</summary>
    protected bool PlayerOverlapsSphere(Vector3 center, float radius)
    {
        foreach (Collider hit in Physics.OverlapSphere(center, radius, playerLayer))
        {
            EntityStats es = hit.GetComponentInParent<EntityStats>();
            if (es != null && es.isPlayer) return true;
        }
        return false;
    }

    /// <summary>Damage roll: Random(min..max) + Strength × the stat block's
    /// attackStrengthBonus. Pass the min/max of whichever attack is firing.</summary>
    protected int RollDamage(int min, int max)
    {
        int strength = _selfStats != null ? _selfStats.Strength : 0;
        int bonus    = _sb != null ? _sb.attackStrengthBonus : 0;
        return Random.Range(min, max + 1) + strength * bonus;
    }

    /// <summary>Damage roll for the stat block's Basic Attack (Tier 1).</summary>
    protected int RollBasicAttackDamage()
    {
        if (_sb == null || _sb.basicAttack == null) return 1;
        return RollDamage(_sb.basicAttack.damageMin, _sb.basicAttack.damageMax);
    }

    /// <summary>Raycast straight down from <paramref name="anchor"/> to find the
    /// REAL floor height (skipping this enemy's own colliders). Returns true and
    /// the floor Y when ground is found within <paramref name="maxDistance"/>.</summary>
    protected bool TryFindGroundY(Vector3 anchor, out float groundY, float maxDistance = 12f)
    {
        Vector3 origin = new Vector3(anchor.x,
                                     Mathf.Max(anchor.y, transform.position.y) + 1f,
                                     anchor.z);
        groundY = float.NegativeInfinity;
        bool found = false;

        foreach (RaycastHit h in Physics.RaycastAll(origin, Vector3.down,
                     maxDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.root == transform.root) continue;   // skip own colliders
            if (h.point.y > groundY) { groundY = h.point.y; found = true; }
        }
        return found;
    }

    /// <summary>Anchor point snapped down onto the detected floor. Falls back to
    /// the anchor unchanged if no ground is found. Use for placing decals so they
    /// sit on the real floor instead of the NavMesh's approximation.</summary>
    protected Vector3 GroundSnap(Vector3 anchor)
    {
        if (TryFindGroundY(anchor, out float y))
            return new Vector3(anchor.x, y, anchor.z);
        return anchor;
    }

    /// <summary>Fire an animator trigger only if the controller actually has it —
    /// lets combat scripts work before their animations are authored.</summary>
    protected void SetTriggerIfPresent(string trigger)
    {
        if (_animator == null || string.IsNullOrEmpty(trigger)) return;
        foreach (var p in _animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == trigger)
            {
                _animator.SetTrigger(trigger);
                return;
            }
        }
    }

    private PlayerMovement _playerMovement;

    /// <summary>Deal damage to the player + apply the matching hit reaction.
    /// decalHit = true → player STAGGER (big shove + recovery lockout).
    /// decalHit = false → basic hit reaction (small push + brief i-frames, no
    /// control loss). pushDir (optional) = attacker-chosen shove direction —
    /// e.g. the tough's dash shoves ALONG the charge; default = away from us.</summary>
    protected void DamagePlayer(int amount, bool decalHit, Vector3 pushDir = default)
    {
        if (_playerStats == null) return;
        if (_playerStats.IsInvulnerable) return;   // don't stack reactions during i-frames

        _playerStats.TakeDamage(amount);

        if (_playerMovement == null && _playerStats != null)
            _playerMovement = _playerStats.GetComponent<PlayerMovement>();
        _playerMovement?.ApplyHitReaction(transform.position, decalHit, pushDir);

        if (verboseAttackLog)
            Debug.Log($"[{GetType().Name}] {gameObject.name} hit player for {amount} " +
                      $"({(decalHit ? "DECAL → stagger" : "basic → flinch")})");
    }

    /// <summary>Back-compat overload — treats the hit as a basic (non-decal) hit.</summary>
    protected void DamagePlayer(int amount) => DamagePlayer(amount, false);
}
