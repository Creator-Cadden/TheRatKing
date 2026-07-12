using UnityEngine;

/// <summary>
/// Attach to every enemy prefab alongside EntityStats.
/// When the enemy dies (onDeath fires), it grants XP to the player.
/// </summary>
public class EnemyXPDrop : MonoBehaviour
{
    [Header("XP Reward")]
    [Tooltip("XP granted to the player when this enemy is killed.\n" +
             "Tune this per-prefab to control difficulty pacing.")]
    public int xpValue = 10;

    [Tooltip("Display name shown in the floating '+X XP from {name}' indicator.\n" +
             "Leave blank to use the GameObject's name (with '(Clone)' stripped).")]
    public string displayName = "";

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

        string sourceName = !string.IsNullOrEmpty(displayName)
            ? displayName
            : gameObject.name.Replace("(Clone)", "").Trim();

        xpSystem.AddXP(xpValue, sourceName);
        Debug.Log($"[EnemyXPDrop] '{sourceName}' dropped {xpValue} XP.");
    }
}
