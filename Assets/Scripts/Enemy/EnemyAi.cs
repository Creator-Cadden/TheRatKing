using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Movement/aggro brain for regular enemies (NOT the boss — see FatRatBoss).
/// Drives the NavMeshAgent: idle → chase when player is in aggro range → stop at
/// attack reach (read from EnemyCombat.CurrentAttackReach) → knockback handling.
/// BossMinionSpawner sets speedMultiplier/permanentlyAggroed right after Instantiate,
/// BEFORE Start() runs — Start() bakes speedMultiplier into agent.speed.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("Legacy Attack Migration")]
    public LayerMask playerLayer;
    public Transform attackOrigin;

    [Header("Post-Attack")]
    [Tooltip("Seconds after an attack completes before the enemy resumes rotating and chasing.\n" +
             "Gives the player a window to reposition after dodging.")]
    public float postAttackDelay = 0.4f;

    [Header("Debug")]
    public bool showAttackGizmo = true;
    public bool showAggroGizmo = true;
    public bool verboseAttackLog = false;

    private NavMeshAgent _agent;
    private Transform _player;
    private EntityStats _stats;
    private EntityStats _playerStats;
    private Animator _animator;
    private EnemyStatBlock _sb;
    private EnemyCombatBase _combat;   // any enemy type's combat script (GruntCombat, EnemyCombat legacy, ...)

    private bool  _isAggroed;
    private bool  _isKnockedBack;
    private bool  _isPostAttackPause;
    private float _postAttackPauseUntil;
    private Vector3 _knockbackVelocity;
    private float _knockbackTimer;

    // Persistent aggro — once damaged, the enemy stays angry for damagedAggroDuration
    // seconds even if the player runs out of aggroRange. Set by OnDamaged callback.
    private float _damagedAggroUntil = -1f;

    [Header("Special Aggro")]
    [Tooltip("If on, this enemy is permanently aggroed against the player — " +
             "ignores aggroRange and always pursues. Used for boss-arena spawn " +
             "minions that should hunt the player from anywhere in the arena.\n\n" +
             "Set by BossMinionSpawner on spawn. Leave OFF on normal enemies " +
             "that should patrol passively until the player gets close.")]
    public bool permanentlyAggroed = false;

    [Tooltip("Multiplier applied to the NavMeshAgent's movement speed on Start. " +
             "1.0 = stat block value, 1.5 = 50% faster, 2.0 = double speed.\n\n" +
             "Used by BossMinionSpawner to make spawned grunts more aggressive " +
             "without modifying the shared EnemyStatBlock asset. Leave at 1 for " +
             "normal-speed enemies.")]
    public float speedMultiplier = 1f;

    // Tracks whether we were locked last frame so we can detect the
    // exact frame the lock releases and start the post-attack pause.
    private bool _wasRotationLocked;

    /// <summary>True during the brief stand-still window after an attack —
    /// exposed for debug visuals (GruntCombat's state ball) and future
    /// punish-window logic.</summary>
    public bool IsInPostAttackPause => _isPostAttackPause;

    void Start()
    {
        _stats = GetComponent<EntityStats>();
        _animator = GetComponentInChildren<Animator>();

        if (_stats == null || _stats.enemyStatBlock == null)
        {
            Debug.LogError($"[EnemyAI] {gameObject.name} is missing EntityStats or EnemyStatBlock.");
            return;
        }

        _sb = _stats.enemyStatBlock;

        // Find whichever combat script this enemy type uses (GruntCombat,
        // legacy EnemyCombat, future ToughCombat...). Only auto-add the legacy
        // decal version if the prefab has no combat script at all.
        _combat = GetComponent<EnemyCombatBase>();
        if (_combat == null) _combat = gameObject.AddComponent<EnemyCombat>();

        _agent = GetComponent<NavMeshAgent>();
        // Apply speed multiplier (default 1.0 = no change). Boss-arena minions
        // get this set above 1 by BossMinionSpawner so they hunt aggressively.
        _agent.speed = _sb.moveSpeed * Mathf.Max(0.01f, speedMultiplier);
        _agent.stoppingDistance = _sb.stopRange;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _player = playerObj.transform;
            _playerStats = playerObj.GetComponent<EntityStats>();
        }

        _stats.onDeath.AddListener(OnDeath);
        _stats.onDamageTaken.AddListener(OnDamaged);
        _combat.ConfigureRuntime(_player, _playerStats, attackOrigin, playerLayer, verboseAttackLog);
    }

    private void OnDamaged(int _)
    {
        if (_sb == null) return;
        if (_sb.damagedAggroDuration <= 0f) return;

        _damagedAggroUntil = Time.time + _sb.damagedAggroDuration;

        if (verboseAttackLog)
            Debug.Log($"[EnemyAI] {gameObject.name} aggro extended → " +
                      $"chasing for {_sb.damagedAggroDuration:F0}s after being damaged.");
    }

    void Update()
    {
        if (_player == null || _sb == null) return;
        if (_stats.IsDead) return;

        if (_isKnockedBack)
        {
            _combat.CancelAttackState();
            HandleKnockback();
            return;
        }

        // Stagger lockout — stumbling, can't move or attack until it ends.
        if (IsStaggered)
        {
            if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
            return;
        }

        _combat.verboseAttackLog = verboseAttackLog;
        _combat.Tick();

        // Detect the frame the rotation lock releases (attack fully finished)
        // and start the post-attack pause timer.
        bool lockedNow = _combat.IsRotationLocked;
        if (_wasRotationLocked && !lockedNow)
        {
            _isPostAttackPause    = true;
            _postAttackPauseUntil = Time.time + postAttackDelay;
        }
        _wasRotationLocked = lockedNow;

        // During the post-attack pause the enemy stands still and faces the
        // direction it attacked — no chasing, no rotation, no new attacks.
        if (_isPostAttackPause)
        {
            _agent.ResetPath();
            if (Time.time >= _postAttackPauseUntil)
                _isPostAttackPause = false;
            return;
        }

        // While the combat system owns rotation, let it — don't steer here.
        if (_combat.IsBusy) return;

        float dist = FlatDist(transform.position, _player.position);

        // Aggro if either: (a) player is within normal aggroRange,
        // (b) enemy was hit recently and is still in its damage-memory window,
        // OR (c) the permanentlyAggroed flag is set (boss-arena minions).
        bool inAggroRange    = dist <= _sb.aggroRange;
        bool rememberDamager = Time.time < _damagedAggroUntil;

        if (permanentlyAggroed || inAggroRange || rememberDamager)
        {
            _isAggroed = true;
            _animator.SetFloat("Running", 1);
        }
        else
        {
            _isAggroed = false;
            _agent.ResetPath();
            _animator.SetFloat("Running", 0);
        }

        if (!_isAggroed) return;

        float walkThreshold   = _sb.stopRange;
        float attackThreshold = _combat.CurrentAttackReach + 0.35f;

        // Offer the attack FIRST, from anywhere within the current reach — the
        // combat script decides if this distance suits any of its attacks
        // (Tough dashes from 3-6m; grunt only bites within ~1.7m). Gating this
        // behind stopRange made ranged attacks like the dash unreachable.
        if (dist <= attackThreshold)
        {
            _combat.TryStartAttack(dist);
            if (_combat.IsBusy)
            {
                _agent.ResetPath();
                return;
            }
        }

        // No attack started — keep chasing until inside stopRange.
        if (dist > walkThreshold)
            _agent.SetDestination(_player.position);
        else
            _agent.ResetPath();
    }

    public void OnAttackHitFrame() => _combat?.OnAttackHitFrame();
    public void OnAttackEnd()      => _combat?.OnAttackEnd();

    private static float FlatDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void HandleKnockback()
    {
        _agent.enabled = false;
        transform.position += _knockbackVelocity * Time.deltaTime;
        _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, 10f * Time.deltaTime);

        _knockbackTimer -= Time.deltaTime;
        if (_knockbackTimer <= 0f)
        {
            _isKnockedBack = false;
            _agent.enabled = true;
        }
    }

    // ── Impact reaction system ──────────────────────────────────────────
    // Reaction = Impact − Toughness:
    //   below 0 → shrug (nothing) · 0 → flinch (delays a basic windup)
    //   · +1 or more → stagger (cancel + lockout + knockback).
    // DECAL actions are unstoppable: any flinch/stagger-tier hit only DELAYS
    // the decal windup (capped in EnemyCombatBase). Perks may interrupt later.

    [Header("Impact Reactions")]
    [Tooltip("Seconds a flinch (Impact == Toughness) pushes a basic windup back.")]
    public float flinchWindupDelay = 0.3f;

    [Tooltip("Seconds each stagger-tier hit pushes a DECAL windup back " +
             "(decals can't be cancelled — only delayed, capped per windup).")]
    public float decalWindupDelay = 0.35f;

    [Tooltip("Seconds the enemy is locked out of acting after a stagger.")]
    public float staggerDuration = 0.7f;

    private float _staggerUntil = -999f;

    /// <summary>True while stumbling from a stagger — can't move or attack.</summary>
    public bool IsStaggered => Time.time < _staggerUntil;

    /// <summary>
    /// Primary entry — call when a player attack hits this enemy.
    /// impact = the weapon attack's Impact value (EntityStats.GetWeaponImpact).
    /// </summary>
    public void ApplyHitReaction(Vector3 sourcePosition, int impact)
    {
        if (_sb == null || _stats == null || _stats.IsDead) return;

        int margin = impact - _stats.Toughness;
        if (margin < 0) return;   // shrug — powers through

        // Decal hyper armor: never cancelled, only delayed during its windup.
        if (_combat != null && _combat.IsInDecalAction)
        {
            _combat.DelayCurrentWindup(decalWindupDelay);
            _combat.FireAnimTrigger("Flinch");
            return;
        }

        if (margin == 0)
        {
            // Flinch: feedback + tax. Basic windup gets pushed back; no cancel,
            // no knockback, no lockout.
            _combat?.DelayCurrentWindup(flinchWindupDelay);
            _combat?.FireAnimTrigger("Flinch");
            return;
        }

        // ── Stagger: cancel + lockout + knockback ──
        _combat?.CancelAttackState();
        _staggerUntil = Time.time + staggerDuration;

        float baseForce = impact switch
        {
            >= 3 => _sb.hammerKnockbackForce,
            2    => _sb.bladeKnockbackForce,
            _    => _sb.bowKnockbackForce
        };
        float finalForce = baseForce - (_stats.Toughness * _sb.toughnessReductionPerPoint);
        if (finalForce > 0f)
        {
            Vector3 direction = (transform.position - sourcePosition).normalized;
            direction.y = 0.3f;
            _knockbackVelocity = direction * finalForce;
            _knockbackTimer    = _sb.knockbackDuration;
            _isKnockedBack     = true;
        }

        // "Stun" is the existing animator trigger name; "Stagger" for new setups.
        _animator.SetTrigger("Stun");
        _combat?.FireAnimTrigger("Stagger");
    }

    // ── Legacy adapters (old staggerForce-based callers) ────────────────
    public void TakeKnockback(Vector3 sourcePosition, int staggerForce, int _)
        => ApplyHitReaction(sourcePosition, staggerForce >= 8 ? 3 : Mathf.Clamp(staggerForce, 0, 5));

    public void TakeKnockback(Vector3 sourcePosition, int staggerForce)
        => TakeKnockback(sourcePosition, staggerForce, 0);

    public void TakeKnockback(Vector3 sourcePosition)
        => TakeKnockback(sourcePosition, 2, 0);

    private void OnDeath()
    {
        _combat.CancelAttackState();
        _agent.enabled = false;
        _animator.SetBool("Death", true);
    }

    void OnDrawGizmos()
    {
        var entityStats = GetComponent<EntityStats>();
        EnemyStatBlock sb = entityStats != null ? entityStats.enemyStatBlock : null;
        if (sb == null) return;

        Vector3 hitOrigin = _combat != null
            ? _combat.HitOriginPosition
            : (attackOrigin != null ? attackOrigin.position : transform.position);

        if (showAggroGizmo)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.06f);
            Gizmos.DrawSphere(transform.position, sb.aggroRange);

            Gizmos.color = new Color(1f, 1f, 0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, sb.aggroRange);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, sb.stopRange);
        }

        if (!showAttackGizmo) return;

        // Attack reach ring — basic attack reach (each combat script draws its
        // own detailed hit-volume gizmo; this is just the engage distance).
        if (sb.hasBasicAttack && sb.basicAttack != null)
        {
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(hitOrigin, sb.basicAttack.reach);
        }
    }
}
