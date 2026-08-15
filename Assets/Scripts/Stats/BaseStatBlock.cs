using UnityEngine;

/// <summary>
/// Shared core stats used by every entity — player, grunt, and boss.
/// Do not create this directly. Use PlayerStatBlock or EnemyStatBlock instead.
/// </summary>
public abstract class BaseStatBlock : ScriptableObject
{
    // Only TRULY shared stats live here. Stamina & Speed are player-only
    // (enemies use EnemyStatBlock.moveSpeed and have no stamina); Toughness is
    // enemy-only (the player has no stagger-resistance stat). Those fields live
    // on PlayerStatBlock / EnemyStatBlock respectively.
    [Header("Core Stats")]
    [Tooltip("Starting HP pool")]
    public int baseHealth    = 100;

    [Tooltip("Scales melee damage output")]
    public int baseStrength  = 10;
}