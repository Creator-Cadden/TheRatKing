using System;
using UnityEngine;

/// <summary>
/// Every player-facing setting, persisted in PlayerPrefs and applied globally.
///
/// Nothing in the game should read PlayerPrefs directly — read these properties.
/// Setting one saves it, applies it, and fires <see cref="OnChanged"/> so any
/// listening system (camera shake, HUD opacity, audio) can react immediately.
///
/// Call <see cref="LoadAndApply"/> once at boot (GameManager.Awake is the right
/// place) so a fresh launch honours what the player picked last session.
/// </summary>
public static class GameSettings
{
    // ── Keys ──────────────────────────────────────────────────────────────────
    private const string K_MASTER      = "rk_vol_master";
    private const string K_MUSIC       = "rk_vol_music";
    private const string K_SFX         = "rk_vol_sfx";
    private const string K_FULLSCREEN  = "rk_fullscreen";
    private const string K_RES_W       = "rk_res_w";
    private const string K_RES_H       = "rk_res_h";
    private const string K_VSYNC       = "rk_vsync";
    private const string K_FPSCAP      = "rk_fpscap";
    private const string K_SENS        = "rk_sensitivity";
    private const string K_INVERT_Y    = "rk_invert_y";
    private const string K_SHAKE       = "rk_shake";
    private const string K_DMGNUM      = "rk_damage_numbers";
    private const string K_HINTS       = "rk_tutorial_hints";
    private const string K_HUD_OPACITY = "rk_hud_opacity";
    private const string K_HOLD_SPRINT = "rk_hold_sprint";

    /// <summary>Fires after ANY setting changes. Systems poll the properties they care about.</summary>
    public static event Action OnChanged;

    private static bool _loaded;

    // ── Audio ─────────────────────────────────────────────────────────────────

    private static float _master = 1f, _music = 0.7f, _sfx = 1f;

    /// <summary>0–1. Applied straight to AudioListener.volume, so it scales everything.</summary>
    public static float MasterVolume
    {
        get { EnsureLoaded(); return _master; }
        set { _master = Mathf.Clamp01(value); Save(K_MASTER, _master); ApplyAudio(); Changed(); }
    }

    /// <summary>
    /// 0–1. Broadcast for AudioManager to honour on its looping music source.
    /// NOTE: one-shot SFX are not routed through a mixer yet — see the build plan's
    /// "AudioMixer" step if you want per-bus control that covers everything.
    /// </summary>
    public static float MusicVolume
    {
        get { EnsureLoaded(); return _music; }
        set { _music = Mathf.Clamp01(value); Save(K_MUSIC, _music); Changed(); }
    }

    /// <summary>0–1. Broadcast for AudioManager / sound emitters to scale by.</summary>
    public static float SfxVolume
    {
        get { EnsureLoaded(); return _sfx; }
        set { _sfx = Mathf.Clamp01(value); Save(K_SFX, _sfx); Changed(); }
    }

    // ── Video ─────────────────────────────────────────────────────────────────

    private static bool _fullscreen = true;
    private static int  _resW, _resH;
    private static bool _vsync = true;
    private static int  _fpsCap = 0;   // 0 = uncapped

    public static bool Fullscreen
    {
        get { EnsureLoaded(); return _fullscreen; }
        set { _fullscreen = value; Save(K_FULLSCREEN, value ? 1 : 0); ApplyVideo(); Changed(); }
    }

    public static Vector2Int Resolution
    {
        get { EnsureLoaded(); return new Vector2Int(_resW, _resH); }
        set
        {
            _resW = Mathf.Max(320, value.x);
            _resH = Mathf.Max(240, value.y);
            Save(K_RES_W, _resW);
            Save(K_RES_H, _resH);
            ApplyVideo();
            Changed();
        }
    }

    public static bool VSync
    {
        get { EnsureLoaded(); return _vsync; }
        set { _vsync = value; Save(K_VSYNC, value ? 1 : 0); ApplyVideo(); Changed(); }
    }

    /// <summary>0 = uncapped. Ignored while VSync is on.</summary>
    public static int FrameRateCap
    {
        get { EnsureLoaded(); return _fpsCap; }
        set { _fpsCap = Mathf.Max(0, value); Save(K_FPSCAP, _fpsCap); ApplyVideo(); Changed(); }
    }

    // ── Gameplay ──────────────────────────────────────────────────────────────

    private static float _sens = 1f;
    private static bool  _invertY, _damageNumbers = true, _hints = true, _holdSprint = true;
    private static float _shake = 1f, _hudOpacity = 1f;

    /// <summary>Camera look multiplier. CamRig / Cinemachine should multiply by this.</summary>
    public static float MouseSensitivity
    {
        get { EnsureLoaded(); return _sens; }
        set { _sens = Mathf.Clamp(value, 0.1f, 5f); Save(K_SENS, _sens); Changed(); }
    }

