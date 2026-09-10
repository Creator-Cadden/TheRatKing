using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Put this on anything hoverable that deserves an explanation — a stat's plus
/// button, a weapon card, a shop item, a status icon. It raises the shared
/// <see cref="TooltipSystem"/> bubble after a short delay.
///
/// For values that change (like "+2 damage per point" where 2 depends on the
/// weapon), don't hard-code the body — call <see cref="SetText"/> from your own
/// script whenever the value changes.
/// </summary>
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Content")]
    [Tooltip("Bold heading line. Leave empty for a body-only tooltip.")]
    public string title;

    [TextArea(2, 6)]
    [Tooltip("Explanation. Supports TMP rich text — <b>, <color=#CF9E24>, line breaks.")]
    public string body;

    [Header("Behaviour")]
    [Tooltip("Seconds the pointer must rest here before the bubble appears. " +
             "0.35–0.5 keeps tooltips from flickering as the mouse sweeps across a menu.")]
    public float delay = 0.4f;

    [Tooltip("Hide the tooltip while the mouse button is held. Stops the bubble " +
             "sitting over a button the player is actively clicking.")]
    public bool hideOnPress = true;

    private bool  _hovering;
    private float _hoverTime;
    private bool  _shown;

    void OnDisable() => ForceHide();
    void OnDestroy()  => ForceHide();

    void Update()
    {
        if (!_hovering || _shown) return;

        _hoverTime += Time.unscaledDeltaTime;
        if (_hoverTime < delay) return;

        TooltipSystem.Show(title, body);
        _shown = true;
    }

    /// <summary>Change the tooltip text at runtime (live stat values, prices, etc.).</summary>
    public void SetText(string newTitle, string newBody)
    {
        title = newTitle;
        body  = newBody;
        if (_shown) TooltipSystem.Show(title, body);   // refresh in place
    }

    private void ForceHide()
    {
        _hovering  = false;
        _hoverTime = 0f;
        if (_shown)
        {
            TooltipSystem.Hide();
            _shown = false;
        }
    }

    public void OnPointerEnter(PointerEventData e)
    {
        _hovering  = true;
        _hoverTime = 0f;
    }

    public void OnPointerExit(PointerEventData e) => ForceHide();
}
