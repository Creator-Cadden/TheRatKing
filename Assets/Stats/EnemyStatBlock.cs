using UnityEngine;

// Right-click in Project → Create → Rat King → Enemy Stat Block
[CreateAssetMenu(fileName = "NewEnemyStatBlock", menuName = "Rat King/Enemy Stat Block")]
public class EnemyStatBlock : BaseStatBlock
{
    // Note: baseStamina from BaseStatBlock is unused on enemies.
    // attackCooldown controls attack frequency instead.

    // ─────────────────────────────────────────
    // ATTACK DAMAGE
    // ─────────────────────────────────────────

    [Header("Attack Damage")]
    [Tooltip("Minimum base damage per hit")]
    public int attackDamageMin = 8;

    [Tooltip("Maximum base damage per hit")]
    public int attackDamageMax = 10;

    [Tooltip("Flat bonus damage added per point of Strength.\n" +
             "Total damage = Random(min, max) + (Strength × this value)")]
    public int attackStrengthBonus = 2;

    // ─────────────────────────────────────────
    // ATTACK HITBOX
    // ─────────────────────────────────────────

    [Header("Attack Hitbox")]
    [Tooltip("Radius of the attack cone in world units.\n" +
             "Equivalent to basicAttackRadius on PlayerCombat.")]
    public float attackRadius = 1.8f;

    [Tooltip("Sweep of the attack cone in degrees.\n" +
             "60 = focused forward swing. Equivalent to basicAttackAngle on PlayerCombat.")]
    [Range(10f, 360f)]
    public float attackAngle = 60f;

    [Tooltip("Vertical height of the hitbox capsule in world units.\n" +
             "Equivalent to basicAttackHeight on PlayerCombat. Increase for taller enemies.")]
    public float attackHeight = 1.0f;

    [Tooltip("Seconds between each attack.\n" +
             "Replaces stamina cost for enemies.")]
    public float attackCooldown = 1.5f;

    [Tooltip("Seconds the enemy pauses and shows the attack indicator before the hit lands.\n" +
             "Gives the player time to react. Equivalent to attackWindupTime on EnemyCombat.")]
    public float attackWindupTime = 0.3f;

    [Tooltip("Safety timeout in seconds. If the attack animation never fires its end event\n" +
             "(e.g. animation is missing or misconfigured) the attack state is force-cleared\n" +
             "after this many seconds. Equivalent to attackAnimTimeout on EnemyCombat.")]
    public float attackAnimTimeout = 2.5f;

    // ─────────────────────────────────────────
    // DETECTION & MOVEMENT
    // ─────────────────────────────────────────

    [Header("Detection")]
    [Tooltip("Radius at which this enemy notices and chases the player")]
    public float aggroRange = 8f;

    [Tooltip("Distance at which the enemy stops moving and prepares to attack")]
    public float stopRange = 1.5f;

    [Tooltip("NavMesh movement speed toward the player")]
    public float moveSpeed = 3.5f;

    // ─────────────────────────────────────────
    // KNOCKBACK RESPONSE
    // ─────────────────────────────────────────

    [Header("Knockback Response")]
    [Tooltip("Force each Toughness point absorbs from an incoming hit.\n" +
             "finalForce = weaponForce - (Toughness × this value)")]
    public float toughnessReductionPerPoint = 1f;

    [Tooltip("Duration of knockback movement in seconds")]
    public float knockbackDuration = 0.2f;

    [Tooltip("Raw knockback force from a blade hit (before Toughness reduction)")]
    public float bladeKnockbackForce  = 5f;

    [Tooltip("Raw knockback force from a hammer hit (before Toughness reduction)")]
    public float hammerKnockbackForce = 12f;

    [Tooltip("Raw knockback force from a bow hit (before Toughness reduction)")]
    public float bowKnockbackForce    = 2f;
}