    public static bool InvertLookY
    {
        get { EnsureLoaded(); return _invertY; }
        set { _invertY = value; Save(K_INVERT_Y, value ? 1 : 0); Changed(); }
    }

    /// <summary>0 = off, 1 = full. CameraJuice should scale its shake by this. Accessibility-critical.</summary>
    public static float ScreenShake
    {
        get { EnsureLoaded(); return _shake; }
        set { _shake = Mathf.Clamp01(value); Save(K_SHAKE, _shake); Changed(); }
    }

    public static bool ShowDamageNumbers
    {
        get { EnsureLoaded(); return _damageNumbers; }
        set { _damageNumbers = value; Save(K_DMGNUM, value ? 1 : 0); Changed(); }
    }

    public static bool ShowTutorialHints
    {
        get { EnsureLoaded(); return _hints; }
        set { _hints = value; Save(K_HINTS, value ? 1 : 0); Changed(); }
    }

    /// <summary>0.3–1. Multiplies the HUD CanvasGroup alpha for players who want it faint.</summary>
    public static float HudOpacity
    {
        get { EnsureLoaded(); return _hudOpacity; }
        set { _hudOpacity = Mathf.Clamp(value, 0.3f, 1f); Save(K_HUD_OPACITY, _hudOpacity); Changed(); }
    }

    /// <summary>True = hold Shift to sprint. False = tap to toggle. Accessibility.</summary>
    public static bool HoldToSprint
    {
        get { EnsureLoaded(); return _holdSprint; }
        set { _holdSprint = value; Save(K_HOLD_SPRINT, value ? 1 : 0); Changed(); }
    }

    // ── Load / apply / reset ──────────────────────────────────────────────────

    /// <summary>Read everything from disk and push it to the engine. Call once at boot.</summary>
    public static void LoadAndApply()
    {
        Load();
        ApplyAudio();
        ApplyVideo();
        Changed();
    }

    private static void EnsureLoaded()
    {
        if (!_loaded) Load();
    }

    private static void Load()
    {
        _loaded = true;   // set first so property getters below don't recurse

        _master     = PlayerPrefs.GetFloat(K_MASTER, 1f);
        _music      = PlayerPrefs.GetFloat(K_MUSIC,  0.7f);
        _sfx        = PlayerPrefs.GetFloat(K_SFX,    1f);

        _fullscreen = PlayerPrefs.GetInt(K_FULLSCREEN, 1) == 1;
        _resW       = PlayerPrefs.GetInt(K_RES_W, Screen.currentResolution.width);
        _resH       = PlayerPrefs.GetInt(K_RES_H, Screen.currentResolution.height);
        _vsync      = PlayerPrefs.GetInt(K_VSYNC, 1) == 1;
        _fpsCap     = PlayerPrefs.GetInt(K_FPSCAP, 0);

        _sens          = PlayerPrefs.GetFloat(K_SENS, 1f);
        _invertY       = PlayerPrefs.GetInt(K_INVERT_Y, 0) == 1;
        _shake         = PlayerPrefs.GetFloat(K_SHAKE, 1f);
        _damageNumbers = PlayerPrefs.GetInt(K_DMGNUM, 1) == 1;
        _hints         = PlayerPrefs.GetInt(K_HINTS, 1) == 1;
        _hudOpacity    = PlayerPrefs.GetFloat(K_HUD_OPACITY, 1f);
        _holdSprint    = PlayerPrefs.GetInt(K_HOLD_SPRINT, 1) == 1;
    }

    /// <summary>Wipe every saved setting back to the defaults above.</summary>
    public static void ResetToDefaults()
    {
        foreach (string key in new[]
        {
            K_MASTER, K_MUSIC, K_SFX, K_FULLSCREEN, K_RES_W, K_RES_H, K_VSYNC,
            K_FPSCAP, K_SENS, K_INVERT_Y, K_SHAKE, K_DMGNUM, K_HINTS,
            K_HUD_OPACITY, K_HOLD_SPRINT
        })
        {
            PlayerPrefs.DeleteKey(key);
        }
        PlayerPrefs.Save();
        _loaded = false;
        LoadAndApply();
    }

    private static void ApplyAudio()
    {
        AudioListener.volume = _master;
    }

    private static void ApplyVideo()
    {
        FullScreenMode mode = _fullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;

        if (_resW > 0 && _resH > 0)
            Screen.SetResolution(_resW, _resH, mode);
        else
            Screen.fullScreenMode = mode;

        QualitySettings.vSyncCount  = _vsync ? 1 : 0;
        Application.targetFrameRate = _vsync ? -1 : (_fpsCap > 0 ? _fpsCap : -1);
    }

    private static void Save(string key, float v)
    {
        PlayerPrefs.SetFloat(key, v);
        PlayerPrefs.Save();
    }

    private static void Save(string key, int v)
    {
        PlayerPrefs.SetInt(key, v);
        PlayerPrefs.Save();
    }

    private static void Changed() => OnChanged?.Invoke();
}
