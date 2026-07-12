using UnityEngine;

/// <summary>
/// Flying arrow projectile. Spawned by <see cref="BowController"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Arrow : MonoBehaviour
{
    [Header("Defaults (overridden by Launch call)")]
    public float speed     = 30f;
    public float lifetime  = 3f;
    public int   damage    = 10;
    public int   staggerForce = 2;
    public LayerMask enemyLayer;

    [Header("Gravity")]
    [Tooltip("Downward acceleration applied to the arrow each frame, in units/sec². " +
             "0  = flies perfectly straight, no curve.\n" +
             "3  = barely-noticeable arc, almost a straight line.\n" +
             "6  = a clear arrow arc — close shots feel direct, long shots " +
             "      need you to aim slightly above the target. (Recommended)\n" +
             "9.81 = real Earth gravity, drops fast — for short-range crossbow feel.")]
    public float gravity = 6f;

    [Header("Behaviour")]
    [Tooltip("If true, the arrow auto-orients along its velocity direction.")]
    public bool faceVelocity = true;

    [Tooltip("If true, hitting world geometry (anything NOT on enemyLayer) " +
             "destroys the arrow too. If false, the arrow only dies on enemy hit.")]
    public bool dieOnWorldHit = true;

    [Header("Hit Flash (visual on enemy hit)")]
    [Tooltip("If true, a brief white flash disc spawns at the impact point on " +
             "every enemy hit. Subtle confirmation that the shot connected.")]
    public bool spawnHitFlash = true;

    [Tooltip("Color of the hit flash.")]
    public Color hitFlashColor = new Color(1f, 1f, 1f, 0.85f);

    [Tooltip("Radius of the hit flash in world units. Small = subtle, " +
             "big = punchy.")]
    public float hitFlashRadius = 0.4f;

    [Tooltip("How long the hit flash stays visible.")]
    public float hitFlashLifetime = 0.15f;

    [Header("Stick on Enemy Hit")]
    [Tooltip("If true, the arrow lodges in the enemy on hit instead of " +
             "destroying immediately. Looks great visually — quivers full of " +
             "arrows on a tanky enemy.")]
    public bool stickOnHit = true;

    [Tooltip("Seconds the arrow stays stuck in the enemy before destroying. " +
             "If the enemy dies first, the arrow goes with it automatically.")]
    public float stickDuration = 10f;

    [Tooltip("How far back along the arrow's flight direction to nudge it on " +
             "stick so the tip is buried in the enemy and the shaft pokes out. " +
             "0 = stop exactly at hit point. 0.15 = arrow tip ~0.15 units inside.")]
    public float stickInsertionOffset = 0.15f;

    [Header("Debug")]
    public bool verbose = false;

    private Vector3 _velocity;
    private float   _aliveTime;
    private bool    _spent;       // prevents double-hit on same frame


    /// <summary>
    /// Configure the arrow at spawn-time. Call this immediately after
    /// Instantiate so the arrow has its damage / direction set before
    /// the first physics step.
    /// </summary>
    public void Launch(Vector3 direction, float speedOverride, int damageOverride,
                       int staggerOverride, LayerMask layerMask, float lifetimeOverride,
                       float gravityOverride)
    {
        direction.Normalize();
        speed        = speedOverride;
        damage       = damageOverride;
        staggerForce = staggerOverride;
        enemyLayer   = layerMask;
        lifetime     = lifetimeOverride;
        gravity      = gravityOverride;

        _velocity  = direction * speed;
        _aliveTime = 0f;
        _spent     = false;

        if (faceVelocity && _velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(_velocity);
    }

    // Backwards-compatible overload (in case anything else calls Launch without gravity).
    public void Launch(Vector3 direction, float speedOverride, int damageOverride,
                       int staggerOverride, LayerMask layerMask, float lifetimeOverride)
    {
        Launch(direction, speedOverride, damageOverride, staggerOverride,
               layerMask, lifetimeOverride, gravity);
    }

    void Update()
    {
        if (_spent) return;

        // Apply gravity to vertical velocity so the arrow arcs downward.
        _velocity.y -= gravity * Time.deltaTime;

        // Move along the (now-arcing) velocity.
        transform.position += _velocity * Time.deltaTime;
        _aliveTime         += Time.deltaTime;

        // Re-orient the visual so the arrow tilts down with the trajectory.
        if (faceVelocity && _velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(_velocity);

        if (_aliveTime >= lifetime)
            Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_spent) return;

        // Hit an enemy?
        bool isEnemy = ((1 << other.gameObject.layer) & enemyLayer.value) != 0;

        if (isEnemy)
        {
            var stats = other.GetComponentInParent<EntityStats>();
            if (stats != null && !stats.IsDead)
            {
                stats.TakeDamage(damage);
                other.GetComponentInParent<EnemyAI>()
                    ?.TakeKnockback(transform.position, staggerForce);

                if (verbose)
                    Debug.Log($"[Arrow] Hit {other.name} for {damage} dmg.");
            }

            // Visual: quick flash at the impact point so the player sees the hit
            if (spawnHitFlash)
                AttackRipple.SpawnFlash(transform.position, hitFlashRadius,
                                        hitFlashColor, hitFlashLifetime);

            _spent = true;

            if (stickOnHit)
                StickTo(other);
            else
                Destroy(gameObject);
            return;
        }

        // Hit world geometry
        if (dieOnWorldHit)
        {
            // Ignore the player's own collider — Don't die if the arrow brushes
            // its shooter on spawn.
            if (other.CompareTag("Player")) return;
            _spent = true;
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Parent the arrow to the enemy so it travels with them, stop motion,
    /// disable collider so no further trigger events fire, schedule self-
    /// destroy after stickDuration. If the enemy dies first, this arrow is
    /// </summary>
    private void StickTo(Collider enemyCollider)
    {
        _velocity = Vector3.zero;

        // Nudge the arrow forward a touch so its visible shaft pokes into the
        // enemy rather than sitting flush at the collider surface.
        if (stickInsertionOffset != 0f)
            transform.position += transform.forward * stickInsertionOffset;

        // Parent to the enemy's TOP-MOST transform so the arrow rides with
        // them as they move/animate. If the collider is on a child (often
        // the case with armatures), GetComponentInParent<EntityStats>
        // already pointed us at the right root via stats.transform.
        Transform parentTarget = enemyCollider.GetComponentInParent<EntityStats>()?.transform
                              ?? enemyCollider.transform;
        transform.SetParent(parentTarget, worldPositionStays: true);

        // Disable every collider on this arrow so it can't trigger again or
        // be hit by other arrows/projectiles.
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        // After stickDuration, Unity destroys this GameObject. If the enemy
        // is destroyed first, this arrow is destroyed as their child anyway
        // and Unity safely no-ops the pending destroy.
        Destroy(gameObject, stickDuration);

        if (verbose)
            Debug.Log($"[Arrow] Stuck to {parentTarget.name} for {stickDuration}s.");
    }
}
