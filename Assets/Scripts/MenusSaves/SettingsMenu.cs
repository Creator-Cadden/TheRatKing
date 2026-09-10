using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The settings panel — the one screen the game was missing entirely
/// (MainMenuUI, PauseMenu and DeathScreen all had a Settings button wired to a
/// Debug.Log stub). Reads and writes <see cref="GameSettings"/>, which handles
/// persistence and applying values to the engine.
///
/// Every reference below is OPTIONAL. Wire up only the controls you actually
/// build; the rest are skipped silently. That means you can ship an audio-only
/// settings panel today and add the video tab later without touching this file.
///
/// Usage from anywhere:  SettingsMenu.Open();
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu Instance { get; private set; }

    private const string CURSOR_OWNER = "settings";

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Root")]
    [Tooltip("Panel root. Leave ACTIVE in the editor — this script hides it in Start " +
             "so all the child components get to initialise first.")]
    public GameObject settingsRoot;

    [Tooltip("Optional. Fades the panel in/out instead of a hard cut.")]
    public CanvasGroup canvasGroup;

    [Header("Tabs (optional)")]
    [Tooltip("Tab buttons, in the same order as Tab Panels below.")]
    public Button[] tabButtons;
    [Tooltip("Tab content panels, in the same order as Tab Buttons.")]
    public GameObject[] tabPanels;

    [Header("Audio")]
    public Slider  masterVolumeSlider;
    public Slider  musicVolumeSlider;
    public Slider  sfxVolumeSlider;
    public TMP_Text masterVolumeLabel;
    public TMP_Text musicVolumeLabel;
    public TMP_Text sfxVolumeLabel;

    [Header("Video")]
    public TMP_Dropdown resolutionDropdown;
    public Toggle       fullscreenToggle;
    public Toggle       vsyncToggle;
    public TMP_Dropdown frameCapDropdown;

    [Header("Gameplay")]
    public Slider   sensitivitySlider;
    public TMP_Text sensitivityLabel;
    public Toggle   invertYToggle;
    public Slider   screenShakeSlider;
    public TMP_Text screenShakeLabel;
    public Toggle   damageNumbersToggle;
    public Toggle   tutorialHintsToggle;
    public Toggle   holdToSprintToggle;
    public Slider   hudOpacitySlider;

    [Header("Buttons")]
    public Button backButton;
    public Button resetDefaultsButton;

    [Header("Behaviour")]
    [Tooltip("Pause the game (timeScale 0) while this panel is open. Leave OFF when " +
             "the panel is opened from PauseMenu, which already paused for you.")]
    public bool pauseGameWhileOpen = false;

    [Tooltip("Fade time in seconds. 0 = instant.")]
    public float fadeDuration = 0.18f;

    // ── Private ───────────────────────────────────────────────────────────────

    private bool _isOpen;
    private bool _suppressCallbacks;          // stops SetValue() firing our own handlers
    private float _fade;
    private List<Resolution> _resolutions = new List<Resolution>();
    private readonly int[] _frameCaps = { 0, 30, 60, 120, 144, 240 };

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        if (settingsRoot == null) settingsRoot = gameObject;
    }

    void Start()
    {
        BuildResolutionList();
        BuildFrameCapList();
        HookControls();
        RefreshFromSettings();
        SelectTab(0);

        _isOpen = false;
        _fade   = 0f;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        settingsRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        CursorManager.Release(CURSOR_OWNER);
    }

    void Update()
    {
        if (canvasGroup == null || fadeDuration <= 0f) return;

        float target = _isOpen ? 1f : 0f;
        if (Mathf.Approximately(_fade, target)) return;

        _fade = Mathf.MoveTowards(_fade, target, Time.unscaledDeltaTime / fadeDuration);
        canvasGroup.alpha = _fade;

        if (!_isOpen && _fade <= 0f) settingsRoot.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Open the settings panel from anywhere. No-op if none exists in the scene.</summary>
    public static void Open()
    {
        if (Instance == null)
        {
            Debug.LogWarning("[SettingsMenu] No SettingsMenu in the scene — add the " +
                             "Settings panel to your UIs prefab.");
            return;
        }
        Instance.Show();
    }

    public static void Close() => Instance?.Hide();

    public static bool IsOpen => Instance != null && Instance._isOpen;

    public void Show()
    {
        if (_isOpen) return;
        _isOpen = true;

        RefreshFromSettings();
        settingsRoot.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.interactable   = true;
            canvasGroup.blocksRaycasts = true;
            if (fadeDuration <= 0f) { _fade = 1f; canvasGroup.alpha = 1f; }
        }

        CursorManager.Request(CURSOR_OWNER);
        if (pauseGameWhileOpen) Time.timeScale = 0f;
    }

    public void Hide()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (canvasGroup != null)
        {
            canvasGroup.interactable   = false;
            canvasGroup.blocksRaycasts = false;
            if (fadeDuration <= 0f) { _fade = 0f; canvasGroup.alpha = 0f; settingsRoot.SetActive(false); }
        }
        else
        {
            settingsRoot.SetActive(false);
        }

        CursorManager.Release(CURSOR_OWNER);
        if (pauseGameWhileOpen) Time.timeScale = 1f;
    }

    /// <summary>Hook this to a tab button's OnClick if you'd rather wire it in the inspector.</summary>
    public void SelectTab(int index)
    {
        if (tabPanels == null || tabPanels.Length == 0) return;

        for (int i = 0; i < tabPanels.Length; i++)
            if (tabPanels[i] != null) tabPanels[i].SetActive(i == index);

        if (tabButtons == null) return;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;
            tabButtons[i].interactable = i != index;   // the active tab reads as "pressed"
        }
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    private void HookControls()
    {
        // Audio
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(v => { if (!_suppressCallbacks) { GameSettings.MasterVolume = v; SetPercent(masterVolumeLabel, v); } });
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(v => { if (!_suppressCallbacks) { GameSettings.MusicVolume = v; SetPercent(musicVolumeLabel, v); } });
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(v => { if (!_suppressCallbacks) { GameSettings.SfxVolume = v; SetPercent(sfxVolumeLabel, v); } });

        // Video
        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(v => { if (!_suppressCallbacks) GameSettings.Fullscreen = v; });
        if (vsyncToggle != null)
            vsyncToggle.onValueChanged.AddListener(v => { if (!_suppressCallbacks) GameSettings.VSync = v; });
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionPicked);
        if (frameCapDropdown != null)
            frameCapDropdown.onValueChanged.AddListener(i =>
            {
                if (_suppressCallbacks) return;
                if (i >= 0 && i < _frameCaps.Length) GameSettings.FrameRateCap = _frameCaps[i];
            });

        // Gameplay
        if (sensitivitySlider != null)
            sensitivitySlider.onValueChanged.AddListener(v => { if (!_suppressCallbacks) { GameSettings.MouseSensitivity = v; SetMultiplier(sensitivityLabel, v); } });
        if (invertYToggle != null)
            invertYToggle.onValueChanged.AddListener(v => { if (!_suppressCallbacks) GameSettings.InvertLookY = v; });
        if (screenShakeSlider != null)
            screenShakeSlider.onValueChanged.AddListener(v => { if (!_suppressCallbacks) { GameSettings.ScreenShake = v; SetPercent(screenShakeLabel, v); } });
        if (damageNumbersToggle != null)
            damageNumbersToggle.onValueChanged.AddListener(v => { if (!_suppressCallbacks) GameSettings.ShowDamageNumbers = v; });
        if (tutorialHintsToggle != null)
            tutorialHintsToggle.onValueChanged.AddListener(v => { if (!_suppressCallbacks) GameSettings.ShowTutorialHints = v; });
        if (holdToSprintToggle != null)
            holdToSprintToggle.onValueChanged.AddListener(v => { if (!_suppressCallbacks) GameSettings.HoldToSprint = v; });
        if (hudOpacitySlider != null)
            hudOpacitySlider.onValueChanged.AddListener(v => { if (!_suppressCallbacks) GameSettings.HudOpacity = v; });

        // Buttons
        backButton?.onClick.AddListener(Hide);
        resetDefaultsButton?.onClick.AddListener(() =>
        {
            GameSettings.ResetToDefaults();
            RefreshFromSettings();
        });
    }

    /// <summary>Push the current GameSettings values into every control, without re-firing handlers.</summary>
    private void RefreshFromSettings()
    {
        _suppressCallbacks = true;

        SetSlider(masterVolumeSlider, GameSettings.MasterVolume, masterVolumeLabel, percent: true);
        SetSlider(musicVolumeSlider,  GameSettings.MusicVolume,  musicVolumeLabel,  percent: true);
        SetSlider(sfxVolumeSlider,    GameSettings.SfxVolume,    sfxVolumeLabel,    percent: true);

        if (fullscreenToggle != null) fullscreenToggle.isOn = GameSettings.Fullscreen;
        if (vsyncToggle      != null) vsyncToggle.isOn      = GameSettings.VSync;

        if (resolutionDropdown != null)
        {
            Vector2Int cur = GameSettings.Resolution;
            int idx = _resolutions.FindIndex(r => r.width == cur.x && r.height == cur.y);
            resolutionDropdown.value = Mathf.Max(0, idx);
            resolutionDropdown.RefreshShownValue();
        }

        if (frameCapDropdown != null)
        {
            int idx = System.Array.IndexOf(_frameCaps, GameSettings.FrameRateCap);
            frameCapDropdown.value = Mathf.Max(0, idx);
            frameCapDropdown.RefreshShownValue();
        }

        SetSlider(sensitivitySlider, GameSettings.MouseSensitivity, sensitivityLabel, percent: false);
        SetSlider(screenShakeSlider, GameSettings.ScreenShake,      screenShakeLabel, percent: true);
        SetSlider(hudOpacitySlider,  GameSettings.HudOpacity,       null,             percent: true);

        if (invertYToggle        != null) invertYToggle.isOn        = GameSettings.InvertLookY;
        if (damageNumbersToggle  != null) damageNumbersToggle.isOn  = GameSettings.ShowDamageNumbers;
        if (tutorialHintsToggle  != null) tutorialHintsToggle.isOn  = GameSettings.ShowTutorialHints;
        if (holdToSprintToggle   != null) holdToSprintToggle.isOn   = GameSettings.HoldToSprint;

        _suppressCallbacks = false;
    }

    private void OnResolutionPicked(int index)
    {
        if (_suppressCallbacks) return;
        if (index < 0 || index >= _resolutions.Count) return;
        Resolution r = _resolutions[index];
        GameSettings.Resolution = new Vector2Int(r.width, r.height);
    }

    private void BuildResolutionList()
    {
        _resolutions.Clear();
        if (resolutionDropdown == null) return;

        // Deduplicate — Unity reports the same size once per refresh rate.
        var seen = new HashSet<long>();
        foreach (Resolution r in Screen.resolutions)
        {
            long key = ((long)r.width << 32) | (uint)r.height;
            if (seen.Add(key)) _resolutions.Add(r);
        }

        var options = new List<string>(_resolutions.Count);
        foreach (Resolution r in _resolutions)
            options.Add($"{r.width} × {r.height}");

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(options);
    }

    private void BuildFrameCapList()
    {
        if (frameCapDropdown == null) return;

        var options = new List<string>(_frameCaps.Length);
        foreach (int cap in _frameCaps)
            options.Add(cap == 0 ? "Unlimited" : cap.ToString());

        frameCapDropdown.ClearOptions();
        frameCapDropdown.AddOptions(options);
    }

    // ── Small helpers ─────────────────────────────────────────────────────────

    private void SetSlider(Slider s, float value, TMP_Text label, bool percent)
    {
        if (s == null) return;
        s.value = value;
        if (label == null) return;
        if (percent) SetPercent(label, value);
        else         SetMultiplier(label, value);
    }

    private static void SetPercent(TMP_Text label, float v)
    {
        if (label != null) label.text = Mathf.RoundToInt(v * 100f) + "%";
    }

    private static void SetMultiplier(TMP_Text label, float v)
    {
        if (label != null) label.text = v.ToString("0.0") + "×";
    }
}
