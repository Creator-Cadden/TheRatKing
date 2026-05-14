using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main menu with title, slot selection, continue/new game, and delete.
///
/// Hierarchy:
///   Canvas
///   └── MainMenuRoot
///       ├── TitleLabel          (TMP_Text)
///       ├── MainPanel           shown first — Play / Quit
///       │   ├── PlayButton      (Button)
///       │   └── QuitButton      (Button)
///       └── SlotPanel           shown after Play — three save slots
///           ├── SlotButton_0    (Button)
///           ├── SlotButton_1    (Button)
///           ├── SlotButton_2    (Button)
///           ├── DeleteButton    (Button) — deletes selected slot
///           ├── NewGameButton   (Button) — starts fresh in selected slot
///           └── BackButton      (Button) — back to main panel
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Scene To Load")]
    [Tooltip("Name of the first game scene for a new game.")]
    public string firstGameScene = "Floor1";

    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject slotPanel;

    [Header("Main Panel")]
    public Button playButton;
    public Button quitButton;

    [Header("Slot Panel")]
    public Button[]   slotButtons   = new Button[3];
    public TMP_Text[] slotLabels    = new TMP_Text[3];
    public TMP_Text[] slotSubLabels = new TMP_Text[3];   // date / play time
    public Button     newGameButton;
    public Button     deleteButton;
    public Button     backButton;

    // ── Private ──
    private int _selectedSlot = -1;

    // ═════════════════════════════════════════════════════════════

    void Start()
    {
        CursorManager.Request("mainmenu");
        Time.timeScale = 1f;

        // Main panel buttons
        playButton?.onClick.AddListener(OnPlayClicked);
        quitButton?.onClick.AddListener(OnQuitClicked);

        // Slot buttons
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            slotButtons[i]?.onClick.AddListener(() => SelectSlot(slot));
        }

        newGameButton?.onClick.AddListener(OnNewGame);
        deleteButton ?.onClick.AddListener(OnDeleteSlot);
        backButton   ?.onClick.AddListener(ShowMainPanel);

        ShowMainPanel();
    }

    void OnDestroy()
    {
        CursorManager.Release("mainmenu");
    }

    // ═════════════════════════════════════════════════════════════
    // Panel switching
    // ═════════════════════════════════════════════════════════════

    private void ShowMainPanel()
    {
        mainPanel?.SetActive(true);
        slotPanel?.SetActive(false);
        _selectedSlot = -1;
    }

    private void ShowSlotPanel()
    {
        mainPanel?.SetActive(false);
        slotPanel?.SetActive(true);
        RefreshSlots();
        SelectSlot(0);    // default to first slot
    }

    // ═════════════════════════════════════════════════════════════
    // Slot display
    // ═════════════════════════════════════════════════════════════

    private void RefreshSlots()
    {
        for (int i = 0; i < 3; i++)
        {
            SaveData data = SaveSystem.Load(i);

            if (data.hasData)
            {
                SetLabel(slotLabels[i],    $"Save {i + 1}  —  Floor {data.currentFloor}  Lv{data.currentLevel}");
                SetLabel(slotSubLabels[i], $"{data.saveDate}   {FormatTime(data.totalPlayTime)}");
            }
            else
            {
                SetLabel(slotLabels[i],    $"Save {i + 1}  —  Empty");
                SetLabel(slotSubLabels[i], "");
            }
        }

        RefreshSlotButtons();
    }

    private void SelectSlot(int slot)
    {
        _selectedSlot = slot;
        RefreshSlotButtons();
    }

    private void RefreshSlotButtons()
    {
        bool hasSave = _selectedSlot >= 0 && SaveSystem.SlotHasData(_selectedSlot);

        // Highlight selected slot
        for (int i = 0; i < 3; i++)
        {
            if (slotButtons[i] == null) continue;
            var colors = slotButtons[i].colors;
            colors.normalColor = (i == _selectedSlot)
                ? new Color(0.25f, 0.25f, 0.25f, 1f)
                : new Color(0.12f, 0.12f, 0.12f, 1f);
            slotButtons[i].colors = colors;
        }

        // "Continue" if save exists, "New Game" label changes
        if (newGameButton != null)
        {
            var label = newGameButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = hasSave ? "Continue" : "New Game";
        }

        // Delete only available when save exists
        deleteButton?.gameObject.SetActive(hasSave);
    }

    // ═════════════════════════════════════════════════════════════
    // Button callbacks
    // ═════════════════════════════════════════════════════════════

    private void OnPlayClicked() => ShowSlotPanel();

    private void OnNewGame()
    {
        if (_selectedSlot < 0) return;

        if (SaveSystem.SlotHasData(_selectedSlot))
        {
            // Slot has data — Continue
            GameManager.Instance?.ContinueGame(_selectedSlot);
        }
        else
        {
            // Empty slot — New Game
            GameManager.Instance?.StartNewGame(_selectedSlot, firstGameScene);
        }
    }

    private void OnDeleteSlot()
    {
        if (_selectedSlot < 0) return;
        SaveSystem.Delete(_selectedSlot);
        RefreshSlots();
    }

    private void OnQuitClicked()
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

    private static void SetLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

    private static string FormatTime(float seconds)
    {
        int h = (int)(seconds / 3600);
        int m = (int)(seconds % 3600 / 60);
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}