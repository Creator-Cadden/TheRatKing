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

    [Header("Facing & Spacing (engaged idle)")]
    [Tooltip("Degrees/sec the enemy turns to face the player while in range — " +
             "smooth tracking instead of the frozen-then-snap look.")]
    public float turnSpeedDegrees = 300f;

    [Tooltip("If the player gets closer than this, the enemy backs up — no more " +
             "standing inside each other.")]
    public float personalSpace = 1.2f;

    [Tooltip("Backpedal speed when the player crowds it.")]
    public float backpedalSpeed = 1.5f;

    [Tooltip("Sideways drift speed while waiting on attack cooldown — reads as " +
             "circling prey. 0 = off. Try 1.0-1.5 for the natural look.")]
    public float orbitSpeed = 1.2f;

    // Each enemy picks a persistent circling direction so packs don't sync up.
    private int _orbitDir = 1;

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

        _orbitDir = Random.value < 0.5f ? -1 : 1;   // per-enemy circling direction

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
        if (_stats.IsDead)
        {
            // Corpse launch — if the killing blow applied knockback, let the body
            // keep flying even though the enemy is now dead.
            if (_isKnockedBack) HandleKnockback();
            return;
        }

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
        {
            _agent.SetDestination(_player.position);
        }
        else
        {
            // ── Engaged idle: face + space naturally instead of freezing ──
            _agent.ResetPath();
            SmoothFacePlayer();

            // Personal space: if the player is basically inside it, back up.
            if (dist < personalSpace)
            {
                Vector3 awayDir = (transform.position - _player.position);
                awayDir.y = 0f;
                if (awayDir.sqrMagnitude > 0.0001f)
                    _agent.Move(awayDir.normalized * backpedalSpeed * Time.deltaTime);
            }
            else if (orbitSpeed > 0f)
            {
                // Gentle sideways drift while waiting on cooldown — reads as
                // circling prey rather than statue-standing.
                Vector3 toPlayer = (_player.position - transform.position);
                toPlayer.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up, toPlayer.normalized) * _orbitDir;
                _agent.Move(side * orbitSpeed * Time.deltaTime);
            }
        }
    }

    private void SmoothFacePlayer()
    {
        Vector3 look = _player.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(look);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, turnSpeedDegrees * Time.deltaTime);
    }

    public void OnAttackHitFrame() => _combat?.OnAttackHitFrame();
    public void OnAttackEnd()      => _combat?.OnAttackEnd();

    private static float FlatDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ── Ballistic knockback (launch + arc + bounces) ──

    [Header("Stagger Launch")]
    [Tooltip("Fraction of the knockback force converted to UPWARD velocity. " +
             "0 = flat shove · 0.5–0.7 = proper cartoon launch.")]
    public float launchUpFactor = 0.55f;

    [Tooltip("Extra launch multiplier per point of Impact ABOVE the stagger " +
             "threshold — overkill hits fling harder. Hammer (4) vs Grunt (0) " +
             "= margin 4 → ×" + "1.6 with the default 0.2.")]
    public float launchPerMargin = 0.2f;

    [Tooltip("How much vertical speed survives each ground bounce. 0.45 = " +
             "satisfying thump-thump settle.")]
    public float bounciness = 0.45f;

    [Tooltip("Max ground bounces before it just slides to a stop.")]
    public int maxBounces = 2;

    [Tooltip("Horizontal speed kept after each bounce.")]
    public float bounceHorizontalKeep = 0.6f;

    [Header("Hitstop (impact freeze — scales with the reaction)")]
    [Tooltip("Freeze seconds when the hit does nothing (powers through). Keep tiny.")]
    public float shrugHitStop = 0.02f;
    [Tooltip("Freeze seconds on a flinch.")]
    public float flinchHitStop = 0.05f;
    [Tooltip("Freeze seconds on a stagger (baseline, margin 1).")]
    public float staggerHitStop = 0.10f;
    [Tooltip("Extra freeze seconds per point of overkill margin above 1 — bigger, " +
             "more decisive hits freeze longer.")]
    public float hitStopPerMargin = 0.03f;

    private const float KnockbackGravity   = -30f;
    private const float MinBounceSpeed     = 3f;    // below this, no more bouncing
    private const float KnockbackSafetyCap = 3f;    // absolute max airtime

    private float _knockbackGroundY;
    private int   _bouncesLeft;

    private void HandleKnockback()
    {
        _agent.enabled = false;

        // Ballistic step.
        _knockbackVelocity.y += KnockbackGravity * Time.deltaTime;
        transform.position   += _knockbackVelocity * Time.deltaTime;

        // Ground contact → bounce or settle.
        if (transform.position.y <= _knockbackGroundY && _knockbackVelocity.y < 0f)
        {
            Vector3 p = transform.position;
            p.y = _knockbackGroundY;
            transform.position = p;

            if (_bouncesLeft > 0 && -_knockbackVelocity.y > MinBounceSpeed)
            {
                _bouncesLeft--;
                _knockbackVelocity.y  = -_knockbackVelocity.y * bounciness;
                _knockbackVelocity.x *= bounceHorizontalKeep;
                _knockbackVelocity.z *= bounceHorizontalKeep;
            }
            else
            {
                // Grounded slide-out.
                _knockbackVelocity.y = 0f;
                _knockbackVelocity   = Vector3.Lerp(_knockbackVelocity, Vector3.zero,
                                                    12f * Time.deltaTime);
                if (_knockbackVelocity.sqrMagnitude < 0.25f)
                {
                    _isKnockedBack = false;
                    if (!_stats.IsDead) _agent.enabled = true;   // don't revive a corpse's agent
                    return;
                }
            }
        }

        // Safety cap — never stuck airborne (e.g. launched onto a ledge).
        _knockbackTimer -= Time.deltaTime;
        if (_knockbackTimer <= 0f)
        {
            _isKnockedBack = false;
            if (!_stats.IsDead) _agent.enabled = true;
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

    [Tooltip("Small positional shove on FLINCH-tier hits (no launch, no cancel) — " +
             "keeps combo targets drifting backward each hit so they can't sit " +
             "inside the player. Shrug-tier hits still don't move (powers through), " +
             "and decal windups are never displaced (armor holds position).")]
    public float flinchNudgeForce = 2.5f;

    [Tooltip("DEBUG — floats the reaction result above the enemy on every player " +
             "hit: SHRUG (gray) / FLINCH (yellow) / STAGGER (red) / DELAYED " +
             "(orange, decal hyper-armor). Turn OFF for builds.")]
    public bool showReactionDebug = true;

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
        if (margin < 0)
        {
            HitStop.Freeze(shrugHitStop);
            CameraJuice.Shake(0.05f);
            AudioManager.Instance?.Play(AudioManager.SoundType.HitShrug);
            SpawnReactionText("SHRUG", new Color(0.7f, 0.7f, 0.7f));
            return;   // powers through
        }

        // Decal hyper armor: never cancelled, only delayed during its windup.
        if (_combat != null && _combat.IsInDecalAction)
        {
            _combat.DelayCurrentWindup(decalWindupDelay);
            _combat.FireAnimTrigger("Flinch");
            HitStop.Freeze(flinchHitStop);
            CameraJuice.Shake(0.08f);
            AudioManager.Instance?.Play(AudioManager.SoundType.HitDelayed);
            SpawnReactionText("DELAYED", new Color(1f, 0.6f, 0.1f));
            return;
        }

        if (margin == 0)
        {
            // Flinch: feedback + tax. Basic windup gets pushed back; no cancel,
            // no lockout — but a small NUDGE so repeated combo hits walk the
            // target backwards instead of letting it sit inside the player.
            _combat?.DelayCurrentWindup(flinchWindupDelay);
            _combat?.FireAnimTrigger("Flinch");

            if (flinchNudgeForce > 0f)
            {
                Vector3 flat = transform.position - sourcePosition;
                flat.y = 0f;
                flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : -transform.forward;

                _knockbackVelocity   = flat * flinchNudgeForce;
                _knockbackVelocity.y = flinchNudgeForce * 0.15f;   // tiny impact hop
                _knockbackGroundY    = transform.position.y;
                _bouncesLeft         = 0;
                _knockbackTimer      = 0.5f;
                _isKnockedBack       = true;
            }

            HitStop.Freeze(flinchHitStop);
            CameraJuice.Shake(0.1f);
            AudioManager.Instance?.Play(AudioManager.SoundType.HitFlinch);
            SpawnReactionText("FLINCH", Color.yellow);
            return;
        }

        // ── Stagger: cancel + lockout + knockback ──
        HitStop.Freeze(staggerHitStop + Mathf.Max(0, margin - 1) * hitStopPerMargin);
        CameraJuice.Shake(0.2f + Mathf.Max(0, margin - 1) * 0.06f);
        AudioManager.Instance?.Play(AudioManager.SoundType.HitStagger);
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
            // Overkill scaling: the harder Impact beats Toughness, the harder
            // they fly. Margin 1 = baseline shove, margin 4 (hammer slam vs
            // grunt) = full cartoon launch.
            float overkill = 1f + launchPerMargin * (margin - 1);

            Vector3 flat = transform.position - sourcePosition;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : -transform.forward;

            _knockbackVelocity   = flat * finalForce * overkill;
            _knockbackVelocity.y = finalForce * launchUpFactor * overkill;

            _knockbackGroundY = transform.position.y;
            _bouncesLeft      = maxBounces;
            _knockbackTimer   = KnockbackSafetyCap;
            _isKnockedBack    = true;
        }

        // "Stun" is the existing animator trigger name; "Stagger" for new setups.
        _animator.SetTrigger("Stun");
        _combat?.FireAnimTrigger("Stagger");
        SpawnReactionText("STAGGER", new Color(1f, 0.2f, 0.15f));
    }

    /// <summary>DEBUG — floating reaction label (SHRUG/FLINCH/STAGGER/DELAYED)
    /// above the enemy so the Impact system is visible while tuning.</summary>
    private void SpawnReactionText(string text, Color color)
    {
        if (!showReactionDebug) return;

        var go = new GameObject("ReactionText");
        // HIGHER than the damage numbers (which sit ~1.6 up) so they don't overlap.
        go.transform.position = transform.position
                              + Vector3.up * 3.6f
                              + Random.insideUnitSphere * 0.15f;

        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        tmp.text         = text;
        tmp.fontSize     = 3.8f;
        tmp.color        = color;
        tmp.alignment    = TMPro.TextAlignmentOptions.Center;
        tmp.fontStyle    = TMPro.FontStyles.Bold;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;

        // Pop-in + gentle float + fade (self-contained animator, below).
        go.AddComponent<ReactionPopText>().Init(tmp, 0.9f);
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

/// <summary>
/// One-shot animator for the SHRUG / FLINCH / STAGGER / DELAYED reaction label.
/// Pop-in scale overshoot, gentle float up, fade out, camera billboard, then
/// self-destruct. Kept in this file so EnemyAI has no external dependency.
/// </summary>
public class ReactionPopText : MonoBehaviour
{
    private TMPro.TMP_Text _tmp;
    private float          _lifetime;
    private float          _elapsed;
    private Color          _baseColor;
    private Vector3        _baseScale;

    // Tuning.
    private const float FloatSpeed  = 1.2f;   // gentle upward drift
    private const float PopScale    = 1.35f;  // overshoot on spawn
    private const float PopDuration = 0.12f;

    public void Init(TMPro.TMP_Text tmp, float lifetime)
    {
        _tmp       = tmp;
        _lifetime  = lifetime;
        _baseColor = tmp.color;
        _baseScale = transform.localScale;
        transform.localScale = _baseScale * 0.4f;   // start small for the pop
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = _elapsed / Mathf.Max(0.0001f, _lifetime);

        // Gentle float.
        transform.position += Vector3.up * FloatSpeed * Time.deltaTime;

        // Pop-in overshoot, then settle to 1.
        float s;
        if (_elapsed < PopDuration)
        {
            float p = _elapsed / PopDuration;
            s = Mathf.Lerp(0.4f, PopScale, 1f - (1f - p) * (1f - p));
        }
        else
        {
            float p = Mathf.Clamp01((_elapsed - PopDuration) / 0.1f);
            s = Mathf.Lerp(PopScale, 1f, p);
        }
        transform.localScale = _baseScale * s;

        // Fade over the last 40%.
        if (_tmp != null)
        {
            Color c = _baseColor;
            c.a = _baseColor.a * (1f - Mathf.Clamp01(Mathf.InverseLerp(0.6f, 1f, t)));
            _tmp.color = c;
        }

        // Billboard.
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(
                cam.transform.rotation * Vector3.forward,
                cam.transform.rotation * Vector3.up);

        if (t >= 1f) Destroy(gameObject);
    }
}
