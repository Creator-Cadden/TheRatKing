using UnityEngine;
using UnityEngine.AI;

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
    private EnemyCombat _combat;

    private bool  _isAggroed;
    private bool  _isKnockedBack;
    private bool  _isPostAttackPause;
    private float _postAttackPauseUntil;
    private Vector3 _knockbackVelocity;
    private float _knockbackTimer;

    // Tracks whether we were locked last frame so we can detect the
    // exact frame the lock releases and start the post-attack pause.
    private bool _wasRotationLocked;

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

        _combat = GetComponent<EnemyCombat>();
        if (_combat == null) _combat = gameObject.AddComponent<EnemyCombat>();

        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = _sb.moveSpeed;
        _agent.stoppingDistance = _sb.stopRange;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _player = playerObj.transform;
            _playerStats = playerObj.GetComponent<EntityStats>();
        }

        _stats.onDeath.AddListener(OnDeath);
        _combat.ConfigureRuntime(_player, _playerStats, attackOrigin, playerLayer, verboseAttackLog);
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

        float walkThreshold   = _sb.stopRange;
        float attackThreshold = _sb.AttackReach + 0.35f;

        if (dist > walkThreshold)
        {
            _agent.SetDestination(_player.position);
            _combat.CancelWindup();
        }
        else
        {
            _agent.ResetPath();

            if (dist <= attackThreshold)
                _combat.TryStartAttack(dist);
        }
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

    public void TakeKnockback(Vector3 sourcePosition, int staggerForce = 3)
    {
        if (_sb == null) return;

        int toughness = _stats?.Toughness ?? 0;

        float baseForce = staggerForce switch
        {
            int f when f >= 8 => _sb.hammerKnockbackForce,
            int f when f <= 2 => _sb.bowKnockbackForce,
            _ => _sb.bladeKnockbackForce
        };

        float finalForce = baseForce - (toughness * _sb.toughnessReductionPerPoint);

        Vector3 direction = (transform.position - sourcePosition).normalized;
        direction.y = 0.3f;

        bool staggers = _stats == null || _stats.ShouldStagger(staggerForce);

        if (finalForce > 0f)
        {
            _knockbackVelocity = direction * finalForce;
            _knockbackTimer = _sb.knockbackDuration;
            _isKnockedBack = true;

            _combat.CancelAttackState();

            if (staggers)
                _animator.SetTrigger("Stun");
        }
    }

    public void TakeKnockback(Vector3 sourcePosition)
        => TakeKnockback(sourcePosition, 3);

    private void OnDeath()
    {
        _combat.CancelAttackState();
        _agent.enabled = false;
        _animator.SetTrigger("Death");
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

        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);

        DrawConePrismGizmo(
            hitOrigin,
            transform.forward,
            sb.attackRadius,
            sb.attackAngle,
            sb.attackHeight
        );
    }

    private void DrawConePrismGizmo(Vector3 origin, Vector3 forward, float radius, float angleDeg, float height)
    {
        int segments = 24;
        float halfH = height * 0.5f;
        float halfAngle = angleDeg * 0.5f;
        Vector3 up = Vector3.up;

        Vector3[] topArc    = new Vector3[segments + 1];
        Vector3[] bottomArc = new Vector3[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float t   = (float)i / segments;
            float a   = Mathf.Lerp(-halfAngle, halfAngle, t);
            Quaternion rot = Quaternion.Euler(0, a, 0);
            Vector3 dir    = rot * forward;
            topArc[i]    = origin + dir * radius + up * halfH;
            bottomArc[i] = origin + dir * radius - up * halfH;
        }

        for (int i = 0; i < segments; i++)
        {
            Gizmos.DrawLine(topArc[i],    topArc[i + 1]);
            Gizmos.DrawLine(bottomArc[i], bottomArc[i + 1]);
        }

        for (int i = 0; i <= segments; i++)
            Gizmos.DrawLine(topArc[i], bottomArc[i]);

        Vector3 topOrigin    = origin + up * halfH;
        Vector3 bottomOrigin = origin - up * halfH;

        Gizmos.DrawLine(topOrigin,    topArc[0]);
        Gizmos.DrawLine(bottomOrigin, bottomArc[0]);
        Gizmos.DrawLine(topOrigin,    topArc[segments]);
        Gizmos.DrawLine(bottomOrigin, bottomArc[segments]);
        Gizmos.DrawLine(topOrigin,    bottomOrigin);
    }
}