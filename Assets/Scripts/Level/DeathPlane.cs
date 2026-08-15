using UnityEngine;

/// <summary>
/// Void catcher under every level. Falling in no longer teleports to a fixed
/// respawn tag — it deals PARTIAL damage (fraction of max HP, never lethal —
/// floors you at 1 HP) and returns the player to the last safe ground they
/// stood on before falling (PlayerMovement's fall-recovery memory), with brief
/// i-frames so a ledge enemy can't instantly punish the return.
/// Enemies that fall in still die instantly.
/// </summary>
public class DeathPlane : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Tag to check for. Must match your Player GameObject's tag.")]
    public string playerTag = "Player";

    [Tooltip("If true, any enemy that falls in dies instantly.")]
    public bool killEnemies = true;

    [Header("Fall Damage")]
    [Range(0f, 1f)]
    [Tooltip("Fraction of MAX HP lost per fall. 0.25 = a quarter of your health.")]
    public float fallDamageFraction = 0.25f;

    [Tooltip("Falls can never kill — the player is left at 1 HP minimum.")]
    public bool neverLethal = true;

    [Tooltip("I-frames granted after the return teleport.")]
    public float postFallInvuln = 0.6f;

    [Header("Fallback Respawn")]
    [Tooltip("Used ONLY if no safe ground was recorded yet (e.g. falling within " +
             "the first second of a level). Empty = searches for a GameObject " +
             "tagged 'Respawn'.")]
    public Transform respawnPoint;

    void Start()
    {
        if (respawnPoint == null)
        {
            GameObject found = GameObject.FindWithTag("Respawn");
            if (found != null) respawnPoint = found.transform;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            HandlePlayerFall(other);
            return;
        }

        if (killEnemies)
        {
            EntityStats stats = other.GetComponentInParent<EntityStats>();
            if (stats != null && !stats.isPlayer)
                stats.TakeDamage(99999);
        }
    }

    private void HandlePlayerFall(Collider player)
    {
        var stats = player.GetComponentInParent<EntityStats>();
        var pm    = player.GetComponentInParent<PlayerMovement>();

        // Damage first — a fraction of max HP, clamped non-lethal.
        if (stats != null)
        {
            int dmg = Mathf.RoundToInt(stats.MaxHealth * fallDamageFraction);
            if (neverLethal) stats.TakeFallDamage(dmg);
            else             stats.TakeDamage(dmg);
        }

        // Return to the last safe ground before the fall.
        if (pm != null && pm.TryGetSafeRespawn(out Vector3 safePos))
        {
            pm.TeleportTo(safePos);
        }
        else if (respawnPoint != null)
        {
            // Fallback: no safe ground recorded yet this level.
            if (pm != null)
            {
                pm.TeleportTo(respawnPoint.position);
            }
            else
            {
                player.transform.position = respawnPoint.position;
            }
        }

        // Brief protection so an enemy waiting at the lip can't instant-punish.
        stats?.GrantInvulnerability(postFallInvuln);
    }
}
