using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// Bow-specific attack logic. Sits on the Player GameObject alongside
/// <see cref="PlayerCombat"/>. Active only when the equipped weapon is Bow.
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class BowController : MonoBehaviour
{
    [Header("Projectile")]
    [Tooltip("Arrow prefab to spawn. Must have the Arrow component.")]
    public Arrow arrowPrefab;

    [Tooltip("World-space spawn point for arrows. Usually a child empty " +
             "positioned at the bow tip or the rat's head.")]
    public Transform arrowSpawnPoint;

    [Tooltip("Arrows travel at this many units/sec.\n" +
             "30 = floaty, drops fast. 50 = solid baseline. 80+ = sniper-feel.")]
    public float arrowSpeed = 50f;

    [Tooltip("Arrows self-destroy after this many seconds (failsafe). At default " +
             "speed, 4s covers ~200 units of range — well past visible draw distance.")]
    public float arrowLifetime = 4f;

    [Tooltip("Layer mask used by the arrow to detect enemies.")]
    public LayerMask enemyLayer;

    [Tooltip("Downward acceleration applied to arrows in flight, units/sec².\n" +
             "0 = perfectly straight. 3 = subtle arc, long range stays usable. " +
             "6 = clear arc, mid-range only. 9.81 = real-world drop.")]
    public float arrowGravity = 3f;

    [Header("Aim Direction (for aim-mode shots)")]
    [Tooltip("OPTIONAL — drag a transform whose forward direction is the aim " +
             "direction (e.g. the cameraPitch transform from PlayerMovement). " +
             "If left null, the script automatically uses Camera.main.transform.forward, " +
             "which means arrows fly exactly where the camera is looking. " +
             "Most users want auto (null) — only set this manually if you have a " +
             "custom aim rig.")]
    public Transform aimDirectionSource;

    [Tooltip("If true and aimDirectionSource is null, fall back to Camera.main's " +
             "forward direction in aim mode. Recommended ON — gives proper " +
             "shoot-where-camera-looks behavior automatically.")]
    public bool useMainCameraIfUnset = true;

    [Header("Charge (Aimed Shot)")]
    [Tooltip("Time in seconds to reach max charge from press to release.")]
    public float maxChargeTime = 1.4f;

    [Tooltip("Damage multiplier at zero charge (basically a tap-fire). " +
             "Damage scales linearly from this to maxChargeMultiplier.")]
    public float minChargeMultiplier = 1.0f;

    [Tooltip("Damage multiplier at full charge. 3x is the typical 'bow super shot' value.")]
    public float maxChargeMultiplier = 3.0f;

    [Tooltip("Speed multiplier at full charge. 2x = arrow flies twice as fast at full hold, " +
             "so it covers about twice the distance before gravity drops it. " +
             "Set to 1 to disable distance scaling and only scale damage.")]
    public float maxChargedSpeedMultiplier = 2.0f;

    [Tooltip("Optional UI hook — read this 0..1 value from your HUD to draw a charge bar.")]
    public float CurrentChargeFraction { get; private set; }

    [Header("Free-Look Auto-Target")]
    [Tooltip("Half-angle (degrees) of the cone in front of the rat that's " +
             "checked for auto-aim. 0 = no assist.")]
    [Range(0f, 45f)]
    public float autoTargetHalfAngle = 18f;

    [Tooltip("How far to scan for enemies for auto-target.")]
    public float autoTargetRange = 14f;

    [Tooltip("Strength of the nudge: 0 = no nudge, 1 = arrow flies directly " +
             "at the chosen target. 0.4-0.6 feels like aim assist without being aimbot.")]
    [Range(0f, 1f)]
    public float autoTargetStrength = 0.5f;

    [Header("Jump-Attack Triple Shot")]
    [Tooltip("How many shots the jump-attack fires (3 by default).")]
    public int  tripleShotCount    = 3;

    [Tooltip("Delay between each shot of the triple. Smaller = punchier burst.")]
    public float tripleShotInterval = 0.08f;

    [Tooltip("Small horizontal spread (degrees) applied randomly to each of " +
             "the three shots so they don't all fly the same line.")]
    public float tripleShotSpread  = 6f;

    [Tooltip("Extra downward tilt applied to each jump-shot arrow, ON TOP of " +
             "the rat's animation pose. Use this if the animation alone doesn't " +
             "angle the arrows steeply enough toward the ground.\n" +
             "0  = follows animation only.\n" +
             "30 = arrows pitch 30 degrees further down — useful with a flatter animation.\n" +
             "60 = arrows fire nearly straight down.")]
    [Range(0f, 80f)]
    public float jumpShotPitchDown = 30f;

    [Header("Animator Parameters")]
    [Tooltip("Name of the bool parameter on the bow's Animator that is set TRUE " +
             "while the player holds a charge (aim mode, LMB held). " +
             "Must exactly match the parameter name in the bow Animator Controller.\n" +
             "This mirrors how PlayerCombat fires 'Attk' / 'BowAttk' triggers — " +
             "BowController calls SetBool on the bow's own Animator via " +
             "WeaponModelSwapper.ActiveWeaponAnimator.")]
    public string holdAnimParam   = "Hold";

    [Tooltip("Name of the bool parameter on the bow's Animator that is set TRUE " +
             "while the player is charging AND moving horizontally. " +
             "Must exactly match the parameter name in the bow Animator Controller.")]
    public string movingAnimParam = "Moving";

    [Header("Movement Detection")]
    [Tooltip("Minimum squared speed (units/sec squared) before the player is considered " +
             "to be moving. Raise slightly if IsMovingWhileHolding flickers at rest. " +
             "Default 0.01 works for most setups.")]
    public float movingSpeedThresholdSq = 0.01f;

    [Header("Debug")]
    public bool verbose = false;

    // ── Private state ──
    private EntityStats        _stats;
    private WeaponModelSwapper _swapper;
    private InputAction        _attackAction;   // resolved at runtime from PlayerInput

    private bool  _isCharging;
    private float _chargeStartTime;

    // Tracks the last values pushed to the bow animator so we only call
    // SetBool when something actually changes — mirrors how PlayerCombat
    // resets triggers before firing ("ResetTrigger" before "SetTrigger").
    private bool _lastHoldSent;
    private bool _lastMovingSent;

    // Cached movement components — resolved once in Start to avoid per-frame GetComponent.
    private Rigidbody           _rb;
    private CharacterController _cc;


    void Awake()
    {
        _stats   = GetComponent<EntityStats>();
        _swapper = GetComponent<WeaponModelSwapper>();
    }

    void Start()
    {
        // Look up the Attack action so we can detect release for the
        // charged shot. PlayerCombat.OnAttack handles the press for the
        // free-look + jump cases; we need both edges for the aim+hold flow.
        var pi = GetComponent<PlayerInput>();
        if (pi != null) _attackAction = pi.actions["Attack"];

        if (_attackAction == null)
            Debug.LogWarning("[BowController] No 'Attack' action found on PlayerInput — " +
                             "charged-shot release detection will be disabled.");

        // Cache movement components once so IsMoving() has no allocation cost.
        _rb = GetComponent<Rigidbody>();
        _cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (_isCharging)
        {
            // Build charge while LMB is held.
            CurrentChargeFraction = Mathf.Clamp01(
                (Time.time - _chargeStartTime) / Mathf.Max(0.0001f, maxChargeTime));

            // Detect release.
            bool released = _attackAction != null
                ? _attackAction.WasReleasedThisFrame()
                : !Input.GetMouseButton(0); // Legacy fallback

            if (released)
            {
                FireChargedAimedShot(CurrentChargeFraction);
                StopCharging();
            }
        }

        // Push Hold / Moving bools to the bow's own Animator every frame.
        // Uses WeaponModelSwapper.ActiveWeaponAnimator — the exact same path
        // PlayerCombat uses when it calls:
        //   _swapper?.ActiveWeaponAnimator?.SetTrigger("BowAttk")
        // So the bow's Animator Controller sees consistent driving from both
        // attack triggers (PlayerCombat) and charge-state bools (here).
        PushAnimatorBools();
    }

    // ── Public API — called by PlayerCombat.OnAttack when weapon = Bow ──

    /// <summary>
    /// Called by PlayerCombat when LMB is pressed while aiming (RMB held).
    /// Starts charging until LMB release.
    /// </summary>
    public void BeginAimedShot()
    {
        if (_isCharging) return;
        _isCharging           = true;
        _chargeStartTime      = Time.time;
        CurrentChargeFraction = 0f;
        // Animator bools are driven each frame in Update via PushAnimatorBools().
    }

    /// <summary>
    /// Called by PlayerCombat when LMB is pressed while grounded and NOT aiming.
    /// Single arrow forward along rat facing, with mild auto-target nudge.
    /// </summary>
    public void FreeLookShot()
    {
        Vector3 dir = transform.forward;

        if (autoTargetStrength > 0f)
        {
            EntityStats target = FindAutoTarget();
            if (target != null)
            {
                Vector3 toTarget = target.transform.position - arrowSpawnPoint.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Vector3 nudged = Vector3.Slerp(transform.forward, toTarget.normalized,
                                                    autoTargetStrength);
                    dir = nudged.normalized;
                }
            }
        }

        int dmg = _stats?.CalculateWeaponDamage() ?? 5;
        SpawnArrow(dir, dmg);

        if (verbose) Debug.Log($"[BowController] Free-look shot for {dmg} dmg.");
    }

    /// <summary>
    /// Called by PlayerCombat when LMB is pressed in the air (jump-attack).
    /// Fires <see cref="tripleShotCount"/> arrows in rapid stagger along the
    /// rat's forward (which the animation tilts ~45 degrees down).
    /// </summary>
    public void JumpTripleShot()
    {
        StartCoroutine(TripleShotRoutine());
    }

    // ── Animation state bools ──

    /// <summary>
    /// TRUE while the player is holding LMB in aim mode and a charge is building.
    /// Also drives the holdAnimParam bool on the bow's Animator every frame.
    /// </summary>
    public bool IsHolding => _isCharging;

    /// <summary>
    /// TRUE while the player is charging AND moving (any horizontal velocity above threshold).
    /// Also drives the movingAnimParam bool on the bow's Animator every frame.
    /// Returns false whenever the player is not charging.
    /// </summary>
    public bool IsMovingWhileHolding => _isCharging && IsMoving();

    /// <summary>
    /// UI hook — true while LMB is held and a charge is building.
    /// </summary>
    public bool IsCharging => _isCharging;

    // ── Internals ──

    /// <summary>
    /// Pushes Hold and Moving bools to the bow's Animator each frame.
    /// </summary>
    private void PushAnimatorBools()
    {
        Animator bowAnim = _swapper?.ActiveWeaponAnimator;
        if (bowAnim == null) return;

        bool holding = IsHolding;
        bool moving  = IsMovingWhileHolding;

        if (holding != _lastHoldSent)
        {
            bowAnim.SetBool(holdAnimParam, holding);
            _lastHoldSent = holding;
            if (verbose) Debug.Log($"[BowController] Bow Animator '{holdAnimParam}' -> {holding}");
        }

        if (moving != _lastMovingSent)
        {
            bowAnim.SetBool(movingAnimParam, moving);
            _lastMovingSent = moving;
            if (verbose) Debug.Log($"[BowController] Bow Animator '{movingAnimParam}' -> {moving}");
        }
    }

    private void StopCharging()
    {
        _isCharging           = false;
        CurrentChargeFraction = 0f;
        // PushAnimatorBools() in the next Update will flip Hold -> false automatically.
    }

    private void FireChargedAimedShot(float chargeFraction)
    {
        // Damage scales 1x -> maxChargeMultiplier with charge fraction.
        float dmgMult = Mathf.Lerp(minChargeMultiplier, maxChargeMultiplier, chargeFraction);
        int   baseDmg = _stats?.CalculateWeaponDamage() ?? 5;
        int   dmg     = Mathf.RoundToInt(baseDmg * dmgMult);

        // Speed scales 1x -> maxChargedSpeedMultiplier with charge fraction,
        // so a full-charge shot flies further AND hits harder.
        float speedMult = Mathf.Lerp(1f, maxChargedSpeedMultiplier, chargeFraction);
        float spd       = arrowSpeed * speedMult;

        // Aimed shots use the camera's full 3D look direction (yaw + pitch)
        // so the player can aim up at flying enemies. No auto-target assist
        // while aiming — the player chose to aim manually.
        Vector3 dir = GetAimDirection();
        SpawnArrow(dir, dmg, spd, charged: true);

        if (verbose)
            Debug.Log($"[BowController] Aimed shot — charge {chargeFraction:F2}, " +
                      $"dmg x{dmgMult:F2} ({dmg}), speed x{speedMult:F2} ({spd:F0}), dir {dir}.");
    }

    /// <summary>
    /// Returns the world-space aim direction used for shots while aiming.
    /// Priority order:
    /// 1. aimDirectionSource.forward (manual Inspector override)
    /// </summary>
    private Vector3 GetAimDirection()
    {
        if (aimDirectionSource != null)
            return aimDirectionSource.forward;

        if (useMainCameraIfUnset)
        {
            Camera cam = Camera.main;
            if (cam != null) return cam.transform.forward;
        }

        return transform.forward;
    }

    private IEnumerator TripleShotRoutine()
    {
        for (int i = 0; i < tripleShotCount; i++)
        {
            // Start with the rat's current forward (animation already tilts it).
            Vector3 dir = transform.forward;

            // Pitch the direction further down around the rat's right axis so
            // arrows rain steeply at the ground in front of the player.
            if (jumpShotPitchDown > 0.0001f)
                dir = Quaternion.AngleAxis(jumpShotPitchDown, transform.right) * dir;

            // Optional yaw spread so the three shots don't fly the same line.
            if (tripleShotSpread > 0.0001f)
            {
                float yaw = Random.Range(-tripleShotSpread, tripleShotSpread);
                dir = Quaternion.AngleAxis(yaw, Vector3.up) * dir;
            }

            int dmg = _stats?.CalculateWeaponDamage() ?? 5;
            SpawnArrow(dir, dmg);

            if (i < tripleShotCount - 1)
                yield return new WaitForSeconds(tripleShotInterval);
        }

        if (verbose) Debug.Log($"[BowController] Triple-shot fired {tripleShotCount} arrows.");
    }

    private void SpawnArrow(Vector3 direction, int damage, float speedOverride = -1f, bool charged = false)
    {
        if (arrowPrefab == null || arrowSpawnPoint == null)
        {
            Debug.LogWarning("[BowController] Missing arrowPrefab or arrowSpawnPoint — cannot fire.");
            return;
        }

        Arrow arrow = Instantiate(arrowPrefab,
                                  arrowSpawnPoint.position,
                                  Quaternion.LookRotation(direction.normalized));

        float spd    = speedOverride > 0f ? speedOverride : arrowSpeed;
        // Impact rides along in the arrow's staggerForce slot: basic 1 / charged 2.
        int   impact = _stats != null ? _stats.GetWeaponImpact(charged) : (charged ? 2 : 1);
        arrow.Launch(direction, spd, damage, impact, enemyLayer, arrowLifetime, arrowGravity);
    }

    // ── Movement detection ──

    /// <summary>
    /// Returns true if the player has meaningful horizontal velocity.
    /// Checks Rigidbody first, then CharacterController, then falls back to false.
    /// Components are cached in Start — no per-frame allocation.
    /// </summary>
    private bool IsMoving()
    {
        if (_rb != null)
        {
            Vector3 flatVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            return flatVel.sqrMagnitude > movingSpeedThresholdSq;
        }

        if (_cc != null)
        {
            Vector3 flatVel = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z);
            return flatVel.sqrMagnitude > movingSpeedThresholdSq;
        }

        return false;
    }

    // ── Auto-target lookup ──

    private EntityStats FindAutoTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, autoTargetRange, enemyLayer);
        if (hits == null || hits.Length == 0) return null;

        EntityStats best         = null;
        float       bestDot      = -1f;
        Vector3     fwd          = transform.forward;
        float       halfAngleRad = autoTargetHalfAngle * Mathf.Deg2Rad;
        float       cosLimit     = Mathf.Cos(halfAngleRad);

        foreach (var c in hits)
        {
            var es = c.GetComponentInParent<EntityStats>();
            if (es == null || es.isPlayer || es.IsDead) continue;

            Vector3 to = es.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;
            to.Normalize();

            float dot = Vector3.Dot(fwd, to);
            if (dot < cosLimit) continue;   // outside the cone

            if (dot > bestDot)
            {
                bestDot = dot;
                best    = es;
            }
        }

        return best;
    }

    // ── Gizmos — auto-target cone preview ──

    void OnDrawGizmosSelected()
    {
        if (autoTargetHalfAngle <= 0.001f) return;

        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.5f);
        Vector3    origin = transform.position;
        Vector3    fwd    = transform.forward;
        Quaternion rotL   = Quaternion.AngleAxis(-autoTargetHalfAngle, Vector3.up);
        Quaternion rotR   = Quaternion.AngleAxis( autoTargetHalfAngle, Vector3.up);
        Gizmos.DrawLine(origin, origin + (rotL * fwd) * autoTargetRange);
        Gizmos.DrawLine(origin, origin + (rotR * fwd) * autoTargetRange);
        Gizmos.DrawLine(origin + (rotL * fwd) * autoTargetRange,
                        origin + (rotR * fwd) * autoTargetRange);
    }
}
