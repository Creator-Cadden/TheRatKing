using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the StaminaBar Panel on your Canvas.
/// Assign backgroundImage, fillImage, and playerStats in the Inspector.
/// Fades in when stamina is used, fades out after a delay once full.
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

    private float _currentAlpha  = 0f;
    private float _fullSinceTime = -999f;
    private bool  _wasFull       = true;

    void Start()
    {
        SetAlpha(0f);
    }

    void Update()
    {
        if (playerStats == null) return;

        float pct  = (float)playerStats.CurrentStamina / playerStats.MaxStamina;
        bool  full = pct >= 1f;

        // Update fill amount
        fillImage.fillAmount = pct;

        // Track when stamina first became full
        if (full && !_wasFull)
            _fullSinceTime = Time.time;

        _wasFull = full;

        // Hide only when full AND fade delay has passed
        bool shouldHide   = full && Time.time >= _fullSinceTime + fadeDelay;
        float targetAlpha = shouldHide ? 0f : 1f;

        float speed   = targetAlpha > _currentAlpha ? fadeInSpeed : fadeOutSpeed;
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, speed * Time.deltaTime);

        SetAlpha(_currentAlpha);
    }

    private void SetAlpha(float a)
    {
        _currentAlpha = a;

        Color bg   = backgroundColor;
        Color fill = fillColor;
        bg.a       = a;
        fill.a     = a;

        backgroundImage.color = bg;
        fillImage.color       = fill;
    }
}