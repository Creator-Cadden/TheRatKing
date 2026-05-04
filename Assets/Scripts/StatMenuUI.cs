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
///   This script lives on a Canvas object, not on the Player, so PlayerInput
///   Send Messages cannot reach it. Use the InputActionReference field instead:
///   1. Add a "StatMenu" Button action bound to Tab in your Input Action Asset.
///   2. Drag that action into the [Toggle Action] field in the Inspector.
///   3. Done. The script enables/disables the action itself.
///
/// XP fill bar setup:
///   The xpFillImage must have Image Type = Filled and Fill Method = Horizontal
///   in the Inspector, OR the script will force-set it at runtime in Start().
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
    [Tooltip("Image Type must be Filled + Horizontal. Script will force this at runtime.")]
    public Image    xpFillImage;
    public TMP_Text xpLabel;

    [Header("Stat Values")]
    public TMP_Text healthValueLabel;
    public TMP_Text strengthValueLabel;
    public TMP_Text staminaValueLabel;
    public TMP_Text speedValueLabel;
    public TMP_Text toughnessValueLabel;

    [Header("Plus Buttons (hidden when no points available)")]
    public GameObject healthPlusButton;
    public GameObject strengthPlusButton;
    public GameObject staminaPlusButton;
    public GameObject speedPlusButton;

    [Header("Points Label")]
    public TMP_Text pointsAvailableLabel;

    [Header("Input")]
    [Tooltip("Drag your 'StatMenu' InputActionReference here. Script manages Enable/Disable.")]
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
        // Subscribe to input in Awake so it's live regardless of active state
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

        // Find player components early
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
        // Force correct Image type on XP fill bar so fillAmount actually works.
        // If the Image is set to Simple in the Inspector, fillAmount is silently ignored
        // and the bar always renders full. This guarantees the correct setup at runtime.
        if (xpFillImage != null)
        {
            xpFillImage.type       = Image.Type.Filled;
            xpFillImage.fillMethod = Image.FillMethod.Horizontal;
            xpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        // Subscribe to XP/level events
        if (xpSystem != null)
        {
            xpSystem.onXPGained.AddListener(OnXPGained);
            xpSystem.onLevelUp.AddListener(RefreshIfOpen);
            xpSystem.onStatPointSpent.AddListener(OnStatPointSpent);
        }

        _initialized = true;

        // Hide root panel at runtime. statMenuRoot must be active in the Editor
        // so its child components can initialize -- this hides it on frame 1.
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

        // Always release cursor on destroy so nothing gets stuck
        CursorManager.Release(CURSOR_OWNER);
    }

    // =========================================================================
    // INPUT
    // =========================================================================

    /// <summary>
    /// Called by the InputActionReference subscription.
    /// This is the correct path when StatMenuUI is on a Canvas object.
    /// </summary>
    private void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.phase == InputActionPhase.Performed)
            ToggleMenu();
    }

    /// <summary>
    /// Option A fallback — only fires if PlayerInput is on the SAME GameObject
    /// as this script with Behavior = Send Messages.
    /// </summary>
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
            Debug.LogWarning("[StatMenuUI] statMenuRoot is null.");

        // Cursor: request when open, release when closed
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
    // EVENT RELAY (named methods so RemoveListener works correctly)
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

        SetLabel(healthValueLabel,    playerStats.CurrentHealth.ToString());
        SetLabel(strengthValueLabel,  playerStats.Strength.ToString());
        SetLabel(staminaValueLabel,   $"{playerStats.CurrentStamina} / {playerStats.MaxStamina}");
        SetLabel(speedValueLabel,     playerStats.Speed.ToString());
        SetLabel(toughnessValueLabel, playerStats.Toughness.ToString());
    }

    private void RefreshPlusButtons()
    {
        bool hasPoints = xpSystem != null && xpSystem.UnspentPoints > 0;

        SetActive(healthPlusButton,   hasPoints);
        SetActive(strengthPlusButton, hasPoints);
        SetActive(staminaPlusButton,  hasPoints);
        SetActive(speedPlusButton,    hasPoints);
        // Toughness has no + button -- weapon-driven
    }

    private void RefreshPointsLabel()
    {
        if (pointsAvailableLabel == null || xpSystem == null) return;
        int pts = xpSystem.UnspentPoints;
        pointsAvailableLabel.text = pts > 0 ? $"STAT POINTS: {pts}" : string.Empty;
    }

    // =========================================================================
    // BUTTON CALLBACKS -- wire each + Button's OnClick() to these in Inspector
    // =========================================================================

    public void OnSpendHealth()   => SpendPoint("health");
    public void OnSpendStrength() => SpendPoint("strength");
    public void OnSpendStamina()  => SpendPoint("stamina");
    public void OnSpendSpeed()    => SpendPoint("speed");

    private void SpendPoint(string stat)
    {
        if (xpSystem == null) return;
        xpSystem.SpendPoint(stat);
        RefreshAll();
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static void SetLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}