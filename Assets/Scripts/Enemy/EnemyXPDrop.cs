using UnityEngine;

/// <summary>
/// Attach to every enemy prefab alongside EntityStats.
/// When the enemy dies (onDeath fires), it grants XP to the player.
/// XP amount and display name come from the EnemyStatBlock (per enemy TYPE);
/// the local fields below are only fallbacks for enemies without a stat block value.
/// </summary>
public class EnemyXPDrop : MonoBehaviour
{
    [Header("Fallbacks (EnemyStatBlock.xpReward / displayName take priority)")]
    [Tooltip("Used only when the stat block's xpReward is 0.")]
    public int xpValue = 10;

    [Tooltip("Used only when the stat block's displayName is blank. " +
             "Blank here too = the GameObject's name (with '(Clone)' stripped).")]
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

        // Stat block values win; component fields are fallbacks.
        EnemyStatBlock sb = _myStats != null ? _myStats.enemyStatBlock : null;

        int amount = (sb != null && sb.xpReward > 0) ? sb.xpReward : xpValue;

        string sourceName =
            (sb != null && !string.IsNullOrEmpty(sb.displayName)) ? sb.displayName :
            !string.IsNullOrEmpty(displayName)                    ? displayName :
            gameObject.name.Replace("(Clone)", "").Trim();

        xpSystem.AddXP(amount, sourceName);
        Debug.Log($"[EnemyXPDrop] '{sourceName}' dropped {amount} XP.");
    }
}
