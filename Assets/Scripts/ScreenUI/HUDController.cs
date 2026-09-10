using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The switchboard for HUD visibility. Cutscenes, the boss intro, the stat menu
/// and photo mode all want the combat HUD gone; without a single owner for that,
/// you end up with six scripts each toggling a different GameObject and one of
/// them always forgets to turn it back on.
///
/// Systems hide the HUD by name:
///     HUDController.Hide("cutscene");
///     HUDController.Show("cutscene");
/// The HUD is visible only when nobody is hiding it — same pattern as
/// <see cref="CursorManager"/>.
///
/// Also applies the player's HUD Opacity setting, so this is the one place that
/// multiplies the final alpha.
/// </summary>
public class HUDController : MonoBehaviour
{
    public static HUDController Instance { get; private set; }

    [Header("Groups")]
    [Tooltip("The CanvasGroup holding the combat HUD — health, stamina, XP, coins, " +
             "cooldowns, room progress. NOT menus.")]
    public CanvasGroup combatHUD;

    [Tooltip("Optional. Elements that should stay up even in cutscenes (subtitles, " +
             "the boss bar). Left alone by Hide/Show.")]
    public CanvasGroup persistentHUD;

    [Header("Fade")]
    public float fadeSpeed = 8f;

    [Tooltip("Alpha the HUD fades to when hidden. 0 = fully gone.")]
    [Range(0f, 1f)] public float hiddenAlpha = 0f;

    [Header("Settings")]
    [Tooltip("Multiply the HUD's alpha by the player's HUD Opacity setting.")]
    public bool respectOpacitySetting = true;

    // ── Private ───────────────────────────────────────────────────────────────

    private readonly HashSet<string> _hiders = new HashSet<string>();
    private float _current = 1f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (combatHUD == null) return;

        float target = _hiders.Count > 0 ? hiddenAlpha : 1f;
        _current = Mathf.MoveTowards(_current, target, Time.unscaledDeltaTime * fadeSpeed);

        float opacity = respectOpacitySetting ? GameSettings.HudOpacity : 1f;
        combatHUD.alpha = _current * opacity;

        // Don't eat clicks with an invisible HUD.
        combatHUD.blocksRaycasts = _current > 0.05f;

        if (persistentHUD != null && respectOpacitySetting)
            persistentHUD.alpha = opacity;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Hide the combat HUD on behalf of 'reason'. Pair with Show(reason).</summary>
    public static void Hide(string reason)
    {
        if (Instance == null || string.IsNullOrEmpty(reason)) return;
        Instance._hiders.Add(reason);
    }

    /// <summary>Withdraw a hide request. The HUD returns when all reasons are gone.</summary>
    public static void Show(string reason)
    {
        if (Instance == null || string.IsNullOrEmpty(reason)) return;
        Instance._hiders.Remove(reason);
    }

    /// <summary>Drop every hide request — the HUD comes back. Use on scene load / respawn.</summary>
    public static void ForceShow()
    {
        if (Instance == null) return;
        Instance._hiders.Clear();
    }

    /// <summary>Snap to the current target instead of fading. Use before a hard cut.</summary>
    public static void SnapToTarget()
    {
        if (Instance == null) return;
        Instance._current = Instance._hiders.Count > 0 ? Instance.hiddenAlpha : 1f;
    }

    public static bool IsHidden => Instance != null && Instance._hiders.Count > 0;

    // Inspector-friendly wrappers so UnityEvents (cutscene timelines, triggers)
    // can call these without a custom script.
    public void HideForCutscene()  => Hide("cutscene");
    public void ShowAfterCutscene() => Show("cutscene");
}
