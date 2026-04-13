using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    [Header("Debug")]
    public bool showAggroGizmo = true;

    // ── Private State ──
    private NavMeshAgent  _agent;
    private Transform     _player;
    private EntityStats   _stats;
    private EntityStats   _playerStats;
    private Animator      _animator;
    private EnemyStatBlock _sb;   // cached reference for clean Update reads

    private bool    _isAggroed;
    private bool    _isKnockedBack;
    private Vector3 _knockbackVelocity;
    private float   _knockbackTimer;
    private float   _lastAttackTime;

    void Start()
    {
        _stats    = GetComponent<EntityStats>();
        _animator = GetComponentInChildren<Animator>();

        if (_stats == null || _stats.enemyStatBlock == null)
        {
            Debug.LogError($"[EnemyAI] {gameObject.name} is missing EntityStats or an EnemyStatBlock!");
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
            Debug.LogWarning("[EnemyAI] No GameObject tagged 'Player' found in scene.");
        }

        _stats.onDeath.AddListener(OnDeath);
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

        float dist = Vector3.Distance(transform.position, _player.position);

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

        if (_isAggroed)
        {
            if (dist > _sb.stopRange)
                _agent.SetDestination(_player.position);
            else
            {
                _agent.ResetPath();
                TryAttackPlayer(dist);
            }
        }
    }

    private void TryAttackPlayer(float dist)
    {
        if (dist > _sb.attackRange) return;
        if (Time.time < _lastAttackTime + _sb.attackCooldown) return;

        _lastAttackTime = Time.time;

        int baseDamage    = Random.Range(_sb.attackDamageMin, _sb.attackDamageMax + 1);
        int strengthBonus = _stats.Strength * _sb.attackStrengthBonus;
        int totalDamage   = baseDamage + strengthBonus;

        _playerStats?.TakeDamage(totalDamage);
        Debug.Log($"[EnemyAI] {gameObject.name} hit player for {totalDamage} " +
                  $"(base {baseDamage} + str {strengthBonus})");
    }

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

    /// <summary>
    /// Called by PlayerCombat on a successful hit.
    /// staggerForce codes match PlayerCombat: blade = 3, hammer = 8, bow = 2
    /// </summary>
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

            if (staggers)
            {
                _animator.SetTrigger("Stun");
                Debug.Log($"[EnemyAI] {gameObject.name} staggered — " +
                          $"force {finalForce:F1} (base {baseForce} - toughness {toughness})");
            }
            else
            {
                Debug.Log($"[EnemyAI] {gameObject.name} pushed but not staggered — " +
                          $"force {finalForce:F1}");
            }
        }
        else
        {
            Debug.Log($"[EnemyAI] {gameObject.name} fully resisted — " +
                      $"toughness {toughness} absorbed all {baseForce} base force");
        }
    }

    // Backward-compatible overload
    public void TakeKnockback(Vector3 sourcePosition)
        => TakeKnockback(sourcePosition, 3);

    private void OnDeath()
    {
        _agent.enabled = false;
        _animator.SetTrigger("Death");
        Debug.Log($"[EnemyAI] {gameObject.name} died.");
    }

    void OnDrawGizmos()
    {
        if (!showAggroGizmo) return;

        float aggro = _sb != null ? _sb.aggroRange : 8f;
        float stop  = _sb != null ? _sb.stopRange  : 1.5f;

        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, aggro);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, aggro);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stop);
    }
}