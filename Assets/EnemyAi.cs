using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    [Header("Layer Mask")]
    [Tooltip("Set this to the layer your Player is on")]
    public LayerMask playerLayer;

    [Header("Attack Origin")]
    public Transform attackOrigin;

    [Header("Attack Timeout")]
    [Tooltip("Safety fallback if OnAttackEnd() Animation Event is never fired. " +
             "Set to your longest attack animation length + 0.5s.")]
    public float attackAnimTimeout = 2.5f;

    [Header("Debug")]
    public bool showAttackGizmo  = true;
    public bool showAggroGizmo   = true;
    public bool verboseAttackLog = false;

    // ── Private State ──
    private NavMeshAgent   _agent;
    private Transform      _player;
    private EntityStats    _stats;
    private EntityStats    _playerStats;
    private Animator       _animator;
    private EnemyStatBlock _sb;

    private bool    _isAggroed;
    private bool    _isKnockedBack;
    private bool    _isAttacking;
    private float   _attackStartTime;
    private Vector3 _knockbackVelocity;
    private float   _knockbackTimer;
    private float   _lastAttackTime;

    private Transform HitOrigin => attackOrigin != null ? attackOrigin : transform;

    void Start()
    {
        _stats    = GetComponent<EntityStats>();
        _animator = GetComponentInChildren<Animator>();

        if (_stats == null || _stats.enemyStatBlock == null)
        {
            Debug.LogError($"[EnemyAI] {gameObject.name} is missing EntityStats or EnemyStatBlock.");
            return;
        }

        _sb = _stats.enemyStatBlock;

        _agent                  = GetComponent<NavMeshAgent>();
        _agent.speed            = _sb.moveSpeed;
        _agent.stoppingDistance = _sb.stopRange;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _player      = playerObj.transform;
            _playerStats = playerObj.GetComponent<EntityStats>();
        }
        else
        {
            Debug.LogWarning("[EnemyAI] No GameObject tagged 'Player' found.");
        }

        _stats.onDeath.AddListener(OnDeath);

        Debug.Log($"[EnemyAI] {gameObject.name} initialised — " +
                  $"aggroRange:{_sb.aggroRange} stopRange:{_sb.stopRange} " +
                  $"attackCooldown:{_sb.attackCooldown} attackRadius:{_sb.attackRadius} " +
                  $"playerLayer:{playerLayer.value}");
    }

    void Update()
    {
        if (_player == null || _sb == null) return;
        if (_stats.IsDead) return;

        if (_isKnockedBack)
        {
            HandleKnockback();
            return;
        }

        // ── Attack timeout safety net ────────────────────────────────────
        if (_isAttacking && Time.time >= _attackStartTime + attackAnimTimeout)
        {
            Debug.LogWarning($"[EnemyAI] {gameObject.name} attack timed out after {attackAnimTimeout}s — " +
                             "OnAttackEnd() Animation Event may be missing from the attack clip.");
            _isAttacking = false;
        }

        if (_isAttacking) return;

        float dist = Vector3.Distance(transform.position, _player.position);

        // ── Aggro ────────────────────────────────────────────────────────
        if (dist <= _sb.aggroRange)
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

        // ── Chase or attack ──────────────────────────────────────────────
        // Use a small buffer so the agent doesn't stop exactly on the boundary
        // and then fail the dist > stopRange check due to floating point.
        float attackThreshold = _sb.stopRange + 0.4f;

        if (dist > attackThreshold)
        {
            _agent.SetDestination(_player.position);

            if (verboseAttackLog)
                Debug.Log($"[EnemyAI] {gameObject.name} chasing — " +
                          $"dist:{dist:F2} attackThreshold:{attackThreshold:F2}");
        }
        else
        {
            _agent.ResetPath();
            TryStartAttack(dist);
        }
    }

    // ─────────────────────────────────────────
    // Attack
    // ─────────────────────────────────────────

    private void TryStartAttack(float dist)
    {
        float cooldownRemaining = (_lastAttackTime + _sb.attackCooldown) - Time.time;

        if (verboseAttackLog)
            Debug.Log($"[EnemyAI] {gameObject.name} TryStartAttack — " +
                      $"dist:{dist:F2} stopRange:{_sb.stopRange} " +
                      $"cooldownRemaining:{cooldownRemaining:F2} " +
                      $"_isAttacking:{_isAttacking}");

        float attackThreshold = _sb.stopRange + 0.4f;
        if (dist > attackThreshold)
        {
            if (verboseAttackLog)
                Debug.Log($"[EnemyAI] {gameObject.name} BLOCKED — dist:{dist:F2} > threshold:{attackThreshold:F2}");
            return;
        }

        if (Time.time < _lastAttackTime + _sb.attackCooldown)
        {
            if (verboseAttackLog)
                Debug.Log($"[EnemyAI] {gameObject.name} BLOCKED — cooldown ({cooldownRemaining:F2}s left)");
            return;
        }

        // ── All gates passed — fire the attack ──────────────────────────
        _lastAttackTime  = Time.time;
        _isAttacking     = true;
        _attackStartTime = Time.time;
        _agent.ResetPath();

        // Face the player before swinging
        Vector3 lookDir = (_player.position - transform.position);
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        // Only fire the trigger if the parameter actually exists on this animator.
        // Once you have a real attack clip, name the trigger "Attk" and this
        // will start working automatically — no code change needed.
        bool hasAttackTrigger = false;
        foreach (var param in _animator.parameters)
        {
            if (param.name == "Attk") { hasAttackTrigger = true; break; }
        }

        if (hasAttackTrigger)
        {
            // Animation-driven: OnAttackHitFrame event deals damage, OnAttackEnd clears _isAttacking
            _animator.SetTrigger("Attk");
        }
        else
        {
            // No animation yet — deal damage immediately and clear the lock after the cooldown
            Debug.Log($"[EnemyAI] {gameObject.name} no 'Attk' trigger found — dealing damage directly");
            OnAttackHitFrame();
            _isAttacking = false;
        }

        Debug.Log($"[EnemyAI] {gameObject.name} *** ATTACK FIRED *** " +
                  $"dist:{dist:F2} time:{Time.time:F2}");
    }

    /// <summary>
    /// Animation Event — place on the hit frame of the attack clip.
    /// Function name: OnAttackHitFrame
    /// </summary>
    public void OnAttackHitFrame()
    {
        Debug.Log($"[EnemyAI] {gameObject.name} OnAttackHitFrame — checking for player hit");

        if (_playerStats == null)
        {
            Debug.LogWarning($"[EnemyAI] {gameObject.name} OnAttackHitFrame — _playerStats is null!");
            return;
        }

        bool hit = _sb.attackShape == AttackShape.Sphere
            ? CheckSphereHit()
            : CheckConeHit();

        if (hit)
        {
            int baseDamage    = Random.Range(_sb.attackDamageMin, _sb.attackDamageMax + 1);
            int strengthBonus = _stats.Strength * _sb.attackStrengthBonus;
            int totalDamage   = baseDamage + strengthBonus;

            Debug.Log($"[EnemyAI] {gameObject.name} calling TakeDamage({totalDamage}) on {_playerStats.gameObject.name} — " +
                      $"player HP before: {_playerStats.CurrentHealth}/{_playerStats.MaxHealth} isDead:{_playerStats.IsDead}");

            _playerStats.TakeDamage(totalDamage);

            Debug.Log($"[EnemyAI] {gameObject.name} after TakeDamage — " +
                      $"player HP now: {_playerStats.CurrentHealth}/{_playerStats.MaxHealth}");
        }
        else
        {
            Debug.LogWarning($"[EnemyAI] {gameObject.name} swing MISSED — " +
                      $"playerLayer:{playerLayer.value} (is it set in Inspector?) " +
                      $"hitOrigin:{HitOrigin.position} attackRadius:{_sb.attackRadius} " +
                      $"attackShape:{_sb.attackShape}");
        }
    }

    /// <summary>
    /// Animation Event — place at the END of the attack clip.
    /// Function name: OnAttackEnd
    /// </summary>
    public void OnAttackEnd()
    {
        Debug.Log($"[EnemyAI] {gameObject.name} OnAttackEnd received");
        _isAttacking = false;
    }

    // ─────────────────────────────────────────
    // Hitbox shapes
    // ─────────────────────────────────────────

    private bool CheckSphereHit()
    {
        Collider[] hits = Physics.OverlapSphere(HitOrigin.position, _sb.attackRadius, playerLayer);

        if (verboseAttackLog)
            Debug.Log($"[EnemyAI] CheckSphereHit — radius:{_sb.attackRadius} " +
                      $"layer:{playerLayer.value} hits:{hits.Length}");

        return hits.Length > 0;
    }

    private bool CheckConeHit()
    {
        Collider[] hits = Physics.OverlapSphere(HitOrigin.position, _sb.attackRadius, playerLayer);

        if (verboseAttackLog)
            Debug.Log($"[EnemyAI] CheckConeHit — radius:{_sb.attackRadius} " +
                      $"layer:{playerLayer.value} hits:{hits.Length}");

        foreach (Collider hit in hits)
        {
            Vector3 dir   = (hit.transform.position - HitOrigin.position).normalized;
            float   angle = Vector3.Angle(transform.forward, dir);

            if (angle <= _sb.attackAngle / 2f)
                return true;
        }

        return false;
    }

    // ─────────────────────────────────────────
    // Knockback
    // ─────────────────────────────────────────

    private void HandleKnockback()
    {
        _agent.enabled     = false;
        transform.position += _knockbackVelocity * Time.deltaTime;
        _knockbackVelocity  = Vector3.Lerp(_knockbackVelocity, Vector3.zero, 10f * Time.deltaTime);

        _knockbackTimer -= Time.deltaTime;
        if (_knockbackTimer <= 0f)
        {
            _isKnockedBack = false;
            _agent.enabled = true;
        }
    }

    public void TakeKnockback(Vector3 sourcePosition, int staggerForce = 3)
    {
        if (_sb == null) return;

        int   toughness = _stats?.Toughness ?? 0;

        float baseForce = staggerForce switch
        {
            int f when f >= 8 => _sb.hammerKnockbackForce,
            int f when f <= 2 => _sb.bowKnockbackForce,
            _                 => _sb.bladeKnockbackForce
        };

        float finalForce = baseForce - (toughness * _sb.toughnessReductionPerPoint);

        Vector3 direction = (transform.position - sourcePosition).normalized;
        direction.y       = 0.3f;

        bool staggers = _stats == null || _stats.ShouldStagger(staggerForce);

        if (finalForce > 0f)
        {
            _knockbackVelocity = direction * finalForce;
            _knockbackTimer    = _sb.knockbackDuration;
            _isKnockedBack     = true;
            _isAttacking       = false;

            if (staggers)
            {
                _animator.SetTrigger("Stun");
                Debug.Log($"[EnemyAI] {gameObject.name} staggered — force {finalForce:F1}");
            }
            else
            {
                Debug.Log($"[EnemyAI] {gameObject.name} pushed but not staggered — force {finalForce:F1}");
            }
        }
        else
        {
            Debug.Log($"[EnemyAI] {gameObject.name} fully resisted — toughness {toughness} absorbed all");
        }
    }

    public void TakeKnockback(Vector3 sourcePosition)
        => TakeKnockback(sourcePosition, 3);

    // ─────────────────────────────────────────
    // Death
    // ─────────────────────────────────────────

    private void OnDeath()
    {
        _isAttacking   = false;
        _agent.enabled = false;
        _animator.SetTrigger("Death");
        Debug.Log($"[EnemyAI] {gameObject.name} died.");
    }

    // ─────────────────────────────────────────
    // Gizmos
    // ─────────────────────────────────────────

    void OnDrawGizmos()
    {
        var entityStats = GetComponent<EntityStats>();
        EnemyStatBlock sb = entityStats != null ? entityStats.enemyStatBlock : null;
        if (sb == null) return;

        Vector3 hitOrigin = attackOrigin != null ? attackOrigin.position : transform.position;

        if (showAggroGizmo)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.06f);
            Gizmos.DrawSphere(transform.position, sb.aggroRange);
            Gizmos.color = new Color(1f, 1f, 0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, sb.aggroRange);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, sb.stopRange);
        }

        if (showAttackGizmo)
        {
            if (sb.attackShape == AttackShape.Sphere)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.12f);
                Gizmos.DrawSphere(hitOrigin, sb.attackRadius);
                Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
                Gizmos.DrawWireSphere(hitOrigin, sb.attackRadius);
            }
            else
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.12f);
                DrawConeFan(hitOrigin, transform.forward, sb.attackRadius, sb.attackAngle);
                Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
                DrawConeOutline(hitOrigin, transform.forward, sb.attackRadius, sb.attackAngle);
            }
        }
    }

    private void DrawConeFan(Vector3 origin, Vector3 forward, float radius, float angle)
    {
        int   segments  = 20;
        float halfAngle = angle / 2f;

        for (int i = 0; i <= segments; i++)
        {
            float   a     = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 point = origin + Quaternion.Euler(0, a, 0) * forward * radius;
            Gizmos.DrawLine(origin, point);
        }
    }

    private void DrawConeOutline(Vector3 origin, Vector3 forward, float radius, float angle)
    {
        int   segments  = 20;
        float halfAngle = angle / 2f;

        Vector3 leftEdge  = origin + Quaternion.Euler(0, -halfAngle, 0) * forward * radius;
        Vector3 rightEdge = origin + Quaternion.Euler(0,  halfAngle, 0) * forward * radius;
        Gizmos.DrawLine(origin, leftEdge);
        Gizmos.DrawLine(origin, rightEdge);

        Vector3 prev = leftEdge;
        for (int i = 1; i <= segments; i++)
        {
            float   a    = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 next = origin + Quaternion.Euler(0, a, 0) * forward * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}