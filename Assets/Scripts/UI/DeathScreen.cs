using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls the death / game-over overlay.
/// All cursor state goes through CursorManager.
///
/// UI Hierarchy:
///   Canvas  (Screen Space - Overlay, sort order 10)
///   └── DeathScreenRoot          <- attach DeathScreen here
///       ├── Backdrop             (Image, full-screen, black semi-transparent)
///       ├── Panel                (Image, centered dark card)
///       │   ├── TitleLabel       (TMP_Text "YOU DIED")
///       │   ├── SubtitleLabel    (TMP_Text optional flavour)
///       │   └── RetryButton      (Button -> TMP_Text child "RETRY")
///       └── VignetteImage        (Image, full-screen, optional)
///
/// Set autoBuiltLayout = true and the script builds the whole hierarchy at Start().
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class DeathScreen : MonoBehaviour
{
    [Header("References - leave null to auto-build")]
    public Button   retryButton;
    public TMP_Text titleLabel;
    public TMP_Text subtitleLabel;

    [Header("Content")]
    public string titleText    = "YOU DIED";
    public string subtitleText = "The rats reclaim the dark.";

    [Header("Fade")]
    public float fadeInDuration  = 0.6f;
    public float fadeOutDuration = 0.35f;

    [Header("Auto-build Layout")]
    public bool autoBuiltLayout = true;

    private CanvasGroup _group;
    private Coroutine   _fadeRoutine;

    // Cursor owner key
    private const string CURSOR_OWNER = "death";

    void Awake()
    {
        _group = GetComponent<CanvasGroup>();

        if (autoBuiltLayout)
            BuildLayout();

        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetryClicked);
    }

    void OnDestroy()
    {
        CursorManager.Release(CURSOR_OWNER);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Show()
    {
        gameObject.SetActive(true);
        SetInteractable(true);
        FadeTo(1f, fadeInDuration);

        // Player died — we need the cursor so they can click Retry
        CursorManager.Request(CURSOR_OWNER);
    }

    public void Hide(bool instant)
    {
        SetInteractable(false);

        // Release cursor before hiding — game is resuming
        CursorManager.Release(CURSOR_OWNER);

        if (instant)
        {
            StopAllCoroutines();
            _group.alpha = 0f;
            gameObject.SetActive(false);
        }
        else
        {
            FadeTo(0f, fadeOutDuration, onComplete: () => gameObject.SetActive(false));
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void OnRetryClicked()
    {
        SetInteractable(false);
        GameManager.Instance?.Retry();
    }

    private void SetInteractable(bool state)
    {
        _group.interactable   = state;
        _group.blocksRaycasts = state;
    }

    // ── Fade ──────────────────────────────────────────────────────────────────

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

    // ── Auto-build Layout ─────────────────────────────────────────────────────

    private void BuildLayout()
    {
        GameObject backdropGO = CreateUIObject("Backdrop", transform);
        Image backdrop        = backdropGO.AddComponent<Image>();
        backdrop.color        = new Color(0f, 0f, 0f, 0.82f);
        StretchFull(backdropGO.GetComponent<RectTransform>());

        GameObject panelGO    = CreateUIObject("Panel", transform);
        Image panelImg        = panelGO.AddComponent<Image>();
        panelImg.color        = new Color(0.05f, 0.03f, 0.03f, 0.95f);

        RectTransform panelRt    = panelGO.GetComponent<RectTransform>();
        panelRt.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRt.pivot            = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta        = new Vector2(480f, 320f);
        panelRt.anchoredPosition = Vector2.zero;

        GameObject titleGO    = CreateUIObject("TitleLabel", panelGO.transform);
        titleLabel            = titleGO.AddComponent<TextMeshProUGUI>();
        titleLabel.text       = titleText;
        titleLabel.fontSize   = 64f;
        titleLabel.fontStyle  = FontStyles.Bold;
        titleLabel.alignment  = TextAlignmentOptions.Center;
        titleLabel.color      = new Color(0.85f, 0.12f, 0.12f, 1f);

        RectTransform titleRt = titleGO.GetComponent<RectTransform>();
        titleRt.anchorMin     = new Vector2(0f, 0.55f);
        titleRt.anchorMax     = new Vector2(1f, 0.9f);
        titleRt.offsetMin     = new Vector2(20f, 0f);
        titleRt.offsetMax     = new Vector2(-20f, 0f);

        if (!string.IsNullOrEmpty(subtitleText))
        {
            GameObject subGO       = CreateUIObject("SubtitleLabel", panelGO.transform);
            subtitleLabel          = subGO.AddComponent<TextMeshProUGUI>();
            subtitleLabel.text     = subtitleText;
            subtitleLabel.fontSize = 18f;
            subtitleLabel.fontStyle  = FontStyles.Italic;
            subtitleLabel.alignment  = TextAlignmentOptions.Center;
            subtitleLabel.color    = new Color(0.65f, 0.55f, 0.55f, 1f);

            RectTransform subRt   = subGO.GetComponent<RectTransform>();
            subRt.anchorMin       = new Vector2(0f, 0.38f);
            subRt.anchorMax       = new Vector2(1f, 0.55f);
            subRt.offsetMin       = new Vector2(20f, 0f);
            subRt.offsetMax       = new Vector2(-20f, 0f);
        }

        GameObject btnGO   = CreateUIObject("RetryButton", panelGO.transform);
        retryButton        = btnGO.AddComponent<Button>();
        Image btnImg       = btnGO.AddComponent<Image>();
        btnImg.color       = new Color(0.72f, 0.08f, 0.08f, 1f);

        ColorBlock cb         = retryButton.colors;
        cb.normalColor        = new Color(0.72f, 0.08f, 0.08f, 1f);
        cb.highlightedColor   = new Color(0.88f, 0.15f, 0.15f, 1f);
        cb.pressedColor       = new Color(0.50f, 0.05f, 0.05f, 1f);
        cb.selectedColor      = cb.normalColor;
        cb.fadeDuration       = 0.1f;
        retryButton.colors    = cb;
        retryButton.targetGraphic = btnImg;

        RectTransform btnRt      = btnGO.GetComponent<RectTransform>();
        btnRt.anchorMin          = new Vector2(0.5f, 0f);
        btnRt.anchorMax          = new Vector2(0.5f, 0f);
        btnRt.pivot              = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta          = new Vector2(200f, 52f);
        btnRt.anchoredPosition   = new Vector2(0f, 32f);

        GameObject btnTextGO = CreateUIObject("Label", btnGO.transform);
        TMP_Text btnText     = btnTextGO.AddComponent<TextMeshProUGUI>();
        btnText.text         = "RETRY";
        btnText.fontSize     = 22f;
        btnText.fontStyle    = FontStyles.Bold;
        btnText.alignment    = TextAlignmentOptions.Center;
        btnText.color        = Color.white;
        StretchFull(btnTextGO.GetComponent<RectTransform>());

        retryButton.onClick.RemoveAllListeners();
        retryButton.onClick.AddListener(OnRetryClicked);
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}