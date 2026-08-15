using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Death screen — references YOUR own Canvas hierarchy.
/// No auto-building. Set up the UI however you want in the Editor,
/// then drag the references in here.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class DeathScreen : MonoBehaviour
{
    [Header("Root Panel")]
    [Tooltip("Drag your DeathScreenRoot GameObject here. Must be active in Editor, hidden at runtime by Start().")]
    public GameObject deathMenuRoot;

    [Header("Labels")]
    public TMP_Text titleLabel;
    public TMP_Text subtitleLabel;
    public TMP_Text levelInfoLabel;

    [Header("Buttons")]
    public Button restartButton;
    public Button settingsButton;
    public Button menuButton;

    [Header("Content")]
    public string titleText    = "YOU DIED";
    public string subtitleText = "The rats reclaim the dark.";

    [Header("Fade")]
    public float fadeInDuration  = 0.8f;
    public float fadeOutDuration = 0.3f;

    private CanvasGroup _group;
    private Coroutine   _fadeRoutine;
    private const string CURSOR_OWNER = "death";


    void Awake()
    {
        _group = GetComponent<CanvasGroup>();

        restartButton ?.onClick.AddListener(OnRestartClicked);
        settingsButton?.onClick.AddListener(OnSettingsClicked);
        menuButton    ?.onClick.AddListener(OnMenuClicked);
    }

    void Start()
    {
        // Hide at runtime — root must be active in Editor so children initialize
        SetRootVisible(false);
        _group.alpha          = 0f;
        _group.interactable   = false;
        _group.blocksRaycasts = false;
    }

    void OnDestroy()
    {
        CursorManager.Release(CURSOR_OWNER);
    }

    // ── Public API — called by GameManager ──

    public void Show()
    {
        SetRootVisible(true);
        SetInteractable(true);

        // Fill static labels
        if (titleLabel    != null) titleLabel.text    = titleText;
        if (subtitleLabel != null) subtitleLabel.text = subtitleText;

        // Fill last-save info
        if (levelInfoLabel != null)
            levelInfoLabel.text = BuildLevelInfoString();

        FadeTo(1f, fadeInDuration);
        CursorManager.Request(CURSOR_OWNER);
    }

    public void Hide(bool instant)
    {
        SetInteractable(false);
        CursorManager.Release(CURSOR_OWNER);

        if (instant)
        {
            StopAllCoroutines();
            _group.alpha = 0f;
            SetRootVisible(false);
        }
        else
        {
            FadeTo(0f, fadeOutDuration, () => SetRootVisible(false));
        }
    }

    // ── Level info string ──

    private string BuildLevelInfoString()
    {
        GameManager gm = GameManager.Instance;

        if (gm == null || !gm.HasActiveGame || gm.ActiveSave == null)
            return "Last save: unknown";

        SaveData s       = gm.ActiveSave;
        string saveName  = string.IsNullOrEmpty(s.saveName)
                           ? "Save " + (gm.ActiveSlot + 1)
                           : s.saveName;
        string saveDate  = string.IsNullOrEmpty(s.saveDate) ? "unknown time" : s.saveDate;
        string floor     = "Floor " + s.currentFloor;
        string scene     = s.currentSceneName;

        // e.g. "Last save: My Run  —  Floor 1  ·  lvl1  ·  May 13  14:32"
        return "Last save: " + saveName
             + "  —  " + floor + "  ·  " + scene
             + "  ·  " + saveDate;
    }

    // ── Button callbacks ──

    private void OnRestartClicked()
    {
        SetInteractable(false);
        Hide(instant: false);
        GameManager.Instance?.ResetToCheckpoint();
    }

    private void OnSettingsClicked()
    {
        // Wire to your settings panel when ready
        Debug.Log("[DeathScreen] Settings clicked — wire to your settings panel.");
    }

    private void OnMenuClicked()
    {
        SetInteractable(false);
        Hide(instant: true);
        GameManager.Instance?.ReturnToMainMenu();
    }

    // ── Helpers ──

    private void SetRootVisible(bool visible)
    {
        if (deathMenuRoot != null)
            deathMenuRoot.SetActive(visible);
        else
            Debug.LogWarning("[DeathScreen] deathMenuRoot is null — " +
                             "drag your DeathScreenRoot into the Inspector.");
    }

    private void SetInteractable(bool state)
    {
        _group.interactable   = state;
        _group.blocksRaycasts = state;
    }

    private void FadeTo(float target, float duration, System.Action onComplete = null)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine(target, duration, onComplete));
    }

    private IEnumerator FadeRoutine(float target, float duration, System.Action onComplete)
    {
        float start   = _group.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed      += Time.unscaledDeltaTime;
            _group.alpha  = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }
        _group.alpha = target;
        onComplete?.Invoke();
    }
}
