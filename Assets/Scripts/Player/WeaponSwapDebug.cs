using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug-only weapon swap. Press 1 / 2 / 3 to instantly equip Blade / Hammer / Bow.
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
