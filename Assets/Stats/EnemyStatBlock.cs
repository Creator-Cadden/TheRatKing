using UnityEngine;

// Right-click in Project → Create → Rat King → Enemy Stat Block
[CreateAssetMenu(fileName = "NewEnemyStatBlock", menuName = "Rat King/Enemy Stat Block")]
public class EnemyStatBlock : BaseStatBlock
{
    // ─────────────────────────────────────────
    // ATTACK
    // ─────────────────────────────────────────

    [Header("Attack")]
    [Tooltip("Minimum base damage on a standard attack")]
    public int attackDamageMin = 8;

    [Tooltip("Maximum base damage on a standard attack")]
    public int attackDamageMax = 10;

    [Tooltip("Bonus damage added per Strength point on top of the base range")]
    public int attackStrengthBonus = 2;

    [Tooltip("Seconds between each attack attempt")]
    public float attackCooldown = 1.5f;

    [Tooltip("Distance at which the enemy will attempt an attack")]
    public float attackRange = 1.8f;

    // ─────────────────────────────────────────
    // DETECTION & MOVEMENT
    // ─────────────────────────────────────────

    [Header("Detection")]
    [Tooltip("Radius at which this enemy notices and chases the player")]
    public float aggroRange = 8f;

    [Tooltip("Distance at which the enemy stops moving and starts attacking")]
    public float stopRange = 1.5f;

    [Tooltip("NavMesh movement speed toward the player")]
    public float moveSpeed = 3.5f;

    // ─────────────────────────────────────────
    // KNOCKBACK RESPONSE
    // ─────────────────────────────────────────

    [Header("Knockback Response")]
    [Tooltip("How much force each Toughness point absorbs from an incoming hit.\n" +
             "finalForce = weaponForce - (Toughness x this value)")]
    public float toughnessReductionPerPoint = 1f;

    [Tooltip("How long the knockback movement lasts in seconds")]
    public float knockbackDuration = 0.2f;

    [Tooltip("Raw knockback force applied by a blade hit (before Toughness reduction)")]
    public float bladeKnockbackForce  = 5f;

    [Tooltip("Raw knockback force applied by a hammer hit (before Toughness reduction)")]
    public float hammerKnockbackForce = 12f;

    [Tooltip("Raw knockback force applied by a bow hit (before Toughness reduction)")]
    public float bowKnockbackForce    = 2f;
}