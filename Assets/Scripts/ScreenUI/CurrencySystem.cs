using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The player's wallet. Attach to the Player GameObject (alongside EntityStats /
/// XPSystem). Enemies grant currency on death via EnemyCurrencyDrop, and
/// CurrencyPickup objects grant it on touch. Persists through GameManager.ActiveSave.
/// </summary>
public class CurrencySystem : MonoBehaviour
{
    // ── Events (hook your HUD counter to these) ──
    public UnityEvent<int>         onCurrencyChanged;        // passes the new total
    public UnityEvent<int, string> onCurrencyGainedFromSource; // (amount, source)

    // ── Runtime state ──
    public int CurrentCurrency { get; private set; }

    void Start()
    {
        // Load from the active save if a real game is running.
        SaveData save = GameManager.Instance != null ? GameManager.Instance.ActiveSave : null;
        if (save != null) CurrentCurrency = save.currency;

        onCurrencyChanged?.Invoke(CurrentCurrency);
    }

    /// <summary>Add currency (kills, pickups). Ignores non-positive amounts.</summary>
    public void AddCurrency(int amount, string source = "")
    {
        if (amount <= 0) return;

        CurrentCurrency += amount;
        SyncToSave();

        onCurrencyChanged?.Invoke(CurrentCurrency);
        onCurrencyGainedFromSource?.Invoke(amount, source ?? "");

        Debug.Log($"[CurrencySystem] +{amount} from '{source}' — total {CurrentCurrency}");
    }

    /// <summary>Try to spend currency (shops). Returns false if you can't afford it.</summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0) return true;
        if (CurrentCurrency < amount) return false;

        CurrentCurrency -= amount;
        SyncToSave();
        onCurrencyChanged?.Invoke(CurrentCurrency);
        return true;
    }

    /// <summary>Restore from a save (call from SaveSystem if you wire it in there).</summary>
    public void ApplySaveData(SaveData data)
    {
        CurrentCurrency = data.currency;
        onCurrencyChanged?.Invoke(CurrentCurrency);
    }

    private void SyncToSave()
    {
        SaveData save = GameManager.Instance != null ? GameManager.Instance.ActiveSave : null;
        if (save != null) save.currency = CurrentCurrency;
    }
}
