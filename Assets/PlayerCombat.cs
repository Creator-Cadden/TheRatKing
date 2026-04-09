using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    [Header("Basic Attack")]
    public float basicAttackRadius   = 2f;
    public float basicAttackAngle    = 60f;
    public float basicAttackCooldown = 0.4f;
    public float basicAttackHeight   = 0.5f;

    [Header("Jump Attack")]
    public float jumpAttackRadius   = 3.5f;
    public float jumpAttackAngle    = 90f;
    public float jumpAttackCooldown = 1.2f;
    public float jumpAttackHeight   = 1.2f;

    [Header("Attack Origin")]
    public Transform attackOrigin;

    [Header("Target Layer")]
    public LayerMask enemyLayer;

    [Header("Debug")]
    public bool showAttackGizmos = true;

    // ── Private State ──
    private CharacterController _controller;
    private Animator _animator;
    private float _lastAttackTime;
    private float _lastJumpAttackTime;
    private bool _hasJumpAttacked;

    void Start()
    {
        _controller = GetComponent<CharacterController>();
        _animator   = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (_controller.isGrounded)
            _hasJumpAttacked = false;
    }

    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;

        bool isGrounded = _controller.isGrounded;

        if (!isGrounded && !_hasJumpAttacked)
            JumpAttack();
        else if (isGrounded && Time.time >= _lastAttackTime + basicAttackCooldown)
            BasicAttack();
    }

    private void BasicAttack()
    {
        _lastAttackTime = Time.time;
        Debug.Log("[Combat] Basic Attack fired");

        // TODO: _animator.SetTrigger("BasicAttack");

        HitScan(basicAttackRadius, basicAttackAngle);
    }

    private void JumpAttack()
    {
        if (Time.time < _lastJumpAttackTime + jumpAttackCooldown) return;

        _hasJumpAttacked    = true;
        _lastJumpAttackTime = Time.time;
        Debug.Log("[Combat] Jump Attack fired");

        // TODO: _animator.SetTrigger("JumpAttack");

        HitScan(jumpAttackRadius, jumpAttackAngle);
    }

    private void HitScan(float radius, float angle)
    {
        Collider[] hits = Physics.OverlapSphere(attackOrigin.position, radius, enemyLayer);

        foreach (Collider hit in hits)
        {
            Vector3 directionToTarget = (hit.transform.position - attackOrigin.position).normalized;
            float angleToTarget = Vector3.Angle(transform.forward, directionToTarget);

            if (angleToTarget <= angle / 2f)
            {
                Debug.Log($"[Combat] Hit: {hit.name}");
                hit.GetComponent<EnemyAI>()?.TakeKnockback(attackOrigin.position);
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showAttackGizmos || attackOrigin == null) return;

        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        DrawConeGizmo(attackOrigin.position, transform.forward, basicAttackRadius, basicAttackAngle, basicAttackHeight);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
        DrawConeGizmo(attackOrigin.position, transform.forward, jumpAttackRadius, jumpAttackAngle, jumpAttackHeight);
    }

    private void DrawConeGizmo(Vector3 origin, Vector3 forward, float radius, float angle, float height)
    {
        int segments    = 20;
        int layers      = 5;
        float halfAngle = angle / 2f;

        for (int l = 0; l <= layers; l++)
        {
            float t           = (float)l / layers;
            Vector3 layerOrigin = origin + Vector3.up * (t * height - height / 2f);
            Vector3 prevPoint   = layerOrigin + Quaternion.Euler(0, -halfAngle, 0) * forward * radius;

            for (int i = 0; i <= segments; i++)
            {
                float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
                Vector3 nextPoint  = layerOrigin + Quaternion.Euler(0, currentAngle, 0) * forward * radius;

                Gizmos.DrawLine(layerOrigin, nextPoint);
                Gizmos.DrawLine(prevPoint, nextPoint);
                prevPoint = nextPoint;
            }
        }

        // Vertical lines connecting top and bottom
        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 bottom = origin + Vector3.up * (-height / 2f) + Quaternion.Euler(0, currentAngle, 0) * forward * radius;
            Vector3 top    = origin + Vector3.up * ( height / 2f) + Quaternion.Euler(0, currentAngle, 0) * forward * radius;
            Gizmos.DrawLine(bottom, top);
        }
    }
}