using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Dock-style magnifier for a column/row of buttons. Put this on the object that
/// DIRECTLY parents the buttons. It auto-collects them and, by list-distance from
/// the hovered/selected button:
///   • scales each one (focused = regular, furthest = smallest),
///   • lifts the focused button to the front (draw order) so it's not clipped,
///   • dims the others (further = dimmer).
/// Distances use the fixed collect order, so re-ordering for the front-lift does
/// NOT change the fisheye. Owns button SCALE (overrides UIButtonJuice). Unscaled
/// time; runs in LateUpdate so it wins the frame.
/// </summary>
public class UIMenuMagnifier : MonoBehaviour
{
    [Header("Scale (smaller overall)")]
    [Tooltip("Size of every button when nothing is hovered/selected.")]
    public float idleScale    = 0.75f;
    [Tooltip("Size of the focused (closest) button — the 'regular' size.")]
    public float focusedScale = 1.10f;
    [Tooltip("Size of the button furthest from the focused one.")]
    public float minScale     = 0.40f;
    [Tooltip("Squash multiplier while a button is held down.")]
    public float pressScale   = 0.92f;

    [Header("Falloff")]
    [Tooltip("Steps away to reach minScale. 0 = auto (furthest button = minScale).")]
    public int   falloffSteps = 0;
    [Tooltip("Curve of the drop-off. Below 1 = neighbours shrink fast right next " +
             "to the focus (more drastic per step).")]
    public float falloffExponent = 0.65f;
    [Tooltip("Higher = snappier response.")]
    public float responsiveness = 14f;

    [Header("Bring focused to front")]
    [Tooltip("Draw the focused button on top of its neighbours (re-orders it last; " +
             "safe for manually-positioned buttons, NOT for Layout Group children).")]
    public bool bringFocusedToFront = true;

    [Header("Dim the others")]
    public bool  dimNonFocused = true;
    [Tooltip("Alpha of the furthest button while another is focused.")]
    public float dimMinAlpha   = 0.5f;

    private RectTransform[] _items;
    private Vector3[]       _baseScales;
    private bool[]          _pressed;
    private CanvasGroup[]   _groups;
    private int  _hoverIndex = -1, _selectIndex = -1, _frontIndex = -1;
    private bool _engaged;   // false until first interaction, so the entrance owns alpha

    void Awake() => Collect();

    void OnEnable()
    {
        _engaged = false; _hoverIndex = _selectIndex = -1; _frontIndex = -1;
    }

    private void Collect()
    {
        var list = new List<RectTransform>();
        foreach (Transform c in transform)
            if (c.GetComponent<Selectable>() != null)
                list.Add((RectTransform)c);

        _items      = list.ToArray();
        _baseScales = new Vector3[_items.Length];
        _pressed    = new bool[_items.Length];
        _groups     = new CanvasGroup[_items.Length];

        for (int i = 0; i < _items.Length; i++)
        {
            _baseScales[i] = _items[i].localScale;

            var item = _items[i].GetComponent<MagnifierItem>()
                       ?? _items[i].gameObject.AddComponent<MagnifierItem>();
            item.owner = this; item.index = i;

            if (dimNonFocused)
                _groups[i] = _items[i].GetComponent<CanvasGroup>()
                             ?? _items[i].gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void SetHover(int i, bool on)
    {
        if (on) { _hoverIndex = i; _engaged = true; }
        else if (_hoverIndex == i) _hoverIndex = -1;
    }
    public void SetSelected(int i, bool on)
    {
        if (on) { _selectIndex = i; _engaged = true; }
        else if (_selectIndex == i) _selectIndex = -1;
    }
    public void SetPressed(int i, bool on)
    {
        if (i >= 0 && i < _pressed.Length) _pressed[i] = on;
    }

    void LateUpdate()
    {
        if (_items == null || _items.Length == 0) return;

        int   focus = _hoverIndex >= 0 ? _hoverIndex : _selectIndex;
        int   maxD  = falloffSteps > 0 ? falloffSteps : Mathf.Max(1, _items.Length - 1);
        float k     = 1f - Mathf.Exp(-responsiveness * Time.unscaledDeltaTime);

        // Front-lift: re-order the focused button last so it draws on top.
        if (bringFocusedToFront && focus >= 0 && focus != _frontIndex)
        {
            _items[focus].SetAsLastSibling();
            _frontIndex = focus;
        }

        for (int i = 0; i < _items.Length; i++)
        {
            float dist = focus < 0 ? 0f
                : Mathf.Pow(Mathf.Clamp01(Mathf.Abs(i - focus) / (float)maxD), falloffExponent);

            float mult = focus < 0 ? idleScale : Mathf.Lerp(focusedScale, minScale, dist);
            if (_pressed[i]) mult *= pressScale;
            _items[i].localScale = Vector3.Lerp(_items[i].localScale, _baseScales[i] * mult, k);

            if (dimNonFocused && _groups[i] != null && _engaged)
            {
                float targetA = focus < 0 ? 1f : Mathf.Lerp(1f, dimMinAlpha, dist);
                _groups[i].alpha = Mathf.Lerp(_groups[i].alpha, targetA, k);
            }
        }
    }
}

/// <summary>Auto-added to each button by UIMenuMagnifier; forwards hover / select
/// / press events with the button's index. Coexists with UISelectionFX /
/// UIButtonJuice (Unity dispatches events to all handlers on the object).</summary>
public class MagnifierItem : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [HideInInspector] public UIMenuMagnifier owner;
    [HideInInspector] public int index;

    public void OnPointerEnter(PointerEventData e) => owner?.SetHover(index, true);
    public void OnPointerExit (PointerEventData e) => owner?.SetHover(index, false);
    public void OnSelect      (BaseEventData e)    => owner?.SetSelected(index, true);
    public void OnDeselect    (BaseEventData e)    => owner?.SetSelected(index, false);
    public void OnPointerDown (PointerEventData e) => owner?.SetPressed(index, true);
    public void OnPointerUp   (PointerEventData e) => owner?.SetPressed(index, false);
}
