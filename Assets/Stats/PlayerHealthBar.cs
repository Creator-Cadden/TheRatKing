using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Screen-space player health bar — matches EnemyHealthBar style.
///
/// Hierarchy (on your HUD Canvas):
///   Canvas (Screen Space — Overlay)
///   └── PlayerHealthBarRoot        ← attach this script here
///       ├── Background             (Image)
///       ├── Fill                   (Image, Type: Filled, Horizontal)
///       └── HPLabel                (TMP_Text, optional)
///
/// OR let forceBoxStyle build the whole layout automatically in Start().
/// </summary>
public class PlayerHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Fill image — assign in Inspector or let forceBoxStyle create it.")]
    public Image fillImage;

    [Tooltip("Optional TMP label showing current / max HP.")]
    public TMP_Text hpLabel;

    [Tooltip("Leave null — auto-found via tag 'Player'.")]
    public EntityStats playerStats;

    [Header("Appearance")]
    [Tooltip("How fast the bar lerps to the new value.")]
    public float lerpSpeed = 8f;

    [Header("Colours")]  // identical palette to EnemyHealthBar
    public Color fullColour      = new Color(0.27f, 0.72f, 0.18f);
    public Color halfColour      = new Color(0.96f, 0.74f, 0.10f);
    public Color criticalColour  = new Color(0.85f, 0.15f, 0.15f);
    public Color backgroundColour = new Color(0f, 0f, 0f, 0.8f);

    [Header("Layout")]
    public Vector2 barSize      = new Vector2(220f, 20f);
    public float   labelFontSize = 12f;
    public bool    forceBoxStyle = true;

    // ── Private ──
    private float  _displayedFill;
    private float  _targetFill;
    private Image  _backgroundImage;
    private static Sprite _squareSprite;

    // ─────────────────────────────────────────
    void Start()
    {
        // Auto-locate player stats
        if (playerStats == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerStats = player.GetComponent<EntityStats>();
        }

        if (playerStats == null)
        {
            Debug.LogError("[PlayerHealthBar] No EntityStats found on Player!");
            return;
        }

        // Hook events — same pattern as EnemyHealthBar
        playerStats.onDamageTaken.AddListener(OnDamageTaken);
        playerStats.onHeal.AddListener(OnHealed);
        playerStats.onDeath.AddListener(OnDeath);

        // Cache background reference
        Transform bgTransform = transform.Find("Background");
        if (bgTransform != null)
            _backgroundImage = bgTransform.GetComponent<Image>();

        if (forceBoxStyle) ApplyLayoutStyle();

        // Snap to current HP — no lerp flash on scene load
        _targetFill    = GetFillRatio();
        _displayedFill = _targetFill;
        RefreshBar(snap: true);
    }

    void OnDestroy()
    {
        if (playerStats == null) return;
        playerStats.onDamageTaken.RemoveListener(OnDamageTaken);
        playerStats.onHeal.RemoveListener(OnHealed);
        playerStats.onDeath.RemoveListener(OnDeath);
    }

    // ─────────────────────────────────────────
    void Update()
    {
        if (playerStats == null) return;

        _displayedFill = Mathf.Lerp(_displayedFill, _targetFill, Time.deltaTime * lerpSpeed);
        RefreshBar(snap: false);
    }

    // ─────────────────────────────────────────
    // Event callbacks
    // ─────────────────────────────────────────

    private void OnDamageTaken(int _) => _targetFill = GetFillRatio();
    private void OnHealed(int _)      => _targetFill = GetFillRatio();
    private void OnDeath()            => _targetFill = 0f;

    // ─────────────────────────────────────────
    // Helpers — identical logic to EnemyHealthBar
    // ─────────────────────────────────────────

    private float GetFillRatio()
    {
        if (playerStats == null || playerStats.MaxHealth <= 0) return 1f;
        return Mathf.Clamp01((float)playerStats.CurrentHealth / playerStats.MaxHealth);
    }

    private void RefreshBar(bool snap)
    {
        float v = snap ? _targetFill : _displayedFill;

        if (fillImage != null)
        {
            fillImage.fillAmount = v;
            fillImage.color = v > 0.5f
                ? Color.Lerp(halfColour,     fullColour,    (v - 0.5f) * 2f)
                : Color.Lerp(criticalColour,  halfColour,   v * 2f);
        }

        if (hpLabel != null && playerStats != null)
            hpLabel.text = $"{playerStats.CurrentHealth} / {playerStats.MaxHealth}";
    }

    private void ApplyLayoutStyle()
    {
        Sprite square = GetSquareSprite();

        if (_backgroundImage != null)
        {
            _backgroundImage.type   = Image.Type.Simple;
            _backgroundImage.sprite = square;
            _backgroundImage.color  = backgroundColour;

            RectTransform bgRt  = _backgroundImage.rectTransform;
            bgRt.anchorMin      = new Vector2(0f, 0f);
            bgRt.anchorMax      = new Vector2(0f, 0f);
            bgRt.pivot          = new Vector2(0f, 0f);
            bgRt.sizeDelta      = barSize;
            bgRt.anchoredPosition = Vector2.zero;
        }

        if (fillImage != null)
        {
            fillImage.type        = Image.Type.Filled;
            fillImage.fillMethod  = Image.FillMethod.Horizontal;
            fillImage.fillOrigin  = (int)Image.OriginHorizontal.Left;
            fillImage.sprite      = square;

            RectTransform fillRt  = fillImage.rectTransform;
            fillRt.anchorMin      = new Vector2(0f, 0f);
            fillRt.anchorMax      = new Vector2(0f, 0f);
            fillRt.pivot          = new Vector2(0f, 0f);
            fillRt.sizeDelta      = new Vector2(barSize.x - 4f, barSize.y - 4f);
            fillRt.anchoredPosition = new Vector2(2f, 2f); // 2px inset from background
        }

        if (hpLabel != null)
        {
            RectTransform labelRt   = hpLabel.rectTransform;
            labelRt.anchorMin       = new Vector2(0f, 0f);
            labelRt.anchorMax       = new Vector2(0f, 0f);
            labelRt.pivot           = new Vector2(0f, 0f);
            labelRt.sizeDelta       = barSize;
            labelRt.anchoredPosition = Vector2.zero;

            hpLabel.alignment       = TextAlignmentOptions.Center;
            hpLabel.fontSize        = labelFontSize;
            hpLabel.enableAutoSizing = false;
            hpLabel.color           = Color.white;
            hpLabel.overflowMode    = TextOverflowModes.Overflow;
        }
    }

    // Shared static sprite factory — same as EnemyHealthBar so they reuse one texture
    private static Sprite GetSquareSprite()
    {
        if (_squareSprite != null) return _squareSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags  = HideFlags.HideAndDontSave;
        _squareSprite  = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        _squareSprite.name = "HealthBarSquareSprite";
        return _squareSprite;
    }
}