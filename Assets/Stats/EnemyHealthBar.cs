using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to a child GameObject on the enemy (e.g. "HealthBarPivot").
/// Requires:
///   - A child Canvas set to World Space (assign via Inspector or auto-created below)
///   - EntityStats on the parent enemy
///
/// Setup in hierarchy:
///   Enemy
///   └── HealthBarPivot          ← this script lives here
///       └── Canvas (World Space)
///           ├── Background      (Image, dark)
///           ├── Fill            (Image, green → red gradient via colour lerp)
///           └── HPLabel         (TextMeshProUGUI, optional)
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The UI Image used as the HP fill. Assign in Inspector.")]
    public Image fillImage;

    [Tooltip("Optional TMP label showing current / max HP.")]
    public TMP_Text hpLabel;

    [Header("Appearance")]
    [Tooltip("Height above the enemy's origin in world units.")]
    public float heightOffset = 2.2f;

    [Tooltip("How fast the bar lerps to the new value (visual smoothing).")]
    public float lerpSpeed = 8f;

    [Tooltip("Hide the bar when the enemy is at full health.")]
    public bool hideWhenFull = true;

    [Tooltip("Seconds to keep the bar visible after taking damage before it fades.")]
    public float visibleDuration = 3f;

    [Header("Colours")]
    public Color fullColour    = new Color(0.27f, 0.72f, 0.18f); // green
    public Color halfColour    = new Color(0.96f, 0.74f, 0.10f); // amber
    public Color criticalColour = new Color(0.85f, 0.15f, 0.15f); // red
    public Color backgroundColour = new Color(0f, 0f, 0f, 0.8f);

    [Header("Layout")]
    public Vector2 barSize = new Vector2(120f, 16f);
    public float labelFontSize = 11f;
    public bool forceBoxStyle = true;

    // ── Private ──
    private EntityStats _stats;
    private Camera      _cam;
    private float       _displayedFill;   // 0-1, smoothed
    private float       _targetFill;
    private float       _hideTimer;
    private CanvasGroup _canvasGroup;
    private Image       _backgroundImage;
    private static Sprite _squareSprite;

    void Awake()
    {
        // Walk up to find EntityStats on the parent enemy
        _stats = GetComponentInParent<EntityStats>();
        if (_stats == null)
            Debug.LogError("[EnemyHealthBar] No EntityStats found in parent hierarchy.");

        _canvasGroup = GetComponentInChildren<CanvasGroup>();
        if (_canvasGroup == null)
        {
            // Auto-add one to the Canvas child if missing
            Canvas c = GetComponentInChildren<Canvas>();
            if (c != null) _canvasGroup = c.gameObject.AddComponent<CanvasGroup>();
        }

        _cam = Camera.main;
        _backgroundImage = transform.Find("Canvas/Background")?.GetComponent<Image>();
    }

    void OnEnable()
    {
        if (_stats != null)
        {
            _stats.onDamageTaken.AddListener(OnDamageTaken);
            _stats.onHeal.AddListener(OnHealed);
            _stats.onDeath.AddListener(OnDeath);
        }
    }

    void OnDisable()
    {
        if (_stats != null)
        {
            _stats.onDamageTaken.RemoveListener(OnDamageTaken);
            _stats.onHeal.RemoveListener(OnHealed);
            _stats.onDeath.RemoveListener(OnDeath);
        }
    }

    void Start()
    {
        // Snap to current HP immediately — no lerp flash on spawn
        _targetFill   = GetFillRatio();
        _displayedFill = _targetFill;
        ApplyLayoutStyle();
        RefreshBar(snap: true);

        if (hideWhenFull && Mathf.Approximately(_targetFill, 1f))
            SetVisible(false, instant: true);
    }

    void LateUpdate()
    {
        // ── Billboard: face the camera ───────────────────────────────
        if (_cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.transform.position);

        // ── Keep position above enemy origin (works even if enemy moves) ─
        if (transform.parent != null)
            transform.position = transform.parent.position + Vector3.up * heightOffset;

        // ── Smooth fill ──────────────────────────────────────────────
        _displayedFill = Mathf.Lerp(_displayedFill, _targetFill, Time.deltaTime * lerpSpeed);
        RefreshBar(snap: false);

        // ── Auto-hide after visibleDuration ─────────────────────────
        if (_hideTimer > 0f)
        {
            _hideTimer -= Time.deltaTime;
            if (_hideTimer <= 0f && hideWhenFull && Mathf.Approximately(_targetFill, 1f))
                SetVisible(false, instant: false);
        }

        // ── Fade alpha ───────────────────────────────────────────────
        if (_canvasGroup != null)
        {
            float target = (_canvasGroup.alpha < 0.99f && _hideTimer <= 0f) ? 0f : 1f;
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, target, Time.deltaTime * 4f);
        }
    }

    // ─────────────────────────────────────────
    // Event callbacks
    // ─────────────────────────────────────────

    private void OnDamageTaken(int _)
    {
        _targetFill = GetFillRatio();
        SetVisible(true, instant: false);
        _hideTimer = visibleDuration;
    }

    private void OnHealed(int _)
    {
        _targetFill = GetFillRatio();
        SetVisible(true, instant: false);
        _hideTimer = visibleDuration;
    }

    private void OnDeath()
    {
        SetVisible(false, instant: false);
    }

    // ─────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────

    private float GetFillRatio()
    {
        if (_stats == null || _stats.MaxHealth <= 0) return 1f;
        return Mathf.Clamp01((float)_stats.CurrentHealth / _stats.MaxHealth);
    }

    private void RefreshBar(bool snap)
    {
        float v = snap ? _targetFill : _displayedFill;

        if (fillImage != null)
        {
            fillImage.fillAmount = v;
            fillImage.color = v > 0.5f
                ? Color.Lerp(halfColour,    fullColour,    (v - 0.5f) * 2f)
                : Color.Lerp(criticalColour, halfColour,   v * 2f);
        }

        if (hpLabel != null && _stats != null)
            hpLabel.text = $"{_stats.CurrentHealth} / {_stats.MaxHealth}";
    }

    private void SetVisible(bool visible, bool instant)
    {
        if (_canvasGroup == null) return;
        if (instant)
            _canvasGroup.alpha = visible ? 1f : 0f;
        // Gradual fade is handled in LateUpdate
        if (visible) _canvasGroup.alpha = 1f;
    }

    private void ApplyLayoutStyle()
    {
        if (!forceBoxStyle) return;

        Sprite square = GetSquareSprite();

        if (_backgroundImage != null)
        {
            _backgroundImage.type = Image.Type.Simple;
            _backgroundImage.sprite = square;
            _backgroundImage.color = backgroundColour;
            RectTransform bgRt = _backgroundImage.rectTransform;
            bgRt.anchorMin = new Vector2(0.5f, 0.5f);
            bgRt.anchorMax = new Vector2(0.5f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.sizeDelta = barSize;
            bgRt.anchoredPosition = Vector2.zero;
        }

        if (fillImage != null)
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.sprite = square;
            RectTransform fillRt = fillImage.rectTransform;
            fillRt.anchorMin = new Vector2(0.5f, 0.5f);
            fillRt.anchorMax = new Vector2(0.5f, 0.5f);
            fillRt.pivot = new Vector2(0.5f, 0.5f);
            fillRt.sizeDelta = new Vector2(barSize.x - 4f, barSize.y - 4f);
            fillRt.anchoredPosition = Vector2.zero;
        }

        if (hpLabel != null)
        {
            RectTransform labelRt = hpLabel.rectTransform;
            labelRt.anchorMin = new Vector2(0.5f, 0.5f);
            labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.pivot = new Vector2(0.5f, 0.5f);
            labelRt.sizeDelta = barSize;
            labelRt.anchoredPosition = Vector2.zero;
            hpLabel.alignment = TextAlignmentOptions.Center;
            hpLabel.fontSize = labelFontSize;
            hpLabel.enableAutoSizing = false;
            hpLabel.color = Color.white;
            hpLabel.overflowMode = TextOverflowModes.Overflow;
        }
    }

    private static Sprite GetSquareSprite()
    {
        if (_squareSprite != null) return _squareSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        _squareSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        _squareSprite.name = "EnemyHealthBarSquareSprite";
        return _squareSprite;
    }
}