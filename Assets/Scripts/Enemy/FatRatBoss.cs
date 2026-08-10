using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Giant Fat Rat boss. Completely separate from regular EnemyAI/EnemyCombat.
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class FatRatBoss : MonoBehaviour
{
    public enum BossState
    {
        Idle, Chase, PauseBefore,
        RollWindup, Rolling, RollRecover,
        JumpWindup, InAir, Slamming, SlamRecover,
        Stunned, Dead,
        StompWindup, StompRecover,   // Ground Stomp basic (hit anim only, no decal)
        RollPairGap                  // brief re-aim between the two rolls of a pair
    }

    public enum AttackKind { Roll, Slam }

    [Header("Detection & Movement")]
    [Tooltip("Distance at which the boss notices the player and starts chasing.")]
    public float aggroRange = 15f;

    [Tooltip("Slow chase speed. Boss is meant to feel ponderous.")]
    public float moveSpeed = 1.8f;

    [Tooltip("Distance at which the boss stops walking and prepares to attack.\n" +
             "Measured from the boss's TRANSFORM ORIGIN — if your boss model is scaled up, " +
             "this needs to grow with it. For a ~2x scaled rat, 7–9 is a good starting point.")]
    public float stopRange = 8f;

    [Header("Pre-Attack Pause")]
    [Tooltip("Seconds the boss stands still before EACH attack windup.")]
    public float pauseDuration = 2f;

    [Header("Attack Pattern")]
    [Tooltip("Order of attacks. Loops forever. Roll = the DOUBLE roll pair. " +
             "Design doc pattern: Roll (pair), Slam.")]
    public AttackKind[] attackPattern = { AttackKind.Roll, AttackKind.Slam };

    // ── Ground Stomp — the BASIC attack (hit anim only, no decal) ──
    // Fires opportunistically whenever the player hugs the boss, independent
    // of the Roll/Slam pattern — melee range is never free.

    [Header("Ground Stomp (basic attack — no decal)")]
    [Tooltip("Player closer than this (and stomp off cooldown) → stomp.")]
    public float stompRange = 3.5f;

    [Tooltip("Hit check: sphere radius in front of the boss.")]
    public float stompRadius = 2.6f;

    [Tooltip("How far in front of the boss the stomp sphere sits.")]
    public float stompForwardOffset = 1.2f;

    public int   stompDamage          = 30;
    public float stompWindupDuration  = 0.8f;
    public float stompRecoverDuration = 0.5f;
    public float stompCooldown        = 2.0f;

    // ── Double Roll pairing + per-attack cooldowns (design doc) ──

    [Header("Double Roll & Cooldowns")]
    [Tooltip("Rolls per pattern entry (doc: rolls twice in a row).")]
    public int rollsInPair = 2;

    [Tooltip("Seconds between the two rolls. First part re-aims at the player, " +
             "the rest is LOCKED — that locked window is the dodge for roll two. " +
             "Longer = the boss feels less frantic and gives a bigger dodge window.")]
    public float rollPairGap = 1.3f;

    [Range(0f, 1f)]
    [Tooltip("Fraction of the gap spent re-aiming. After this the corridor " +
             "LOCKS (indicator flips to the committed color) — sidestep now.")]
    public float pairGapLockFraction = 0.5f;

    [Tooltip("Cooldown after the full pair before the next pattern attack.")]
    public float rollPairCooldown = 4f;

    [Tooltip("Cooldown after the Jump & Slam before the next pattern attack.")]
    public float slamCooldown = 6f;

    // ── Berserk (design doc: ≤25% HP → everything faster) ──

    [Header("Berserk")]
    [Tooltip("HP fraction at which the boss enrages.")]
    public float berserkHealthFraction = 0.25f;

    [Tooltip("Multiplier on ALL windups, recoveries, gaps, and cooldowns while " +
             "berserk. 0.6 = 40% faster everything.")]
    public float berserkTimeScale = 0.6f;

    private bool  _isBerserk;
    private int   _rollsDoneInPair;
    private float _attackReadyAt;   // gate for the next Roll/Slam pattern attack
    private float _stompReadyAt;    // independent stomp cooldown

    /// <summary>All boss timings route through this — berserk shrinks them.</summary>
    private float T(float seconds) => _isBerserk ? seconds * berserkTimeScale : seconds;

    [Header("Roll Attack")]
    [Tooltip("Seconds the boss spends balling up and aiming before launching the roll.")]
    public float rollWindupDuration = 1.5f;

    [Tooltip("Speed of the actual roll (much faster than walk speed).")]
    public float rollSpeed = 14f;

    [Tooltip("How far the boss rolls past its starting point.")]
    public float rollDistance = 12f;

    [Tooltip("Width of the rectangular roll hitbox / indicator (matches body width).")]
    public float rollWidth = 1.8f;

    [Tooltip("Damage dealt when the rolling body touches the player.")]
    public int rollDamage = 35;

    [Tooltip("Recovery time after the roll completes before the boss resumes.")]
    public float rollRecoverDuration = 1.2f;

    [Header("Slam Attack")]
    [Tooltip("Time spent crouching before the boss leaves the ground.")]
    public float jumpWindupDuration = 0.45f;

    [Tooltip("How high the boss rises off the ground during the jump.")]
    public float jumpHeight = 6f;

    [Tooltip("Time spent in the air between leaving the ground and crashing down. " +
             "This is the player's window to leave the marked circle.")]
    public float airDuration = 1.2f;

    [Tooltip("Radius of the AoE circle telegraph at the landing spot.")]
    public float slamRadius = 3.5f;

    [Tooltip("Damage dealt to anyone caught in the slam radius.")]
    public int slamDamage = 50;

    [Tooltip("Recovery time after slam impact.")]
    public float slamRecoverDuration = 1.4f;

    [Header("Contact Damage (touch the rat = ouch)")]
    [Tooltip("If on, the player takes damage just from being close to the boss's body " +
             "(not just when an attack lands). Disabled during active attacks (Rolling, " +
             "InAir, Slamming) because those have their own damage.")]
    public bool enableContactDamage = true;

    [Tooltip("How close the player must be to the boss's transform origin to take " +
             "body-contact damage. For a scaled-up boss, this should roughly match the " +
             "visible body radius.")]
    public float contactDamageRadius = 2.8f;

    [Tooltip("Damage dealt per contact tick.")]
    public int contactDamage = 8;

    [Tooltip("Seconds between contact-damage ticks while you're touching the boss.\n" +
             "Lower = more painful to stand near.")]
    public float contactDamageInterval = 0.6f;

    [Tooltip("Force of the radial shove when the player takes body-contact damage.")]
    public float contactKnockbackForce = 6f;

    [Tooltip("How long the contact knockback push lasts.")]
    public float contactKnockbackDuration = 0.2f;

    [Header("Hit Knockback")]
    [Tooltip("Force of the player shove when caught by the roll (pushed in the roll's " +
             "direction — you get carried by the body that rolled into you).")]
    public float rollKnockbackForce = 16f;

    [Tooltip("Duration of the roll knockback.")]
    public float rollKnockbackDuration = 0.5f;

    [Tooltip("Force of the player shove when caught by the slam (pushed radially away " +
             "from the slam center).")]
    public float slamKnockbackForce = 14f;

    [Tooltip("Duration of the slam knockback.")]
    public float slamKnockbackDuration = 0.4f;

    [Header("References")]
    [Tooltip("Layer the player is on, for hit detection during attacks.")]
    public LayerMask playerLayer;

    [Tooltip("Optional override — point this at a hitbox bone or origin transform. " +
             "Leave null to use the boss transform.")]
    public Transform attackOrigin;

    [Header("Indicator Colors")]
    public Color windupColor  = new Color(1f,   0.15f, 0.1f, 0.55f);
    public Color committedColor = new Color(1f, 0.6f,  0.0f, 0.85f);

    [Header("Debug")]
    public bool verbose = false;
    public bool showGizmos = true;

    // ── Private state ──

    private BossState     _state = BossState.Idle;
    private NavMeshAgent  _agent;
    private EntityStats   _stats;
    private Animator      _animator;
    private Transform     _player;
    private EntityStats   _playerStats;
    private PlayerMovement _playerMovement;   // for TakeKnockback

    // Contact damage cooldown
    private float         _lastContactDamageTime = -999f;

    private int           _patternIndex = 0;
    private float         _stateUntil;     // generic timer for current state

    // Roll state
    private Vector3       _rollDir;
    private Vector3       _rollStart;
    private float         _rollDistTravelled;
    private float         _effectiveRollDistance;   // capped to NavMesh edge so the boss never rolls off the arena
    private bool          _rollHitPlayerThisAttack;

    // Slam state
    private Vector3       _slamGroundPos;       // boss XZ at moment of jump
    private Vector3       _slamLandingPos;      // where boss will crash
    private float         _slamStartY;

    // Indicators (lazy-created)
    private GameObject    _rectIndicator;
    private GameObject    _circleIndicator;
    private MeshFilter    _rectFilter, _circleFilter;
    private MeshRenderer  _rectRenderer, _circleRenderer;
    private Material      _rectMat, _circleMat;

    // ── Unity lifecycle ──

    void Start()
    {
        _stats    = GetComponent<EntityStats>();
        _agent    = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();

        if (_agent != null)
        {
            _agent.speed           = moveSpeed;
            _agent.stoppingDistance = stopRange;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _player         = playerObj.transform;
            _playerStats    = playerObj.GetComponent<EntityStats>();
            _playerMovement = playerObj.GetComponent<PlayerMovement>();
        }

        if (_stats != null) _stats.onDeath.AddListener(OnDeath);
        if (_stats != null) _stats.onDamageTaken.AddListener(OnDamagedBerserkCheck);

        SetState(BossState.Idle);
    }

    void OnDestroy()
    {
        if (_rectIndicator   != null) Destroy(_rectIndicator);
        if (_circleIndicator != null) Destroy(_circleIndicator);
        if (_rectMat   != null) Destroy(_rectMat);
        if (_circleMat != null) Destroy(_circleMat);
    }

    void Update()
    {
        if (_state == BossState.Dead || _player == null) return;

        switch (_state)
        {
            case BossState.Idle:        TickIdle();        break;
            case BossState.Chase:       TickChase();       break;
            case BossState.PauseBefore: TickPauseBefore(); break;

            case BossState.RollWindup:  TickRollWindup();  break;
            case BossState.Rolling:     TickRolling();     break;
            case BossState.RollRecover: TickRollRecover(); break;

            case BossState.StompWindup:  TickStompWindup();  break;
            case BossState.StompRecover: TickStompRecover(); break;
            case BossState.RollPairGap:  TickRollPairGap();  break;

            case BossState.JumpWindup:  TickJumpWindup();  break;
            case BossState.InAir:       TickInAir();       break;
            case BossState.Slamming:    TickSlamming();    break;
            case BossState.SlamRecover: TickSlamRecover(); break;
        }

        TickContactDamage();
    }

    // ── Contact damage — touch the rat = ouch ──

    private void TickContactDamage()
    {
        if (!enableContactDamage) return;
        if (_playerStats == null) return;

        // Skip when an attack is doing its own damage routine, or when boss is
        // off the ground / dead. Body contact only fires from the actual body.
        switch (_state)
        {
            case BossState.Rolling:
            case BossState.InAir:
            case BossState.Slamming:
            case BossState.SlamRecover:
            case BossState.Dead:
                return;
        }

        if (Time.time < _lastContactDamageTime + contactDamageInterval) return;
        if (FlatDistToPlayer() > contactDamageRadius) return;

        // Knock the player radially away from the boss.
        Vector3 away = _player.position - transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = transform.forward;

        DamagePlayer(contactDamage, "Body Contact");
        KnockbackPlayer(away, contactKnockbackForce, contactKnockbackDuration);

        _lastContactDamageTime = Time.time;
    }

    private void KnockbackPlayer(Vector3 direction, float force, float duration)
    {
        if (_playerMovement == null) return;
        _playerMovement.TakeKnockback(direction, force, duration);
    }

    // ── State machine helpers ──

    private void SetState(BossState next)
    {
        _state = next;
        if (verbose) Debug.Log($"[FatRatBoss] {gameObject.name} → {next}");
    }

    private float FlatDistToPlayer()
    {
        Vector3 a = transform.position;
        Vector3 b = _player.position;
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void OnDamagedBerserkCheck(int _)
    {
        if (_isBerserk || _stats == null || _stats.MaxHealth <= 0) return;
        if ((float)_stats.CurrentHealth / _stats.MaxHealth <= berserkHealthFraction)
        {
            _isBerserk = true;
            FireTriggerIfPresent("Berserk");   // enrage anim/roar hook (optional)
            if (verbose) Debug.Log("[FatRatBoss] BERSERK — all timings ×" + berserkTimeScale);
        }
    }

    private void FireTriggerIfPresent(string trigger)
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

    private void FacePlayer()
    {
        Vector3 dir = _player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    // ── Per-state ticks ──

    private void TickIdle()
    {
        if (FlatDistToPlayer() <= aggroRange)
        {
            if (_animator != null) _animator.SetFloat("Running", 1f);
            SetState(BossState.Chase);
        }
    }

    private void TickChase()
    {
        float dist = FlatDistToPlayer();

        if (dist > aggroRange * 1.5f)
        {
            // Player is way out of range — drop aggro
            if (_agent != null && _agent.enabled) _agent.ResetPath();
            if (_animator != null) _animator.SetFloat("Running", 0f);
            SetState(BossState.Idle);
            return;
        }

        // Ground Stomp — the hug punisher. Fires whenever the player is in the
        // boss's face and the stomp is off cooldown, independent of the pattern.
        if (dist <= stompRange && Time.time >= _stompReadyAt)
        {
            BeginStomp();
            return;
        }

        if (dist <= stopRange)
        {
            if (_agent != null && _agent.enabled) _agent.ResetPath();
            if (_animator != null) _animator.SetFloat("Running", 0f);
            _stateUntil = Time.time + T(pauseDuration);
            SetState(BossState.PauseBefore);
            return;
        }

        if (_agent != null && _agent.enabled)
            _agent.SetDestination(_player.position);
    }

    private void TickPauseBefore()
    {
        FacePlayer();

        // Stomp stays available while waiting out pattern cooldowns.
        if (FlatDistToPlayer() <= stompRange && Time.time >= _stompReadyAt)
        {
            BeginStomp();
            return;
        }

        if (Time.time < _stateUntil)  return;
        if (Time.time < _attackReadyAt) return;   // per-attack cooldown gate (4s pair / 6s slam)

        AttackKind next = attackPattern[_patternIndex % attackPattern.Length];
        _patternIndex   = (_patternIndex + 1) % Mathf.Max(1, attackPattern.Length);

        switch (next)
        {
            case AttackKind.Roll: BeginRollWindup(); break;
            case AttackKind.Slam: BeginSlam();       break;
        }
    }

    // ── Ground Stomp (basic — hit anim only, no decal, no player stagger) ──

    private void BeginStomp()
    {
        if (_agent != null && _agent.enabled) _agent.ResetPath();
        if (_animator != null) _animator.SetFloat("Running", 0f);
        FireTriggerIfPresent("StompWindup");
        _stateUntil = Time.time + T(stompWindupDuration);
        SetState(BossState.StompWindup);
    }

    private void TickStompWindup()
    {
        FacePlayer();   // basics track — the dodge answer is spacing, not angle

        if (Time.time < _stateUntil) return;

        FireTriggerIfPresent("Stomp");
        ResolveStompHit();

        _stompReadyAt = Time.time + T(stompCooldown);
        _stateUntil   = Time.time + T(stompRecoverDuration);
        SetState(BossState.StompRecover);
    }

    private void ResolveStompHit()
    {
        Vector3 center = HitOrigin() + transform.forward * stompForwardOffset;
        Collider[] hits = Physics.OverlapSphere(center, stompRadius, playerLayer);
        if (hits != null && hits.Length > 0)
            DamagePlayer(stompDamage, "Stomp", staggerHit: false);   // basic = hit reaction only
    }

    private void TickStompRecover()
    {
        if (Time.time < _stateUntil) return;
        SetState(BossState.Chase);
    }

    // ── Double-roll pair gap (quick re-aim between the two rolls) ──

    private void TickRollPairGap()
    {
        float dur = Mathf.Max(0.0001f, T(rollPairGap));
        float t   = 1f - Mathf.Clamp01((_stateUntil - Time.time) / dur);

        if (t < pairGapLockFraction)
        {
            // Re-aim phase: corridor swings with the player — stay moving!
            FacePlayer();
            _rollDir   = transform.forward;
            _rollDir.y = 0f;
            if (_rollDir.sqrMagnitude > 0.001f) _rollDir.Normalize();
            UpdateRectIndicatorPose();
            SetRectColor(windupColor);      // still aiming — not committed yet
        }
        else
        {
            // LOCKED: corridor frozen, committed color — this is the dodge
            // window for roll two. Sidestep NOW.
            SetRectColor(committedColor);
        }

        if (Time.time >= _stateUntil)
            CommitRoll();                   // roll two — re-caps to the NavMesh edge
    }

    // ── Roll attack ──

    private void BeginRollWindup()
    {
        FacePlayer();
        _rollDir   = transform.forward;
        _rollDir.y = 0f;
        if (_rollDir.sqrMagnitude < 0.001f) _rollDir = Vector3.forward;
        _rollDir.Normalize();

        // Lock movement during windup
        if (_agent != null) _agent.enabled = false;

        // Build & show the rectangle indicator along the roll path
        EnsureRectIndicator();
        UpdateRectIndicatorPose();
        SetRectColor(new Color(windupColor.r, windupColor.g, windupColor.b, 0f));
        _rectIndicator.SetActive(true);

        if (_animator != null) _animator.SetBool("Roll", true);

        _rollsDoneInPair = 0;   // fresh pair
        _stateUntil = Time.time + T(rollWindupDuration);
        SetState(BossState.RollWindup);
    }

    private void TickRollWindup()
    {
        // Fade the windup color in over the duration
        float t = Mathf.Clamp01(1f - (_stateUntil - Time.time) / Mathf.Max(0.0001f, rollWindupDuration));
        Color c = windupColor;
        c.a *= t;
        SetRectColor(c);

        // Keep the indicator pinned to the current forward direction.
        UpdateRectIndicatorPose();

        if (Time.time >= _stateUntil)
            CommitRoll();
    }

    private void CommitRoll()
    {
        SetRectColor(committedColor);

        _rollStart                 = transform.position;
        _rollDistTravelled         = 0f;
        _rollHitPlayerThisAttack   = false;

        // Cap the roll distance to the NavMesh edge so the boss can't fly off
        // the arena. NavMesh.Raycast walks along the navigable surface from
        // the start toward the target. If it hits an edge (i.e. there's no
        // walkable surface that far), hit.position is the edge intersection.
        _effectiveRollDistance = rollDistance;
        Vector3 target = transform.position + _rollDir * rollDistance;
        if (UnityEngine.AI.NavMesh.Raycast(transform.position, target,
                                           out UnityEngine.AI.NavMeshHit hit,
                                           UnityEngine.AI.NavMesh.AllAreas))
        {
            // hit.position is the edge of the navigable area along the roll path.
            // Subtract a small buffer so the boss doesn't end up exactly on the
            // edge (where collisions sometimes catch funny).
            float edgeDist = Vector3.Distance(transform.position, hit.position);
            _effectiveRollDistance = Mathf.Max(1f, edgeDist - 0.5f);

            if (verbose)
                Debug.Log($"[FatRatBoss] Roll capped from {rollDistance:F1} → " +
                          $"{_effectiveRollDistance:F1} (NavMesh edge).");
        }

        SetState(BossState.Rolling);
    }

    private void TickRolling()
    {
        float step = rollSpeed * Time.deltaTime;
        transform.position += _rollDir * step;
        _rollDistTravelled += step;

        UpdateRectIndicatorPose();

        // Check body-collision damage along the path.
        if (!_rollHitPlayerThisAttack && CheckRollHit())
        {
            _rollHitPlayerThisAttack = true;
            DamagePlayer(rollDamage, "Roll");

            // Player gets carried in the direction of the roll — they're
            // shoved by the body that just hit them.
            KnockbackPlayer(_rollDir, rollKnockbackForce, rollKnockbackDuration);
        }

        if (_rollDistTravelled >= _effectiveRollDistance)
            EndRoll();
    }

    private void EndRoll()
    {
        if (_animator != null) _animator.SetBool("Roll", false);

        _rollsDoneInPair++;

        if (_rollsDoneInPair < rollsInPair)
        {
            // Roll one of the pair done — quick re-aim gap, indicator stays up.
            _stateUntil = Time.time + T(rollPairGap);
            SetState(BossState.RollPairGap);
            return;
        }

        // Full pair complete — short exhale, then the 4s pattern cooldown.
        if (_rectIndicator != null) _rectIndicator.SetActive(false);
        _attackReadyAt = Time.time + T(rollPairCooldown);
        _stateUntil    = Time.time + T(rollRecoverDuration);
        SetState(BossState.RollRecover);
    }

    private void TickRollRecover()
    {
        if (Time.time < _stateUntil) return;

        // Resume NavMeshAgent control
        if (_agent != null) _agent.enabled = true;
        SetState(BossState.Chase);
    }

    private bool CheckRollHit()
    {
        // Box centred on the boss, oriented along _rollDir, sized roughly to body.
        Vector3 center   = HitOrigin() + _rollDir * (rollWidth * 0.25f);
        Vector3 halfExt  = new Vector3(rollWidth * 0.5f, 1f, rollWidth * 0.5f);
        Quaternion rot   = Quaternion.LookRotation(_rollDir);

        Collider[] hits = Physics.OverlapBox(center, halfExt, rot, playerLayer);
        return hits != null && hits.Length > 0;
    }

    // ── Slam attack ──

    private void BeginSlam()
    {
        FacePlayer();

        // Lock movement during the whole slam sequence
        if (_agent != null) _agent.enabled = false;

        if (_animator != null) _animator.SetTrigger("Jump");

        _stateUntil = Time.time + T(jumpWindupDuration);
        SetState(BossState.JumpWindup);
    }

    private void TickJumpWindup()
    {
        if (Time.time < _stateUntil) return;

        // Lock the landing spot to wherever the player IS RIGHT NOW.
        _slamGroundPos  = transform.position;
        _slamLandingPos = new Vector3(_player.position.x,
                                      transform.position.y,
                                      _player.position.z);
        _slamStartY     = transform.position.y;

        // Show the AoE telegraph at the landing spot
        EnsureCircleIndicator();
        _circleIndicator.transform.position = _slamLandingPos + Vector3.up * 0.02f;
        _circleIndicator.transform.rotation = Quaternion.identity;
        SetCircleColor(windupColor);
        _circleIndicator.SetActive(true);

        if (_animator != null) _animator.SetTrigger("Jump");

 

        _stateUntil = Time.time + T(airDuration);
        SetState(BossState.InAir);
    }

    private void TickInAir()
    {
        // Pop the boss up off the ground in a parabolic arc to feel like a jump.
        float remaining = _stateUntil - Time.time;
        float t         = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.0001f, airDuration));
        // Parabola peaking at t = 0.5
        float arc       = 4f * t * (1f - t);
        Vector3 pos     = Vector3.Lerp(_slamGroundPos, _slamLandingPos, t);
        pos.y           = _slamStartY + arc * jumpHeight;
        transform.position = pos;

        if (_animator != null) _animator.SetTrigger("InAir");

        // Indicator slowly ramps to the committed color as impact nears
        Color c = Color.Lerp(windupColor, committedColor, t);
        SetCircleColor(c);

        if (Time.time >= _stateUntil)
            CommitSlam();
    }

    private void CommitSlam()
    {
        // Snap the boss to the landing spot.
        transform.position = _slamLandingPos;
        SetCircleColor(committedColor);

        if (_animator != null) _animator.SetTrigger("Slam");

        // Resolve damage on whoever is inside the circle right now.
        Vector3 c       = HitOrigin();
        Vector3 bottom  = c - Vector3.up * 1f;
        Vector3 top     = c + Vector3.up * 1f;
        Collider[] hits = Physics.OverlapCapsule(bottom, top, slamRadius, playerLayer);

        if (hits != null && hits.Length > 0)
        {
            DamagePlayer(slamDamage, "Slam");

            // Player gets blown radially outward from the slam impact center.
            Vector3 away = _player.position - _slamLandingPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;
            KnockbackPlayer(away, slamKnockbackForce, slamKnockbackDuration);
        }

        // Slam's LONG recovery = the main punish window; 6s cooldown gates the
        // next pattern attack after it.
        _attackReadyAt = Time.time + T(slamCooldown);
        _stateUntil    = Time.time + T(slamRecoverDuration);
        SetState(BossState.Slamming);
    }

    private void TickSlamming()
    {
        // Indicator stays visible for the first half of recovery so the
        // player can read what just happened, then fades out.
        float remaining = _stateUntil - Time.time;
        float fade      = Mathf.Clamp01(remaining / Mathf.Max(0.0001f, slamRecoverDuration));
        Color c         = committedColor;
        c.a            *= fade;
        SetCircleColor(c);

        

        if (Time.time >= _stateUntil)
            EndSlam();
    }

    private void EndSlam()
    {
        if (_circleIndicator != null) _circleIndicator.SetActive(false);

        _stateUntil = Time.time + 0.1f; // tiny buffer
        SetState(BossState.SlamRecover);
    }

    private void TickSlamRecover()
    {
        if (Time.time < _stateUntil) return;

        if (_agent != null) _agent.enabled = true;
        if (_animator != null) _animator.SetFloat("Running", 0f);

        _animator.ResetTrigger("Jump");

        SetState(BossState.Chase);
    }

    // ── Damage / death ──

    private void DamagePlayer(int amount, string sourceTag, bool staggerHit = true)
    {
        if (_playerStats == null) return;
        if (_playerStats.IsInvulnerable) return;   // hit-reaction i-frames

        // Scale damage by Strength like EnemyCombat does, if a stat block exists.
        int finalDamage = amount;
        if (_stats != null && _stats.enemyStatBlock != null)
            finalDamage += _stats.Strength * _stats.enemyStatBlock.attackStrengthBonus;

        _playerStats.TakeDamage(finalDamage);

        if (staggerHit)
        {
            // Roll / Slam are decal-tier → STAGGER + launch (Impact overhaul).
            // Knockback stays with the boss's own tuned KnockbackPlayer calls.
            _playerMovement?.ApplyStagger();
        }
        else
        {
            // Ground Stomp is the BASIC — hit reaction + small push, no
            // control loss. Melee range is taxed, not denied.
            _playerMovement?.ApplyHitReaction(transform.position, false);
        }

        if (verbose)
            Debug.Log($"[FatRatBoss] {gameObject.name} {sourceTag} hit player for {finalDamage}");
    }

    private void OnDeath()
    {
        SetState(BossState.Dead);
        if (_agent != null) _agent.enabled = false;
        if (_rectIndicator   != null) _rectIndicator.SetActive(false);
        if (_circleIndicator != null) _circleIndicator.SetActive(false);
        if (_animator != null) _animator.SetBool("Death", true);
    }

    // ── Indicators (rectangle for roll, disk for slam) ──

    private Vector3 HitOrigin() =>
        attackOrigin != null ? attackOrigin.position : transform.position;

    private void EnsureRectIndicator()
    {
        if (_rectIndicator != null) return;

        _rectIndicator = new GameObject("Boss_RollIndicator");
        _rectFilter    = _rectIndicator.AddComponent<MeshFilter>();
        _rectRenderer  = _rectIndicator.AddComponent<MeshRenderer>();
        _rectFilter.sharedMesh = BuildRectMesh(rollWidth, rollDistance);

        _rectMat = CreateIndicatorMaterial();
        _rectRenderer.material = _rectMat;
        _rectRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rectRenderer.receiveShadows    = false;
    }

    private void UpdateRectIndicatorPose()
    {
        if (_rectIndicator == null) return;

        // Sit just above the ground at the boss's XZ, rotated to face _rollDir.
        Vector3 pos = new Vector3(transform.position.x, HitOrigin().y + 0.02f, transform.position.z);
        _rectIndicator.transform.position = pos;
        _rectIndicator.transform.rotation = Quaternion.LookRotation(_rollDir.sqrMagnitude > 0.001f ? _rollDir : Vector3.forward);
    }

    private void SetRectColor(Color c)
    {
        if (_rectMat != null) _rectMat.color = c;
    }

    private void EnsureCircleIndicator()
    {
        if (_circleIndicator != null) return;

        _circleIndicator = new GameObject("Boss_SlamIndicator");
        _circleFilter    = _circleIndicator.AddComponent<MeshFilter>();
        _circleRenderer  = _circleIndicator.AddComponent<MeshRenderer>();
        _circleFilter.sharedMesh = BuildDiskMesh(slamRadius, 48);

        _circleMat = CreateIndicatorMaterial();
        _circleRenderer.material = _circleMat;
        _circleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _circleRenderer.receiveShadows    = false;
    }

    private void SetCircleColor(Color c)
    {
        if (_circleMat != null) _circleMat.color = c;
    }

    private static Material CreateIndicatorMaterial()
    {
        Shader s = Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Unlit/Color");
        var m = new Material(s) { renderQueue = 3000 };
        return m;
    }

    // ── Mesh builders (rect + disk) ──

    private static Mesh BuildRectMesh(float width, float length)
    {
        float hw = width * 0.5f;
        var verts = new Vector3[]
        {
            new Vector3(-hw, 0f, 0f),
            new Vector3( hw, 0f, 0f),
            new Vector3(-hw, 0f, length),
            new Vector3( hw, 0f, length),
        };
        var tris = new int[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };
        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildDiskMesh(float radius, int segments)
    {
        var verts = new Vector3[segments + 1];
        var tris  = new int[segments * 6];
        verts[0] = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments + 1;
            int b    = i * 6;
            tris[b + 0] = 0; tris[b + 1] = i + 1; tris[b + 2] = next;
            tris[b + 3] = 0; tris[b + 4] = next;  tris[b + 5] = i + 1;
        }

        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── Gizmos ──

    void OnDrawGizmos()
    {
        if (!showGizmos) return;

        // Aggro
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, aggroRange);

        // Stop range
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, stopRange);

        // Roll preview — long rectangle forward
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.6f);
        Vector3 origin = transform.position;
        Vector3 fwd    = transform.forward;
        Vector3 right  = transform.right * (rollWidth * 0.5f);
        Vector3 a      = origin + right;
        Vector3 b      = origin - right;
        Vector3 c      = a + fwd * rollDistance;
        Vector3 d      = b + fwd * rollDistance;
        Gizmos.DrawLine(a, c);
        Gizmos.DrawLine(b, d);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(a, b);
    }
}
