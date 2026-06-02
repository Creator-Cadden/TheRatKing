using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

/// <summary>
/// Screen-space boss health bar.
///
/// Sits on the MainUI canvas. Auto-finds a FatRatBoss in the active scene
/// (or any boss tagged with the matching boss tag, or whatever you drag
/// into Target Boss). Fades in when the boss first takes damage by default,
/// stays visible while the boss is alive, then fades out a moment after
/// the boss dies.
///
/// Setup:
///   1. On the MainUI canvas, create a hierarchy like:
///        BossHealthBarRoot         (RectTransform + CanvasGroup, anchored
///                                   top-center or wherever you want it)
///          ├── Background          (Image — dark backdrop, optional)
///          ├── Fill                (Image, Type = Filled, Fill Method = Horizontal)
///          ├── Border              (Image — optional decoration)
///          ├── NameLabel           (TMP_Text — "Fat King")
///          └── HpLabel             (TMP_Text — "230 / 500", optional)
///   2. Add this component on BossHealthBarRoot. Drag Fill / NameLabel /
///      HpLabel into the Inspector. The CanvasGroup is auto-found on the
///      root (or one will be added).
///   3. Set Boss Display Name to whatever name you want to show.
///   4. Leave Target Boss null to auto-find — works for any scene that
///      contains exactly one FatRatBoss.
///   5. The HealthBarPivot inside the boss prefab can be removed/disabled.
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    public enum ShowMode
    {
        [InspectorName("Wait for cutscene PlayIntro() / Manual Show()")]
        Manual,
        [InspectorName("On first damage")]
        OnFirstDamage,
        [InspectorName("Immediately on scene start")]
        OnStart,
    }

    // ─────────────────────────────────────────
    [Header("UI References")]
    [Tooltip("CanvasGroup on this GameObject — controls overall fade. " +
             "Auto-added if missing.")]
    public CanvasGroup canvasGroup;

    [Tooltip("The fill bar — must be Image Type = Filled, Fill Method = Horizontal.")]
    public Image fillImage;

    [Tooltip("Boss name text.")]
    public TMP_Text nameLabel;

    [Tooltip("Optional 'currentHP / maxHP' text. Leave null to hide.")]
    public TMP_Text hpLabel;

    // ─────────────────────────────────────────
    [Header("Display")]
    [Tooltip("Name shown on the bar. e.g. 'Fat King', 'Captain', 'The Ratlord'.")]
    public string bossDisplayName = "Fat Rat Boss";

    [Tooltip("Format for the HP label. {0} = current, {1} = max. " +
             "Examples: '{0} / {1}', '{0}', 'HP {0}/{1}'.")]
    public string hpLabelFormat = "{0} / {1}";

    [Tooltip("Color the bar tints at full HP.")]
    public Color fullColor = new Color(0.85f, 0.15f, 0.15f);

    [Tooltip("Color the bar tints when nearly empty.")]
    public Color lowColor  = new Color(0.95f, 0.6f, 0.1f);

    [Tooltip("How fast the displayed fill catches up to the actual HP. " +
             "Higher = snappier. 0 = instant.")]
    public float lerpSpeed = 6f;

    // ─────────────────────────────────────────
    [Header("Show / Hide")]
    public ShowMode showMode = ShowMode.Manual;

    [Tooltip("Seconds for the fade-in animation.")]
    public float fadeInDuration = 0.5f;

    [Tooltip("Seconds for the fade-out animation.")]
    public float fadeOutDuration = 0.8f;

    [Tooltip("Wait this many seconds AFTER the boss dies before starting the fade-out.")]
    public float hideAfterDeathDelay = 1.5f;

    [Tooltip("Default duration for PlayIntro() if it's called without a parameter. " +
             "Time it takes the bar's fill to grow from empty to current HP.")]
    public float introGrowDuration = 1.5f;

    // ─────────────────────────────────────────
    [Header("Boss Discovery")]
    [Tooltip("Drag the boss's EntityStats here directly, OR leave null to auto-find " +
             "the first FatRatBoss in the active scene at Start (and after each scene load).")]
    public EntityStats targetBoss;

    [Tooltip("Verbose logging while debugging.")]
    public bool verbose = false;

    // ─────────────────────────────────────────
    // Runtime state
    // ─────────────────────────────────────────
    private float     _targetFill    = 1f;
    private float     _displayedFill = 1f;
    private bool      _isShown;
    private bool      _introPlaying;     // true during the PlayIntro grow animation
    private Coroutine _fadeRoutine;
    private Coroutine _introRoutine;

    // ─────────────────────────────────────────

    void Awake()
    {
        // Safety: if the user assigned a CanvasGroup that lives on a DIFFERENT
        // GameObject (typically the whole UI root), using it here would hide
        // the entire UI when we set alpha = 0. Detect that and fall back to
        // a CanvasGroup on this GameObject so only THIS bar fades.
        if (canvasGroup != null && canvasGroup.gameObject != gameObject)
        {
            Debug.LogWarning(
                $"[BossHealthBarUI] '{name}' had its Canvas Group field set to a " +
                $"CanvasGroup on '{canvasGroup.gameObject.name}', which would hide " +
                $"that whole hierarchy (not just this bar). Falling back to a local " +
                $"CanvasGroup on this GameObject instead. Clear the field in the " +
                $"Inspector or assign one that lives on this GameObject.", this);
            canvasGroup = null;
        }

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha          = 0f;
        canvasGroup.interactable   = false;
        canvasGroup.blocksRaycasts = false;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        TryBindBoss();
    }

    void OnDestroy()
    {
        Unbind();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Each scene gets a fresh boss instance — re-find and re-bind.
        Unbind();
        targetBoss = null;
        _isShown   = false;
        canvasGroup.alpha = 0f;
        TryBindBoss();
    }

    private void TryBindBoss()
    {
        if (targetBoss == null) AutoFindBoss();
        if (targetBoss == null)
        {
            if (verbose) Debug.Log("[BossHealthBarUI] No boss in scene — staying hidden.");
            return;
        }

        // Subscribe to the boss's stats events
        targetBoss.onDamageTaken.RemoveListener(OnBossDamaged);
        targetBoss.onDeath.RemoveListener(OnBossDied);
        targetBoss.onDamageTaken.AddListener(OnBossDamaged);
        targetBoss.onDeath.AddListener(OnBossDied);

        // Seed the bar at full
        _targetFill   = GetFillRatio();
        _displayedFill = _targetFill;
        ApplyFill(_displayedFill);
        ApplyText();

        if (showMode == ShowMode.OnStart) Show();
    }

    private void Unbind()
    {
        if (targetBoss == null) return;
        targetBoss.onDamageTaken.RemoveListener(OnBossDamaged);
        targetBoss.onDeath.RemoveListener(OnBossDied);
    }

    private void AutoFindBoss()
    {
        var boss = FindFirstObjectByType<FatRatBoss>(FindObjectsInactive.Exclude);
        if (boss == null) return;
        targetBoss = boss.GetComponent<EntityStats>();
        if (verbose && targetBoss != null)
            Debug.Log($"[BossHealthBarUI] Auto-bound to '{boss.name}'.");
    }

    // ─────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────

    /// <summary>Manually fade the bar in (useful with ShowMode.Manual).</summary>
    public void Show()
    {
        if (_isShown) return;
        _isShown = true;

        if (verbose) Debug.Log("[BossHealthBarUI] Showing.");
        if (nameLabel != null) nameLabel.text = bossDisplayName;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeTo(1f, fadeInDuration));
    }

    /// <summary>
    /// Cutscene-friendly intro. Fades the bar in AND animates its fill
    /// from 0 up to the boss's current HP fraction over <paramref name="growDuration"/>
    /// seconds. After the intro the bar STAYS visible and tracks HP normally.
    ///
    /// Call this from your cutscene controller / Timeline signal / boss
    /// arena trigger at the moment the player gains control.
    /// </summary>
    public void PlayIntro(float growDuration)
    {
        if (verbose) Debug.Log($"[BossHealthBarUI] PlayIntro({growDuration}).");

        // If the boss reference isn't bound yet (e.g. cutscene starts before
        // boss spawns), try once more.
        if (targetBoss == null) TryBindBoss();

        // Reset the displayed fill so the intro starts from 0.
        _displayedFill = 0f;
        ApplyFill(0f);
        ApplyText();

        // Mark as shown so Update() drives the fill once intro is done.
        _isShown = true;
        if (nameLabel != null) nameLabel.text = bossDisplayName;

        // Fade in the canvas group.
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeTo(1f, fadeInDuration));

        // Grow the fill from 0 to the actual HP fraction over growDuration.
        if (_introRoutine != null) StopCoroutine(_introRoutine);
        _introRoutine = StartCoroutine(IntroGrowRoutine(growDuration));
    }

    /// <summary>Calls <see cref="PlayIntro(float)"/> with the Inspector's introGrowDuration.</summary>
    public void PlayIntro() => PlayIntro(introGrowDuration);

    private IEnumerator IntroGrowRoutine(float duration)
    {
        _introPlaying = true;

        float endFill = GetFillRatio();
        if (duration <= 0f)
        {
            _displayedFill = endFill;
            _introPlaying  = false;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float lerp = Mathf.Clamp01(t / duration);
            // Use SmoothStep so the grow eases out at the top — feels punchier.
            lerp = Mathf.SmoothStep(0f, 1f, lerp);
            _displayedFill = endFill * lerp;
            ApplyFill(_displayedFill);
            ApplyText();
            yield return null;
        }
        _displayedFill = endFill;
        _introPlaying  = false;
    }

    /// <summary>Manually fade the bar out.</summary>
    public void Hide()
    {
        if (!_isShown && canvasGroup.alpha <= 0.001f) return;
        _isShown = false;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeTo(0f, fadeOutDuration));
    }

    // ─────────────────────────────────────────
    // Per-frame fill lerp + text update
    // ─────────────────────────────────────────

    void Update()
    {
        if (!_isShown || targetBoss == null) return;

        // While the intro grow animation is playing, IntroGrowRoutine owns
        // the fill — don't double-drive it here.
        if (_introPlaying) return;

        if (lerpSpeed <= 0f) _displayedFill = _targetFill;
        else _displayedFill = Mathf.Lerp(_displayedFill, _targetFill,
                                          Time.deltaTime * lerpSpeed);

        ApplyFill(_displayedFill);
        ApplyText();
    }

    private void ApplyFill(float fraction)
    {
        if (fillImage == null) return;
        fillImage.fillAmount = fraction;
        fillImage.color      = Color.Lerp(lowColor, fullColor, fraction);
    }

    private void ApplyText()
    {
        if (hpLabel == null || targetBoss == null) return;
        hpLabel.text = string.Format(hpLabelFormat,
                                     Mathf.Max(0, targetBoss.CurrentHealth),
                                     targetBoss.MaxHealth);
    }

    // ─────────────────────────────────────────
    // Event handlers from boss stats
    // ─────────────────────────────────────────

    private void OnBossDamaged(int _)
    {
        _targetFill = GetFillRatio();

        if (!_isShown && showMode == ShowMode.OnFirstDamage)
            Show();
    }

    private void OnBossDied()
    {
        _targetFill = 0f;
        StartCoroutine(HideAfterDeathRoutine());
    }

    private IEnumerator HideAfterDeathRoutine()
    {
        yield return new WaitForSeconds(hideAfterDeathDelay);
        Hide();
    }

    // ─────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────

    private float GetFillRatio()
    {
        if (targetBoss == null || targetBoss.MaxHealth <= 0) return 1f;
        return Mathf.Clamp01((float)targetBoss.CurrentHealth / targetBoss.MaxHealth);
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        float start = canvasGroup.alpha;
        if (duration <= 0f)
        {
            canvasGroup.alpha = target;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        canvasGroup.alpha = target;
    }
}
