using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Weapon selection screen shown once at the start of a new game.
/// Reads the pending slot and name from GameManager, shows three weapon
/// choices, then calls GameManager.StartNewGame() with the chosen weapon.
///
/// Hierarchy (WeaponSelect scene):
///   Canvas
///   └── WeaponSelectRoot        ← attach this script
///       ├── TitleLabel          (TMP_Text) "Choose Your Weapon"
///       ├── BladeButton         (Button)
///       │   ├── Icon            (Image — blade sprite)
///       │   ├── NameLabel       (TMP_Text) "Blade"
///       │   └── DescLabel       (TMP_Text) "Fast attacks. Damage scales 2x Strength."
///       ├── HammerButton        (Button)
///       │   ├── Icon            (Image)
///       │   ├── NameLabel       (TMP_Text) "Hammer"
///       │   └── DescLabel       (TMP_Text) "Slow, heavy. Damage scales 4x Strength."
///       └── BowButton           (Button)
///           ├── Icon            (Image)
///           ├── NameLabel       (TMP_Text) "Bow"
///           └── DescLabel       (TMP_Text) "Ranged. Charged shot deals 3x damage."
///
/// No back button — the player must pick a weapon to continue.
/// </summary>
public class WeaponSelectUI : MonoBehaviour
{
    [Header("Buttons")]
    public Button bladeButton;
    public Button hammerButton;
    public Button bowButton;

    [Header("Selection Highlight")]
    [Tooltip("Color tint applied to the selected weapon button.")]
    public Color selectedColor   = new Color(0.85f, 0.75f, 0.25f, 1f);
    public Color unselectedColor = new Color(0.18f, 0.18f, 0.18f, 1f);

    [Header("Confirm")]
    public Button   confirmButton;
    public TMP_Text confirmLabel;

    [Header("Info Panel (optional)")]
    [Tooltip("TMP_Text that shows a longer description of the highlighted weapon.")]
    public TMP_Text infoLabel;

    // ── Private ──
    private EntityStats.WeaponType _selected = EntityStats.WeaponType.Blade;
    private bool _hasSelected = false;

    private static readonly string[] WeaponNames =
    {
        "Blade",
        "Hammer",
        "Bow"
    };

    private static readonly string[] WeaponDescs =
    {
        "Fast attacks every second. Damage = Strength x2.\nGrants +1 Toughness. Great all-rounder.",
        "Slow, heavy strikes. Damage = Strength x4.\nGrants +4 Toughness but cuts move and attack speed.",
        "Ranged attacks. Damage = Strength x1.\nCharged aimed shot deals 3x damage. No Toughness bonus."
    };

    // ═════════════════════════════════════════════════════════════

    void Start()
    {
        CursorManager.Request("weaponselect");
        Time.timeScale = 1f;

        bladeButton ?.onClick.AddListener(() => SelectWeapon(EntityStats.WeaponType.Blade));
        hammerButton?.onClick.AddListener(() => SelectWeapon(EntityStats.WeaponType.Hammer));
        bowButton   ?.onClick.AddListener(() => SelectWeapon(EntityStats.WeaponType.Bow));
        confirmButton?.onClick.AddListener(OnConfirm);

        // Default to no selection — player must actively choose
        SetAllUnselected();

        if (confirmButton != null)
            confirmButton.interactable = false;

        if (confirmLabel != null)
            confirmLabel.text = "SELECT A WEAPON";

        if (infoLabel != null)
            infoLabel.text = "Pick your starting weapon.\nYou can find others during your adventure.";
    }

    void OnDestroy() => CursorManager.Release("weaponselect");

    // ═════════════════════════════════════════════════════════════

    private void SelectWeapon(EntityStats.WeaponType weapon)
    {
        _selected    = weapon;
        _hasSelected = true;

        // Highlight the chosen button
        SetButtonColor(bladeButton,  weapon == EntityStats.WeaponType.Blade);
        SetButtonColor(hammerButton, weapon == EntityStats.WeaponType.Hammer);
        SetButtonColor(bowButton,    weapon == EntityStats.WeaponType.Bow);

        // Update info
        int idx = (int)weapon;
        if (infoLabel != null && idx < WeaponDescs.Length)
            infoLabel.text = WeaponDescs[idx];

        // Enable confirm
        if (confirmButton != null)
            confirmButton.interactable = true;

        if (confirmLabel != null)
            confirmLabel.text = $"START WITH {WeaponNames[idx].ToUpper()}";
    }

    private void OnConfirm()
    {
        if (!_hasSelected) return;

        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogError("[WeaponSelectUI] No GameManager found!");
            return;
        }

        // Start the actual game — GameManager loads firstGameScene and on
        // scene load it will apply save data + equip the chosen weapon.
        gm.StartNewGame(gm.PendingSlot, gm.PendingFirstScene, gm.PendingName, _selected);
    }

    // ═════════════════════════════════════════════════════════════

    private void SetAllUnselected()
    {
        SetButtonColor(bladeButton,  false);
        SetButtonColor(hammerButton, false);
        SetButtonColor(bowButton,    false);
    }

    private void SetButtonColor(Button btn, bool selected)
    {
        if (btn == null) return;
        var cb = btn.colors;
        cb.normalColor = selected ? selectedColor : unselectedColor;
        btn.colors = cb;
    }
}