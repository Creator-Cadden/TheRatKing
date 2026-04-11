using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    [Header("Detection")]
    public float aggroRange     = 8f;
    public float stopRange      = 1.5f;

    [Header("Movement")]
    public float moveSpeed      = 3.5f;

    [Header("Knockback")]
    public float knockbackForce    = 5f;
    public float knockbackDuration = 0.2f;

    [Header("Debug")]
    public bool showAggroGizmo = true;

    // Animation
    private Animator animator;

    // ── Private State ──
    private NavMeshAgent _agent;
    private Transform    _player;
    private bool         _isAggroed;
    private bool         _isKnockedBack;
    private Vector3      _knockbackVelocity;
    private float        _knockbackTimer;

    void Start()
    {
        _agent        = GetComponent<NavMeshAgent>();
        _agent.speed  = moveSpeed;
        _agent.stoppingDistance = stopRange;

        // Find the player by tag — make sure Player object has tag "Player"
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
            _player = playerObj.transform;
        else
            Debug.LogWarning("[EnemyAI] No Player tag found in scene");

        animator = GetComponentInChildren<Animator>();
        Debug.Log("Animator found: " + (animator != null ? animator.gameObject.name : "NULL"));
    }

    void Update()
    {
        if (_player == null) return;

        if (_isKnockedBack)
        {
            HandleKnockback();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, _player.position);

        if (distanceToPlayer <= aggroRange)
        {
            _isAggroed = true;
            Debug.Log("[EnemyAI] Aggroed on player");

            animator.SetFloat("Running", 1);
        }
        else
        {
            _isAggroed = false;
            _agent.ResetPath();
            Debug.Log("[EnemyAI] Lost aggro, idling");

            animator.SetFloat("Running", 0);
        }

        if (_isAggroed)
        {
            if (distanceToPlayer > stopRange)
                _agent.SetDestination(_player.position);
            else
                _agent.ResetPath();
        }
    }

    private void HandleKnockback()
    {
        // Disable navmesh during knockback so it doesn't fight the force
        _agent.enabled = false;

        transform.position += _knockbackVelocity * Time.deltaTime;
        _knockbackVelocity  = Vector3.Lerp(_knockbackVelocity, Vector3.zero, 10f * Time.deltaTime);

        _knockbackTimer -= Time.deltaTime;
        if (_knockbackTimer <= 0f)
        {
            _isKnockedBack = false;
            _agent.enabled = true;
            Debug.Log("[EnemyAI] Knockback ended");
        }
    }

    // Called by the combat script when this enemy is hit
    public void TakeKnockback(Vector3 sourcePosition)
    {
        Vector3 direction  = (transform.position - sourcePosition).normalized;
        direction.y        = 0.3f; // slight upward pop

        _knockbackVelocity = direction * knockbackForce;
        _knockbackTimer    = knockbackDuration;
        _isKnockedBack     = true;

        Debug.Log($"[EnemyAI] Knockback received from {sourcePosition}");

        animator.SetTrigger("Stun");
    }

    void OnDrawGizmos()
    {
        if (!showAggroGizmo) return;

        // Aggro range — yellow
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, aggroRange);

        // Wireframe outline
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, aggroRange);

        // Stop range — red
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stopRange);
    }
}