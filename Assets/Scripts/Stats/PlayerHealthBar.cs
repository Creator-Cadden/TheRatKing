using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Juicy screen-space player health bar.
/// DAMAGE: the green fill drops instantly, a RED trail holds where the health was
/// for a moment, then drains to catch up, revealing the dark background.
/// HEAL: the green fill rises into a bright-green "lead" showing the gained chunk.
/// Both flash the whole bar and punch its scale; low health pulses.
/// </summary>
public class PlayerHealthBar : MonoBehaviour
{
    [Header("References")]
    public Image fillImage;
    public TMP_Text hpLabel;
    [Tooltip("Leave null — auto-found via tag 'Player'.")]
    public EntityStats playerStats;

    [Header("Appearance")]
    [Tooltip("How fast the main green fill lerps to the new value.")]
    public float lerpSpeed = 14f;

    [Header("Colours")]
    public Color fullColour       = new Color(0.27f, 0.72f, 0.18f);
    public Color halfColour       = new Color(0.96f, 0.74f, 0.10f);
    public Color criticalColour   = new Color(0.85f, 0.15f, 0.15f);
    public Color backgroundColour = new Color(0f, 0f, 0f, 0.85f);

    [Header("Damage Trail")]
    [Tooltip("Seconds the red trail HOLDS in place before it starts draining.")]
    public float chipHoldDelay = 0.35f;
    [Tooltip("Drain speed of the red trail after the hold — slower = longer trail.")]
    public float chipLerpSpeed = 3f;
    public Color chipColour = new Color(0.85f, 0.12f, 0.12f, 0.95f);

    [Header("Heal Lead")]
    [Tooltip("Colour of the gained-health chunk the fill rises into on heal.")]
    public Color healColour = new Color(0.55f, 1f, 0.45f, 0.95f);

    [Header("Flash + Punch")]
    public float flashDuration    = 0.14f;
    public Color damageFlashColour = new Color(1f, 0.35f, 0.35f, 1f);
    public Color healFlashColour   = new Color(0.55f, 1f, 0.55f, 1f);
    [Range(0f, 1f)] public float maxFlashAlpha = 0.5f;
    [Tooltip("Scale-punch amount on any change (0.12 = pops 12% bigger).")]
    public float punchOnHit   = 0.12f;
    public float punchRecover = 12f;

    [Header("Damage Shake")]
    public float shakeOnHit = 9f;
    public float shakeDecay = 45f;

    [Header("Low-Health Pulse")]
    [Range(0f, 1f)] public float criticalThreshold = 0.25f;
    public float pulseSpeed  = 6f;
    public float pulseAmount = 0.06f;

    [Header("Layout")]
    public Vector2 barSize       = new Vector2(220f, 20f);
    public float   labelFontSize = 12f;
    public bool    forceBoxStyle = true;

    // ── Private ──
    private float _displayedFill;
    private float _targetFill;
    private float _chipFill;
    private float _chipHoldUntil;
    private bool  _healMode;

    private Image _chipImage;
    private Image _flashImage;
    private Image _backgroundImage;

    private float   _flashTimer;
    private bool    _flashIsHeal;
    private float   _shakeMag;
    private Vector2 _shakeOffset;
    private float   _punch;

    private RectTransform _rt;
    private Vector2       _basePos;
    private static Sprite _squareSprite;

    void Start()
    {
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

        playerStats.onDamageTaken.AddListener(OnDamageTaken);
        playerStats.onHeal.AddListener(OnHealed);
        playerStats.onDeath.AddListener(OnDeath);

        Transform bgTransform = transform.Find("Background");
        if (bgTransform != null) _backgroundImage = bgTransform.GetComponent<Image>();

        if (forceBoxStyle) ApplyLayoutStyle();
        EnsureChipImage();
        EnsureFlashImage();

        _rt      = GetComponent<RectTransform>();
        _basePos = _rt != null ? _rt.anchoredPosition : Vector2.zero;

        _targetFill = _displayedFill = _chipFill = GetFillRatio();
        RefreshBar(snap: true);
    }

    void OnDestroy()
    {
        if (playerStats == null) return;
        playerStats.onDamageTaken.RemoveListener(OnDamageTaken);
        playerStats.onHeal.RemoveListener(OnHealed);
        playerStats.onDeath.RemoveListener(OnDeath);
    }

