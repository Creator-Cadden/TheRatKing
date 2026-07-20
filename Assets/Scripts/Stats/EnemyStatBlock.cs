using System;
using UnityEngine;

// Right-click in Project → Create → Rat King → Enemy Stat Block

/// <summary>
/// How the decal attack list is walked when the enemy has more than one entry.
/// </summary>
public enum DecalCycleMode
{
    [InspectorName("None (always first entry)")]
    None,
    [InspectorName("Sequence (1 → 2 → 3 → 1 ...)")]
    Sequence,
    [InspectorName("Random (never repeats the same one)")]
    Random,
}

/// <summary>
/// Tier 1 attack — no decal. The windup ANIMATION is the telegraph.
/// Read by per-enemy scripts like GruntCombat (hit volume position/size
/// lives on the script's attackPoint, not here).
/// </summary>
[Serializable]
public class BasicAttackConfig
{
    [Tooltip("Minimum damage per hit (before Strength bonus).")]
    public int damageMin = 5;

    [Tooltip("Maximum damage per hit (before Strength bonus).")]
    public int damageMax = 5;

    [Tooltip("Distance at which the enemy will attempt this attack.")]
    public float reach = 1.7f;

    [Tooltip("Seconds of windup (rear-back pose) before the strike. " +
             "Match this to the windup animation length.")]
    public float windupTime = 0.45f;

    [Tooltip("Seconds after this attack before the next attack can start.")]
    public float cooldown = 1.3f;
}

/// <summary>
/// Tier 2 attack — telegraphed by a floor decal. One entry per distinct
/// attack; the cycle mode on the stat block decides how entries rotate
/// (Captain = 3 entries + Sequence, Tough dash = 1 rect entry, Balloon =
/// 1 circle entry).
/// </summary>
[Serializable]
public class DecalAttackConfig
{
    [Tooltip("Label shown in the Inspector list — has no gameplay effect.")]
    public string name = "Attack";

    [Tooltip("Cone = forward arc. Circle = 360° area. Rectangle = forward box (dash/charge).")]
    public AttackShape shape = AttackShape.Cone;

    [Header("Damage")]
    [Tooltip("Minimum damage (before Strength bonus). Decals hit harder than basics.")]
    public int damageMin = 15;

    [Tooltip("Maximum damage (before Strength bonus).")]
    public int damageMax = 15;

    [Header("Cone (only when shape = Cone)")]
    [Tooltip("Forward reach of the cone. Also the attack range for this entry.")]
    public float coneRadius = 3f;

    [Range(10f, 360f)]
    [Tooltip("Sweep of the cone in degrees. 90 = quarter circle.")]
    public float coneAngle = 90f;

    [Header("Circle (only when shape = Circle)")]
    [Tooltip("Radius of the 360° area. Also the attack range for this entry.")]
    public float circleRadius = 2.5f;

    [Header("Rectangle (only when shape = Rectangle)")]
    [Tooltip("Side-to-side width of the box.")]
    public float rectWidth = 1.5f;

    [Tooltip("Forward length of the box. Also the attack range for this entry.")]
    public float rectLength = 4f;

    [Header("Shared")]
    [Tooltip("Vertical height of the damage volume (hits slightly above/below).")]
    public float height = 1.5f;

    [Tooltip("Seconds the decal shows before the hit lands. Decals are a promise — " +
             "give the player time to move.")]
    public float windupTime = 0.8f;

    [Tooltip("Seconds after this attack before the next attack can start.")]
    public float cooldown = 2f;

    [Tooltip("Seconds of vulnerable recovery after the hit lands — the punish " +
             "window. Give big attacks (wide cones) the longest recovery.")]
    public float recoverTime = 1f;

    /// <summary>Distance at which the enemy attempts this attack.</summary>
    public float Reach => shape switch
    {
        AttackShape.Circle    => circleRadius,
        AttackShape.Rectangle => rectLength,
        _                     => coneRadius,
    };
}

/// <summary>
/// All tunable numbers for ONE enemy type, organized by the two-tier telegraph
/// grammar: Basic Attack (no decal, animation telegraph) vs Decal Attacks
/// (floor telegraph, optional rotation cycle). One asset per enemy kind;
/// prefabs reference it through EntityStats. Per-enemy BEHAVIOR lives in the
/// enemy's own combat script (GruntCombat etc.) — only DATA lives here.
/// Inherited from BaseStatBlock: baseHealth, baseStrength. Enemies have no
/// stamina and use moveSpeed (below) for movement, so baseStamina/baseSpeed
/// don't exist here. Toughness is enemy-only and lives on this block.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyStatBlock", menuName = "Rat King/Enemy Stat Block")]
public class EnemyStatBlock : BaseStatBlock
{
    // ── DEFENSE ──

    [Header("Defense")]
    [Tooltip("Stagger resistance (enemy-only). A player weapon staggers this enemy " +
             "when the weapon's Impact/staggerForce is greater than this value. " +
             "Tiers (design): Grunt 0 · Soldier 1 · Strong 2 · Elite 3 · Mini-boss 4 · Boss 5.")]
    public int baseToughness = 3;

    // ── IDENTITY ──

