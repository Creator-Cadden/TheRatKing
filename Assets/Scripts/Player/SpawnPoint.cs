using UnityEngine;

/// <summary>
/// Drop this on any empty GameObject in your scene to mark the player's spawn/respawn point.
/// The GameManager looks for a GameObject tagged "SpawnPoint" at startup.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("Visualise the spawn point in the editor even when not selected.")]
    public bool alwaysDrawGizmo = true;

    void OnDrawGizmos()
    {
        if (!alwaysDrawGizmo) return;
        Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.8f);
        Gizmos.DrawSphere(transform.position, 0.3f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
