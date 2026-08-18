using UnityEngine;

/// <summary>
/// Drop on a trigger-collider zone in the Tutorial scene. When the player enters,
/// it reports its objectiveId to the TutorialManager, advancing any active step
/// whose Advance mode is Objective and whose objectiveId matches.
///
/// Use it for "reach here" / "step into the arena" style objectives. For "defeat
/// the dummy" style, just call TutorialManager.Instance?.NotifyObjective("id")
/// from the dummy's death handler instead.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TutorialTrigger : MonoBehaviour
{
    [Tooltip("Must match a tutorial step's Objective Id.")]
    public string objectiveId = "";
    [Tooltip("Only fire once, then disable the trigger.")]
    public bool fireOnce = true;
    [Tooltip("Tag that counts as the player.")]
    public string playerTag = "Player";

    private bool _fired;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_fired && fireOnce) return;
        if (!other.CompareTag(playerTag)) return;

        _fired = true;
        TutorialManager.Instance?.NotifyObjective(objectiveId);
        if (fireOnce) gameObject.SetActive(false);
    }
}
