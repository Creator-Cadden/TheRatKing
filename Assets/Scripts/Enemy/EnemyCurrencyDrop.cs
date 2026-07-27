using UnityEngine;

/// <summary>
/// Attach to enemy prefabs alongside EntityStats. When the enemy dies, grants
/// currency to the player's CurrencySystem. Amount comes from the EnemyStatBlock
/// (per enemy TYPE); the local field is only a fallback. Mirrors EnemyXPDrop.
/// </summary>
public class EnemyCurrencyDrop : MonoBehaviour
{
    [Header("Fallback (EnemyStatBlock.currencyReward takes priority)")]
    [Tooltip("Used only when the stat block's currencyReward is 0.")]
    public int currencyValue = 5;

    [Tooltip("Tag used to find the player. Must match your Player GameObject's tag.")]
    public string playerTag = "Player";

    private EntityStats _myStats;

    void Start()
    {
        _myStats = GetComponent<EntityStats>();
        if (_myStats == null)
        {
            Debug.LogError($"[EnemyCurrencyDrop] No EntityStats on '{gameObject.name}'. Currency won't be granted.");
            return;
        }
        _myStats.onDeath.AddListener(GrantCurrency);
    }

    void OnDestroy()
    {
        if (_myStats != null)
            _myStats.onDeath.RemoveListener(GrantCurrency);
    }

    private void GrantCurrency()
    {
        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null)
        {
            Debug.LogWarning("[EnemyCurrencyDrop] Player not found — currency not granted.");
            return;
        }

        CurrencySystem wallet = player.GetComponent<CurrencySystem>();
        if (wallet == null)
        {
            Debug.LogWarning("[EnemyCurrencyDrop] Player has no CurrencySystem — currency not granted.");
            return;
        }

        EnemyStatBlock sb = _myStats != null ? _myStats.enemyStatBlock : null;
        int amount = (sb != null && sb.currencyReward > 0) ? sb.currencyReward : currencyValue;

        string sourceName =
            (sb != null && !string.IsNullOrEmpty(sb.displayName)) ? sb.displayName :
            gameObject.name.Replace("(Clone)", "").Trim();

        wallet.AddCurrency(amount, sourceName);
    }
}
