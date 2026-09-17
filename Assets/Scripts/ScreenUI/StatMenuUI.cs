using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Tab-key stat menu for spending level-up points (Health / Strength / Stamina / Speed).
/// Reads and writes EntityStats + XPSystem; per-floor point caps come from PlayerStatBlock.
/// </summary>
public class StatMenuUI : MonoBehaviour
{
    // ── INSPECTOR REFERENCES ──

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
    public Image    xpFillImage;
    public TMP_Text xpLabel;

    [Header("Stat Value Labels")]
    public TMP_Text healthValueLabel;
    public TMP_Text strengthValueLabel;
    public TMP_Text staminaValueLabel;
    public TMP_Text speedValueLabel;
    public TMP_Text toughnessValueLabel;

    [Header("Plus Buttons — drag the Button component, not the GameObject")]
    public Button healthPlusButton;
    public Button strengthPlusButton;
    public Button staminaPlusButton;
    public Button speedPlusButton;

    [Header("Points Label")]
    public TMP_Text pointsAvailableLabel;

    [Header("Side Panel — Damage")]
    [Tooltip("Shows flat damage output for current weapon + Strength. e.g. '20'")]
    public TMP_Text damageValueLabel;

    [Header("Side Panel — Speed")]
    [Tooltip("Shows walk speed in m/s. e.g. '6.0 m/s'")]
    public TMP_Text moveSpeedLabel;
    [Tooltip("Shows attack cooldown in seconds. e.g. '1.0s'")]
    public TMP_Text attackSpeedLabel;

    [Header("Damage Preview")]
    [Tooltip("Label showing current weapon damage output based on Strength + weapon multiplier.")]

    [Header("Input")]
    public InputActionReference toggleAction;

    [Header("Open animation")]
    [Tooltip("Faded and scaled up as the menu opens. Added to Stat Menu Root " +
             "automatically if left empty.")]
    public CanvasGroup menuGroup;
    [Tooltip("Higher = snappier. Runs on UNSCALED time, because opening this " +
             "menu freezes the game and Time.deltaTime is then zero.")]
    public float openFadeSpeed = 18f;
    [Tooltip("Scale the panel rises from as it fades in. 1 = no scale.")]
    public float openFromScale = 0.94f;

    // ── PRIVATE STATE ──

    private bool _menuOpen    = false;
    private bool _initialized = false;
    private int  _lastToggleFrame = -1;   // guards a double-toggle in one frame
    private Coroutine _openAnim;

    private const string CURSOR_OWNER = "statmenu";
    private const string FREEZE_OWNER = "statmenu";

    // ── LIFECYCLE ──

