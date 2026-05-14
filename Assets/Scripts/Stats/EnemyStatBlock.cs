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
    // ATTACK SHAPE
    // ─────────────────────────────────────────

    [Header("Attack Shape")]
    [Tooltip("Which shape this enemy's attack uses.\n" +
             "Cone       = forward arc    — set attackRadius + attackAngle\n" +
             "Circle     = full 360° area — set circleRadius\n" +
             "Rectangle  = forward box    — set rectWidth + rectLength")]
    public AttackShape attackShape = AttackShape.Cone;

    // ─────────────────────────────────────────
    // ATTACK HITBOX — shared
    // ─────────────────────────────────────────

    [Header("Attack Hitbox — Shared")]
    [Tooltip("Vertical height of the overlap check in world units.\n" +
             "Does NOT affect the visible flat indicator — only controls how tall\n" +
             "the damage volume is so it can still hit the player if they are\n" +
             "slightly above or below the attack origin.")]
    public float attackHeight = 1.5f;

    // ─────────────────────────────────────────
    // ATTACK HITBOX — Cone
    // ─────────────────────────────────────────

    [Header("Attack Hitbox — Cone")]
    [Tooltip("Radius of the attack cone in world units.\n" +
             "This is ALSO the distance at which the enemy will attempt to attack.\n" +
             "Only used when attackShape = Cone.")]
    public float attackRadius = 1.8f;

    [Tooltip("Sweep of the attack cone in degrees.\n" +
             "60 = focused forward swing. 180 = wide half-circle.\n" +
             "Only used when attackShape = Cone.")]
    [Range(10f, 360f)]
    public float attackAngle = 60f;

    // ─────────────────────────────────────────
    // ATTACK HITBOX — Circle
    // ─────────────────────────────────────────

    [Header("Attack Hitbox — Circle")]
    [Tooltip("Radius of the full-circle AoE in world units.\n" +
             "This is ALSO the distance at which the enemy will attempt to attack.\n" +
             "Only used when attackShape = Circle.")]
    public float circleRadius = 2f;

    // ─────────────────────────────────────────
    // ATTACK HITBOX — Rectangle
    // ─────────────────────────────────────────

    [Header("Attack Hitbox — Rectangle")]
    [Tooltip("Side-to-side width of the rectangular hitbox in world units.\n" +
             "Only used when attackShape = Rectangle.")]
    public float rectWidth = 1.5f;

    [Tooltip("Forward reach of the rectangular hitbox in world units.\n" +
             "This is ALSO the distance at which the enemy will attempt to attack.\n" +
             "Only used when attackShape = Rectangle.")]
    public float rectLength = 2.5f;

    // ─────────────────────────────────────────
    // TIMING
    // ─────────────────────────────────────────

    [Header("Attack Timing")]
    [Tooltip("Seconds between each attack.")]
    public float attackCooldown = 1.5f;

    [Tooltip("Seconds the enemy shows the indicator before the hit lands.\n" +
             "Gives the player time to react and step out.")]
    public float attackWindupTime = 0.6f;

    [Tooltip("Safety timeout. If the attack animation never fires its end event\n" +
             "the attack state is force-cleared after this many seconds.")]
    public float attackAnimTimeout = 2.5f;

    // ─────────────────────────────────────────
    // DETECTION & MOVEMENT
    // ─────────────────────────────────────────

    [Header("Detection & Movement")]
    [Tooltip("Radius at which this enemy notices and chases the player.")]
    public float aggroRange = 8f;

    [Tooltip("Distance at which the enemy stops walking and waits to attack.\n" +
             "Set this to roughly match your attack reach so the enemy stops\n" +
             "just before it fires. Does NOT control when the attack fires —\n" +
             "the attack shape's own reach does that.")]
    public float stopRange = 1.5f;

    [Tooltip("NavMesh movement speed toward the player.")]
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

    // ─────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────

    /// <summary>
    /// The flat XZ distance at which this enemy should attempt to attack.
    /// Driven entirely by the attack shape's reach — NOT by stopRange.
    /// </summary>
    public float AttackReach => attackShape switch
    {
        AttackShape.Circle    => circleRadius,
        AttackShape.Rectangle => rectLength,
        _                     => attackRadius,  // Cone
    };
}