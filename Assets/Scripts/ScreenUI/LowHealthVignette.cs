using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The full-screen red edge glow that creeps in as the player's health drops,
/// plus a quick flash on every hit. This is the single cheapest thing you can add
/// to make combat feel dangerous — players read health from the screen edges long
/// before they read the number on the bar.
///
/// Put this on a full-screen Image at the BOTTOM of the HUD canvas's draw order
/// (first child) so it sits behind the bars. Assign a vignette sprite: a square
/// with a soft transparent hole in the middle. If you leave the sprite empty it
/// falls back to a flat tint, which works but reads much worse — draw the real
/// one, it's a 15-minute job in Krita.
/// </summary>
[RequireComponent(typeof(Image))]
public class LowHealthVignette : MonoBehaviour
{
    [Header("Player")]
    public string playerTag = "Player";
    [Tooltip("Leave null — auto-found via the player tag.")]
    public EntityStats playerStats;

    [Header("Vignette")]
    [Tooltip("Health fraction at which the vignette starts appearing. 0.5 = half health.")]
    [Range(0f, 1f)] public float startThreshold = 0.5f;

    [Tooltip("Alpha of the vignette at 0 HP.")]
    [Range(0f, 1f)] public float maxAlpha = 0.62f;

    [Tooltip("Colour of the vignette. Keep it dark and desaturated — a bright red " +
             "overlay hides enemies.")]
    public Color vignetteColor = new Color(0.52f, 0.07f, 0.05f, 1f);

    [Tooltip("How fast the vignette catches up to the health value.")]
    public float responsiveness = 5f;

    [Header("Heartbeat pulse")]
    [Tooltip("Health fraction below which the vignette pulses like a heartbeat.")]
    [Range(0f, 1f)] public float pulseThreshold = 0.25f;
    public float pulseSpeed  = 3.4f;
    [Tooltip("How much the alpha swings during the pulse.")]
    [Range(0f, 0.5f)] public float pulseAmount = 0.14f;

    [Header("Hit flash")]
    [Tooltip("Extra alpha added instantly when damage lands, then decaying.")]
    [Range(0f, 1f)] public float hitFlashAlpha = 0.35f;
    public float hitFlashDecay = 3.5f;

    [Header("Accessibility")]
    [Tooltip("Scale the whole effect by the player's Screen Shake setting — players " +
             "who turn shake down are usually asking for less screen effect overall.")]
    public bool respectShakeSetting = true;

    // ── Private ───────────────────────────────────────────────────────────────

    private Image _image;
    private float _displayed;
    private float _flash;

    void Awake()
    {
        _image = GetComponent<Image>();
        _image.raycastTarget = false;
        if (_image.sprite == null) _image.sprite = UITheme.SolidSprite;

        Color c = vignetteColor; c.a = 0f;
        _image.color = c;
    }

    void Start()
    {
        BindPlayer();
    }

    void OnDestroy()
    {
        if (playerStats != null) playerStats.onDamageTaken.RemoveListener(OnDamage);
    }

    void Update()
    {
        if (playerStats == null) { BindPlayer(); return; }
        if (playerStats.MaxHealth <= 0) return;

        float ratio = Mathf.Clamp01((float)playerStats.CurrentHealth / playerStats.MaxHealth);

        // 0 at the threshold, 1 at zero health.
        float severity = startThreshold > 0f
            ? Mathf.Clamp01(1f - ratio / startThreshold)
            : 0f;

        float targetAlpha = severity * maxAlpha;

        if (ratio > 0f && ratio <= pulseThreshold)
            targetAlpha += Mathf.Sin(Time.time * pulseSpeed) * pulseAmount * severity;

        _displayed = Mathf.Lerp(_displayed, targetAlpha, Time.deltaTime * responsiveness);

        _flash = Mathf.Max(0f, _flash - Time.deltaTime * hitFlashDecay);

        float finalAlpha = Mathf.Clamp01(_displayed + _flash);
        if (respectShakeSetting) finalAlpha *= Mathf.Max(0.25f, GameSettings.ScreenShake);

        Color c = vignetteColor;
        c.a = finalAlpha;
        _image.color = c;
    }

    private void BindPlayer()
    {
        if (playerStats != null) return;

        GameObject go = GameObject.FindWithTag(playerTag);
        if (go == null) return;

        playerStats = go.GetComponent<EntityStats>();
        if (playerStats != null) playerStats.onDamageTaken.AddListener(OnDamage);
    }

    private void OnDamage(int amount) => _flash = hitFlashAlpha;
}
