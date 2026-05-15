using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Weapon selection screen for the PlayerCustom scene.
/// Hover over a weapon card to see its full description.
/// Click to select, then confirm to start.
///
/// Hierarchy (PlayerCustom scene):
///   Canvas
///   └── WeaponSelectRoot        ← attach this script
///       ├── TitleLabel          (TMP_Text) "Choose Your Weapon"
///       ├── SubtitleLabel       (TMP_Text) "Your choice shapes how you fight"
///       │
///       ├── WeaponCardsPanel    (horizontal layout group)
///       │   ├── BladeCard       (Button)
///       │   │   ├── WeaponIcon  (Image — optional sprite)
///       │   │   ├── NameLabel   (TMP_Text) "Blade"
///       │   │   └── TagLabel    (TMP_Text) "Fast · Mobile"
///       │   ├── HammerCard      (same structure)
///       │   └── BowCard         (same structure)
///       │
///       ├── DescriptionPanel    (Image — gray panel on right or below)
///       │   ├── WeaponTitleLabel    (TMP_Text) — big weapon name
///       │   ├── WeaponDescLabel     (TMP_Text) — full description text
///       │   └── StatsPanel
///       │       ├── StatRow_Damage  (TMP_Text)
///       │       ├── StatRow_Attack  (TMP_Text)
///       │       ├── StatRow_Move    (TMP_Text)
///       │       └── StatRow_Tough   (TMP_Text)
///       │
///       ├── ConfirmButton       (Button) — disabled until a weapon is selected
///       │   └── Label           (TMP_Text) "Select a Weapon"
///       └── BackButton          (Button) — returns to main menu
/// </summary>
public class WeaponSelectUI : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================

    [Header("Weapon Card Buttons")]
    public Button bladeCard;
    public Button hammerCard;
    public Button bowCard;

    [Header("Card Highlight Colors")]
    public Color cardNormal   = new Color(0.15f, 0.15f, 0.15f, 1f);
    public Color cardHovered  = new Color(0.22f, 0.22f, 0.22f, 1f);
    public Color cardSelected = new Color(0.28f, 0.22f, 0.10f, 1f);  // warm gold tint
    public Color cardSelectedBorder = new Color(0.85f, 0.70f, 0.25f, 1f);

    [Header("Description Panel")]
    public TMP_Text weaponTitleLabel;
    public TMP_Text weaponDescLabel;

    [Header("Stat Row Labels (in DescriptionPanel)")]
    [Tooltip("e.g. 'Damage:  Strength x 2'")]
    public TMP_Text statDamageLabel;
    public TMP_Text statAttackLabel;
    public TMP_Text statMoveLabel;
    public TMP_Text statToughLabel;
    public TMP_Text statSpecialLabel;   // optional — bow charged shot, hammer penalty etc.

    [Header("Confirm / Back")]
    public Button   confirmButton;
    public TMP_Text confirmLabel;
    public Button   backButton;

    [Header("Default description shown before hover")]
    [TextArea(3, 6)]
    public string defaultDescription =
        "Hover over a weapon to learn more.\nEach changes how you move, fight, and survive.";

    // =========================================================================
    // WEAPON DATA  — edit these strings to tune display without touching stats
    // =========================================================================

    private static readonly WeaponInfo[] Weapons = new WeaponInfo[]
    {
        new WeaponInfo
        {
            name        = "Blade",
            tag         = "Fast  ·  Mobile  ·  Balanced",
            description = "A quick blade clenched in iron jaws. " +
                          "Rewards aggressive play — strike fast, dodge faster. " +
                          "Strength amplifies every hit, making it scale well into late game.",
            damage      = "Damage:      Strength  ×  2  per hit",
            attack      = "Attack Speed:  1 hit / sec  (improves with Speed stat)",
            move        = "Move Speed:  Full speed at all times",
            toughness   = "Toughness:   +1  (light protection)",
            special     = ""
        },
        new WeaponInfo
        {
            name        = "Hammer",
            tag         = "Slow  ·  Heavy  ·  High Damage",
            description = "A crushing weight that breaks bones and floors. " +
                          "Each swing hits twice as hard as a blade at the same Strength, " +
                          "but you pay for it — your movement and attacks both suffer while equipped.",
            damage      = "Damage:      Strength  ×  4  per hit",
            attack      = "Attack Speed:  1 hit / 2 sec  (half normal speed)",
            move        = "Move Speed:  Reduced by  ⅓  while equipped",
            toughness   = "Toughness:   +4  (heavy armor bonus)",
            special     = "Knockback:   Staggers nearly all enemies on hit"
        },
        new WeaponInfo
        {
            name        = "Bow",
            tag         = "Ranged  ·  Tactical  ·  Burst Damage",
            description = "Strike from a distance before they can reach you. " +
                          "Normal shots are light but safe. " +
                          "Hold aim and release for a charged shot that triples your damage " +
                          "— at the cost of slowed movement while aiming.",
            damage      = "Normal Shot:   Strength  ×  1  per hit",
            attack      = "Charged Shot:  Strength  ×  3  (aim then attack)",
            move        = "Move Speed:  Reduced by  ⅓  while aiming",
            toughness   = "Toughness:   +0  (no armor bonus)",
            special     = "Range:   Hits enemies outside melee reach"
        },
    };

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================

    private int  _hoveredIndex  = -1;
    private int  _selectedIndex = -1;

    private static readonly EntityStats.WeaponType[] WeaponTypes =
    {
        EntityStats.WeaponType.Blade,
        EntityStats.WeaponType.Hammer,
        EntityStats.WeaponType.Bow,
    };

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    void Start()
    {
        CursorManager.Request("weaponselect");
        Time.timeScale = 1f;

        SetupCard(bladeCard,  0);
        SetupCard(hammerCard, 1);
        SetupCard(bowCard,    2);

        confirmButton?.onClick.AddListener(OnConfirm);
        backButton   ?.onClick.AddListener(OnBack);

        if (confirmButton != null)
            confirmButton.interactable = false;

        if (confirmLabel != null)
            confirmLabel.text = "SELECT A WEAPON";

        ShowDefaultDescription();
    }

    void OnDestroy()
    {
        CursorManager.Release("weaponselect");
    }

    // =========================================================================
    // CARD SETUP
    // =========================================================================

    private void SetupCard(Button card, int index)
    {
        if (card == null) return;

        SetCardColor(card, cardNormal);

        // Click — select this weapon
        card.onClick.AddListener(() => SelectWeapon(index));

        // Hover — show description (using EventTrigger)
        var trigger = card.gameObject.GetComponent<EventTrigger>()
                   ?? card.gameObject.AddComponent<EventTrigger>();

        AddTriggerEntry(trigger, EventTriggerType.PointerEnter,
            (_) => OnCardHoverEnter(index));
        AddTriggerEntry(trigger, EventTriggerType.PointerExit,
            (_) => OnCardHoverExit(index));
    }

    private void AddTriggerEntry(EventTrigger trigger,
        EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }

    // =========================================================================
    // HOVER
    // =========================================================================

    private void OnCardHoverEnter(int index)
    {
        _hoveredIndex = index;
        ShowWeaponInfo(index);

        // Only tint to hover color if this card isn't already selected
        Button card = GetCard(index);
        if (index != _selectedIndex)
            SetCardColor(card, cardHovered);
    }

    private void OnCardHoverExit(int index)
    {
        _hoveredIndex = -1;

        Button card = GetCard(index);
        if (index == _selectedIndex)
            SetCardColor(card, cardSelected);
        else
            SetCardColor(card, cardNormal);

        // Revert description to selected weapon or default
        if (_selectedIndex >= 0)
            ShowWeaponInfo(_selectedIndex);
        else
            ShowDefaultDescription();
    }

    // =========================================================================
    // SELECTION
    // =========================================================================

    private void SelectWeapon(int index)
    {
        _selectedIndex = index;

        // Update all card colors
        Button[] cards = { bladeCard, hammerCard, bowCard };
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;
            SetCardColor(cards[i], i == index ? cardSelected : cardNormal);
        }

        ShowWeaponInfo(index);

        if (confirmButton != null)
            confirmButton.interactable = true;

        if (confirmLabel != null)
            confirmLabel.text = "START WITH " + Weapons[index].name.ToUpper();
    }

    // =========================================================================
    // DESCRIPTION DISPLAY
    // =========================================================================

    private void ShowWeaponInfo(int index)
    {
        if (index < 0 || index >= Weapons.Length) return;
        WeaponInfo w = Weapons[index];

        SetLabel(weaponTitleLabel, w.name);
        SetLabel(weaponDescLabel,  w.description);
        SetLabel(statDamageLabel,  w.damage);
        SetLabel(statAttackLabel,  w.attack);
        SetLabel(statMoveLabel,    w.move);
        SetLabel(statToughLabel,   w.toughness);

        if (statSpecialLabel != null)
        {
            statSpecialLabel.gameObject.SetActive(!string.IsNullOrEmpty(w.special));
            statSpecialLabel.text = w.special;
        }
    }

    private void ShowDefaultDescription()
    {
        SetLabel(weaponTitleLabel, "");
        SetLabel(weaponDescLabel,  defaultDescription);
        SetLabel(statDamageLabel,  "");
        SetLabel(statAttackLabel,  "");
        SetLabel(statMoveLabel,    "");
        SetLabel(statToughLabel,   "");
        if (statSpecialLabel != null)
        {
            statSpecialLabel.gameObject.SetActive(false);
            statSpecialLabel.text = "";
        }
    }

    // =========================================================================
    // CONFIRM / BACK
    // =========================================================================

    private void OnConfirm()
    {
        if (_selectedIndex < 0) return;

        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogError("[WeaponSelectUI] No GameManager in scene!");
            return;
        }

        gm.StartNewGame(
            gm.PendingSlot,
            gm.PendingFirstScene,
            gm.PendingName,
            WeaponTypes[_selectedIndex]);
    }

    private void OnBack()
    {
        // Return to main menu — GameManager handles cursor and timeScale
        GameManager.Instance?.ReturnToMainMenu();
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private Button GetCard(int index) => index switch
    {
        0 => bladeCard,
        1 => hammerCard,
        2 => bowCard,
        _ => null
    };

    private static void SetCardColor(Button card, Color color)
    {
        if (card == null) return;
        var cb = card.colors;
        cb.normalColor      = color;
        cb.highlightedColor = color;   // we handle hover manually via EventTrigger
        cb.selectedColor    = color;
        card.colors = cb;
    }

    private static void SetLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

    // =========================================================================
    // WEAPON DATA STRUCT
    // =========================================================================

    private struct WeaponInfo
    {
        public string name;
        public string tag;
        public string description;
        public string damage;
        public string attack;
        public string move;
        public string toughness;
        public string special;
    }
}