    [Header("Identity")]
    [Tooltip("Display name for '+X XP from {name}' and (later) miniboss health bars. " +
             "Blank = GameObject's name.")]
    public string displayName = "";

    [Tooltip("XP granted when this enemy dies. EnemyXPDrop reads this.")]
    public int xpReward = 10;

    // ── MOVEMENT & AGGRO ──

    [Header("Movement & Aggro")]
    [Tooltip("NavMesh movement speed toward the player.")]
    public float moveSpeed = 3.5f;

    [Tooltip("Radius at which this enemy notices and chases the player.")]
    public float aggroRange = 8f;

    [Tooltip("Distance at which the enemy stops walking and waits to attack. " +
             "Set roughly to the shortest attack reach.")]
    public float stopRange = 1.5f;

    [Tooltip("Seconds the enemy stays aggro'd after being damaged even outside " +
             "aggroRange (anti plink-from-safety). 0 = disabled.")]
    public float damagedAggroDuration = 60f;

    // ── BASIC ATTACK (TIER 1 — no decal, animation is the telegraph) ──

    [Header("Basic Attack (Tier 1 — no decal)")]
    [Tooltip("Does this enemy have an unmarked melee attack? Grunt: yes (its only " +
             "attack). Tough/Captain: yes (filler swipe). Balloon/Boss: no.")]
    public bool hasBasicAttack = true;

    public BasicAttackConfig basicAttack = new BasicAttackConfig();

    // ── DECAL ATTACKS (TIER 2 — floor telegraph) ──

    [Header("Decal Attacks (Tier 2 — floor telegraph)")]
    [Tooltip("Does this enemy have telegraphed decal attacks? Grunt: no. " +
             "Tough: 1 (rect dash). Captain: 3 (cone/circle/rect rotation). " +
             "Balloon: 1 (circle).")]
    public bool hasDecalAttack = false;

    [Tooltip("How the list below is walked when there's more than one entry.\n" +
             "None = always the first entry. Sequence = fixed learnable rotation " +
             "(the Captain's midterm pattern). Random = shuffled, no repeats.")]
    public DecalCycleMode decalCycleMode = DecalCycleMode.None;

    [Tooltip("One entry per distinct decal attack, each with its own shape, " +
             "damage, and timing.")]
    public DecalAttackConfig[] decalAttacks = new DecalAttackConfig[0];

    [Tooltip("Interleave rule for enemies that have BOTH tiers: the decal attack " +
             "is locked until this many basic attacks have landed since the last " +
             "decal, then it becomes the next attack. Makes the rhythm learnable " +
             "(e.g. 2 = swipe, swipe, DASH, repeat).\n" +
             "0 = no chain — the decal fires whenever it's off cooldown and in range.")]
    public int basicAttacksBetweenDecals = 0;

    // ── DAMAGE SCALING ──

    [Header("Damage Scaling")]
    [Tooltip("Flat bonus damage added per point of Strength to EVERY attack " +
             "(basic and decal): total = Random(min,max) + Strength × this. " +
             "Combined with strengthScalePerFloor this is how enemy damage " +
             "grows on deeper floors. FatRatBoss reads this too.")]
    public int attackStrengthBonus = 0;

    // ── FLOOR SCALING ──

    [Header("Floor Scaling")]
    [Tooltip("BOSSES: tick this ON. Bosses are hand-tuned per floor and must " +
             "never scale — their HP/Strength are exactly what you type. " +
             "(This is why General Chonk was spawning with 1562 HP instead of " +
             "1000 — the grunt-oriented floor multiplier was hitting him too.)")]
    public bool ignoreFloorScaling = false;

    [Tooltip("Multiplier compounded onto baseHealth per floor above 1. " +
             "1.0 = none. 1.25 = +25%/floor.")]
    [Range(0.5f, 3f)]
    public float healthScalePerFloor = 1.25f;

    [Tooltip("Same rule applied to Strength (scales damage via attackStrengthBonus).")]
    [Range(0.5f, 3f)]
    public float strengthScalePerFloor = 1.20f;

    [Tooltip("Same rule applied to MaxStamina. Cosmetic for enemies — leave at 1.")]
    [Range(0.5f, 3f)]
    public float staminaScalePerFloor = 1.0f;

    // ── KNOCKBACK RESPONSE ──

    [Header("Knockback Response")]
    [Tooltip("Force each Toughness point absorbs: finalForce = weaponForce − (Toughness × this).")]
    public float toughnessReductionPerPoint = 1f;

    [Tooltip("Duration of knockback movement in seconds.")]
    public float knockbackDuration = 0.2f;

    [Tooltip("Raw knockback force from a blade hit (before Toughness reduction).")]
    public float bladeKnockbackForce  = 5f;

    [Tooltip("Raw knockback force from a hammer hit (before Toughness reduction).")]
    public float hammerKnockbackForce = 12f;

    [Tooltip("Raw knockback force from a bow hit (before Toughness reduction).")]
    public float bowKnockbackForce    = 2f;

    // ── SAFETY ──

    [Header("Safety")]
    [Tooltip("If an attack animation never fires its end event, the attack state " +
             "is force-cleared after this many seconds.")]
    public float attackAnimTimeout = 2.5f;
}
