using UnityEngine;

/// <summary>
/// Persistent (PlayerPrefs) player choices for what the new-game tutorial shows.
/// Two independent switches:
///   • ShowBasics  — the movement / controls part
///   • ShowCombat  — the weapon-specific combat part
/// Both off = the tutorial is skipped entirely (straight into the game).
/// Bind these to settings-menu toggles via TutorialSettingsToggle.
/// </summary>
public static class TutorialSettings
{
    private const string KBasics = "tut_show_basics";
    private const string KCombat = "tut_show_combat";

    public static bool ShowBasics
    {
        get => PlayerPrefs.GetInt(KBasics, 1) == 1;
        set { PlayerPrefs.SetInt(KBasics, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static bool ShowCombat
    {
        get => PlayerPrefs.GetInt(KCombat, 1) == 1;
        set { PlayerPrefs.SetInt(KCombat, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>True if any tutorial part is enabled.</summary>
    public static bool AnyEnabled => ShowBasics || ShowCombat;
}
