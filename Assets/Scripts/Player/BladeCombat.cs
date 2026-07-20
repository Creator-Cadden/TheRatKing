using System.Collections;
using UnityEngine;

/// <summary>
/// Blade-specific attack logic. Sits on the Player alongside <see cref="PlayerCombat"/>,
/// <see cref="HammerCombat"/>, and <see cref="BowController"/>. Active when the
/// equipped weapon is Blade.
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class BladeCombat : MonoBehaviour
{
    [Header("Basic Swing")]
    [Tooltip("Forward reach of the swing.")]
    public float swingRadius = 2f;

    [Tooltip("Sweep arc in degrees. 60 = focused two-handed blade strike.")]
    [Range(20f, 180f)]
    public float swingAngle = 60f;

    [Tooltip("Vertical hit volume height.")]
    public float swingHeight = 0.5f;

    [Header("Basic Cooldown")]
    [Tooltip("Cooldown at base Speed. Speed points reduce this toward the minimum.")]
    public float cooldownBase = 1.0f;

    [Tooltip("Fastest possible cooldown regardless of Speed.")]
    public float cooldownMin  = 0.3f;

    [Tooltip("Seconds shaved off the cooldown per Speed point above base.")]
    public float cooldownPerSpeed = 0.1f;

    [Header("Jump Attack (mid-air LMB)")]
    [Tooltip("Radius of the 360° hit volume around the player.")]
    public float jumpAttackRadius   = 3.5f;

    [Tooltip("Vertical hit volume height for the jump attack.")]
    public float jumpAttackHeight   = 1.2f;

    [Tooltip("Cooldown between jump attacks.")]
    public float jumpAttackCooldown = 1.2f;

    [Tooltip("Duration of the visual spin (seconds).")]
    public float jumpSpinDuration   = 0.35f;

    [Tooltip("How many degrees of rotation the spin covers. 360 = one full spin.")]
    public float jumpSpinDegrees    = 360f;

    [Tooltip("Visual transform to rotate during the jump spin. " +
             "Defaults to the rat body's transform.")]
    public Transform jumpSpinVisual;

    [Header("References")]
    [Tooltip("World position the swing originates from. Defaults to PlayerCombat's " +
             "attack origin if left null.")]
    public Transform attackOrigin;

    [Tooltip("Layer mask used by hit detection.")]
    public LayerMask enemyLayer;

    [Tooltip("UNUSED — replaced by the Impact system (PlayerStatBlock's " +
             "bladeImpactBasic/Special). Kept so prefab data isn't lost.")]
    public int staggerForce = 0;

    [Header("Visual Ripple")]
    [Tooltip("Color of the arc ripple shown on the basic swing.")]
    public Color swingRippleColor = new Color(1f, 1f, 1f, 0.55f);

    [Tooltip("Color of the ring ripple shown on the jump spin attack.")]
    public Color jumpRippleColor  = new Color(1f, 1f, 1f, 0.55f);

    [Tooltip("How long the ripple effect stays visible.")]
    public float rippleLifetime   = 0.3f;

    [Header("Debug")]
    public bool showGizmos = true;
    public bool verbose    = false;

    // ── Private state ──

    private EntityStats _stats;
    private float       _currentCooldown;
    private float       _lastSwingTime    = -999f;
    private float       _lastJumpTime     = -999f;
    private Coroutine   _jumpSpinRoutine;

    // ── Public read-only — for HUD cooldown bars / debug ──

    public float SwingCooldownProgress =>
        Mathf.Clamp01((Time.time - _lastSwingTime) / Mathf.Max(0.0001f, _currentCooldown));

    public float JumpCooldownProgress =>
        Mathf.Clamp01((Time.time - _lastJumpTime) / Mathf.Max(0.0001f, jumpAttackCooldown));

    public float CurrentCooldown => _currentCooldown;
    public float JumpCooldown    => jumpAttackCooldown;

    public bool IsSwingReady => Time.time >= _lastSwingTime + _currentCooldown;
    public bool IsJumpReady  => Time.time >= _lastJumpTime  + jumpAttackCooldown;


    void Awake()
    {
        _stats = GetComponent<EntityStats>();

        // Auto-pull attackOrigin and enemyLayer from PlayerCombat so the user
        // doesn't have to set them three times across blade/hammer/bow.
        var pc = GetComponent<PlayerCombat>();
        if (pc != null)
        {
            if (attackOrigin == null)       attackOrigin = pc.attackOrigin;
            if (enemyLayer.value == 0)      enemyLayer   = pc.enemyLayer;
        }
    }

    void Start()
    {
        if (jumpSpinVisual == null)
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim != null) jumpSpinVisual = anim.transform;
        }
        RecalculateCooldown();
    }

    // ── Cooldown recalc — call when Speed changes or weapon swap ──

    /// <summary>
    /// Recalculates the swing cooldown based on the player's current Speed.
    /// Called via PlayerCombat.RecalculateAttackCooldown() which is hooked to
    /// EntityStats.onStatsChanged.
    /// </summary>
    public void RecalculateCooldown()
    {
        if (_stats == null) { _currentCooldown = cooldownBase; return; }

        int baseSpd    = _stats.playerStatBlock != null ? _stats.playerStatBlock.baseSpeed : 5;
        int speedBonus = Mathf.Max(0, _stats.Speed - baseSpd);
        float reduction = speedBonus * cooldownPerSpeed;

        _currentCooldown = Mathf.Max(cooldownMin, cooldownBase - reduction);

        if (verbose)
            Debug.Log($"[BladeCombat] Cooldown → {_currentCooldown:F2}s " +
                      $"(Speed {_stats.Speed}, base {baseSpd})");
    }

    // ── Public API — called by PlayerCombat.OnAttack when weapon = Blade ──

    /// <summary>
    /// Returns true if the basic swing fired (cooldown was ready).
    /// </summary>
    public bool TryBasicAttack()
    {
        if (!IsSwingReady) return false;
        _lastSwingTime = Time.time;
        HitScan(swingRadius, swingAngle);

        // Visual: arc ripple along the swing cone
        if (attackOrigin != null)
            AttackRipple.SpawnArc(attackOrigin.position, transform.forward,
                                  swingRadius, swingAngle,
                                  swingRippleColor, rippleLifetime);

        if (verbose) Debug.Log("[BladeCombat] Basic swing fired.");
        return true;
    }

    /// <summary>
    /// Returns true if the jump attack fired (cooldown was ready).
    /// </summary>
    public bool TryJumpAttack()
    {
        if (!IsJumpReady) return false;
        _lastJumpTime = Time.time;

        StartJumpSpin();
        HitScan(jumpAttackRadius, 360f);   // 360 = no angle filter — hits all around

        // Visual: 360 ring ripple at the spin radius
        if (attackOrigin != null)
            AttackRipple.SpawnRing(attackOrigin.position, jumpAttackRadius,
                                   jumpRippleColor, rippleLifetime);

        if (verbose) Debug.Log("[BladeCombat] Jump attack fired.");
        return true;
    }

    // ── Hit resolution ──

    private void HitScan(float radius, float angle)
    {
        if (attackOrigin == null) return;

        int damage = _stats?.CalculateWeaponDamage() ?? 10;
        // 360° swing = the jump attack → special Impact tier (+1 over basic).
        int impact = _stats?.GetWeaponImpact(special: angle >= 360f) ?? 1;

        Collider[] hits = Physics.OverlapSphere(attackOrigin.position, radius, enemyLayer);
        foreach (Collider hit in hits)
        {
            // Angle filter for cone swings. A 360 angle (jump attack) means
            // every direction is in the cone — no filter.
            if (angle < 360f)
            {
                Vector3 toTarget = (hit.transform.position - attackOrigin.position).normalized;
                float   ang      = Vector3.Angle(transform.forward, toTarget);
                if (ang > angle * 0.5f) continue;
            }

            hit.GetComponent<EntityStats>()?.TakeDamage(damage);
            hit.GetComponent<EnemyAI>()?.ApplyHitReaction(attackOrigin.position, impact);
        }
    }

    // ── Jump spin visual ──

    private void StartJumpSpin()
    {
        if (jumpSpinVisual == null) return;
        if (_jumpSpinRoutine != null) StopCoroutine(_jumpSpinRoutine);
        _jumpSpinRoutine = StartCoroutine(JumpSpinRoutine());
    }

    private IEnumerator JumpSpinRoutine()
    {
        Quaternion startLocalRot = jumpSpinVisual.localRotation;
        float elapsed = 0f;

        // Phase 1: spin
        while (elapsed < jumpSpinDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / jumpSpinDuration);
            float angle = jumpSpinDegrees * t;

            jumpSpinVisual.localRotation = startLocalRot
                * Quaternion.Euler(0f, 0f, -90f)
                * Quaternion.Euler(0f, angle, 0f);

            yield return null;
        }

        // Phase 2: ease back to upright
        float recoverDuration = 0.1f;
        float recoverElapsed  = 0f;
        Quaternion spinEndRot = jumpSpinVisual.localRotation;
        while (recoverElapsed < recoverDuration)
        {
            recoverElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, recoverElapsed / recoverDuration);
            jumpSpinVisual.localRotation = Quaternion.Slerp(spinEndRot, startLocalRot, t);
            yield return null;
        }

        jumpSpinVisual.localRotation = startLocalRot;
        _jumpSpinRoutine = null;
    }

    // ── Gizmos ──

    void OnDrawGizmos()
    {
        if (!showGizmos || attackOrigin == null) return;

        // Basic swing — red cone facing forward
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        DrawConeGizmo(attackOrigin.position, transform.forward, swingRadius,
                      swingAngle, swingHeight);

        // Jump spin — orange vertical wheel in the player's forward-up plane.
        // Visualizes the forward-flip spin attack: circle goes through front,
        // top, back, and bottom of the player rather than orbiting them
        // horizontally.
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
        DrawVerticalWheelGizmo(attackOrigin.position,
                               transform.right,
                               transform.forward,
                               transform.up,
                               jumpAttackRadius,
                               jumpAttackHeight);
    }

    private static void DrawConeGizmo(Vector3 origin, Vector3 forward, float radius,
                                      float angle, float height)
    {
        int segments    = 20;
        int layers      = 5;
        float halfAngle = angle * 0.5f;

        for (int l = 0; l <= layers; l++)
        {
            float t             = (float)l / layers;
            Vector3 layerOrigin = origin + Vector3.up * (t * height - height * 0.5f);
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
    }

    /// <summary>
    /// Draws a wheel-shaped gizmo whose axle is along <paramref name="axisDir"/>.
    /// The wheel itself lies in the plane spanned by <paramref name="forward"/>
    /// and <paramref name="up"/>. For the blade jump spin, this is the
    /// </summary>
    private static void DrawVerticalWheelGizmo(Vector3 origin, Vector3 axisDir,
                                               Vector3 forward, Vector3 up,
                                               float radius, float thickness)
    {
        int segments = 32;
        float halfT  = thickness * 0.5f;

        Vector3 sideA = origin + axisDir * halfT;
        Vector3 sideB = origin - axisDir * halfT;

        Vector3 prevA = sideA + forward * radius;
        Vector3 prevB = sideB + forward * radius;

        for (int i = 1; i <= segments; i++)
        {
            float a       = (float)i / segments * 360f * Mathf.Deg2Rad;
            Vector3 off   = forward * Mathf.Cos(a) * radius + up * Mathf.Sin(a) * radius;
            Vector3 curA  = sideA + off;
            Vector3 curB  = sideB + off;

            Gizmos.DrawLine(prevA, curA);     // outer ring (A side)
            Gizmos.DrawLine(prevB, curB);     // outer ring (B side)

            // Cross-strut every 8 segments to show the wheel's thickness.
            if (i % 8 == 0) Gizmos.DrawLine(curA, curB);

            prevA = curA;
            prevB = curB;
        }
    }
}
