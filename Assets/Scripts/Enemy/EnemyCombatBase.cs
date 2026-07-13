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

    public Vector3 HitOriginPosition => HitOrigin.position;
    protected Transform HitOrigin => attackOrigin != null ? attackOrigin : transform;

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

    /// <summary>Snap-face the player on the XZ plane and remember that rotation
    /// as the lock direction for the rest of the attack.</summary>
    protected void FaceAndLockOntoPlayer()
    {
        if (_player == null) return;
        Vector3 lookDir = _player.position - transform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir);
        _lockedRotation = transform.rotation;
    }

    /// <summary>Call from Tick while attacking — holds the locked facing so
    /// root motion / NavMesh drift can't turn the enemy mid-attack.</summary>
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

    /// <summary>Deal damage to the player (no-op if refs missing).</summary>
    protected void DamagePlayer(int amount)
    {
        if (_playerStats == null) return;
        _playerStats.TakeDamage(amount);
        if (verboseAttackLog)
            Debug.Log($"[{GetType().Name}] {gameObject.name} hit player for {amount}");
    }
}
