using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Drop this on any Button, card, or clickable UI element to swap the cursor
/// artwork while the pointer is over it. Pairs with <see cref="CursorManager"/>.
///
/// Each instance registers under its own unique owner key, so two overlapping
/// elements can't fight over the cursor — whoever is on top wins, and leaving
/// one cleanly restores the other.
/// </summary>
[DisallowMultipleComponent]
public class CursorHoverTarget : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("States")]
    [Tooltip("Cursor shown while the pointer is over this element.")]
    public CursorManager.CursorState hoverState = CursorManager.CursorState.Hover;

    [Tooltip("Cursor shown while the mouse is held down on it. Set to the same as " +
             "Hover State if you don't have separate press art.")]
    public CursorManager.CursorState pressState = CursorManager.CursorState.Press;

    [Header("Blocked")]
    [Tooltip("When ON, hovering shows the Blocked cursor instead — e.g. a shop item " +
             "you can't afford. Drive this from your own script at runtime.")]
    public bool blocked = false;

    private string _key;
    private bool   _hovering;

    void Awake()
    {
        _key = $"cursorhover:{GetInstanceID()}";
    }

    void OnDisable()
    {
        _hovering = false;
        CursorManager.ClearState(_key);
    }

    void OnDestroy() => CursorManager.ClearState(_key);

    /// <summary>Flip the blocked look on/off from gameplay code (e.g. can't afford).</summary>
    public void SetBlocked(bool value)
    {
        if (blocked == value) return;
        blocked = value;
        if (_hovering) Apply(pressed: false);
    }

    private void Apply(bool pressed)
    {
        if (blocked)
        {
            CursorManager.SetState(_key, CursorManager.CursorState.Blocked);
            return;
        }
        CursorManager.SetState(_key, pressed ? pressState : hoverState);
    }

    public void OnPointerEnter(PointerEventData e) { _hovering = true;  Apply(pressed: false); }
    public void OnPointerExit (PointerEventData e) { _hovering = false; CursorManager.ClearState(_key); }
    public void OnPointerDown (PointerEventData e) { if (_hovering) Apply(pressed: true);  }
    public void OnPointerUp   (PointerEventData e) { if (_hovering) Apply(pressed: false); }
}
