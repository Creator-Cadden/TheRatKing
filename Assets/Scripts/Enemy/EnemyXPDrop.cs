using UnityEngine;

/// <summary>
/// Attach to every enemy prefab alongside EntityStats. When the enemy dies it
/// grants BOTH XP and currency to the player. Amounts come from the
/// EnemyStatBlock (xpReward / currencyReward per enemy TYPE); the local fields
/// below are only fallbacks for enemies without a stat block value.
/// </summary>
public class EnemyXPDrop : MonoBehaviour
{
    [Header("Fallbacks (EnemyStatBlock values / displayName take priority)")]
    [Tooltip("Used only when the stat block's xpReward is 0.")]
    public int xpValue = 10;

    [Tooltip("Used only when the stat block's currencyReward is 0.")]
    public int currencyValue = 5;

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

        _myStats.onDeath.AddListener(GrantRewards);
    }

    void OnDestroy()
    {
        if (_myStats != null)
            _myStats.onDeath.RemoveListener(GrantRewards);
    }

    private void GrantRewards()
    {
        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null)
        {
            Debug.LogWarning("[EnemyXPDrop] Player not found — rewards not granted.");
            return;
        }

        // Stat block values win; component fields are fallbacks.
        EnemyStatBlock sb = _myStats != null ? _myStats.enemyStatBlock : null;

        string sourceName =
            (sb != null && !string.IsNullOrEmpty(sb.displayName)) ? sb.displayName :
            !string.IsNullOrEmpty(displayName)                    ? displayName :
            gameObject.name.Replace("(Clone)", "").Trim();

        // ── XP ──
        XPSystem xpSystem = player.GetComponent<XPSystem>();
        if (xpSystem != null)
        {
            int xpAmount = (sb != null && sb.xpReward > 0) ? sb.xpReward : xpValue;
            xpSystem.AddXP(xpAmount, sourceName);
            Debug.Log($"[EnemyXPDrop] '{sourceName}' dropped {xpAmount} XP.");
        }

        // ── Currency ──
        CurrencySystem wallet = player.GetComponent<CurrencySystem>();
        if (wallet != null)
        {
            int coinAmount = (sb != null && sb.currencyReward > 0) ? sb.currencyReward : currencyValue;
            wallet.AddCurrency(coinAmount, sourceName);
            Debug.Log($"[EnemyXPDrop] '{sourceName}' dropped {coinAmount} currency.");
        }
    }
}
