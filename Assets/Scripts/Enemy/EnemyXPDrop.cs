using UnityEngine;

/// <summary>
/// Attach to every enemy prefab alongside EntityStats.
/// When the enemy dies (onDeath fires), it grants XP to the player.
///
/// Setup:
///   1. Add this component to your enemy prefab.
///   2. Set xpValue in the Inspector (or leave the default).
///   3. No other wiring needed — it hooks EntityStats.onDeath automatically.
///
/// To customise per-enemy-type XP, just change xpValue on each prefab/instance.
/// You can also override it at runtime: enemy.GetComponent<EnemyXPDrop>().xpValue = 50;
/// </summary>
public class EnemyXPDrop : MonoBehaviour
{
    [Header("XP Reward")]
    [Tooltip("XP granted to the player when this enemy is killed.\n" +
             "Tune this per-prefab to control difficulty pacing.")]
    public int xpValue = 10;

    [Tooltip("Tag used to find the player. Must match your Player GameObject's tag.")]
    public string playerTag = "Player";

    private EntityStats _myStats;

    void Start()
    {
        _myStats = GetComponent<EntityStats>();

        if (_myStats == null)
        {
            Debug.LogError($"[EnemyXPDrop] No EntityStats on '{gameObject.name}'. XP won't be granted.");
            return;
        }

        _myStats.onDeath.AddListener(GrantXP);
    }

    void OnDestroy()
    {
        if (_myStats != null)
            _myStats.onDeath.RemoveListener(GrantXP);
    }

    private void GrantXP()
    {
        GameObject player = GameObject.FindWithTag(playerTag);

        if (player == null)
        {
            Debug.LogWarning("[EnemyXPDrop] Player not found — XP not granted.");
            return;
        }

        XPSystem xpSystem = player.GetComponent<XPSystem>();

        if (xpSystem == null)
        {
            Debug.LogWarning("[EnemyXPDrop] Player has no XPSystem component — XP not granted.");
            return;
        }

        xpSystem.AddXP(xpValue);
        Debug.Log($"[EnemyXPDrop] '{gameObject.name}' dropped {xpValue} XP.");
    }
}