using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the StaminaBar Panel on your Canvas.
/// Assign backgroundImage, fillImage, and playerStats in the Inspector.
/// Fades in when stamina is used, fades out after a delay once full.
/// When stamina hits 0, the bar blips red and jiggles side to side —
/// a visual "you're out!" (playtest feedback).
/// </summary>
public class StaminaBarUI : MonoBehaviour
{
    [Header("References")]
    public EntityStats playerStats;
    public Image       backgroundImage;
    public Image       fillImage;

    [Header("Fade Settings")]
    [Tooltip("Seconds after stamina is full before the bar fades out")]
    public float fadeDelay    = 1.5f;
    public float fadeInSpeed  = 8f;
    public float fadeOutSpeed = 3f;

    [Header("Colors")]
    public Color backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    public Color fillColor       = new Color(0.75f, 0.75f, 0.75f, 1f);

    [Header("Empty Blip (stamina hits 0)")]
    [Tooltip("Color the bar flashes when stamina reaches 0. Fades back to normal over Blip Duration.")]
    public Color emptyFlashColor = new Color(0.9f, 0.15f, 0.15f, 1f);

    [Tooltip("How long the red flash + jiggle lasts, in seconds.")]
    public float blipDuration = 0.6f;

    [Tooltip("How far the bar jiggles left/right at the start of the blip, in pixels. Decays to 0 over the blip.")]
    public float jiggleAmplitude = 8f;

    [Tooltip("Side-to-side oscillations per second during the jiggle.")]
    public float jiggleFrequency = 14f;

    private float _currentAlpha  = 0f;
    private float _fullSinceTime = -999f;
    private bool  _wasFull       = true;
    private bool  _wasEmpty      = false;

    // Empty-blip state
    private float         _blipEndTime = -999f;
    private RectTransform _rect;
    private Vector2       _basePosition;   // resting anchoredPosition to jiggle around

    void Start()
    {
        _rect         = GetComponent<RectTransform>();
        _basePosition = _rect != null ? _rect.anchoredPosition : Vector2.zero;
        SetAlpha(0f);
    }

    void Update()
    {
        if (playerStats == null) return;

        float pct   = (float)playerStats.CurrentStamina / playerStats.MaxStamina;
        bool  full  = pct >= 1f;
        bool  empty = playerStats.CurrentStamina <= 0;

        // Update fill amount
        fillImage.fillAmount = pct;

        // Trigger the blip on the frame stamina BECOMES empty
        if (empty && !_wasEmpty)
            _blipEndTime = Time.time + blipDuration;

        _wasEmpty = empty;

        // Track when stamina first became full
        if (full && !_wasFull)
            _fullSinceTime = Time.time;

        _wasFull = full;

        bool blipActive = Time.time < _blipEndTime;

        // Hide only when full AND fade delay has passed (never mid-blip)
        bool shouldHide   = full && !blipActive && Time.time >= _fullSinceTime + fadeDelay;
        float targetAlpha = shouldHide ? 0f : 1f;

        float speed   = targetAlpha > _currentAlpha ? fadeInSpeed : fadeOutSpeed;
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, speed * Time.deltaTime);

        // blipT: 1 at the start of the blip, 0 when it ends — drives both
        // the red→normal color fade and the decaying jiggle amplitude.
        float blipT = blipActive
            ? Mathf.Clamp01((_blipEndTime - Time.time) / Mathf.Max(0.0001f, blipDuration))
            : 0f;

        SetColors(_currentAlpha, blipT);
        ApplyJiggle(blipT);
    }

    private void ApplyJiggle(float blipT)
    {
        if (_rect == null) return;

        if (blipT <= 0f)
        {
            _rect.anchoredPosition = _basePosition;
            return;
        }

        float offsetX = Mathf.Sin(Time.time * jiggleFrequency * 2f * Mathf.PI)
                      * jiggleAmplitude * blipT;
        _rect.anchoredPosition = _basePosition + new Vector2(offsetX, 0f);
    }

    private void SetColors(float a, float blipT)
    {
        _currentAlpha = a;

        // Lerp both images toward the flash color by blipT (0 = normal colors)
        Color bg   = Color.Lerp(backgroundColor, emptyFlashColor, blipT);
        Color fill = Color.Lerp(fillColor,       emptyFlashColor, blipT);
        bg.a       = a;
        fill.a     = a;

        backgroundImage.color = bg;
        fillImage.color       = fill;
    }

    private void SetAlpha(float a)
    {
        SetColors(a, 0f);
    }
}
