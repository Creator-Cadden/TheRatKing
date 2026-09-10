using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Anything the player can walk up to and press Interact on: a shop keeper, a
/// sacrifice statue, a chest, a lever, a door. Put this on the object, give it a
/// verb and a name, and hook <see cref="onInteract"/> to whatever should happen.
///
/// Every enabled Interactable registers itself in a static list;
/// <see cref="InteractionPromptUI"/> picks the closest one in range and shows the
/// "E  Buy from the Ratmonger" prompt. No per-frame FindObjectsOfType anywhere.
/// </summary>
public class Interactable : MonoBehaviour
{
    /// <summary>All enabled interactables in the loaded scenes.</summary>
    public static readonly List<Interactable> All = new List<Interactable>();

    [Header("Prompt")]
    [Tooltip("Verb shown first. e.g. 'Buy from', 'Pray at', 'Open', 'Read'.")]
    public string verb = "Use";

    [Tooltip("Name of the thing. e.g. 'the Ratmonger', 'the Bone Statue'.")]
    public string displayName = "";

    [Tooltip("Optional second line — cost, warning, or a hint. Leave empty to hide it.")]
    public string subtext = "";

    [Header("Range")]
    [Tooltip("How close the player must be, in world units, for the prompt to appear.")]
    public float interactRange = 3f;

    [Tooltip("Where the prompt floats. Leave empty to use this transform + Prompt Height.")]
    public Transform promptAnchor;

    [Tooltip("Height above the anchor for the floating prompt.")]
    public float promptHeight = 1.8f;

    [Header("State")]
    [Tooltip("Off = the prompt still shows but greys out and the interaction is refused. " +
             "Use for 'can't afford' or 'already used'.")]
    public bool available = true;

    [Tooltip("Fire once, then disable this component. Chests, one-time levers.")]
    public bool oneShot = false;

    [Header("Events")]
    [Tooltip("What happens when the player presses Interact in range.")]
    public UnityEvent onInteract;

    [Tooltip("Fires when this becomes the closest targeted interactable.")]
    public UnityEvent onFocused;

    [Tooltip("Fires when the player looks away / walks off.")]
    public UnityEvent onUnfocused;

    // ── Registry ──────────────────────────────────────────────────────────────

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>World position the floating prompt should sit at.</summary>
    public Vector3 PromptPosition
    {
        get
        {
            Transform t = promptAnchor != null ? promptAnchor : transform;
            return t.position + Vector3.up * promptHeight;
        }
    }

    /// <summary>The full prompt line, e.g. "Buy from the Ratmonger".</summary>
    public string PromptLine =>
        string.IsNullOrWhiteSpace(displayName) ? verb : $"{verb} {displayName}";

    /// <summary>Called by InteractionPromptUI. Returns false if it was refused.</summary>
    public bool TryInteract()
    {
        if (!available) return false;

        onInteract?.Invoke();

        if (oneShot)
        {
            available = false;
            enabled   = false;   // OnDisable unregisters it
        }
        return true;
    }

    public void SetFocused(bool focused)
    {
        if (focused) onFocused?.Invoke();
        else         onUnfocused?.Invoke();
    }

    /// <summary>Hook to a UnityEvent to grey the prompt out from gameplay code.</summary>
    public void SetAvailable(bool value) => available = value;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = available ? new Color(0.31f, 0.70f, 0.35f, 0.5f)
                                 : new Color(0.82f, 0.27f, 0.23f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
        Gizmos.DrawSphere(PromptPosition, 0.08f);
    }
}
