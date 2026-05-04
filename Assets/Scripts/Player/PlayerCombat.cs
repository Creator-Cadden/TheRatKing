using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    [Header("Basic Attack")]
    public float basicAttackRadius   = 2f;
    public float basicAttackAngle    = 60f;
    public float basicAttackHeight   = 0.5f;

    [Header("Basic Attack Cooldown")]
    [Tooltip("Cooldown at base Speed. Speed points reduce this toward the minimum.")]
    public float basicAttackCooldownBase = 1.0f;
    [Tooltip("Fastest possible cooldown regardless of Speed — prevents spamming.")]
    public float basicAttackCooldownMin  = 0.3f;
    [Tooltip("Seconds shaved off the cooldown per Speed point above base.\n" +
             "At 0.03: +5 Speed = 0.55 - 0.15 = 0.40s cooldown.")]
    public float attackCooldownPerSpeed  = 0.1f;

    [Header("Jump Attack")]
    public float jumpAttackRadius   = 3.5f;
    // Jump attack is a full 360 spin — no angle field needed, hits everything in radius
    public float jumpAttackCooldown = 1.2f;
    public float jumpAttackHeight   = 1.2f;
    public float jumpSpinDuration   = 0.35f;
    public float jumpSpinDegrees    = 360f;

    [Header("Jump Spin Visual")]
    [Tooltip("Optional visual root to spin. Defaults to animator transform.")]
    public Transform jumpSpinVisual;

    [Header("Stagger Force Per Weapon")]
    public int bladeStaggerForce  = 3;
    public int hammerStaggerForce = 8;
    public int bowStaggerForce    = 2;

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

    // Current effective cooldown — recalculated whenever Speed changes
    private float _currentAttackCooldown;

    void Start()
    {
        _controller = GetComponent<CharacterController>();
        _stats      = GetComponent<EntityStats>();

        if (jumpSpinVisual == null && _primaryAnimator != null)
            jumpSpinVisual = _primaryAnimator.transform;

        if (_stats == null)
            Debug.LogError("[PlayerCombat] No EntityStats found on player!");

        // Subscribe to stat changes so attack speed updates live when Speed is leveled
        if (_stats != null)
            _stats.onStatsChanged.AddListener(RecalculateAttackCooldown);

        // Set the initial cooldown based on starting Speed
        RecalculateAttackCooldown();
    }

    void OnDestroy()
    {
        if (_stats != null)
            _stats.onStatsChanged.RemoveListener(RecalculateAttackCooldown);
    }

    void Update()
    {
        if (_controller.isGrounded)
            _hasJumpAttacked = false;
    }

    // ─────────────────────────────────────────
    // Attack Speed — driven by Speed stat
    // ─────────────────────────────────────────

    /// <summary>
    /// Recalculates the effective attack cooldown based on current Speed.
    /// Called at Start() and every time onStatsChanged fires (stat spend, weapon swap, reset).
    ///
    /// Formula:  cooldown = base - (speedBonus * reductionPerPoint)
    /// Clamped at basicAttackCooldownMin so attacks never become instant.
    ///
    /// Default values example:
    ///   Base Speed  6 → 0.55 - (0 * 0.03) = 0.55s
    ///   Speed      10 → 0.55 - (4 * 0.03) = 0.43s
    ///   Speed      14 → 0.55 - (8 * 0.03) = 0.31s  (noticeably snappier)
    ///   Speed      20 → clamped at 0.20s  (hard floor)
    /// </summary>
    private void RecalculateAttackCooldown()
    {
        if (_stats == null)
        {
            _currentAttackCooldown = basicAttackCooldownBase;
            return;
        }

        int baseSpeed   = _stats.playerStatBlock != null ? _stats.playerStatBlock.baseSpeed : 6;
        int speedBonus  = Mathf.Max(0, _stats.Speed - baseSpeed);
        float reduction = speedBonus * attackCooldownPerSpeed;

        _currentAttackCooldown = Mathf.Max(basicAttackCooldownMin,
                                           basicAttackCooldownBase - reduction);

        Debug.Log($"[PlayerCombat] Attack cooldown → {_currentAttackCooldown:F2}s " +
                  $"(Speed {_stats.Speed}, bonus -{reduction:F2}s)");
    }

    // ─────────────────────────────────────────
    // Input
    // ─────────────────────────────────────────

    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;

        bool isGrounded = _controller.isGrounded;

        if (!isGrounded && !_hasJumpAttacked)
            JumpAttack();
        else if (isGrounded && Time.time >= _lastAttackTime + _currentAttackCooldown)
            BasicAttack();
    }

    // ── Weapon Swap ──────────────────────────────────────────

    public void EquipBlade()  => _stats?.EquipWeapon(EntityStats.WeaponType.Blade);
    public void EquipHammer() => _stats?.EquipWeapon(EntityStats.WeaponType.Hammer);
    public void EquipBow()    => _stats?.EquipWeapon(EntityStats.WeaponType.Bow);

    // ─────────────────────────────────────────
    // Attacks
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
        HitScanFull(jumpAttackRadius);
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
            float t      = Mathf.Clamp01(elapsed / jumpSpinDuration);
            float xAngle = jumpSpinDegrees * t;
            jumpSpinVisual.localRotation = startLocalRot * Quaternion.Euler(0f, xAngle, 0f);
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
    // Public accessor for the HUD cooldown indicator (coming later)
    // Returns 0..1 where 1 = ready to attack, 0 = just attacked
    // ─────────────────────────────────────────

    public float AttackCooldownProgress =>
        Mathf.Clamp01((Time.time - _lastAttackTime) / _currentAttackCooldown);

    public float CurrentAttackCooldown => _currentAttackCooldown;

    // ─────────────────────────────────────────
    // Gizmos
    // ─────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!showAttackGizmos || attackOrigin == null) return;

        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        DrawConeGizmo(attackOrigin.position, transform.forward, basicAttackRadius, basicAttackAngle, basicAttackHeight);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
        DrawCylinderGizmo(attackOrigin.position, jumpAttackRadius, jumpAttackHeight);
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

    /// <summary>
    /// Full 360 hit scan — no angle check needed for the vertical spin attack.
    /// Hits every enemy within radius regardless of direction.
    /// </summary>
    private void HitScanFull(float radius)
    {
        Collider[] hits = Physics.OverlapSphere(attackOrigin.position, radius, enemyLayer);

        foreach (Collider hit in hits)
        {
            int damage       = _stats?.CalculateWeaponDamage() ?? 10;
            int staggerForce = GetCurrentStaggerForce();

            Debug.Log($"[PlayerCombat] Jump spin hit: {hit.name} for {damage} damage");

            hit.GetComponent<EntityStats>()?.TakeDamage(damage);
            hit.GetComponent<EnemyAI>()?.TakeKnockback(attackOrigin.position, staggerForce);
        }
    }

    /// <summary>
    /// Draws a full 360 cylinder gizmo for the jump spin attack.
    /// Represents the vertical spin — hits all directions at the given radius.
    /// </summary>
    private void DrawCylinderGizmo(Vector3 origin, float radius, float height)
    {
        int segments  = 32;
        float halfH   = height / 2f;
        Vector3 top   = origin + Vector3.up * halfH;
        Vector3 bot   = origin - Vector3.up * halfH;

        Vector3 prevTop = top + Vector3.right * radius;
        Vector3 prevBot = bot + Vector3.right * radius;

        for (int i = 1; i <= segments; i++)
        {
            float angle    = (float)i / segments * 360f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

            Vector3 curTop = top + offset;
            Vector3 curBot = bot + offset;

            // Top ring
            Gizmos.DrawLine(prevTop, curTop);
            // Bottom ring
            Gizmos.DrawLine(prevBot, curBot);
            // Vertical edge every 8 segments so it reads as a cylinder
            if (i % 8 == 0)
                Gizmos.DrawLine(curBot, curTop);

            prevTop = curTop;
            prevBot = curBot;
        }

        // Four vertical struts at cardinal points for clarity
        foreach (float a in new float[] { 0f, 90f, 180f, 270f })
        {
            float rad     = a * Mathf.Deg2Rad;
            Vector3 edge  = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            Gizmos.DrawLine(origin - Vector3.up * halfH + edge,
                            origin + Vector3.up * halfH + edge);
        }
    }
}