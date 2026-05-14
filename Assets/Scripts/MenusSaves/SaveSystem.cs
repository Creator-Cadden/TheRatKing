using UnityEngine;
using System.IO;

/// <summary>
/// Static save/load system. Writes one JSON file per slot to
/// Application.persistentDataPath (works on all platforms).
///
/// File names: save_0.json, save_1.json, save_2.json
/// </summary>
public static class SaveSystem
{
    public const int SlotCount = 3;

    private static string SlotPath(int slot) =>
        Path.Combine(Application.persistentDataPath, $"save_{slot}.json");

    // ── Write ─────────────────────────────────────────────────────

    public static void Save(int slot, SaveData data)
    {
        if (slot < 0 || slot >= SlotCount) return;

        data.hasData  = true;
        data.saveDate = System.DateTime.Now.ToString("MMM dd yyyy  HH:mm");

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SlotPath(slot), json);

        Debug.Log($"[SaveSystem] Slot {slot} saved → {SlotPath(slot)}");
    }

    // ── Read ──────────────────────────────────────────────────────

    public static SaveData Load(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return new SaveData();

        string path = SlotPath(slot);
        if (!File.Exists(path)) return new SaveData();   // empty slot

        string json = File.ReadAllText(path);
        SaveData data = JsonUtility.FromJson<SaveData>(json);
        return data ?? new SaveData();
    }

    public static bool SlotHasData(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return false;
        string path = SlotPath(slot);
        if (!File.Exists(path)) return false;
        SaveData d = Load(slot);
        return d != null && d.hasData;
    }

    // ── Delete ────────────────────────────────────────────────────

    public static void Delete(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return;
        string path = SlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
        Debug.Log($"[SaveSystem] Slot {slot} deleted.");
    }

    // ── Populate SaveData from live game state ────────────────────

    public static SaveData CaptureCurrentState(
        EntityStats stats,
        XPSystem     xp,
        string       sceneName,
        float        playTime,
        string       saveName = "")
    {
        var data = new SaveData
        {
            saveName         = saveName,
            currentSceneName = sceneName,
            currentFloor     = stats.CurrentFloor,
            currentLevel     = xp.CurrentLevel,
            currentXP        = xp.CurrentXP,
            unspentPoints    = xp.UnspentPoints,
            maxHealth        = stats.MaxHealth,
            currentHealth    = stats.CurrentHealth,
            strength         = stats.Strength,
            maxStamina       = stats.MaxStamina,
            currentStamina   = stats.CurrentStamina,
            speed            = stats.Speed,
            equippedWeapon   = (int)stats.EquippedWeapon,
            totalPlayTime    = playTime
        };
        return data;
    }

    // ── Apply SaveData to live game state ─────────────────────────

    public static void ApplyToStats(SaveData data, EntityStats stats, XPSystem xp)
    {
        if (data == null || !data.hasData) return;

        // Apply via SpendPoint-equivalent but directly setting values.
        // We call the internal reset first so base stats are correct,
        // then patch the runtime values to match the save.
        stats.ApplySaveData(data);
        xp.ApplySaveData(data);
    }
}