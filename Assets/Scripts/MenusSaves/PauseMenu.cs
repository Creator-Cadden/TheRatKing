using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pause menu shown on Escape.
///
/// Hierarchy:
///   [Any GameObject]              ← attach this script
///   └── PauseMenuRoot             ← drag into pauseMenuRoot (active in Editor, hidden at Start)
///       ├── TitleLabel            (TMP_Text) "PAUSED"
///       ├── LevelInfoLabel        (TMP_Text) last save info
///       ├── ResetButton           (Button)   "RESTART LEVEL"
///       ├── SettingsButton        (Button)   "SETTINGS"  (stub)
///       └── MainMenuButton        (Button)   "MAIN MENU"
///
/// Press Escape again to resume — no resume button needed.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("Must be active in Editor so children initialize. Script hides it at Start.")]
    public GameObject pauseMenuRoot;

    [Header("Labels")]
    public TMP_Text titleLabel;
    public TMP_Text levelInfoLabel;

    [Header("Buttons")]
    public Button resetButton;
    public Button settingsButton;
    public Button mainMenuButton;

    public bool IsPaused { get; private set; }

    private const string CURSOR_OWNER = "pause";

    // ─────────────────────────────────────────

    void Start()
    {
        resetButton   ?.onClick.AddListener(OnReset);
        settingsButton?.onClick.AddListener(OnSettings);
        mainMenuButton?.onClick.AddListener(OnMainMenu);

        // Start hidden — root must be active in Editor so children initialize
        SetVisible(false);
    }

    void OnDestroy()
    {
        CursorManager.Release(CURSOR_OWNER);
    }

    // ─────────────────────────────────────────
    // Input — bound to Escape / Pause action via PlayerInput Send Messages
    // ─────────────────────────────────────────

    public void OnPause(InputValue value)
    {
        if (!value.isPressed) return;
        if (IsPaused) Resume();
        else          Pause();
    }

    // ─────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────

    public void Pause()
    {
        IsPaused       = true;
        Time.timeScale = 0f;

        SetVisible(true);

        // Set labels AFTER making root visible — guarantees the Canvas
        // layout is active and TMP can calculate text correctly
        SetLabel(titleLabel, "PAUSED");
        SetLabel(levelInfoLabel, BuildLevelInfoString());

        CursorManager.Request(CURSOR_OWNER);
    }

    public void Resume()
    {
        IsPaused       = false;
        Time.timeScale = 1f;
        SetVisible(false);
        CursorManager.Release(CURSOR_OWNER);
    }

    // ─────────────────────────────────────────
    // Button callbacks
    // ─────────────────────────────────────────

    private void OnReset()
    {
        Resume();
        GameManager.Instance?.ResetToCheckpoint();
    }

    private void OnSettings()
    {
        // Stub — wire to your settings panel when ready
        Debug.Log("[PauseMenu] Settings clicked — wire to your settings panel.");
    }

    private void OnMainMenu()
    {
        Resume();
        GameManager.Instance?.ReturnToMainMenu();
    }

    // ─────────────────────────────────────────
    // Level info
    // ─────────────────────────────────────────

    private static string BuildLevelInfoString()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.HasActiveGame || gm.ActiveSave == null)
            return "";

        SaveData s      = gm.ActiveSave;
        string saveName = string.IsNullOrEmpty(s.saveName)
                          ? "Save " + (gm.ActiveSlot + 1)
                          : s.saveName;
        string saveDate = string.IsNullOrEmpty(s.saveDate) ? "" : "  ·  " + s.saveDate;

        return "Last save: " + saveName
             + "  —  Floor " + s.currentFloor
             + "  ·  " + s.currentSceneName
             + saveDate;
    }

    // ─────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────

    private void SetVisible(bool v)
    {
        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(v);
        else
            Debug.LogWarning("[PauseMenu] pauseMenuRoot is null — drag it into the Inspector.");
    }

    private static void SetLabel(TMP_Text t, string s)
    {
        if (t != null) t.text = s;
    }
}