using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main Menu flow:
///
///   MainPanel  →  SlotPanel  →  NamePanel  →  [WeaponSelect scene]  →  [Level1]
///
/// MainPanel:
///   CONTINUE  — loads most recent save directly (shown if any save exists)
///   PLAY      — shown when no saves exist, goes straight to SlotPanel
///   LOAD GAME — opens SlotPanel (grayed out if no saves)
///   SETTINGS  — stub
///   EXIT
///
/// SlotPanel:
///   Three slot buttons showing name/floor/level or "Empty"
///   Clicking an existing slot → loads it immediately
///   Clicking an empty slot   → goes to NamePanel
///   Delete button (existing slots only)
///   Back button
///
/// NamePanel:
///   Text input for the save name
///   Confirm → saves the name into GameManager, loads WeaponSelect scene
///   Back → returns to SlotPanel
///
/// Hierarchy:
///   Canvas
///   └── MainMenuRoot          ← attach this script
///       ├── MainPanel
///       │   ├── TitleLabel        (TMP_Text)
///       │   ├── PlayContinueButton(Button)
///       │   │   └── Label         (TMP_Text)
///       │   ├── LoadGameButton    (Button)
///       │   │   └── Label         (TMP_Text)
///       │   ├── SettingsButton    (Button)
///       │   └── ExitButton        (Button)
///       ├── SlotPanel
///       │   ├── SlotButton_0      (Button)
///       │   │   ├── SlotNameLabel (TMP_Text)
///       │   │   └── SlotSubLabel  (TMP_Text)
///       │   │   └── DeleteX_0     (Button) small X on the slot — only shown if save exists
///       │   ├── SlotButton_1      (same structure)
///       │   ├── SlotButton_2      (same structure)
///       │   ├── ConfirmDeletePanel (inactive by default)
///       │   │   ├── ConfirmDeleteLabel  (TMP_Text) "Delete this save?"
///       │   │   ├── YesButton           (Button) "Yes, Delete"
///       │   │   └── NoButton            (Button) "Cancel"
///       │   └── BackButton        (Button)
///       └── NamePanel
///           ├── PromptLabel       (TMP_Text) "Name your adventure:"
///           ├── NameInputField    (TMP_InputField)
///           ├── ConfirmButton     (Button)
///           └── BackButton2       (Button) "Back"
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Scene Names")]
    public string weaponSelectScene = "WeaponSelect";

    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject slotPanel;
    public GameObject namePanel;

    [Header("Main Panel")]
    public Button   playContinueButton;
    public TMP_Text playContinueLabel;
    public Button   loadGameButton;
    public TMP_Text loadGameLabel;
    public Button   settingsButton;
    public Button   exitButton;

    [Header("Slot Panel")]
    public Button[]   slotButtons    = new Button[3];
    public TMP_Text[] slotNameLabels = new TMP_Text[3];
    public TMP_Text[] slotSubLabels  = new TMP_Text[3];
    [Tooltip("One small X button per slot, child of each SlotButton. Only visible on filled slots.")]
    public Button[]   deleteXButtons = new Button[3];
    public Button     slotBackButton;

    [Header("Confirm Delete Panel")]
    public GameObject confirmDeletePanel;
    public TMP_Text   confirmDeleteLabel;
    public Button     confirmYesButton;
    public Button     confirmNoButton;

    [Header("Name Panel")]
    public TMP_Text       promptLabel;
    public TMP_InputField nameInputField;
    public Button         confirmButton;
    public Button         nameBackButton;

    [Header("Colors")]
    public Color activeSlotColor   = new Color(0.28f, 0.28f, 0.28f, 1f);
    public Color inactiveSlotColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    public Color disabledTextColor = new Color(0.38f, 0.38f, 0.38f, 1f);
    public Color normalTextColor   = new Color(1f,    1f,    1f,    1f);

    // ── Private state ──
    private int  _selectedSlot    = -1;
    private int  _mostRecentSlot  = -1;
    private bool _anySaveExists   = false;

    // ═════════════════════════════════════════════════════════════

    void Start()
    {
        CursorManager.Request("mainmenu");
        Time.timeScale = 1f;

        ScanSaves();

        // Main panel
        playContinueButton?.onClick.AddListener(OnPlayContinue);
        loadGameButton    ?.onClick.AddListener(OnLoadGame);
        settingsButton    ?.onClick.AddListener(OnSettings);
        exitButton        ?.onClick.AddListener(OnExit);

        // Slot panel
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            slotButtons[i]?.onClick.AddListener(() => OnSlotClicked(slot));
        }
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            deleteXButtons[i]?.onClick.AddListener(() => OnDeleteXClicked(slot));
        }
        slotBackButton  ?.onClick.AddListener(ShowMainPanel);
        confirmYesButton?.onClick.AddListener(OnConfirmDeleteYes);
        confirmNoButton ?.onClick.AddListener(OnConfirmDeleteNo);

        // Name panel
        confirmButton ?.onClick.AddListener(OnNameConfirmed);
        nameBackButton?.onClick.AddListener(ShowSlotPanel);

        ShowMainPanel();
    }

    void OnDestroy() => CursorManager.Release("mainmenu");

    // ═════════════════════════════════════════════════════════════
    // Save scanning
    // ═════════════════════════════════════════════════════════════

    private void ScanSaves()
    {
        _anySaveExists  = false;
        _mostRecentSlot = -1;
        float latest    = -1f;

        for (int i = 0; i < SaveSystem.SlotCount; i++)
        {
            if (!SaveSystem.SlotHasData(i)) continue;
            _anySaveExists = true;
            SaveData d = SaveSystem.Load(i);
            if (d.totalPlayTime > latest)
            {
                latest          = d.totalPlayTime;
                _mostRecentSlot = i;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Panel switching
    // ═════════════════════════════════════════════════════════════

    private void ShowMainPanel()
    {
        SetPanels(main: true, slot: false, name: false);
        _selectedSlot = -1;
        ScanSaves();
        RefreshMainPanel();
    }

    private void ShowSlotPanel()
    {
        SetPanels(main: false, slot: true, name: false);
        HideConfirmDelete();
        RefreshSlots();
        if (_mostRecentSlot >= 0)
            HighlightSlot(_mostRecentSlot);
        else
            HighlightSlot(-1);
    }

    private void ShowNamePanel()
    {
        SetPanels(main: false, slot: false, name: true);

        if (promptLabel    != null) promptLabel.text    = "Name your adventure:";
        if (nameInputField != null) nameInputField.text = "";
    }

    private void SetPanels(bool main, bool slot, bool name)
    {
        mainPanel?.SetActive(main);
        slotPanel?.SetActive(slot);
        namePanel?.SetActive(name);
    }

    // ═════════════════════════════════════════════════════════════
    // Main panel
    // ═════════════════════════════════════════════════════════════

    private void RefreshMainPanel()
    {
        if (playContinueLabel != null)
            playContinueLabel.text = _anySaveExists ? "CONTINUE" : "PLAY";

        if (loadGameButton != null)
            loadGameButton.interactable = _anySaveExists;

        // Don't touch the label color — let Unity's button interactable
        // color block handle the visual state. This avoids the text
        // going white or invisible against the button background.
    }

    // ═════════════════════════════════════════════════════════════
    // Slot panel
    // ═════════════════════════════════════════════════════════════

    private void RefreshSlots()
    {
        ScanSaves();

        for (int i = 0; i < 3; i++)
        {
            SaveData d = SaveSystem.Load(i);

            if (slotNameLabels[i] == null)
                Debug.LogWarning($"[MainMenuUI] slotNameLabels[{i}] is null — " +
                                 "drag the TMP_Text into the slot in the Inspector.");
            if (slotSubLabels[i] == null)
                Debug.LogWarning($"[MainMenuUI] slotSubLabels[{i}] is null — " +
                                 "drag the TMP_Text into the slot in the Inspector.");

            if (d != null && d.hasData)
            {
                string saveName = string.IsNullOrEmpty(d.saveName)
                    ? ("Save " + (i + 1))
                    : d.saveName;

                string floor = "Floor " + d.currentFloor;
                string lv    = "Lv " + d.currentLevel;
                string time  = FormatTime(d.totalPlayTime);

                SetLabel(slotNameLabels[i], saveName + "  —  " + floor + "  " + lv);
                SetLabel(slotSubLabels[i],  d.saveDate + "   " + time);

                Debug.Log($"[MainMenuUI] Slot {i}: '{saveName}' floor:{d.currentFloor} lv:{d.currentLevel}");
            }
            else
            {
                SetLabel(slotNameLabels[i], "— Empty Slot " + (i + 1) + " —");
                SetLabel(slotSubLabels[i],  "Click to start a new game");

                Debug.Log($"[MainMenuUI] Slot {i}: empty");
            }
        }

        RefreshDeleteXButtons();
    }

    private void HighlightSlot(int slot)
    {
        _selectedSlot = slot;
        for (int i = 0; i < 3; i++)
        {
            if (slotButtons[i] == null) continue;
            var cb = slotButtons[i].colors;
            cb.normalColor = (i == slot) ? activeSlotColor : inactiveSlotColor;
            slotButtons[i].colors = cb;
        }
        RefreshDeleteXButtons();
    }

    private void RefreshDeleteXButtons()
    {
        for (int i = 0; i < 3; i++)
        {
            if (deleteXButtons[i] == null) continue;
            // X button only visible on slots that have save data
            deleteXButtons[i].gameObject.SetActive(SaveSystem.SlotHasData(i));
        }
    }

    private void OnSlotClicked(int slot)
    {
        HighlightSlot(slot);

        if (SaveSystem.SlotHasData(slot))
        {
            // Existing save — load it directly
            GameManager.Instance?.ContinueGame(slot);
        }
        else
        {
            // Empty slot — go to name input
            _selectedSlot = slot;
            ShowNamePanel();
        }
    }

    private void OnDeleteXClicked(int slot)
    {
        // Don't act if there's no save in this slot
        if (!SaveSystem.SlotHasData(slot)) return;

        _selectedSlot = slot;
        ShowConfirmDelete(slot);
    }

    private void ShowConfirmDelete(int slot)
    {
        if (confirmDeletePanel != null)
            confirmDeletePanel.SetActive(true);

        SaveData d    = SaveSystem.Load(slot);
        string   name = !string.IsNullOrEmpty(d.saveName) ? d.saveName : ("Save " + (slot + 1));

        if (confirmDeleteLabel != null)
            confirmDeleteLabel.text = "Delete [" + name + "] - This cannot be undone.";
    }

    private void HideConfirmDelete()
    {
        if (confirmDeletePanel != null)
            confirmDeletePanel.SetActive(false);
    }

    private void OnConfirmDeleteYes()
    {
        if (_selectedSlot < 0) return;
        SaveSystem.Delete(_selectedSlot);
        HideConfirmDelete();
        HighlightSlot(-1);
        RefreshSlots();
        ScanSaves();
        RefreshMainPanel();
    }

    private void OnConfirmDeleteNo()
    {
        HideConfirmDelete();
    }

    // ═════════════════════════════════════════════════════════════
    // Name panel
    // ═════════════════════════════════════════════════════════════

    private void OnNameConfirmed()
    {
        if (_selectedSlot < 0) return;

        string enteredName = nameInputField != null
            ? nameInputField.text.Trim()
            : "";

        if (string.IsNullOrEmpty(enteredName))
            enteredName = $"Save {_selectedSlot + 1}";

        // Tell GameManager to prepare a new game in this slot with this name.
        // WeaponSelect will call GameManager.StartNewGame() after the player
        // picks a weapon so the weapon choice is baked into the first save.
        GameManager.Instance?.PrepareNewGame(_selectedSlot, enteredName, weaponSelectScene);
    }

    // ═════════════════════════════════════════════════════════════
    // Main panel callbacks
    // ═════════════════════════════════════════════════════════════

    private void OnPlayContinue()
    {
        if (_anySaveExists)
            GameManager.Instance?.ContinueGame(_mostRecentSlot);
        else
            ShowSlotPanel();
    }

    private void OnLoadGame() => ShowSlotPanel();

    private void OnSettings()
    {
        Debug.Log("[MainMenuUI] Settings — wire to your settings panel.");
    }

    private void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ═════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════

    private static void SetLabel(TMP_Text t, string s)
    {
        if (t != null) t.text = s;
    }

    private static string FormatTime(float seconds)
    {
        int h = (int)(seconds / 3600);
        int m = (int)(seconds % 3600 / 60);
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}