    void Update()
    {
        if (playerStats == null) return;

        _displayedFill = Mathf.Lerp(_displayedFill, _targetFill, Time.deltaTime * lerpSpeed);

        if (_healMode)
        {
            // Green lead sits at the new (higher) target; the fill rises into it.
            _chipFill = _targetFill;
            if (_displayedFill >= _targetFill - 0.005f) _healMode = false;
        }
        else
        {
            // Damage: the red trail HOLDS, then drains after the delay.
            if (Time.time >= _chipHoldUntil)
                _chipFill = Mathf.Lerp(_chipFill, _targetFill, Time.deltaTime * chipLerpSpeed);
            if (_chipFill < _displayedFill) _chipFill = _displayedFill;
        }

        if (_flashTimer > 0f) _flashTimer -= Time.deltaTime;

        _shakeMag    = Mathf.Max(0f, _shakeMag - shakeDecay * Time.deltaTime);
        _shakeOffset = _shakeMag > 0.01f
            ? new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * _shakeMag
            : Vector2.zero;

        _punch = Mathf.Lerp(_punch, 0f, Time.deltaTime * punchRecover);

        float ratio = GetFillRatio();
        float pulse = ratio <= criticalThreshold && ratio > 0f
            ? 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount
            : 1f;

        if (_rt != null)
        {
            _rt.anchoredPosition = _basePos + _shakeOffset;
            _rt.localScale       = Vector3.one * ((1f + _punch) * pulse);
        }

        RefreshBar(snap: false);
    }

    // ── Event callbacks ──

    private void OnDamageTaken(int _)
    {
        _targetFill    = GetFillRatio();
        _chipHoldUntil = Time.time + chipHoldDelay;   // red trail holds first
        _healMode      = false;
        _flashTimer    = flashDuration;
        _flashIsHeal   = false;
        _shakeMag      = shakeOnHit;
        _punch         = punchOnHit;
    }

    private void OnHealed(int amount)
    {
        _targetFill  = GetFillRatio();
        _chipFill    = Mathf.Max(_chipFill, _targetFill);   // green lead jumps to target
        _healMode    = true;
        _flashTimer  = flashDuration;
        _flashIsHeal = true;
        _punch       = punchOnHit;
    }

    private void OnDeath()
    {
        _targetFill = 0f;
        Animator anim = playerStats.GetComponentInChildren<Animator>();
        if (anim != null) anim.SetTrigger("Dead");
    }

    // ── Helpers ──

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
                ? Color.Lerp(halfColour, fullColour, (v - 0.5f) * 2f)
                : Color.Lerp(criticalColour, halfColour, v * 2f);
        }

        if (_chipImage != null)
        {
            _chipImage.fillAmount = snap ? _targetFill : _chipFill;
            _chipImage.color      = _healMode ? healColour : chipColour;
        }

        if (_flashImage != null)
        {
            float a = flashDuration > 0f ? Mathf.Clamp01(_flashTimer / flashDuration) * maxFlashAlpha : 0f;
            Color c = _flashIsHeal ? healFlashColour : damageFlashColour;
            c.a = a;
            _flashImage.color = c;
        }

        if (hpLabel != null && playerStats != null)
            hpLabel.text = $"{playerStats.CurrentHealth} / {playerStats.MaxHealth}";
    }

    /// <summary>Delayed damage/heal trail bar, created behind the fill.</summary>
    private void EnsureChipImage()
    {
        if (_chipImage != null || fillImage == null) return;

        var go = new GameObject("HealthChip", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(fillImage.transform.parent, false);
        go.transform.SetSiblingIndex(fillImage.transform.GetSiblingIndex());   // behind the fill

        _chipImage = go.GetComponent<Image>();
        _chipImage.raycastTarget = false;
        _chipImage.type       = Image.Type.Filled;
        _chipImage.fillMethod = Image.FillMethod.Horizontal;
        _chipImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        _chipImage.sprite     = GetSquareSprite();

        CopyRect(_chipImage.rectTransform, fillImage.rectTransform);
    }

    /// <summary>Full-bar flash overlay, created on TOP of everything.</summary>
    private void EnsureFlashImage()
    {
        if (_flashImage != null) return;

        var go = new GameObject("HealthFlash", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        go.transform.SetAsLastSibling();   // on top

        _flashImage = go.GetComponent<Image>();
        _flashImage.raycastTarget = false;
        _flashImage.sprite = GetSquareSprite();
        _flashImage.color  = new Color(1f, 1f, 1f, 0f);

        RectTransform frt = _flashImage.rectTransform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(0f, 0f);
        frt.pivot     = new Vector2(0f, 0f);
        frt.sizeDelta = barSize;
        frt.anchoredPosition = Vector2.zero;
    }

    private static void CopyRect(RectTransform dst, RectTransform src)
    {
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.pivot     = src.pivot;
        dst.sizeDelta = src.sizeDelta;
        dst.anchoredPosition = src.anchoredPosition;
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
            fillRt.anchoredPosition = new Vector2(2f, 2f);
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
