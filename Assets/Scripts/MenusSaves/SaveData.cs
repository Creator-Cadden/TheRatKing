using System;

/// <summary>
/// All data persisted between sessions.
/// Serialized to JSON and written to Application.persistentDataPath.
/// </summary>
[Serializable]
public class SaveData
{
    // ── Meta ──────────────────────────────────────────────────────
    public bool   hasData      = false;   // false = empty slot
    public string saveDate     = "";      // display string e.g. "May 13 2026  14:32"
    public float  totalPlayTime = 0f;     // seconds

    // ── Progression ───────────────────────────────────────────────
    public string currentSceneName = "";  // scene to load on continue
    public int    currentFloor     = 1;

    // ── XP / Level ────────────────────────────────────────────────
    public int currentLevel    = 0;
    public int currentXP       = 0;
    public int unspentPoints   = 0;

    // ── Stats (runtime values, not base) ─────────────────────────
    public int maxHealth       = 100;
    public int currentHealth   = 100;
    public int strength        = 5;
    public int maxStamina      = 50;
    public int currentStamina  = 50;
    public int speed           = 5;

    // ── Weapon ───────────────────────────────────────────────────
    public int equippedWeapon  = 0;       // cast to EntityStats.WeaponType
}