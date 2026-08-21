using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Manages XP, leveling, and unspent stat points for the player.
/// Attach to the Player GameObject alongside EntityStats.
/// </summary>
public class XPSystem : MonoBehaviour
{
    // ── CONFIGURATION ──

    [Header("XP Curve")]
    [Tooltip("XP required to reach level 1 from level 0.")]
    public int baseXPPerLevel = 10;

    [Tooltip("Each level costs this much MORE XP than the previous.\n" +
             "e.g. 0 = flat 10 per level.  5 = 10, 15, 20, 25 …")]
    public int xpScalingPerLevel = 5;

    // ── EVENTS ──

    public UnityEvent          onLevelUp;
    public UnityEvent<int>     onXPGained;            // passes amount gained
    public UnityEvent<int, string> onXPGainedFromSource; // (amount, sourceName)
    public UnityEvent<int>     onStatPointSpent;      // passes remaining points

    // ── RUNTIME STATE  (read-only from outside) ──

    public int CurrentXP       { get; private set; }
    public int CurrentLevel    { get; private set; }
    public int UnspentPoints   { get; private set; }

    /// <summary>
    /// XP needed to advance from CurrentLevel to CurrentLevel+1.
    /// </summary>
    public int XPToNextLevel   => XPRequiredForLevel(CurrentLevel + 1);


    private EntityStats _stats;

    void Awake()
    {
        _stats = GetComponent<EntityStats>();
        if (_stats == null)
            Debug.LogError("[XPSystem] No EntityStats found on the same GameObject!");
    }

    // ── PUBLIC API ──

    /// <summary>
    /// Call this whenever the player kills an enemy (or picks up XP).
    /// </summary>
    public void AddXP(int amount) => AddXP(amount, "");

    /// <summary>
    /// Same as AddXP(amount) but also fires onXPGainedFromSource so the
    /// floating "+X XP from {source}" indicator can show what the player got it from.
    /// </summary>
    public void AddXP(int amount, string sourceName) => AddXP(amount, sourceName, true);

    /// <summary>
    /// Full form. When <paramref name="announceSource"/> is false, XP is granted
    /// and onXPGained still fires (so the XP bar animates), but the "+X XP from
    /// {source}" feed is NOT triggered. XP orbs use this so a burst of orbs
    /// doesn't spam the feed with one line per orb — EnemyXPDrop fires a single
    /// informative popup at the kill via <see cref="AnnounceXPSource"/> instead.
    /// </summary>
    public void AddXP(int amount, string sourceName, bool announceSource)
    {
        if (amount <= 0) return;

        CurrentXP += amount;
        onXPGained?.Invoke(amount);
        if (announceSource)
            onXPGainedFromSource?.Invoke(amount, sourceName ?? "");

        Debug.Log($"[XPSystem] +{amount} XP from '{sourceName}' — total {CurrentXP}/{XPToNextLevel} (Lv{CurrentLevel})");

        // Level up as many times as warranted
        while (CurrentXP >= XPToNextLevel)
        {
            LevelUp();
        }
    }

    /// <summary>
    /// Fire the "+X XP from {source}" feed WITHOUT granting any XP. Used when the
    /// XP itself is delivered gradually (by flying XP orbs) but you still want one
    /// informative popup at the moment of the kill.
    /// </summary>
    public void AnnounceXPSource(int amount, string sourceName)
    {
        if (amount <= 0) return;
        onXPGainedFromSource?.Invoke(amount, sourceName ?? "");
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

    // ── PRIVATE HELPERS ──

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

    /// <summary>
    /// XP required to go from level (n-1) to level n.
    /// </summary>
    private int XPRequiredForLevel(int targetLevel)
    {
        // Linear growth: level 1 = base, level 2 = base + scaling, etc.
        return baseXPPerLevel + (targetLevel - 1) * xpScalingPerLevel;
    }

    // ── Save / Load ──

    public void ApplySaveData(SaveData data)
    {
        CurrentLevel  = data.currentLevel;
        CurrentXP     = data.currentXP;
        UnspentPoints = data.unspentPoints;

        onXPGained?.Invoke(0);
        Debug.Log($"[XPSystem] Save data applied — Lv{CurrentLevel} XP:{CurrentXP} Points:{UnspentPoints}");
    }
}
