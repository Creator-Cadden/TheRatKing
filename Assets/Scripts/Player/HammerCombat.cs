using UnityEngine;

/// <summary>
/// Hammer-specific attack logic. Sits on the Player alongside <see cref="PlayerCombat"/>
/// and <see cref="BowController"/>. Active only when the equipped weapon is Hammer.
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class HammerCombat : MonoBehaviour
{
    [Header("Basic Swing")]
    [Tooltip("Forward reach of the swing — typically larger than the blade's so the " +
             "hammer feels appropriately weighty and outranges shorter weapons.")]
    public float swingRadius = 3.5f;

    [Tooltip("Sweep arc in degrees. Default 80 = focused two-handed swing. " +
             "150+ = wide horizontal sweep that hits multiple targets.")]
    [Range(20f, 180f)]
    public float swingAngle = 80f;

    [Tooltip("Vertical height of the swing's hit volume — keeps the swing honest " +
             "against enemies on slight slopes or ledges.")]
    public float swingHeight = 0.7f;

    [Tooltip("Swing cooldown at base Speed, in seconds. About double the blade's " +
             "base — the hammer is heavy. Speed points reduce this toward " +
             "swingCooldownMin.")]
    public float swingCooldown = 2.0f;

    [Tooltip("Fastest possible swing cooldown regardless of Speed. The hammer " +
             "never gets as fast as a blade even at max Speed.")]
    public float swingCooldownMin = 0.8f;

    [Tooltip("Seconds shaved off the cooldown per Speed point above base. " +
             "Smaller than the blade's per-Speed reduction so the hammer scales " +
             "less aggressively with Speed.")]
    public float swingCooldownPerSpeed = 0.08f;

    [Tooltip("How long before the hit lands — visual telegraph time. Bigger windup " +
             "= more committed swing = punishable if the enemy dodges.")]
    public float swingWindup = 0.15f;

    [Header("Jump Slam (mid-air LMB)")]
    [Tooltip("Radius of the circular AoE slam. Hits enemies all around the player " +
             "when the hammer lands.")]
    public float slamRadius = 4.5f;

    [Tooltip("Vertical height of the slam hit volume.")]
    public float slamHeight = 1.2f;

    [Tooltip("Fixed cooldown between slams, in seconds. Speed stat does NOT reduce.")]
    public float slamCooldown = 2.0f;

    [Tooltip("Small windup before the impact frame — gives the player a moment " +
             "to see they're about to slam.")]
    public float slamWindup = 0.1f;

    [Tooltip("Multiplier applied to base hammer damage on slam impact. " +
             "1.5 = slam hits 50% harder than the basic swing.")]
    public float slamDamageMultiplier = 1.5f;

    [Header("References")]
    [Tooltip("World position the swing/slam originates from. Defaults to the " +
             "PlayerCombat attack origin if left null.")]
    public Transform attackOrigin;

    [Tooltip("Layer mask used by hit detection.")]
    public LayerMask enemyLayer;

    [Header("Stagger Force (DEPRECATED)")]
    [Tooltip("UNUSED — replaced by the Impact system (PlayerStatBlock's " +
             "hammerImpactBasic/Special: 3 basic / 4 slam). Kept so prefab " +
             "data isn't lost.")]
    public int staggerForce = 8;

    [Header("Visual Ripple")]
    [Tooltip("Color of the arc ripple shown on the basic swing — orange feels weighty.")]
    public Color swingRippleColor = new Color(1f, 0.55f, 0.15f, 0.6f);

    [Tooltip("Color of the ring ripple shown on the slam attack.")]
    public Color slamRippleColor  = new Color(1f, 0.45f, 0.15f, 0.7f);

    [Tooltip("How long the ripple effect stays visible.")]
    public float rippleLifetime   = 0.35f;

    [Header("Debug")]
    public bool showGizmos = true;
    public bool verbose = false;

    // ── Private state ──

    private EntityStats _stats;
    private float       _lastSwingTime  = -999f;
    private float       _lastSlamTime   = -999f;
    private float       _currentSwingCooldown;   // recalculated by RecalculateCooldown()

    // ── Public read-only — for HUD cooldown bars / debug ──

    public float SwingCooldownProgress =>
        Mathf.Clamp01((Time.time - _lastSwingTime) / Mathf.Max(0.0001f, _currentSwingCooldown));

    public float SlamCooldownProgress =>
        Mathf.Clamp01((Time.time - _lastSlamTime) / Mathf.Max(0.0001f, slamCooldown));

    public bool IsSwingReady => Time.time >= _lastSwingTime + _currentSwingCooldown;
    public bool IsSlamReady  => Time.time >= _lastSlamTime  + slamCooldown;

    public float CurrentSwingCooldown => _currentSwingCooldown;


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
        RecalculateCooldown();
    }

    // ── Cooldown recalc — called by PlayerCombat.RecalculateAttackCooldown which is hooked to EntityStats.onStatsChanged ──

    /// <summary>
    /// Recalculates the swing cooldown based on the player's current Speed.
    /// Hammer scales less aggressively with Speed than the blade (smaller
    /// per-Speed reduction, higher minimum floor) so it stays distinctly
    /// </summary>
    public void RecalculateCooldown()
    {
        if (_stats == null) { _currentSwingCooldown = swingCooldown; return; }

        int   baseSpd    = _stats.playerStatBlock != null ? _stats.playerStatBlock.baseSpeed : 5;
        int   speedBonus = Mathf.Max(0, _stats.Speed - baseSpd);
        float reduction  = speedBonus * swingCooldownPerSpeed;

        _currentSwingCooldown = Mathf.Max(swingCooldownMin, swingCooldown - reduction);

        if (verbose)
            Debug.Log($"[HammerCombat] Cooldown → {_currentSwingCooldown:F2}s " +
                      $"(Speed {_stats.Speed}, base {baseSpd})");
    }

    // ── Public API — called by PlayerCombat.OnAttack when weapon = Hammer ──

    /// <summary>
    /// Player pressed LMB while grounded with the hammer equipped.
    /// Returns true if the swing actually fired (false on cooldown).
    /// </summary>
    public bool TryBasicSwing()
    {
        if (!IsSwingReady) return false;
        _lastSwingTime = Time.time;

        if (swingWindup > 0.0001f)
            Invoke(nameof(ResolveSwing), swingWindup);
        else
            ResolveSwing();

        if (verbose) Debug.Log("[HammerCombat] Basic swing started.");
        return true;
    }

    /// <summary>
    /// Player pressed LMB mid-air with the hammer equipped.
    /// Returns true if the slam actually fired (false on cooldown).
    /// </summary>
    public bool TryJumpSlam()
    {
        if (!IsSlamReady) return false;
        _lastSlamTime = Time.time;

        if (slamWindup > 0.0001f)
            Invoke(nameof(ResolveSlam), slamWindup);
        else
            ResolveSlam();

        if (verbose) Debug.Log("[HammerCombat] Jump slam started.");
        return true;
    }

    // ── Hit resolution ──

    private void ResolveSwing()
    {
        if (attackOrigin == null) return;

        int damage    = _stats?.CalculateWeaponDamage() ?? 10;
        int impact    = _stats?.GetWeaponImpact(special: false) ?? 3;
        int hitsLanded = 0;

        Collider[] hits = Physics.OverlapSphere(attackOrigin.position, swingRadius, enemyLayer);
        foreach (Collider hit in hits)
        {
            Vector3 toTarget = (hit.transform.position - attackOrigin.position).normalized;
            float   angle    = Vector3.Angle(transform.forward, toTarget);
            if (angle > swingAngle * 0.5f) continue;

            hit.GetComponent<EntityStats>()?.TakeDamage(damage);
            hit.GetComponent<EnemyAI>()?.ApplyHitReaction(attackOrigin.position, impact);
            hitsLanded++;
        }

        // Visual: arc ripple along the swing cone
        AttackRipple.SpawnArc(attackOrigin.position, transform.forward,
                              swingRadius, swingAngle,
                              swingRippleColor, rippleLifetime);

        if (verbose) Debug.Log($"[HammerCombat] Swing landed on {hitsLanded} target(s), {damage} dmg each.");
    }

    private void ResolveSlam()
    {
        if (attackOrigin == null) return;

        int baseDmg = _stats?.CalculateWeaponDamage() ?? 10;
        int damage  = Mathf.RoundToInt(baseDmg * slamDamageMultiplier);
        int impact  = _stats?.GetWeaponImpact(special: true) ?? 4;   // jump slam = special tier
        int hitsLanded = 0;

        // Use OverlapSphere — full 360° around the player. No angle filter.
        Collider[] hits = Physics.OverlapSphere(attackOrigin.position, slamRadius, enemyLayer);
        foreach (Collider hit in hits)
        {
            hit.GetComponent<EntityStats>()?.TakeDamage(damage);
            hit.GetComponent<EnemyAI>()?.ApplyHitReaction(attackOrigin.position, impact);
            hitsLanded++;
        }

        // Visual: 360 ring ripple at the slam radius
        AttackRipple.SpawnRing(attackOrigin.position, slamRadius,
                               slamRippleColor, rippleLifetime);

        if (verbose) Debug.Log($"[HammerCombat] Slam landed on {hitsLanded} target(s), {damage} dmg each.");
    }

    // ── Gizmos ──

    void OnDrawGizmos()
    {
        if (!showGizmos || attackOrigin == null) return;

        // Basic swing — purple cone
        Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.35f);
        DrawConeGizmo(attackOrigin.position, transform.forward, swingRadius,
                      swingAngle, swingHeight);

        // Slam — magenta cylinder
        Gizmos.color = new Color(1f, 0.2f, 0.6f, 0.25f);
        DrawCylinderGizmo(attackOrigin.position, slamRadius, slamHeight);
    }

    private static void DrawConeGizmo(Vector3 origin, Vector3 forward, float radius,
                                      float angle, float height)
    {
        int segments    = 16;
        int layers      = 3;
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

    private static void DrawCylinderGizmo(Vector3 origin, float radius, float height)
    {
        int segments  = 24;
        float halfH   = height * 0.5f;
        Vector3 top   = origin + Vector3.up * halfH;
        Vector3 bot   = origin - Vector3.up * halfH;
        Vector3 prevTop = top + Vector3.right * radius;
        Vector3 prevBot = bot + Vector3.right * radius;

        for (int i = 1; i <= segments; i++)
        {
            float a = (float)i / segments * 360f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Vector3 curTop = top + offset;
            Vector3 curBot = bot + offset;
            Gizmos.DrawLine(prevTop, curTop);
            Gizmos.DrawLine(prevBot, curBot);
            if (i % 6 == 0) Gizmos.DrawLine(curBot, curTop);
            prevTop = curTop;
            prevBot = curBot;
        }
    }
}
