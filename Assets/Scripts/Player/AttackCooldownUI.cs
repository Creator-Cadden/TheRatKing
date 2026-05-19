using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minecraft-style attack cooldown indicator.
/// Supports both basic attack and jump attack bars on the same component.
///
/// Setup:
///   Basic attack bar:
///     Icon        (Image — sword/weapon sprite)
///     FillBar     (Image — Type: Filled, Vertical, Origin: Bottom)
///     Background  (Image — dark backing)
///
///   Jump attack bar (optional — leave null to disable):
///     JumpIcon        (Image)
///     JumpFillBar     (Image — Type: Filled, Vertical, Origin: Bottom)
///     JumpBackground  (Image)
///
/// Both bars:
///   - Fade IN when the attack is used
///   - Fill sweeps upward as cooldown recharges
///   - Fade OUT once fully charged after fadeOutDelay
/// </summary>
public class AttackCooldownHUD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave null to auto-find by Player tag.")]
    public PlayerCombat playerCombat;

    [Header("Basic Attack Bar")]
    public Image iconImage;
    public Image fillImage;
    public Image backgroundImage;

    [Header("Jump Attack Bar (leave null to disable)")]
    public Image jumpIconImage;
    public Image jumpFillImage;
    public Image jumpBackgroundImage;

    [Header("Appearance")]
    public Color fillColor       = new Color(0.95f, 0.90f, 0.55f, 1f);
    public Color fillReadyColor  = new Color(1f,    1f,    1f,    1f);
    public Color jumpFillColor   = new Color(0.55f, 0.80f, 0.95f, 1f);  // blue tint for jump
    public Color iconCooldown    = new Color(0.45f, 0.45f, 0.45f, 1f);
    public Color iconReady       = new Color(1f,    1f,    1f,    1f);
    public Color backgroundColor = new Color(0f,    0f,    0f,    0.55f);

    [Header("Fade")]
    public float fadeOutDelay = 0.6f;
    public float fadeInSpeed  = 12f;
    public float fadeOutSpeed = 4f;

    // ── Private ──
    private float _basicAlpha    = 0f;
    private float _jumpAlpha     = 0f;
    private float _basicReady    = -999f;
    private float _jumpReady     = -999f;
    private bool  _wasBasicReady = true;
    private bool  _wasJumpReady  = true;
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

        SetupFillBar(fillImage, backgroundImage);
        SetupFillBar(jumpFillImage, jumpBackgroundImage);

        SetBasicAlpha(0f);
        SetJumpAlpha(0f);
        _initialized = true;
    }

    void Update()
    {
        if (!_initialized || playerCombat == null) return;

        UpdateBar(
            playerCombat.AttackCooldownProgress,
            fillColor,
            fillImage, backgroundImage, iconImage,
            ref _basicAlpha, ref _basicReady, ref _wasBasicReady);

        UpdateBar(
            playerCombat.JumpAttackCooldownProgress,
            jumpFillColor,
            jumpFillImage, jumpBackgroundImage, jumpIconImage,
            ref _jumpAlpha, ref _jumpReady, ref _wasJumpReady);
    }

    // ─────────────────────────────────────────

    private void UpdateBar(
        float progress, Color barColor,
        Image fill, Image bg, Image icon,
        ref float alpha, ref float readySince, ref bool wasReady)
    {
        if (fill == null) return;

        bool isReady = progress >= 1f;

        if (isReady && !wasReady) readySince = Time.time;
        wasReady = isReady;

        fill.fillAmount = progress;
        fill.color      = Color.Lerp(barColor, fillReadyColor, progress);

        if (icon != null)
            icon.color = Color.Lerp(iconCooldown, iconReady, progress);

        bool  shouldHide = isReady && Time.time >= readySince + fadeOutDelay;
        float target     = shouldHide ? 0f : 1f;
        float speed      = target > alpha ? fadeInSpeed : fadeOutSpeed;
        alpha = Mathf.MoveTowards(alpha, target, speed * Time.deltaTime);

        // Apply alpha to all three images
        SetImageAlpha(fill, alpha);
        SetImageAlpha(icon, alpha);
        if (bg != null)
        {
            Color c = backgroundColor;
            c.a = alpha * backgroundColor.a;
            bg.color = c;
        }
    }

    private void SetBasicAlpha(float a)
    {
        _basicAlpha = a;
        SetImageAlpha(fillImage, a);
        SetImageAlpha(iconImage, a);
        if (backgroundImage != null)
        {
            Color c = backgroundColor; c.a = a * backgroundColor.a;
            backgroundImage.color = c;
        }
    }

    private void SetJumpAlpha(float a)
    {
        _jumpAlpha = a;
        SetImageAlpha(jumpFillImage, a);
        SetImageAlpha(jumpIconImage, a);
        if (jumpBackgroundImage != null)
        {
            Color c = backgroundColor; c.a = a * backgroundColor.a;
            jumpBackgroundImage.color = c;
        }
    }

    private static void SetImageAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color; c.a = a; img.color = c;
    }

    private static void SetupFillBar(Image fill, Image bg)
    {
        if (fill != null)
        {
            fill.type       = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = (int)Image.OriginVertical.Bottom;
            if (fill.sprite == null) fill.sprite = GetSquareSprite();
        }
        if (bg != null)
        {
            if (bg.sprite == null) bg.sprite = GetSquareSprite();
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