using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Tab-toggled stat screen for the player.
/// All cursor state goes through CursorManager.
///
/// IMPORTANT: statMenuRoot must be ACTIVE in the Editor.
/// The script hides it at runtime in Start(). If it starts inactive,
/// child TMP/Image components never initialize and label refs will be null.
///
/// Input setup:
///   1. Add a "StatMenu" Button action bound to Tab in your Input Action Asset.
///   2. Drag that action into the [Toggle Action] field in the Inspector.
///
/// Plus buttons:
///   Drag each + Button component (not the GameObject) into the matching slot.
///   The script wires OnClick automatically — no manual Inspector event setup needed.
/// </summary>
public class StatMenuUI : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================

    [Header("Data Sources (auto-found if null)")]
    public EntityStats playerStats;
    public XPSystem    xpSystem;

    [Header("Root Panel")]
    [Tooltip("Must be ACTIVE in the Editor. Script hides it at runtime.")]
    public GameObject statMenuRoot;

    [Header("Header")]
    public Image    playerIconImage;
    public TMP_Text levelLabel;

    [Header("XP Bar")]
    [Tooltip("Script forces Image.Type.Filled at runtime so fillAmount works.")]
    public Image    xpFillImage;
    public TMP_Text xpLabel;

    [Header("Stat Value Labels")]
    public TMP_Text healthValueLabel;
    public TMP_Text strengthValueLabel;
    public TMP_Text staminaValueLabel;
    public TMP_Text speedValueLabel;
    public TMP_Text toughnessValueLabel;

    [Header("Plus Buttons — drag the Button component, not the GameObject")]
    [Tooltip("Script wires OnClick automatically. No Inspector event setup needed.")]
    public Button healthPlusButton;
    public Button strengthPlusButton;
    public Button staminaPlusButton;
    public Button speedPlusButton;

    [Header("Points Label")]
    public TMP_Text pointsAvailableLabel;

    [Header("Input")]
    [Tooltip("Drag your 'StatMenu' InputActionReference here.")]
    public InputActionReference toggleAction;

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================

    private bool _menuOpen    = false;
    private bool _initialized = false;

    private const string CURSOR_OWNER = "statmenu";

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    void Awake()
    {
        // Wire input early so Tab works regardless of active state
        if (toggleAction != null)
        {
            toggleAction.action.performed += OnTogglePerformed;
            toggleAction.action.Enable();
        }
        else
        {
            Debug.LogWarning("[StatMenuUI] No toggleAction assigned. Drag your 'StatMenu' " +
                             "InputActionReference into the Toggle Action field.");
        }

        // Auto-find player components
        if (playerStats == null || xpSystem == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                if (playerStats == null) playerStats = player.GetComponent<EntityStats>();
                if (xpSystem    == null) xpSystem    = player.GetComponent<XPSystem>();
            }
        }

        if (playerStats == null) Debug.LogError("[StatMenuUI] No EntityStats found on Player!");
        if (xpSystem    == null) Debug.LogError("[StatMenuUI] No XPSystem found on Player!");
    }

    void Start()
    {
        // ── Wire button clicks in code so nothing needs setting up in Inspector ──
        // This is why the fields are Button not GameObject — AddListener needs it.
        if (healthPlusButton   != null) healthPlusButton  .onClick.AddListener(OnSpendHealth);
        if (strengthPlusButton != null) strengthPlusButton.onClick.AddListener(OnSpendStrength);
        if (staminaPlusButton  != null) staminaPlusButton .onClick.AddListener(OnSpendStamina);
        if (speedPlusButton    != null) speedPlusButton   .onClick.AddListener(OnSpendSpeed);

        // Force correct fill type — fillAmount is silently ignored on Simple images
        if (xpFillImage != null)
        {
            xpFillImage.type       = Image.Type.Filled;
            xpFillImage.fillMethod = Image.FillMethod.Horizontal;
            xpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        // Subscribe to XP/level events for live refresh while menu is open
        if (xpSystem != null)
        {
            xpSystem.onXPGained.AddListener(OnXPGained);
            xpSystem.onLevelUp.AddListener(RefreshIfOpen);
            xpSystem.onStatPointSpent.AddListener(OnStatPointSpent);
        }

        // Subscribe to stat changes (weapon swaps, resets, etc.)
        if (playerStats != null)
            playerStats.onStatsChanged.AddListener(RefreshIfOpen);

        _initialized = true;

        // Hide on first frame — root must be active in Editor so children init
        SetMenuVisible(false);
    }

    void OnDestroy()
    {
        if (toggleAction != null)
        {
            toggleAction.action.performed -= OnTogglePerformed;
            toggleAction.action.Disable();
        }

        if (xpSystem != null)
        {
            xpSystem.onXPGained.RemoveListener(OnXPGained);
            xpSystem.onLevelUp.RemoveListener(RefreshIfOpen);
            xpSystem.onStatPointSpent.RemoveListener(OnStatPointSpent);
        }

        if (playerStats != null)
            playerStats.onStatsChanged.RemoveListener(RefreshIfOpen);

        // Remove button listeners to avoid ghost callbacks after destroy
        if (healthPlusButton   != null) healthPlusButton  .onClick.RemoveListener(OnSpendHealth);
        if (strengthPlusButton != null) strengthPlusButton.onClick.RemoveListener(OnSpendStrength);
        if (staminaPlusButton  != null) staminaPlusButton .onClick.RemoveListener(OnSpendStamina);
        if (speedPlusButton    != null) speedPlusButton   .onClick.RemoveListener(OnSpendSpeed);

        CursorManager.Release(CURSOR_OWNER);
    }

    // =========================================================================
    // INPUT
    // =========================================================================

    private void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.phase == InputActionPhase.Performed)
            ToggleMenu();
    }

    // Fallback if PlayerInput Send Messages somehow reaches this object
    public void OnStatMenu(InputValue value)
    {
        if (value.isPressed) ToggleMenu();
    }

    // =========================================================================
    // MENU VISIBILITY
    // =========================================================================

    private void ToggleMenu()
    {
        _menuOpen = !_menuOpen;
        SetMenuVisible(_menuOpen);
    }

    private void SetMenuVisible(bool visible)
    {
        _menuOpen = visible;

        if (statMenuRoot != null)
            statMenuRoot.SetActive(visible);
        else
            Debug.LogWarning("[StatMenuUI] statMenuRoot is null — assign it in the Inspector.");

        if (visible)
            CursorManager.Request(CURSOR_OWNER);
        else
            CursorManager.Release(CURSOR_OWNER);

        if (visible && _initialized)
            RefreshAll();
    }

    private void RefreshIfOpen()
    {
        if (_menuOpen && _initialized) RefreshAll();
    }

    // =========================================================================
    // EVENT RELAY
    // =========================================================================

    private void OnXPGained(int _)       => RefreshIfOpen();
    private void OnStatPointSpent(int _) => RefreshIfOpen();

    // =========================================================================
    // DATA REFRESH
    // =========================================================================

    private void RefreshAll()
    {
        RefreshLevel();
        RefreshXPBar();
        RefreshStats();
        RefreshPlusButtons();
        RefreshPointsLabel();
    }

    private void RefreshLevel()
    {
        if (levelLabel == null || xpSystem == null) return;
        levelLabel.text = $"LEVEL  {xpSystem.CurrentLevel}";
    }

    private void RefreshXPBar()
    {
        if (xpSystem == null) return;

        float ratio = xpSystem.XPToNextLevel > 0
            ? Mathf.Clamp01((float)xpSystem.CurrentXP / xpSystem.XPToNextLevel)
            : 1f;

        if (xpFillImage != null)
            xpFillImage.fillAmount = ratio;

        if (xpLabel != null)
            xpLabel.text = $"{xpSystem.CurrentXP} / {xpSystem.XPToNextLevel} XP";
    }

    private void RefreshStats()
    {
        if (playerStats == null) return;

        // Health shows current / max so the player always knows their ceiling
        SetLabel(healthValueLabel,    $"{playerStats.CurrentHealth} / {playerStats.MaxHealth}");
        SetLabel(strengthValueLabel,  playerStats.Strength.ToString());
        SetLabel(staminaValueLabel,   $"{playerStats.CurrentStamina} / {playerStats.MaxStamina}");
        SetLabel(speedValueLabel,     playerStats.Speed.ToString());
        SetLabel(toughnessValueLabel, playerStats.Toughness.ToString());
    }

    private void RefreshPlusButtons()
    {
        bool hasPoints = xpSystem != null && xpSystem.UnspentPoints > 0;

        SetButtonActive(healthPlusButton,   hasPoints);
        SetButtonActive(strengthPlusButton, hasPoints);
        SetButtonActive(staminaPlusButton,  hasPoints);
        SetButtonActive(speedPlusButton,    hasPoints);
        // Toughness has no + button — weapon-driven, not leveled
    }

    private void RefreshPointsLabel()
    {
        if (pointsAvailableLabel == null || xpSystem == null) return;
        int pts = xpSystem.UnspentPoints;
        pointsAvailableLabel.text = pts > 0 ? $"STAT POINTS: {pts}" : string.Empty;
    }

    // =========================================================================
    // BUTTON CALLBACKS — wired in Start(), do not need Inspector OnClick setup
    // =========================================================================

    private void OnSpendHealth()   => SpendPoint("health");
    private void OnSpendStrength() => SpendPoint("strength");
    private void OnSpendStamina()  => SpendPoint("stamina");
    private void OnSpendSpeed()    => SpendPoint("speed");

    private void SpendPoint(string stat)
    {
        if (xpSystem == null)
        {
            Debug.LogError("[StatMenuUI] xpSystem is null — cannot spend point.");
            return;
        }

        bool spent = xpSystem.SpendPoint(stat);

        if (spent)
        {
            Debug.Log($"[StatMenuUI] Spent point on {stat}.");
            RefreshAll();
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static void SetLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

    // Hides/shows the button's GameObject via the Button component reference
    private static void SetButtonActive(Button btn, bool active)
    {
        if (btn != null) btn.gameObject.SetActive(active);
    }
}