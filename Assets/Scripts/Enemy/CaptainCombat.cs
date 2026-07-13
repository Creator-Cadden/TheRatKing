using UnityEngine;

/// <summary>
/// DEPRECATED — the Captain's shape rotation now lives in the EnemyStatBlock:
/// set Has Decal Attack ON, add three Decal Attack entries (cone / circle /
/// rect), and set Decal Cycle Mode to Sequence (or Random).
/// This component does nothing anymore. Remove it from the Captain prefab.
/// Kept only so existing prefabs don't show a missing-script error before
/// you've had a chance to remove it.
/// </summary>
public class CaptainCombat : MonoBehaviour
{
    void Start()
    {
        Debug.LogWarning($"[CaptainCombat] DEPRECATED on '{gameObject.name}' — " +
                         "shape cycling moved to EnemyStatBlock (Decal Attacks + " +
                         "Cycle Mode). Remove this component from the prefab.");
    }
}
