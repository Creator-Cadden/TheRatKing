using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pause menu shown on Escape.
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

    [Header("Input (optional)")]
    [Tooltip("Optional pause action. If left empty, the menu falls back to the Escape " +
             "key and the gamepad Start button — so it works with ZERO wiring when " +
             "dropped into a scene as its own prefab (no PlayerInput needed).")]
    [SerializeField] private InputActionReference pauseAction;

    public bool IsPaused { get; private set; }

    private const string CURSOR_OWNER = "pause";
    private int _lastToggleFrame = -1;


    void Start()
    {
        resetButton   ?.onClick.AddListener(OnReset);
        settingsButton?.onClick.AddListener(OnSettings);
        mainMenuButton?.onClick.AddListener(OnMainMenu);

        // If the Inspector slot is empty (e.g. the TestingArena UI copy), find
        // the panel by name instead of leaving it stuck visible on screen.
        if (pauseMenuRoot == null)
            AutoFindRoot();

        // Start hidden — root must be active in Editor so children initialize
        SetVisible(false);
    }

    private void AutoFindRoot()
    {
        foreach (Transform child in transform)
        {
            string n = child.name.ToLowerInvariant();
            if (n.Contains("pause") || n.Contains("panel"))
            {
                pauseMenuRoot = child.gameObject;
                Debug.LogWarning($"[PauseMenu] pauseMenuRoot wasn't assigned — auto-found " +
                                 $"'{child.name}'. Assign it in the Inspector to silence this.");
                return;
            }
        }
        Debug.LogWarning("[PauseMenu] pauseMenuRoot not assigned and no child named " +
                         "*pause*/*panel* found — the menu can't hide itself.");
    }

    void OnDestroy()
    {
        CursorManager.Release(CURSOR_OWNER);
    }

    // ── Input — bound to Escape / Pause action via PlayerInput Send Messages ──

    public void OnPause(InputValue value)
    {
        if (!value.isPressed) return;
        Toggle();
    }

    void OnEnable()
    {
        if (pauseAction != null && pauseAction.action != null)
            pauseAction.action.Enable();
    }

    void OnDisable()
    {
        if (pauseAction != null && pauseAction.action != null)
            pauseAction.action.Disable();
    }

    // Self-contained input: the menu drives itself, so it no longer needs to live
    // on the player's PlayerInput. Runs on unscaled Update, so it still works while
    // paused (Time.timeScale = 0).
    void Update()
    {
        bool pressed = false;

        if (pauseAction != null && pauseAction.action != null)
            pressed = pauseAction.action.WasPressedThisFrame();

        if (!pressed && Keyboard.current != null)
            pressed = Keyboard.current.escapeKey.wasPressedThisFrame;

        if (!pressed && Gamepad.current != null)
            pressed = Gamepad.current.startButton.wasPressedThisFrame;

        if (pressed) Toggle();
    }

    private void Toggle()
    {
        // Guard against a double-toggle if BOTH the old PlayerInput 'Send Messages'
        // OnPause AND the direct poll fire on the same frame (can happen while the
        // menu is still parented to the player mid-transition).
        if (Time.frameCount == _lastToggleFrame) return;
        _lastToggleFrame = Time.frameCount;

        if (IsPaused) Resume();
        else          Pause();
    }

    // ── Public API ──

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

    // ── Button callbacks ──

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

    // ── Level info ──

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

    // ── Helpers ──

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
