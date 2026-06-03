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
            tag         = "Heavy  ·  Slow Swing  ·  High Damage",
            description = "A crushing weight that breaks bones and floors. " +
                          "Each swing hits three times as hard as a blade at the same Strength. " +
                          "Slower swing rhythm — but your footwork is unaffected, so platforming and " +
                          "dodging stay nimble.",
            damage      = "Damage:      Strength  ×  3  per hit",
            attack      = "Attack Speed:  ~2.0s per swing  (about half a blade's — improves with Speed)",
            move        = "Move Speed:  Full speed  (no penalty)",
            toughness   = "Toughness:   +4  (heavy armor bonus)",
            special     = "Jump Attack:  360° slam dealing ×1.5 damage"
        },
        new WeaponInfo
        {
            name        = "Bow",
            tag         = "Ranged  ·  Tactical  ·  Burst Damage",
            description = "Strike from a distance before they can reach you. " +
                          "Free-look shots gently auto-target enemies in front. " +
                          "Hold RMB to aim, then hold LMB for a charged shot — full charge triples your damage.",
            damage      = "Normal Shot:   Strength  ×  1  per arrow",
            attack      = "Charged Shot:  Strength  ×  3  (hold LMB while aiming)",
            move        = "Move Speed:  Reduced by  ⅓  while aiming",
            toughness   = "Toughness:   +0  (no armor bonus)",
            special     = "Jump Attack:  3-arrow burst aimed at the ground below"
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

        // Test Arena was queued from the main menu — skip save slot entirely.
        if (gm.IsPendingTestMode)
        {
            gm.StartTestWorld(WeaponTypes[_selectedIndex]);
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