using UnityEngine;

/// <summary>
/// Shared core stats used by every entity — player, grunt, and boss.
/// Do not create this directly. Use PlayerStatBlock or EnemyStatBlock instead.
/// </summary>
public abstract class BaseStatBlock : ScriptableObject
{
    [Header("Core Stats")]
    [Tooltip("Starting HP pool")]
    public int baseHealth    = 30;

    [Tooltip("Scales melee damage output")]
    public int baseStrength  = 5;

    [Tooltip("Pool spent on attacks, dodges, and sprinting")]
    public int baseStamina   = 40;

    [Tooltip("Movement speed and attack recovery rate")]
    public int baseSpeed     = 6;

    [Tooltip("Fixed — never leveled up. Modified only by equipped weapon on the player.")]
    public int baseToughness = 2;
}