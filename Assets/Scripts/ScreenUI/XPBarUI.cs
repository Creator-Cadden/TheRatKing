using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Player XP bar. Fades in when you gain XP and smoothly LERPS its fill toward
/// your progress to the next level; on level-up it sweeps to full, wraps to
/// empty, and keeps going (handles several level-ups landing in a row from a
/// burst of XP orbs). Fades back out after an idle beat, like the stamina bar.
///
/// Wiring: put this on an XP bar panel in your HUD Canvas.
///   • fillImage       — an Image with Image Type = Filled, Fill Method = Horizontal.
///   • backgroundImage — optional track behind the fill.
///   • levelText       — optional TMP text, shows the level number.
///   • xpText          — optional TMP text, shows "currentXP / toNext".
/// It auto-binds to the Player (by tag) at runtime — same pattern as
/// XPGainIndicator / LevelUpIndicator, so no manual player reference needed.
/// </summary>
public class XPBarUI : MonoBehaviour
{
    [Header("References")]
    public Image    fillImage;         // Image Type = Filled, Horizontal
    public Image    backgroundImage;   // optional
    public TMP_Text levelText;         // optional
    public TMP_Text xpText;            // optional

    [Header("Player")]
    public string playerTag = "Player";

    [Header("Fill Animation")]
    [Tooltip("How fast the fill catches up, in roughly 'levels per second'. " +
             "Higher = snappier. The bar eases: it moves faster the further behind it is.")]
    public float fillSpeed = 2.5f;

    [Header("Auto Fade")]
    [Tooltip("Keep the bar visible while it's still animating, plus this long after the last gain.")]
    public float holdAfterGain = 2.0f;
    public float fadeInSpeed  = 10f;
    public float fadeOutSpeed = 3f;
    [Tooltip("Turn OFF to keep the bar on screen permanently.")]
    public bool  autoFade     = true;

    [Header("Level-up flash")]
    public Color normalFill = new Color(0.34f, 0.82f, 0.42f, 1f);  // XP green — matches the orbs
    public Color flashFill  = new Color(1f, 0.86f, 0.32f, 1f);     // gold pop on level-up
    public float flashDuration = 0.5f;

    [Header("Text")]
    [Tooltip("{0} = level number.")]
    public string levelFormat = "Lv {0}";

    // ── runtime ──
    private XPSystem _xp;
    private float _shown;              // displayed progress = level + fraction
    private float _alpha;
    private float _lastGainTime = -999f;
    private float _flashEnd     = -999f;

    void Start()
    {
        BindPlayer();
        if (_xp != null) _shown = CurrentTarget();
        _alpha = autoFade ? 0f : 1f;
        ApplyAlpha(_alpha);
    }

    void OnEnable() { if (_xp == null) BindPlayer(); }

    void OnDestroy()
    {
        if (_xp != null)
        {
            _xp.onXPGained.RemoveListener(OnXPGained);
            _xp.onLevelUp.RemoveListener(OnLevelUp);
        }
    }

    private void BindPlayer()
    {
        GameObject p = GameObject.FindWithTag(playerTag);
        if (p == null) return;
        _xp = p.GetComponent<XPSystem>();
        if (_xp == null) return;

        _xp.onXPGained.RemoveListener(OnXPGained);
        _xp.onXPGained.AddListener(OnXPGained);
        _xp.onLevelUp.RemoveListener(OnLevelUp);
        _xp.onLevelUp.AddListener(OnLevelUp);
    }

    private void OnXPGained(int amount) { _lastGainTime = Time.time; }
    private void OnLevelUp()
    {
        _lastGainTime = Time.time;
        _flashEnd = Time.time + flashDuration;
    }

    // Progress expressed as level + fraction-into-this-level.
    private float CurrentTarget()
    {
        if (_xp == null) return 0f;
        int toNext = Mathf.Max(1, _xp.XPToNextLevel);
        float frac = Mathf.Clamp01((float)_xp.CurrentXP / toNext);
        return _xp.CurrentLevel + frac;
    }

    void Update()
    {
        if (_xp == null) { BindPlayer(); if (_xp == null) return; }

        float target = CurrentTarget();

        // Lerp shown → target. Monotonic increase; the fraction naturally wraps
        // 1 → 0 as it crosses each whole level, which reads as fill-then-reset.
        if (_shown < target)
        {
            float step = fillSpeed * Time.deltaTime * Mathf.Max(1f, target - _shown);
            _shown = Mathf.MoveTowards(_shown, target, step);
        }
        else
        {
            _shown = target;   // never animate backwards (e.g. after loading a save)
        }

        float fill = _shown - Mathf.Floor(_shown);
        if (Mathf.Approximately(_shown, target))
            fill = target - Mathf.Floor(target);

        if (fillImage != null) fillImage.fillAmount = fill;

        int shownLevel = Mathf.FloorToInt(_shown + 1e-4f);
        if (levelText != null) levelText.text = string.Format(levelFormat, shownLevel);
        if (xpText != null)
            xpText.text = $"{_xp.CurrentXP} / {Mathf.Max(1, _xp.XPToNextLevel)}";

        // Colour flash on level-up.
        if (fillImage != null)
        {
            float ft = Time.time < _flashEnd
                ? Mathf.Clamp01((_flashEnd - Time.time) / Mathf.Max(0.0001f, flashDuration))
                : 0f;
            Color c = Color.Lerp(normalFill, flashFill, ft);
            c.a = _alpha;
            fillImage.color = c;
        }

        // Fade in while animating or recently gained; otherwise fade out.
        bool animating = !Mathf.Approximately(_shown, target);
        float targetAlpha = !autoFade
            ? 1f
            : (animating || Time.time < _lastGainTime + holdAfterGain) ? 1f : 0f;

        float sp = targetAlpha > _alpha ? fadeInSpeed : fadeOutSpeed;
        _alpha = Mathf.MoveTowards(_alpha, targetAlpha, sp * Time.deltaTime);
        ApplyAlpha(_alpha);
    }

    private void ApplyAlpha(float a)
    {
        if (backgroundImage != null) { var c = backgroundImage.color; c.a = a; backgroundImage.color = c; }
        if (fillImage != null)       { var c = fillImage.color;       c.a = a; fillImage.color = c; }
        if (levelText != null)       { var c = levelText.color;       c.a = a; levelText.color = c; }
        if (xpText != null)          { var c = xpText.color;          c.a = a; xpText.color = c; }
    }
}
