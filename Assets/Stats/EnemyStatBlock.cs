using UnityEngine;

// Right-click in Project → Create → Rat King → Enemy Stat Block
[CreateAssetMenu(fileName = "NewEnemyStatBlock", menuName = "Rat King/Enemy Stat Block")]
public class EnemyStatBlock : BaseStatBlock
{
    // Note: baseStamina from BaseStatBlock is unused on enemies.
    // Attack cooldown controls attack frequency instead.

    // ─────────────────────────────────────────
    // ATTACK DAMAGE
    // ─────────────────────────────────────────

    [Header("Attack Damage")]
    [Tooltip("Minimum base damage per hit")]
    public int attackDamageMin = 8;

    [Tooltip("Maximum base damage per hit")]
    public int attackDamageMax = 10;

    [Tooltip("Flat bonus damage added per point of Strength.\n" +
             "Total damage = Random(min, max) + (Strength x this value)")]
    public int attackStrengthBonus = 2;

    [Tooltip("Seconds between each attack. Replaces stamina for enemies.")]
    public float attackCooldown = 1.5f;

    // ─────────────────────────────────────────
    // ATTACK HITBOX
    // ─────────────────────────────────────────

    [Header("Attack Hitbox")]
    [Tooltip("Shape of the attack hitbox")]
    public AttackShape attackShape = AttackShape.Sphere;

    [Tooltip("Radius of the attack hitbox in world units")]
    public float attackRadius = 1.8f;

    [Tooltip("Cone angle in degrees — only used if attackShape is Cone.\n" +
             "Matches how PlayerCombat works. 60 is a focused forward swing.")]
    [Range(10f, 360f)]
    public float attackAngle = 60f;

    [Tooltip("Vertical height of the hitbox. Taller enemies need a larger value.")]
    public float attackHeight = 1.0f;

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
             "finalForce = weaponForce - (Toughness x this value)")]
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

public enum AttackShape { Sphere, Cone }