    void Awake()
    {
        if (toggleAction != null)
        {
            toggleAction.action.performed += OnTogglePerformed;
            toggleAction.action.Enable();
        }
        else
        {
            Debug.LogWarning("[StatMenuUI] No toggleAction assigned.");
        }

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
        if (healthPlusButton   != null) healthPlusButton  .onClick.AddListener(OnSpendHealth);
        if (strengthPlusButton != null) strengthPlusButton.onClick.AddListener(OnSpendStrength);
        if (staminaPlusButton  != null) staminaPlusButton .onClick.AddListener(OnSpendStamina);
        if (speedPlusButton    != null) speedPlusButton   .onClick.AddListener(OnSpendSpeed);

        if (xpFillImage != null)
        {
            xpFillImage.type       = Image.Type.Filled;
            xpFillImage.fillMethod = Image.FillMethod.Horizontal;
            xpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        if (xpSystem != null)
        {
            xpSystem.onXPGained.AddListener(OnXPGained);
            xpSystem.onLevelUp.AddListener(RefreshIfOpen);
            xpSystem.onStatPointSpent.AddListener(OnStatPointSpent);
        }

        if (playerStats != null)
            playerStats.onStatsChanged.AddListener(RefreshIfOpen);

        _initialized = true;
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

        if (healthPlusButton   != null) healthPlusButton  .onClick.RemoveListener(OnSpendHealth);
        if (strengthPlusButton != null) strengthPlusButton.onClick.RemoveListener(OnSpendStrength);
        if (staminaPlusButton  != null) staminaPlusButton .onClick.RemoveListener(OnSpendStamina);
        if (speedPlusButton    != null) speedPlusButton   .onClick.RemoveListener(OnSpendSpeed);

        CursorManager.Release(CURSOR_OWNER);
        GameFreeze.Release(FREEZE_OWNER);
    }

    // Safety net: if this menu is hidden/disabled by ANY means (scene unload,
    // a parent deactivating, someone calling SetActive on the root directly)
    // while it was open, make sure we never leak the cursor-visible / frozen
    // state into gameplay — the classic "cursor showing when it shouldn't" bug.
    void OnDisable()
    {
        if (_menuOpen)
        {
            _menuOpen = false;
            CursorManager.Release(CURSOR_OWNER);
            GameFreeze.Release(FREEZE_OWNER);
        }
    }

    // ── INPUT ──

    private void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.phase == InputActionPhase.Performed) ToggleMenu();
    }

    public void OnStatMenu(InputValue value)
    {
        if (value.isPressed) ToggleMenu();
    }

    // ── MENU VISIBILITY ──

    private void ToggleMenu()
    {
        // Guard against Tab firing twice in one frame (action.performed AND the
        // PlayerInput 'Send Messages' OnStatMenu can both land) — same guard the
        // pause menu uses. Without it the menu opens+closes instantly and the
        // cursor flickers.
        if (Time.frameCount == _lastToggleFrame) return;
        _lastToggleFrame = Time.frameCount;

        SetMenuVisible(!_menuOpen);
    }

    private void SetMenuVisible(bool visible)
    {
        _menuOpen = visible;

        if (statMenuRoot != null)
        {
            statMenuRoot.SetActive(visible);
            if (visible) PlayOpenAnimation();
        }
        else
            Debug.LogWarning("[StatMenuUI] statMenuRoot is null.");

        // Cursor + freeze both ref-counted, so this stacks cleanly with the pause menu.
        if (visible)
        {
            CursorManager.Request(CURSOR_OWNER);
            GameFreeze.Request(FREEZE_OWNER);      // freeze the game while allocating points
        }
        else
        {
            CursorManager.Release(CURSOR_OWNER);
            GameFreeze.Release(FREEZE_OWNER);
        }

        if (visible && _initialized) RefreshAll();
    }

    /// <summary>
    /// Quick fade + scale rise so the panel arrives instead of appearing. Kept
    /// to the OPEN direction only: the close is instant, because a panel fading
    /// out while it still holds the cursor and the game freeze is a good way to
    /// let a second Tab press land on a menu that only looks shut.
    /// </summary>
    private void PlayOpenAnimation()
    {
        if (statMenuRoot == null) return;

        if (menuGroup == null)
        {
            menuGroup = statMenuRoot.GetComponent<CanvasGroup>();
            if (menuGroup == null) menuGroup = statMenuRoot.AddComponent<CanvasGroup>();
        }

        if (_openAnim != null) StopCoroutine(_openAnim);
        _openAnim = StartCoroutine(OpenAnimation());
    }

    private System.Collections.IEnumerator OpenAnimation()
    {
        Transform t     = statMenuRoot.transform;
        float     from  = Mathf.Clamp(openFromScale, 0.5f, 1f);
        float     a     = 0f;
        float     guard = 0f;

        menuGroup.alpha = 0f;
        t.localScale    = Vector3.one * from;

        // Unscaled time throughout — see the tooltip on Open Fade Speed. The
        // guard is there so a pathological speed value can't leave the menu
        // hanging invisible forever.
        while (a < 0.999f && guard < 2f)
        {
            float dt = Time.unscaledDeltaTime;
            guard   += dt;
            a        = Mathf.Lerp(a, 1f, 1f - Mathf.Exp(-Mathf.Max(0.01f, openFadeSpeed) * dt));

            menuGroup.alpha = a;
            t.localScale    = Vector3.one * Mathf.Lerp(from, 1f, a);
            yield return null;
        }

        menuGroup.alpha = 1f;
        t.localScale    = Vector3.one;
        _openAnim       = null;
    }

    private void RefreshIfOpen()
    {
        if (_menuOpen && _initialized) RefreshAll();
    }

    // ── EVENT RELAY ──

    private void OnXPGained(int _)       => RefreshIfOpen();
    private void OnStatPointSpent(int _) => RefreshIfOpen();

    // ── DATA REFRESH ──

    private void RefreshAll()
    {
        RefreshLevel();
        RefreshXPBar();
        RefreshStats();
        RefreshPreviews();
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

        if (xpFillImage != null) xpFillImage.fillAmount = ratio;
        if (xpLabel != null)     xpLabel.text = $"{xpSystem.CurrentXP} / {xpSystem.XPToNextLevel} XP";
    }

    private void RefreshStats()
    {
        if (playerStats == null) return;

        SetLabel(healthValueLabel,    $"{playerStats.CurrentHealth} / {playerStats.MaxHealth}");
        SetLabel(strengthValueLabel,  playerStats.Strength.ToString());
        SetLabel(staminaValueLabel,   $"{playerStats.CurrentStamina} / {playerStats.MaxStamina}");
        SetLabel(speedValueLabel,     playerStats.Speed.ToString());
        SetLabel(toughnessValueLabel, playerStats.Toughness.ToString());
    }

    /// <summary>
    /// Shows the player exactly what damage they will deal with their current
    /// </summary>
    private void RefreshPlusButtons()
    {
        bool hasPoints = xpSystem != null && xpSystem.UnspentPoints > 0;

        SetButtonActive(healthPlusButton,   hasPoints);
        SetButtonActive(strengthPlusButton, hasPoints);
        SetButtonActive(staminaPlusButton,  hasPoints);
        SetButtonActive(speedPlusButton,    hasPoints);
    }

    private void RefreshPointsLabel()
    {
        if (pointsAvailableLabel == null || xpSystem == null) return;
        int pts = xpSystem.UnspentPoints;
        pointsAvailableLabel.text = pts > 0 ? $"STAT POINTS: {pts}" : string.Empty;
    }

    // ── BUTTON CALLBACKS ──

    private void OnSpendHealth()   => SpendPoint("health");
    private void OnSpendStrength() => SpendPoint("strength");
    private void OnSpendStamina()  => SpendPoint("stamina");
    private void OnSpendSpeed()    => SpendPoint("speed");

    private void SpendPoint(string stat)
    {
        if (xpSystem == null) return;
        if (xpSystem.SpendPoint(stat)) RefreshAll();
    }

    // ── STAT PREVIEWS ──

    private void RefreshPreviews()
    {
        RefreshDamagePreview();
        RefreshMoveSpeedPreview();
        RefreshAttackSpeedPreview();
    }

    /// <summary>
    /// Flat damage number for the current weapon and Strength.
    /// Blade/Hammer: Strength x multiplier.
    /// Bow: shows "X  /  Y charged" for normal and aimed shot.
    /// </summary>
    private void RefreshDamagePreview()
    {
        if (damageValueLabel == null || playerStats == null ||
            playerStats.playerStatBlock == null) return;

        var sb  = playerStats.playerStatBlock;
        int str = playerStats.Strength;

        string text;
        switch (playerStats.EquippedWeapon)
        {
            case EntityStats.WeaponType.Blade:
                text = Mathf.RoundToInt(str * sb.bladeStrengthMultiplier).ToString();
                break;
            case EntityStats.WeaponType.Hammer:
                text = Mathf.RoundToInt(str * sb.hammerStrengthMultiplier).ToString();
                break;
            case EntityStats.WeaponType.Bow:
                int normal  = Mathf.RoundToInt(str * sb.bowStrengthMultiplier);
                int charged = Mathf.RoundToInt(normal * sb.bowChargedMultiplier);
                text = normal + "  /  " + charged;
                break;
            default:
                text = "0";
                break;
        }

        damageValueLabel.text = text;
    }

    /// <summary>
    /// Walk speed in m/s pulled directly from PlayerMovement.
    /// Reflects Speed stat bonuses and hammer penalty automatically.
    /// </summary>
    private void RefreshMoveSpeedPreview()
    {
        if (moveSpeedLabel == null || playerStats == null) return;

        // Read live walkSpeed from PlayerMovement — already has stat bonuses applied
        PlayerMovement pm = playerStats.GetComponent<PlayerMovement>();
        if (pm == null) { moveSpeedLabel.text = "--"; return; }

        moveSpeedLabel.text = pm.walkSpeed.ToString("F1") + " m/s";
    }

    /// <summary>
    /// Attack cooldown in seconds from PlayerCombat.
    /// Decreases with Speed stat, doubles with Hammer.
    /// </summary>
    private void RefreshAttackSpeedPreview()
    {
        if (attackSpeedLabel == null || playerStats == null) return;

        PlayerCombat pc = playerStats.GetComponent<PlayerCombat>();
        if (pc == null) { attackSpeedLabel.text = "--"; return; }

        attackSpeedLabel.text = pc.CurrentAttackCooldown.ToString("F2") + "s";
    }

    // ── HELPERS ──

    private static void SetLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

    private static void SetButtonActive(Button btn, bool active)
    {
        if (btn != null) btn.gameObject.SetActive(active);
    }
}
