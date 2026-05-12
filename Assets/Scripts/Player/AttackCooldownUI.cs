using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minecraft-style attack cooldown indicator.
/// Shows a small sword icon with a fill bar that sweeps up from empty to full
/// as the attack cooldown recharges. Fades out when fully charged.
///
/// Setup:
///   1. Create a Canvas child GameObject named "AttackCooldownHUD"
///   2. Add these children:
///        Icon       (Image — your sword/weapon sprite, or leave default)
///        FillBar    (Image — Type: Filled, Fill Method: Vertical, Origin: Bottom)
///        Background (Image — dark backing behind the fill)
///   3. Attach this script and assign the references
///   4. Position near bottom-center of screen
///
/// The indicator:
///   - Fades IN the moment you attack (cooldown starts)
///   - Fill sweeps upward as cooldown recharges (like Minecraft sword)
///   - Fades OUT once fully charged and fade delay passes
///   - Tints the icon darker while on cooldown, white when ready
/// </summary>
public class AttackCooldownHUD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave null to auto-find by Player tag.")]
    public PlayerCombat playerCombat;

    public Image iconImage;
    public Image fillImage;
    public Image backgroundImage;

    [Header("Appearance")]
    [Tooltip("Color of the fill bar while recharging.")]
    public Color fillColor       = new Color(0.95f, 0.90f, 0.55f, 1f);  // warm yellow like Minecraft
    [Tooltip("Color of the fill bar when fully charged.")]
    public Color fillReadyColor  = new Color(1f,    1f,    1f,    1f);
    [Tooltip("Icon tint while on cooldown.")]
    public Color iconCooldown    = new Color(0.45f, 0.45f, 0.45f, 1f);
    [Tooltip("Icon tint when fully charged.")]
    public Color iconReady       = new Color(1f,    1f,    1f,    1f);

    public Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("Fade")]
    [Tooltip("Seconds after reaching full charge before the HUD fades out.")]
    public float fadeOutDelay = 0.6f;
    public float fadeInSpeed  = 12f;
    public float fadeOutSpeed = 4f;

    [Header("Size")]
    public Vector2 barSize  = new Vector2(12f, 40f);
    public Vector2 iconSize = new Vector2(28f, 28f);

    // ── Private ──
    private float _currentAlpha  = 0f;
    private float _readySince    = -999f;
    private bool  _wasReady      = true;
    private bool  _initialized   = false;

    private static Sprite _squareSprite;

    // ─────────────────────────────────────────
    void Start()
    {
        if (playerCombat == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerCombat = player.GetComponent<PlayerCombat>();
        }

        if (playerCombat == null)
        {
            Debug.LogError("[AttackCooldownHUD] No PlayerCombat found!");
            return;
        }

        SetupFillImage();
        SetAlpha(0f);
        _initialized = true;
    }

    void Update()
    {
        if (!_initialized || playerCombat == null) return;

        float progress = playerCombat.AttackCooldownProgress; // 0 = just attacked, 1 = ready
        bool  isReady  = progress >= 1f;

        // Track when we first became ready so we can delay the fade-out
        if (isReady && !_wasReady)
            _readySince = Time.time;
        _wasReady = isReady;

        // Update fill
        if (fillImage != null)
        {
            fillImage.fillAmount = progress;
            fillImage.color      = Color.Lerp(fillColor, fillReadyColor, progress);
        }

        // Tint icon
        if (iconImage != null)
            iconImage.color = Color.Lerp(iconCooldown, iconReady, progress);

        // Fade logic — show while on cooldown, hide after fully charged + delay
        bool shouldHide = isReady && Time.time >= _readySince + fadeOutDelay;
        float target    = shouldHide ? 0f : 1f;
        float speed     = target > _currentAlpha ? fadeInSpeed : fadeOutSpeed;

        _currentAlpha = Mathf.MoveTowards(_currentAlpha, target, speed * Time.deltaTime);
        SetAlpha(_currentAlpha);
    }

    // ─────────────────────────────────────────

    private void SetupFillImage()
    {
        if (fillImage == null) return;

        // Force correct fill settings — vertical bottom-to-top sweep like Minecraft
        fillImage.type       = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Vertical;
        fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;

        if (fillImage.sprite == null)
            fillImage.sprite = GetSquareSprite();

        if (backgroundImage != null)
        {
            if (backgroundImage.sprite == null)
                backgroundImage.sprite = GetSquareSprite();
            backgroundImage.color = backgroundColor;
        }
    }

    private void SetAlpha(float a)
    {
        _currentAlpha = a;

        if (fillImage != null)
        {
            Color c = fillImage.color; c.a = a;
            fillImage.color = c;
        }

        if (backgroundImage != null)
        {
            Color c = backgroundColor; c.a = a * backgroundColor.a;
            backgroundImage.color = c;
        }

        if (iconImage != null)
        {
            Color c = iconImage.color; c.a = a;
            iconImage.color = c;
        }
    }

    private static Sprite GetSquareSprite()
    {
        if (_squareSprite != null) return _squareSprite;
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        _squareSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _squareSprite;
    }
}