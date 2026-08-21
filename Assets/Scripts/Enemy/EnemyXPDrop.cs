using UnityEngine;

/// <summary>
/// Attach to every enemy prefab alongside EntityStats. When the enemy dies it
/// grants BOTH XP and currency to the player. Amounts come from the
/// EnemyStatBlock (xpReward / currencyReward per enemy TYPE); the local fields
/// below are only fallbacks for enemies without a stat block value.
///
/// XP is delivered as a burst of flying "XP orbs" (see <see cref="XPOrb"/>) that
/// arc out of the corpse and home in on the player, granting XP as each one
/// lands — so the XP bar ticks up in satisfying chunks. Currency is granted
/// immediately, which drives the rolling Rat Coin counter.
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

    [Header("XP Orbs")]
    [Tooltip("ON = XP flies out as vibrant orbs that home to the player and grant " +
             "on arrival. OFF = XP is granted instantly on death (old behaviour).")]
    public bool xpAsOrbs = true;

    [Tooltip("Colour of this enemy's XP orbs. Leave as the default green unless a " +
             "special enemy wants its own flavour.")]
    public Color xpOrbColor = new Color(0.35f, 1f, 0.55f, 1f);

    [Tooltip("Orb count. 0 = auto (scales with the XP reward).")]
    public int xpOrbCount = 0;

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

        // ── XP ── (as flying orbs by default, so the bar ticks up as they land)
        XPSystem xpSystem = player.GetComponent<XPSystem>();
        if (xpSystem != null)
        {
            int xpAmount = (sb != null && sb.xpReward > 0) ? sb.xpReward : xpValue;

            if (xpAsOrbs)
            {
                XPOrb.SpawnBurst(transform.position, xpAmount, player.transform,
                                 xpSystem, xpOrbCount, xpOrbColor);
                // One informative "+N XP from {enemy}" popup at the kill; the orbs
                // themselves grant silently so the feed isn't spammed per-orb.
                xpSystem.AnnounceXPSource(xpAmount, sourceName);
                Debug.Log($"[EnemyXPDrop] '{sourceName}' dropped {xpAmount} XP as orbs.");
            }
            else
            {
                xpSystem.AddXP(xpAmount, sourceName);
                Debug.Log($"[EnemyXPDrop] '{sourceName}' granted {xpAmount} XP instantly.");
            }
        }

        // ── Currency ── (immediate; drives the rolling Rat Coin counter)
        CurrencySystem wallet = player.GetComponent<CurrencySystem>();
        if (wallet != null)
        {
            int coinAmount = (sb != null && sb.currencyReward > 0) ? sb.currencyReward : currencyValue;
            wallet.AddCurrency(coinAmount, sourceName);
            Debug.Log($"[EnemyXPDrop] '{sourceName}' dropped {coinAmount} Rat Coins.");
        }
    }
}
