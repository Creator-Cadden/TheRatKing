using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main Menu flow:
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
    public Button   creditsButton;
    public Button   exitButton;

    [Header("Credits")]
    [Tooltip("Scene name loaded when the Credits button is clicked. " +
             "Must be added to File → Build Profiles → Scene List.")]
    public string creditsScene = "Credits";

    [Header("Slot Panel")]
    public Button[]   slotButtons    = new Button[3];
    public TMP_Text[] slotNameLabels = new TMP_Text[3];
    public TMP_Text[] slotSubLabels  = new TMP_Text[3];
    [Tooltip("One small X button per slot, child of each SlotButton. Only visible on filled slots.")]
    public Button[]   deleteXButtons = new Button[3];
    public Button     slotBackButton;

    [Header("Test Arena")]
    [Tooltip("Fourth button in the slot panel — opens WeaponSelect then loads TestingArena. " +
             "Bypasses save slots entirely.")]
    public Button     testWorldButton;
    [Tooltip("Optional sub-label on the Test Arena button.")]
    public TMP_Text   testWorldLabel;

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

    [Header("Intro Sequence")]
    [Tooltip("Play the opening 'presents' → title reveal on load. Turn off for quick testing.")]
    public bool     playIntro       = true;
    [Tooltip("The game's title text (e.g. 'The Rat King'). It shrinks into place on reveal.")]
    public TMP_Text titleText;
    public string   presentsLine    = "The Rat King Team presents";
    [Tooltip("Seconds the 'presents' line stays on screen before fading out.")]
    public float    presentsHold    = 1.3f;
    [Tooltip("Title starts this many times its final size, then shrinks into place.")]
    public float    titleStartScale = 2.2f;

    [Header("Colors")]
    public Color activeSlotColor   = new Color(0.28f, 0.28f, 0.28f, 1f);
    public Color inactiveSlotColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    public Color disabledTextColor = new Color(0.38f, 0.38f, 0.38f, 1f);
    public Color normalTextColor   = new Color(1f,    1f,    1f,    1f);

    // ── Private state ──
    private int  _selectedSlot    = -1;
    private int  _mostRecentSlot  = -1;
    private bool _anySaveExists   = false;


    void Start()
    {
        CursorManager.Request("mainmenu");
        Time.timeScale = 1f;

        ScanSaves();

        // Main panel
        playContinueButton?.onClick.AddListener(OnPlayContinue);
        loadGameButton    ?.onClick.AddListener(OnLoadGame);
        settingsButton    ?.onClick.AddListener(OnSettings);
        creditsButton     ?.onClick.AddListener(OnCredits);
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
        slotBackButton  ?.onClick.AddListener(() => MenuFX.Wipe(ShowMainPanel));
        confirmYesButton?.onClick.AddListener(OnConfirmDeleteYes);
        confirmNoButton ?.onClick.AddListener(OnConfirmDeleteNo);

        // Test Arena — sits in the slot panel as the 4th option
        testWorldButton?.onClick.AddListener(OnTestWorld);
        if (testWorldLabel != null) testWorldLabel.text = "ENTER TEST ARENA";

        // Name panel
        confirmButton ?.onClick.AddListener(OnNameConfirmed);
        nameBackButton?.onClick.AddListener(() => MenuFX.Wipe(ShowSlotPanel));

        if (playIntro)
        {
            // Hold black, show "presents", then reveal the menu + animate the title.
            SetPanels(main: false, slot: false, name: false);
            MenuFX.PlayIntro(presentsLine, presentsHold, RevealMenu);
        }
        else
        {
            ShowMainPanel();
            MenuFX.FadeIn();   // menu lerps in from black on load
        }
    }

    void OnDestroy() => CursorManager.Release("mainmenu");

    // ── Opening reveal ──

    private void RevealMenu()
    {
        ShowMainPanel();   // activates the main panel → buttons slide in

        if (titleText != null)
        {
            Vector3 baseScale = titleText.rectTransform.localScale;
            titleText.rectTransform.localScale = baseScale * titleStartScale;  // start big
            titleText.alpha = 0f;
            StartCoroutine(AnimateTitleIn(baseScale));
        }
    }

    private IEnumerator AnimateTitleIn(Vector3 baseScale)
    {
        var rt = titleText.rectTransform;
        float t = 0f; const float dur = 0.7f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            rt.localScale   = Vector3.Lerp(baseScale * titleStartScale, baseScale, EaseOutCubic(p));
            titleText.alpha = Mathf.Clamp01(p * 1.5f);
            yield return null;
        }
        rt.localScale   = baseScale;
        titleText.alpha = 1f;
    }

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    // ── Save scanning ──

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

    // ── Panel switching ──

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

    // ── Main panel ──

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

    // ── Slot panel ──

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
            // Existing save — fade to black, then load
            MenuFX.FadeOutThen(() => GameManager.Instance?.ContinueGame(slot));
        }
        else
        {
            // Empty slot — wipe to the name input
            _selectedSlot = slot;
            MenuFX.Wipe(ShowNamePanel);
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

    // ── Name panel ──

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
        MenuFX.FadeOutThen(() =>
            GameManager.Instance?.PrepareNewGame(_selectedSlot, enteredName, weaponSelectScene));
    }

    // ── Main panel callbacks ──

    private void OnPlayContinue()
    {
        if (_anySaveExists)
            MenuFX.FadeOutThen(() => GameManager.Instance?.ContinueGame(_mostRecentSlot));
        else
            MenuFX.Wipe(ShowSlotPanel);
    }

    private void OnLoadGame() => MenuFX.Wipe(ShowSlotPanel);

    private void OnTestWorld()
    {
        // Routes through WeaponSelect first so the player can pick a weapon.
        // GameManager handles the no-save plumbing.
        MenuFX.FadeOutThen(() => GameManager.Instance?.EnterTestWorld());
    }

    private void OnSettings()
    {
        Debug.Log("[MainMenuUI] Settings — wire to your settings panel.");
    }

    private void OnCredits()
    {
        if (string.IsNullOrEmpty(creditsScene))
        {
            Debug.LogWarning("[MainMenuUI] Credits Scene field is empty.");
            return;
        }
        MenuFX.FadeOutThen(() =>
            UnityEngine.SceneManagement.SceneManager.LoadScene(creditsScene));
    }

    private void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Helpers ──

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
