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
    // ─────────────────────────────────────────

    [Header("Blade")]
    public int bladeDamageMin      = 8;
    public int bladeDamageMax      = 12;
    [Tooltip("+damage per Strength point while blade is equipped")]
    public int bladeStrengthBonus  = 2;
    [Tooltip("Toughness added to player base while blade is equipped")]
    public int bladeToughnessBonus = 1;
    [Tooltip("Stamina cost per blade hit")]
    public int bladeStaminaCost    = 6;

    [Header("Hammer")]
    public int hammerDamageMin      = 20;
    public int hammerDamageMax      = 28;
    [Tooltip("+damage per Strength point while hammer is equipped")]
    public int hammerStrengthBonus  = 3;
    [Tooltip("Toughness added to player base while hammer is equipped")]
    public int hammerToughnessBonus = 4;
    [Tooltip("Stamina cost per hammer hit")]
    public int hammerStaminaCost    = 18;

    [Header("Bow")]
    public int bowDamageMin      = 10;
    public int bowDamageMax      = 18;
    [Tooltip("Bow does not scale with Strength — always 0")]
    public int bowStrengthBonus  = 0;
    [Tooltip("Bow gives no Toughness — fragile while aiming")]
    public int bowToughnessBonus = 0;
    [Tooltip("Stamina cost per bow draw")]
    public int bowStaminaCost    = 10;

    // ─────────────────────────────────────────
    // STAMINA REGEN
    // ─────────────────────────────────────────

    [Header("Stamina Regen")]
    [Tooltip("Stamina recovered per second after the regen delay")]
    public float staminaRegenRate  = 15f;

    [Tooltip("Seconds after last stamina use before regen kicks in")]
    public float staminaRegenDelay = 1.2f;
}