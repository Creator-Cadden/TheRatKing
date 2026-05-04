using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Manages XP, leveling, and unspent stat points for the player.
/// Attach to the Player GameObject alongside EntityStats.
///
/// Wire-up:
///   - EnemyXPDrop calls XPSystem.AddXP(amount) on the player when an enemy dies.
///   - StatMenuUI reads CurrentXP, XPToNextLevel, CurrentLevel, UnspentPoints.
///   - StatMenuUI calls SpendPoint(stat) which delegates to EntityStats.SpendPoint().
/// </summary>
public class XPSystem : MonoBehaviour
{
    // ─────────────────────────────────────────
    // CONFIGURATION
    // ─────────────────────────────────────────

    [Header("XP Curve")]
    [Tooltip("XP required to reach level 1 from level 0.")]
    public int baseXPPerLevel = 10;

    [Tooltip("Each level costs this much MORE XP than the previous.\n" +
             "e.g. 0 = flat 10 per level.  5 = 10, 15, 20, 25 …")]
    public int xpScalingPerLevel = 5;

    // ─────────────────────────────────────────
    // EVENTS
    // ─────────────────────────────────────────

    public UnityEvent          onLevelUp;
    public UnityEvent<int>     onXPGained;      // passes amount gained
    public UnityEvent<int>     onStatPointSpent; // passes remaining points

    // ─────────────────────────────────────────
    // RUNTIME STATE  (read-only from outside)
    // ─────────────────────────────────────────

    public int CurrentXP       { get; private set; }
    public int CurrentLevel    { get; private set; }
    public int UnspentPoints   { get; private set; }

    /// <summary>XP needed to advance from CurrentLevel to CurrentLevel+1.</summary>
    public int XPToNextLevel   => XPRequiredForLevel(CurrentLevel + 1);

    // ─────────────────────────────────────────

    private EntityStats _stats;

    void Awake()
    {
        _stats = GetComponent<EntityStats>();
        if (_stats == null)
            Debug.LogError("[XPSystem] No EntityStats found on the same GameObject!");
    }

    // ─────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────

    /// <summary>Call this whenever the player kills an enemy (or picks up XP).</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        CurrentXP += amount;
        onXPGained?.Invoke(amount);

        Debug.Log($"[XPSystem] +{amount} XP — total {CurrentXP}/{XPToNextLevel} (Lv{CurrentLevel})");

        // Level up as many times as warranted
        while (CurrentXP >= XPToNextLevel)
        {
            LevelUp();
        }
    }

    /// <summary>
    /// Spend one stat point into a stat.
    /// Valid stat names: "health", "strength", "stamina", "speed"
    /// Returns true if successful.
    /// </summary>
    public bool SpendPoint(string stat)
    {
        if (UnspentPoints <= 0)
        {
            Debug.Log("[XPSystem] No unspent points available.");
            return false;
        }

        if (_stats == null) return false;

        _stats.SpendPoint(stat);
        UnspentPoints--;

        onStatPointSpent?.Invoke(UnspentPoints);
        Debug.Log($"[XPSystem] Spent point on '{stat}'. Points remaining: {UnspentPoints}");
        return true;
    }

    // ─────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────

    private void LevelUp()
    {
        int required = XPToNextLevel;
        CurrentXP   -= required;
        CurrentLevel++;
        UnspentPoints++;

        // Also inform EntityStats so its cap logic still works
        _stats?.GainLevel();

        onLevelUp?.Invoke();
        Debug.Log($"[XPSystem] LEVEL UP → Lv{CurrentLevel}  |  Unspent points: {UnspentPoints}");
    }

    /// <summary>XP required to go from level (n-1) to level n.</summary>
    private int XPRequiredForLevel(int targetLevel)
    {
        // Linear growth: level 1 = base, level 2 = base + scaling, etc.
        return baseXPPerLevel + (targetLevel - 1) * xpScalingPerLevel;
    }
}