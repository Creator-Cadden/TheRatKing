using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pause menu shown on Escape.
/// Also used as the post-death overlay by calling ShowDeathState().
///
/// Hierarchy:
///   PauseMenuRoot
///   ├── TitleLabel          (TMP_Text) — "PAUSED" or "YOU DIED"
///   ├── ResetButton         (Button)   — reset to checkpoint
///   ├── MenuButton          (Button)   — back to main menu
///   └── QuitButton          (Button)   — quit to desktop (small, subtle)
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Root")]
    public GameObject pauseMenuRoot;

    [Header("Labels")]
    public TMP_Text titleLabel;
    public TMP_Text levelInfoLabel;

    [Header("Buttons")]
    public Button resetButton;   // "Reset to Checkpoint"
    public Button menuButton;    // "Main Menu"
    public Button quitButton;    // small quit button

    public bool IsPaused { get; private set; }

    private const string CURSOR_OWNER = "pause";

    void Start()
    {
        resetButton?.onClick.AddListener(OnReset);
        menuButton ?.onClick.AddListener(OnMainMenu);
        quitButton ?.onClick.AddListener(OnQuit);

        SetVisible(false);
    }

    // ── Input — bound to Escape / Pause action ────────────────────

    public void OnPause(InputValue value)
    {
        if (!value.isPressed) return;
        if (IsPaused) Resume();
        else          Pause();
    }

    // ── Public API ────────────────────────────────────────────────

    public void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        SetLabel(titleLabel, "PAUSED");
        SetVisible(true);
        CursorManager.Request(CURSOR_OWNER);
    }

    public void Resume()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        SetVisible(false);
        CursorManager.Release(CURSOR_OWNER);
    }

    /// <summary>
    /// Called by DeathScreen to reuse this menu after death.
    /// Removes the resume option — only reset or menu available.
    /// </summary>
    public void ShowDeathState()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        SetLabel(titleLabel, "YOU DIED");
        SetVisible(true);
        CursorManager.Request(CURSOR_OWNER);
    }

    // ── Callbacks ─────────────────────────────────────────────────

    private string BuildLevelInfoString()
    {
        GameManager gm = GameManager.Instance;

        if (gm == null || !gm.HasActiveGame || gm.ActiveSave == null)
            return "Last save: unknown";

        SaveData s       = gm.ActiveSave;
        string saveName  = string.IsNullOrEmpty(s.saveName)
                           ? "Save " + (gm.ActiveSlot + 1)
                           : s.saveName;
        string saveDate  = string.IsNullOrEmpty(s.saveDate) ? "unknown time" : s.saveDate;
        string floor     = "Floor " + s.currentFloor;
        string scene     = s.currentSceneName;

        // e.g. "Last save: My Run  —  Floor 1  ·  lvl1  ·  May 13  14:32"
        return "Last save: " + saveName
             + "  —  " + floor + "  ·  " + scene
             + "  ·  " + saveDate;
    }

    private void OnReset()
    {
        Resume();
        GameManager.Instance?.ResetToCheckpoint();
    }

    private void OnMainMenu()
    {
        Resume();
        GameManager.Instance?.ReturnToMainMenu();
    }

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetVisible(bool v)
    {
        if (pauseMenuRoot != null) pauseMenuRoot.SetActive(v);
    }

    void OnDestroy()
    {
        CursorManager.Release(CURSOR_OWNER);
    }

    private static void SetLabel(TMP_Text t, string s)
    {
        if (t != null) t.text = s;
    }
}