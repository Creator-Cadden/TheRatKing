using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop onto any menu button (or hoverable UI element) for juice:
///   • hover  → smoothly scales up + optional gentle idle bob
///   • press  → quick squash
///   • click  → full-screen flash (via MenuFX)
/// Self-contained; only reaches out to MenuFX for the click flash.
/// Uses unscaled time so it works even while the game is paused.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIButtonJuice : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler,  IPointerUpHandler, IPointerClickHandler
{
    [Header("Hover grow")]
    [Tooltip("Scale multiplier while hovered (1.12 = 12% bigger).")]
    public float hoverScale = 1.12f;
    [Tooltip("Higher = snappier scale response.")]
    public float responsiveness = 14f;

    [Header("Press")]
    [Tooltip("Scale multiplier while held down (squash).")]
    public float pressScale = 0.94f;

    [Header("Idle bob (while hovered)")]
    public bool  bobWhileHovered = true;
    public float bobAmount = 0.03f;
    public float bobSpeed  = 6f;

    [Header("Hover tint (optional)")]
    [Tooltip("Tints the element's Graphic on hover. Leave OFF if the Button " +
             "already uses its own colour transition, or they'll fight.")]
    public bool  tintOnHover = false;
    public Color hoverTint   = new Color(1f, 0.96f, 0.75f, 1f);

    [Header("Click flash")]
    public bool  flashOnClick = true;
    public Color flashColor   = Color.white;

    private RectTransform _rt;
    private Graphic _graphic;
    private Vector3 _baseScale;
    private Color   _baseColor;
    private bool _hover, _pressed;
    private float _bobPhase;

    void Awake()
    {
        _rt        = (RectTransform)transform;
        _baseScale = _rt.localScale;
        _graphic   = GetComponent<Graphic>() ?? GetComponentInChildren<Graphic>();
        if (_graphic != null) _baseColor = _graphic.color;
    }

    void OnDisable()
    {
        // Reset so a panel that re-enables doesn't reappear mid-hover.
        _hover = _pressed = false;
        if (_rt != null) _rt.localScale = _baseScale;
        if (_graphic != null && tintOnHover) _graphic.color = _baseColor;
    }

    void Update()
    {
        float mult = _pressed ? pressScale : (_hover ? hoverScale : 1f);
        Vector3 target = _baseScale * mult;

        if (bobWhileHovered && _hover && !_pressed)
        {
            _bobPhase += Time.unscaledDeltaTime * bobSpeed;
            target *= 1f + Mathf.Sin(_bobPhase) * bobAmount;
        }

        float k = 1f - Mathf.Exp(-responsiveness * Time.unscaledDeltaTime);
        _rt.localScale = Vector3.Lerp(_rt.localScale, target, k);

        if (_graphic != null && tintOnHover)
            _graphic.color = Color.Lerp(_graphic.color,
                                        _hover ? hoverTint : _baseColor, k);
    }

    public void OnPointerEnter(PointerEventData e) { _hover = true; _bobPhase = 0f; }
    public void OnPointerExit (PointerEventData e) { _hover = false; }
    public void OnPointerDown (PointerEventData e) { _pressed = true; }
    public void OnPointerUp   (PointerEventData e) { _pressed = false; }

    public void OnPointerClick(PointerEventData e)
    {
        if (flashOnClick) MenuFX.Flash(flashColor);
    }
}
