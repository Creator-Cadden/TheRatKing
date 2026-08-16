using UnityEngine;

/// <summary>
/// Slides a menu element in from off-screen (with a slight overshoot pop) every
/// time its panel becomes active. Auto-staggers by sibling order so a column of
/// buttons cascades in one after another. Drop onto each button.
/// Uses unscaled time; plays nicely alongside UIButtonJuice (that drives scale,
/// this drives position + fade).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIMenuEntrance : MonoBehaviour
{
    public enum From { Left, Right, Top, Bottom, FadeOnly }

    [Header("Entrance")]
    public From  from     = From.Left;
    [Tooltip("Extra margin BEYOND the screen edge, so it starts fully out of view " +
             "before sliding in. The screen size itself is added automatically.")]
    public float distance = 150f;
    [Tooltip("Seconds for one item to slide in.")]
    public float duration = 0.5f;

    [Header("Stagger")]
    [Tooltip("Delay added per sibling index so items cascade in order.")]
    public float stagger    = 0.07f;
    [Tooltip("Extra flat delay before this item starts.")]
    public float extraDelay = 0f;

    private RectTransform _rt;
    private CanvasGroup   _cg;
    private Vector2 _basePos;
    private bool    _captured;

    private Vector2 _startPos;
    private float   _t, _delay;
    private bool    _animating;

    void Awake()
    {
        _rt = (RectTransform)transform;
        _cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        _basePos  = _rt.anchoredPosition;   // capture design position before any move
        _captured = true;
    }

    void OnEnable()
    {
        if (!_captured) { _basePos = ((RectTransform)transform).anchoredPosition; _captured = true; }
        Begin();
    }

    private void Begin()
    {
        _delay    = extraDelay + Mathf.Max(0, transform.GetSiblingIndex()) * stagger;
        _startPos = _basePos + OffsetForFrom();
        _rt.anchoredPosition = _startPos;
        _cg.alpha = 0f;
        _t = 0f;
        _animating = true;
    }

    private Vector2 OffsetForFrom()
    {
        // Push a FULL screen dimension (+ margin) so the element starts genuinely
        // off-screen and slides in from the edge, rather than a small nudge.
        float w = 1920f, h = 1080f;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.rootCanvas != null &&
            canvas.rootCanvas.transform is RectTransform crt)
        {
            if (crt.rect.width  > 1f) w = crt.rect.width;
            if (crt.rect.height > 1f) h = crt.rect.height;
        }

        switch (from)
        {
            case From.Left:   return new Vector2(-(w + distance), 0f);
            case From.Right:  return new Vector2( (w + distance), 0f);
            case From.Top:    return new Vector2(0f,  (h + distance));
            case From.Bottom: return new Vector2(0f, -(h + distance));
            default:          return Vector2.zero;   // FadeOnly
        }
    }

    void Update()
    {
        if (!_animating) return;

        if (_delay > 0f) { _delay -= Time.unscaledDeltaTime; return; }

        _t += Time.unscaledDeltaTime;
        float p = Mathf.Clamp01(_t / Mathf.Max(0.0001f, duration));

        // Clean decelerating slide-in from off-screen (no overshoot, since travel
        // is now a full screen-width and a proportional overshoot would be huge).
        _rt.anchoredPosition = Vector2.Lerp(_startPos, _basePos, EaseOutCubic(p));
        _cg.alpha = Mathf.Clamp01(p * 1.6f);

        if (p >= 1f)
        {
            _rt.anchoredPosition = _basePos;
            _cg.alpha = 1f;
            _animating = false;
        }
    }

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
}
