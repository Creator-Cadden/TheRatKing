using UnityEngine;

/// <summary>
/// Attach to a GameObject with a Collider set to Is Trigger.
/// Any entity tagged "Player" that enters the trigger instantly dies.
///
/// Setup:
///   1. Create an empty GameObject, name it "DeathPlane"
///   2. Add a Box Collider, tick "Is Trigger", scale it wide and flat
///   3. Position it below your map
///   4. Attach this script
/// </summary>
public class DeathPlane : MonoBehaviour
{
    [Tooltip("Tag to check for. Must match your Player GameObject's tag.")]
    public string playerTag = "Player";

    [Tooltip("If true, any enemy that falls in also dies instantly.")]
    public bool killEnemies = true;

    void OnTriggerEnter(Collider other)
    {
        EntityStats stats = other.GetComponent<EntityStats>();
        if (stats == null) return;

        if (other.CompareTag(playerTag))
        {
            // Deal enough damage to guarantee death regardless of current HP
            stats.TakeDamage(99999);
            return;
        }

        if (killEnemies && !other.CompareTag(playerTag))
        {
            stats.TakeDamage(99999);
        }
    }
}