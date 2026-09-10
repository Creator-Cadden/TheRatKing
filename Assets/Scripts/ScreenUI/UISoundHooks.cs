using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gives a UI element hover / click / refuse sounds without writing a script per
/// button. Drop it next to <see cref="UIButtonJuice"/> on any Button or card.
///
/// Menus feel broken without audio feedback even when they look great — this is
/// three lines of setup for a large chunk of the "polish" impression.
///
/// It routes through the existing AudioManager, so add your UI clips to the
/// SoundType enum + the Sound list on the Audio prefab first, then pick them here.
/// </summary>
[DisallowMultipleComponent]
public class UISoundHooks : MonoBehaviour,
    IPointerEnterHandler, IPointerClickHandler, ISelectHandler, ISubmitHandler
{
    [Header("Sounds")]
    [Tooltip("Played when the pointer enters, or the element is selected with a gamepad.")]
    public AudioManager.SoundType hoverSound = AudioManager.SoundType.Xp;
    public bool playHoverSound = true;

    [Tooltip("Played on click / submit.")]
    public AudioManager.SoundType clickSound = AudioManager.SoundType.Attack;
    public bool playClickSound = true;

    [Tooltip("Played instead of the click sound when the Button is not interactable.")]
    public AudioManager.SoundType refusedSound = AudioManager.SoundType.HitShrug;
    public bool playRefusedSound = false;

    [Header("Throttle")]
    [Tooltip("Ignore repeat hover sounds fired within this many seconds. Stops the " +
             "machine-gun effect when the mouse skims across a row of buttons.")]
    public float hoverCooldown = 0.06f;

    private static float _lastHoverTime;
    private Selectable _selectable;

    void Awake()
    {
        _selectable = GetComponent<Selectable>();
    }

    private bool Interactable => _selectable == null || _selectable.interactable;

    private void PlayHover()
    {
        if (!playHoverSound || !Interactable) return;
        if (Time.unscaledTime - _lastHoverTime < hoverCooldown) return;
        _lastHoverTime = Time.unscaledTime;
        AudioManager.Instance?.Play(hoverSound);
    }

    private void PlayClick()
    {
        if (!Interactable)
        {
            if (playRefusedSound) AudioManager.Instance?.Play(refusedSound);
            return;
        }
        if (playClickSound) AudioManager.Instance?.Play(clickSound);
    }

    public void OnPointerEnter(PointerEventData e) => PlayHover();
    public void OnSelect      (BaseEventData e)    => PlayHover();
    public void OnPointerClick(PointerEventData e) => PlayClick();
    public void OnSubmit      (BaseEventData e)    => PlayClick();
}
