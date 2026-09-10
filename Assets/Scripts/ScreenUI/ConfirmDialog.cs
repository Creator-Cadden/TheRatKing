using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One reusable "are you sure?" modal. MainMenuUI already hand-rolls a delete
/// confirmation; this replaces that pattern everywhere else — quitting to menu
/// mid-run, overwriting a save, spending your last Rat Coin, abandoning a floor.
///
/// Usage:
///     ConfirmDialog.Ask("Return to menu?",
///                       "Unsaved progress since the last checkpoint will be lost.",
///                       "Quit run", "Keep playing",
///                       onConfirm: () => GameManager.Instance.ReturnToMainMenu());
///
/// Put one on your top-most canvas. It pauses the game while open (unscaled time
/// throughout, so it still animates).
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    public static ConfirmDialog Instance { get; private set; }

    private const string CURSOR_OWNER = "confirmdialog";

    [Header("Root")]
    [Tooltip("Leave ACTIVE in the editor. Hidden in Start.")]
    public GameObject dialogRoot;
    public CanvasGroup canvasGroup;

    [Header("Content")]
    public TMP_Text titleLabel;
    public TMP_Text bodyLabel;

    [Header("Buttons")]
    public Button   confirmButton;
    public TMP_Text confirmLabel;
    public Button   cancelButton;
    public TMP_Text cancelLabel;

    [Header("Behaviour")]
    [Tooltip("Freeze the game while the dialog is up.")]
    public bool pauseWhileOpen = true;

    [Tooltip("Focus the CANCEL button when the dialog opens, so a mashed Enter " +
             "doesn't confirm a destructive action. Leave this on.")]
    public bool focusCancelByDefault = true;

    public float fadeDuration = 0.14f;

    // ── Private ───────────────────────────────────────────────────────────────

    private Action _onConfirm;
    private Action _onCancel;
    private bool   _open;
    private float  _fade;
    private bool   _wasPaused;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        if (dialogRoot == null) dialogRoot = gameObject;

        confirmButton?.onClick.AddListener(Confirm);
        cancelButton ?.onClick.AddListener(Cancel);
    }

    void Start()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        dialogRoot.SetActive(false);
        _open = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        CursorManager.Release(CURSOR_OWNER);
    }

    void Update()
    {
        if (canvasGroup == null || fadeDuration <= 0f) return;

        float target = _open ? 1f : 0f;
        if (Mathf.Approximately(_fade, target)) return;

        _fade = Mathf.MoveTowards(_fade, target, Time.unscaledDeltaTime / fadeDuration);
        canvasGroup.alpha = _fade;

        if (!_open && _fade <= 0f) dialogRoot.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Ask a yes/no question. If no dialog exists, the confirm action runs immediately.</summary>
    public static void Ask(string title, string body,
                           string confirmText = "Confirm", string cancelText = "Cancel",
                           Action onConfirm = null, Action onCancel = null)
    {
        if (Instance == null)
        {
            Debug.LogWarning($"[ConfirmDialog] None in scene — auto-confirming '{title}'.");
            onConfirm?.Invoke();
            return;
        }
        Instance.Open(title, body, confirmText, cancelText, onConfirm, onCancel);
    }

    private void Open(string title, string body, string confirmText, string cancelText,
                      Action onConfirm, Action onCancel)
    {
        _onConfirm = onConfirm;
        _onCancel  = onCancel;

        if (titleLabel   != null) titleLabel.text   = title;
        if (bodyLabel    != null) bodyLabel.text    = body;
        if (confirmLabel != null) confirmLabel.text = confirmText;
        if (cancelLabel  != null) cancelLabel.text  = cancelText;

        dialogRoot.SetActive(true);
        _open = true;

        if (canvasGroup != null)
        {
            canvasGroup.interactable   = true;
            canvasGroup.blocksRaycasts = true;
            if (fadeDuration <= 0f) { _fade = 1f; canvasGroup.alpha = 1f; }
        }

        CursorManager.Request(CURSOR_OWNER);

        if (pauseWhileOpen)
        {
            _wasPaused = Mathf.Approximately(Time.timeScale, 0f);
            Time.timeScale = 0f;
        }

        if (focusCancelByDefault && cancelButton != null)
            cancelButton.Select();
    }

    public void Confirm()
    {
        Action cb = _onConfirm;
        CloseInternal();
        cb?.Invoke();
    }

    public void Cancel()
    {
        Action cb = _onCancel;
        CloseInternal();
        cb?.Invoke();
    }

    private void CloseInternal()
    {
        _open      = false;
        _onConfirm = null;
        _onCancel  = null;

        if (canvasGroup != null)
        {
            canvasGroup.interactable   = false;
            canvasGroup.blocksRaycasts = false;
            if (fadeDuration <= 0f) { _fade = 0f; canvasGroup.alpha = 0f; dialogRoot.SetActive(false); }
        }
        else dialogRoot.SetActive(false);

        CursorManager.Release(CURSOR_OWNER);

        // Only unpause if WE paused — don't resume a game the PauseMenu froze.
        if (pauseWhileOpen && !_wasPaused) Time.timeScale = 1f;
    }
}
