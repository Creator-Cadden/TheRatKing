using UnityEngine;

/// <summary>
/// Drops on a trigger volume (Cube / Plane / Capsule with a Collider whose
/// "Is Trigger" is on) placed at the END of a level. When the player walks
/// into it, GameManager captures their current stats and loads the next
/// scene. The save file gets stamped with the new scene name + the new
/// floor, so Continue / Retry now bring the player back to the START of
/// the new level with the stats they had on entry.
///
/// Setup:
///   1. In your scene, GameObject → 3D Object → Cube (or Plane).
///   2. Position / scale it as the "doorway" at the level's exit.
///   3. On the Collider component, tick "Is Trigger".
///   4. Add Component → LevelTransition.
///   5. Set "Next Scene Name" to the destination scene (e.g. "lvl2").
///      The scene MUST be in Build Settings.
///   6. Choose Floor Mode (Advance is the usual choice for forward levels).
///   7. (Optional) Add a material / VFX so the player can see it.
///
/// Saves only happen at the start of each scene via GameManager. This
/// trigger doesn't manually save — it just hands off to GameManager which
/// captures stats, loads scene, then SaveCheckpoint runs on the new scene.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LevelTransition : MonoBehaviour
{
    public enum FloorMode
    {
        [InspectorName("Leave Floor Unchanged")]
        Unchanged   = 0,
        [InspectorName("Advance Floor (+1, max 3)")]
        Advance     = -1,
        [InspectorName("Set To Floor 1")]
        SetToOne    = 1,
        [InspectorName("Set To Floor 2")]
        SetToTwo    = 2,
        [InspectorName("Set To Floor 3")]
        SetToThree  = 3,
    }

    [Header("Destination")]
    [Tooltip("The scene to load when the player crosses this trigger.\n" +
             "Must be in File → Build Profiles → Scene List.")]
    public string nextSceneName = "lvl2";

    [Header("Floor")]
    [Tooltip("How to handle the player's currentFloor when transitioning:\n" +
             "  Unchanged    — keep current floor (useful for side rooms)\n" +
             "  Advance      — +1 floor, capped at 3 (usual forward transitions)\n" +
             "  Set To 1/2/3 — force a specific floor (useful for backtracks)")]
    public FloorMode floorMode = FloorMode.Advance;

    [Header("Detection")]
    [Tooltip("Tag of the player GameObject. Trigger only fires for this tag.")]
    public string playerTag = "Player";

    [Tooltip("Once triggered, never fire again. Off only for special " +
             "two-way doors. Almost always leave on.")]
    public bool oneShot = true;

    [Header("Debug")]
    public bool verbose = false;

    private bool _used = false;

    // ─────────────────────────────────────────

    void Reset()
    {
        // When you Add Component in the editor, make the collider a trigger
        // by default so the user doesn't have to remember.
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_used && oneShot) return;
        if (!other.CompareTag(playerTag)) return;
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning($"[LevelTransition] {gameObject.name} has no Next Scene Name set.");
            return;
        }

        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogError("[LevelTransition] No GameManager in scene — cannot transition.");
            return;
        }

        // Safety: don't fire mid-death sequence (player technically still
        // colliding while the death screen is fading in).
        EntityStats stats = other.GetComponentInParent<EntityStats>();
        if (stats != null && stats.IsDead) return;

        _used = true;

        if (verbose)
            Debug.Log($"[LevelTransition] {gameObject.name} → loading '{nextSceneName}' " +
                      $"(floorMode={floorMode})");

        gm.TransitionToLevel(nextSceneName, (int)floorMode);
    }

    // ─────────────────────────────────────────

    void OnDrawGizmos()
    {
        Collider c = GetComponent<Collider>();
        if (c == null) return;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.20f);

        // Light up the trigger volume so it's obvious in the Scene view.
        if (c is BoxCollider box)
        {
            Matrix4x4 m = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = m;
        }
        else if (c is SphereCollider sphere)
        {
            Gizmos.DrawSphere(transform.TransformPoint(sphere.center), sphere.radius * transform.lossyScale.x);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.TransformPoint(sphere.center), sphere.radius * transform.lossyScale.x);
        }
    }
}
