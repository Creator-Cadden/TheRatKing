using UnityEngine;

// Right-click in Project → Create → Rat King → Player Stat Block
[CreateAssetMenu(fileName = "PlayerStatBlock", menuName = "Rat King/Player Stat Block")]
public class PlayerStatBlock : BaseStatBlock
{
    // ─────────────────────────────────────────
    // LEVELING
    // ─────────────────────────────────────────

    [Header("Per Level Point")]
    [Tooltip("+HP gained each time the player puts a point into Health")]
    public int healthPerPoint   = 10;

    [Tooltip("+Strength gained each time the player puts a point into Strength")]
    public int strengthPerPoint = 2;

    [Tooltip("+Stamina pool gained each time the player puts a point into Stamina")]
    public int staminaPerPoint  = 8;

    [Tooltip("+Speed gained each time the player puts a point into Speed")]
    public int speedPerPoint    = 1;

    [Header("Floor Level Caps")]
    [Tooltip("Max levels allowed until the floor 1 boss is beaten")]
    public int floorOneCap   = 5;

    [Tooltip("Max levels allowed until the floor 2 boss is beaten")]
    public int floorTwoCap   = 10;

    [Tooltip("Max levels allowed on floor 3 before the Rat King")]
    public int floorThreeCap = 15;

    // ─────────────────────────────────────────
    // WEAPON DAMAGE
    // Attacks do NOT cost stamina — combat is always available.
    // Stamina is spent on movement actions (sprint, roll, jump).
    // ─────────────────────────────────────────

    [Header("Blade")]
    public int bladeDamageMin      = 8;
    public int bladeDamageMax      = 12;
    [Tooltip("+damage per Strength point while blade is equipped")]
    public int bladeStrengthBonus  = 2;
    [Tooltip("Toughness added to player base while blade is equipped")]
    public int bladeToughnessBonus = 1;

    [Header("Hammer")]
    public int hammerDamageMin      = 20;
    public int hammerDamageMax      = 28;
    [Tooltip("+damage per Strength point while hammer is equipped")]
    public int hammerStrengthBonus  = 3;
    [Tooltip("Toughness added to player base while hammer is equipped")]
    public int hammerToughnessBonus = 4;

    [Header("Bow")]
    public int bowDamageMin      = 10;
    public int bowDamageMax      = 18;
    [Tooltip("Bow does not scale with Strength — always 0")]
    public int bowStrengthBonus  = 0;
    [Tooltip("Bow gives no Toughness — fragile while aiming")]
    public int bowToughnessBonus = 0;

    // ─────────────────────────────────────────
    // ACTION STAMINA COSTS
    // Walk speed is always free. Everything else costs stamina.
    // ─────────────────────────────────────────

    [Header("Action Stamina Costs")]
    [Tooltip("Stamina drained per second while sprinting (walk is always free)")]
    public float sprintStaminaPerSecond = 3.5f;

    [Tooltip("Flat stamina cost per roll/dodge")]
    public int rollStaminaCost = 12;

    [Tooltip("Flat stamina cost per jump")]
    public int jumpStaminaCost = 5;

    // ─────────────────────────────────────────
    // STAMINA REGEN
    // ─────────────────────────────────────────

    [Header("Stamina Regen")]
    [Tooltip("Stamina recovered per second after the regen delay")]
    public float staminaRegenRate  = 15f;

    [Tooltip("Seconds after last stamina use before regen kicks in")]
    public float staminaRegenDelay = 1.2f;
}