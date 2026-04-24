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
    public float jumpSpinDuration   = 0.35f;
    public float jumpSpinDegrees    = 360f;

    [Header("Jump Spin Visual")]
    [Tooltip("Optional visual root to spin. Defaults to animator transform.")]
    public Transform jumpSpinVisual;

    [Header("Stagger Force Per Weapon")]
    public int bladeStaggerForce  = 3;    // low — only staggers low-toughness enemies
    public int hammerStaggerForce = 8;    // high — staggers most enemies
    public int bowStaggerForce    = 2;    // very low

    [Header("Attack Origin")]
    public Transform attackOrigin;

    [Header("Target Layer")]
    public LayerMask enemyLayer;

    [Header("Debug")]
    public bool showAttackGizmos = true;

    // ── Private State ──
    [Header("Animators")]
    [SerializeField] private Animator _primaryAnimator;
    [SerializeField] private Animator _secondaryAnimator;
    private CharacterController _controller;
    private EntityStats         _stats;
    private float               _lastAttackTime;
    private float               _lastJumpAttackTime;
    private bool                _hasJumpAttacked;
    private Coroutine           _jumpSpinRoutine;

    void Start()
    {
        _controller = GetComponent<CharacterController>();
        _stats      = GetComponent<EntityStats>();
        if (jumpSpinVisual == null && _primaryAnimator != null)
            jumpSpinVisual = _primaryAnimator.transform;

        if (_stats == null)
            Debug.LogError("[PlayerCombat] No EntityStats found on player!");
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

    // ── Weapon Swap — hook up to input or UI buttons ──

    public void EquipBlade()  => _stats?.EquipWeapon(EntityStats.WeaponType.Blade);
    public void EquipHammer() => _stats?.EquipWeapon(EntityStats.WeaponType.Hammer);
    public void EquipBow()    => _stats?.EquipWeapon(EntityStats.WeaponType.Bow);

    // ─────────────────────────────────────────
    // Attacks do NOT cost stamina.
    // Stamina is spent on mobility: sprint, roll, jump.
    // This keeps combat always available and lets the player
    // make interesting decisions about stamina during movement.
    // ─────────────────────────────────────────

    private void BasicAttack()
    {
        _lastAttackTime = Time.time;
        _primaryAnimator?.SetTrigger("Attk");
        _secondaryAnimator?.SetTrigger("Attk");
        HitScan(basicAttackRadius, basicAttackAngle);
    }

    private void JumpAttack()
    {
        if (Time.time < _lastJumpAttackTime + jumpAttackCooldown) return;

        _hasJumpAttacked    = true;
        _lastJumpAttackTime = Time.time;
        _primaryAnimator?.SetTrigger("AirAttk");
        _secondaryAnimator?.SetTrigger("AirAttk");
        StartJumpSpin();

        HitScan(jumpAttackRadius, jumpAttackAngle);
    }

    private void StartJumpSpin()
    {
        if (jumpSpinVisual == null) return;
        if (_jumpSpinRoutine != null) StopCoroutine(_jumpSpinRoutine);
        _jumpSpinRoutine = StartCoroutine(JumpSpinRoutine());
    }

    private System.Collections.IEnumerator JumpSpinRoutine()
    {
        Quaternion startLocalRot = jumpSpinVisual.localRotation;
        float elapsed = 0f;

        while (elapsed < jumpSpinDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / jumpSpinDuration);
            float xAngle = jumpSpinDegrees * t;
            jumpSpinVisual.localRotation = startLocalRot * Quaternion.Euler(xAngle, 0f, 0f);
            yield return null;
        }

        jumpSpinVisual.localRotation = startLocalRot;
        _jumpSpinRoutine = null;
    }

    private void HitScan(float radius, float angle)
    {
        Collider[] hits = Physics.OverlapSphere(attackOrigin.position, radius, enemyLayer);

        foreach (Collider hit in hits)
        {
            Vector3 directionToTarget = (hit.transform.position - attackOrigin.position).normalized;
            float angleToTarget       = Vector3.Angle(transform.forward, directionToTarget);

            if (angleToTarget <= angle / 2f)
            {
                int damage       = _stats?.CalculateWeaponDamage() ?? 10;
                int staggerForce = GetCurrentStaggerForce();

                Debug.Log($"[PlayerCombat] Hit: {hit.name} for {damage} damage");

                hit.GetComponent<EntityStats>()?.TakeDamage(damage);
                hit.GetComponent<EnemyAI>()?.TakeKnockback(attackOrigin.position, staggerForce);
            }
        }
    }

    private int GetCurrentStaggerForce()
    {
        if (_stats == null) return bladeStaggerForce;

        return _stats.EquippedWeapon switch
        {
            EntityStats.WeaponType.Blade  => bladeStaggerForce,
            EntityStats.WeaponType.Hammer => hammerStaggerForce,
            EntityStats.WeaponType.Bow    => bowStaggerForce,
            _                             => bladeStaggerForce
        };
    }

    // ─────────────────────────────────────────
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
            float t             = (float)l / layers;
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

        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 bottom = origin + Vector3.up * (-height / 2f) + Quaternion.Euler(0, currentAngle, 0) * forward * radius;
            Vector3 top    = origin + Vector3.up * ( height / 2f) + Quaternion.Euler(0, currentAngle, 0) * forward * radius;
            Gizmos.DrawLine(bottom, top);
        }
    }
}