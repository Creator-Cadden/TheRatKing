using UnityEngine;

/// <summary>
/// Attach to a GameObject with a Collider set to Is Trigger.
/// Any entity tagged "Player" that enters the trigger is respawned at the respawn point.
/// </summary>
public class DeathPlane : MonoBehaviour
{
    [Tooltip("Tag to check for. Must match your Player GameObject's tag.")]
    public string playerTag = "Player";

    [Tooltip("If true, any enemy that falls in also dies instantly.")]
    public bool killEnemies = true;

    [Tooltip("Where the player is sent on death. If left empty, searches for a GameObject tagged 'Respawn'.")]
    public Transform respawnPoint;

    void Start()
    {
        // Auto-find a respawn point if none is assigned
        if (respawnPoint == null)
        {
            GameObject found = GameObject.FindWithTag("Respawn");
            if (found != null)
                respawnPoint = found.transform;
            else
                Debug.LogWarning("DeathPlane: No respawn point assigned and no GameObject tagged 'Respawn' was found.");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            RespawnPlayer(other);
            return;
        }

        if (killEnemies)
        {
            EntityStats stats = other.GetComponent<EntityStats>();
            if (stats != null)
                stats.TakeDamage(99999);
        }
    }

    void RespawnPlayer(Collider player)
    {
        if (respawnPoint == null)
        {
            Debug.LogError("DeathPlane: Cannot respawn — no respawn point set.");
            return;
        }

        // Handle character controllers separately (they ignore Transform.position)
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            player.transform.position = respawnPoint.position;
            player.transform.rotation = respawnPoint.rotation;
            cc.enabled = true;
        }
        else
        {
            // Rigidbody: zero out velocity before teleporting
            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            player.transform.position = respawnPoint.position;
            player.transform.rotation = respawnPoint.rotation;
        }
    }
}
