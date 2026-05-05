using UnityEngine;

/// <summary>
/// Shared core stats used by every entity — player, grunt, and boss.
/// Do not create this directly. Use PlayerStatBlock or EnemyStatBlock instead.
/// </summary>
public abstract class BaseStatBlock : ScriptableObject
{
    [Header("Core Stats")]
    [Tooltip("Starting HP pool")]
    public int baseHealth    = 100;

    [Tooltip("Scales melee damage output")]
    public int baseStrength  = 10;

    [Tooltip("Pool spent on dodges and sprinting")]
    public int baseStamina   = 50;

    [Tooltip("Movement speed and attack recovery rate")]
    public int baseSpeed     = 5;

    [Tooltip("Fixed — never leveled. Modified only by equipped weapon on the player.")]
    public int baseToughness = 3;
}