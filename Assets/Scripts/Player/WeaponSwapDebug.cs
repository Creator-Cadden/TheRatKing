using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug-only weapon swap. Press 1 / 2 / 3 to instantly equip Blade / Hammer / Bow.
///
/// Uses the Input System (Unity 6's default) via Keyboard.current — no need to
/// edit InputSystem_Actions.inputactions for this to work.
///
/// Setup:
///   1. Add this component to the Player prefab root.
///   2. (Optional) rebind the three keys in the Inspector.
///   3. (Optional) tick "Test Mode Only" so the cheat only works in the
///      Test Arena, not in real save runs.
///
/// What happens when you press a key:
///   The script calls EntityStats.EquipWeapon(...). That cascades through:
///     • Toughness bonus update (Blade +1, Hammer +4, Bow +0)
///     • Move speed recalc (hammer slow / bow aim slow)
///     • Attack cooldown recalc
///     • onStatsChanged → WeaponModelSwapper swaps the visible model
///                         + writes Animator "Weapon" int parameter
///   So model, stats, animator — everything follows automatically.
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class WeaponSwapDebug : MonoBehaviour
{
    [Header("Direct Equip Keys")]
    [Tooltip("Press to equip the blade.")]
    public Key bladeKey  = Key.Digit1;

    [Tooltip("Press to equip the hammer.")]
    public Key hammerKey = Key.Digit2;

    [Tooltip("Press to equip the bow.")]
    public Key bowKey    = Key.Digit3;

    [Header("Filters")]
    [Tooltip("If on, the keys only work while GameManager.IsTestMode is true. " +
             "Turn this on later to lock the cheat out of real save files. " +
             "Leave off while iterating.")]
    public bool testModeOnly = false;

    [Header("Debug")]
    public bool verbose = true;

    private EntityStats _stats;

    void Awake()
    {
        _stats = GetComponent<EntityStats>();
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        if (testModeOnly && (GameManager.Instance == null || !GameManager.Instance.IsTestMode))
            return;

        if (_stats == null || !_stats.isPlayer) return;

        if (Keyboard.current[bladeKey].wasPressedThisFrame)
            Equip(EntityStats.WeaponType.Blade);
        else if (Keyboard.current[hammerKey].wasPressedThisFrame)
            Equip(EntityStats.WeaponType.Hammer);
        else if (Keyboard.current[bowKey].wasPressedThisFrame)
            Equip(EntityStats.WeaponType.Bow);
    }

    private void Equip(EntityStats.WeaponType weapon)
    {
        if (_stats.EquippedWeapon == weapon)
        {
            if (verbose) Debug.Log($"[WeaponSwapDebug] Already equipped {weapon}.");
            return;
        }

        _stats.EquipWeapon(weapon);
        if (verbose) Debug.Log($"[WeaponSwapDebug] Equipped {weapon}.");
    }
}
