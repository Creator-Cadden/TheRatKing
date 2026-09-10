using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// The controls list in the corner of the screen — but generated from the actual
/// Input Actions asset rather than typed by hand.
///
/// This replaces the hard-coded "WASD: Move / SPACEBAR: Jump / …" block. Two
/// reasons that matters: rebinding a key updates this automatically, and plugging
/// in a controller switches every row to its gamepad glyph. A hand-typed list
/// lies the moment either of those happens.
///
/// It also fades out after a while so it stops covering the game once the player
/// has learned the controls — and comes back on demand.
/// </summary>
public class ControlsOverlayUI : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("What this does. Shown on the right of the key. e.g. 'Jump'.")]
        public string label = "";

        [Tooltip("The action to read the binding from. Leave empty to use Manual Key.")]
        public InputActionReference action;

        [Tooltip("Used only when no action is set — for compound prompts the Input " +
                 "System can't express, e.g. 'SPACE + LMB' for the jump attack.")]
        public string manualKey = "";

        [Tooltip("Hide this row (e.g. show Aim only when the bow is equipped).")]
        public bool hidden = false;
    }

    [Header("Target")]
    [Tooltip("The text the list is written into. One TMP_Text is enough — rows are " +
             "just lines, which keeps this cheap and easy to style.")]
    public TMP_Text listLabel;

    [Tooltip("Optional CanvasGroup used for the auto-fade.")]
    public CanvasGroup canvasGroup;

    [Header("Rows")]
    public List<Entry> entries = new List<Entry>();

    [Header("Formatting")]
    [Tooltip("{0} = key/binding, {1} = label. Rich text works here.")]
    public string rowFormat = "<color=#CF9E24><b>{0}</b></color>  {1}";

    [Tooltip("Uppercase the key text — 'SPACE' reads better than 'space'.")]
    public bool uppercaseKeys = true;

    [Tooltip("Strip the device prefix the Input System adds, e.g. 'Keyboard/' or " +
             "'Left Button' → 'LMB'.")]
    public bool prettifyBindings = true;

    [Header("Auto-hide")]
    [Tooltip("Fade the list out after this many seconds. 0 = never hide.")]
    public float autoHideAfter = 20f;
    public float fadeSpeed = 3f;

    [Tooltip("Hold this key to bring the list back at any time.")]
    public Key showKey = Key.F1;

    [Tooltip("Also respect the player's 'Tutorial Hints' setting — turning hints off " +
             "hides this list permanently.")]
    public bool respectHintSetting = true;

    // ── Private ───────────────────────────────────────────────────────────────

    private float _timer;
    private float _alpha = 1f;
    private string _lastControlScheme = "";

    void Start()
    {
        Rebuild();
        _timer = 0f;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    void Update()
    {
        if (canvasGroup == null) return;

        bool hintsOn = !respectHintSetting || GameSettings.ShowTutorialHints;
        bool held    = Keyboard.current != null && Keyboard.current[showKey].isPressed;

        float target;
        if (!hintsOn && !held) target = 0f;
        else if (held)         target = 1f;
        else if (autoHideAfter <= 0f) target = 1f;
        else
        {
            _timer += Time.deltaTime;
            target = _timer < autoHideAfter ? 1f : 0f;
        }

        _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * fadeSpeed);
        canvasGroup.alpha = _alpha;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Re-read every binding and redraw the list. Call after a rebind.</summary>
    public void Rebuild()
    {
        if (listLabel == null) return;

        var sb = new StringBuilder();
        foreach (Entry e in entries)
        {
            if (e == null || e.hidden) continue;
            if (string.IsNullOrWhiteSpace(e.label)) continue;

            string key = ResolveKey(e);
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (sb.Length > 0) sb.Append('\n');
            sb.AppendFormat(rowFormat, key, e.label);
        }
        listLabel.text = sb.ToString();
    }

    /// <summary>Show the list again and restart the auto-hide timer.</summary>
    public void Flash()
    {
        _timer = 0f;
        _alpha = 1f;
    }

    /// <summary>Turn one row on or off by its label — e.g. hide "Aim" without a bow.</summary>
    public void SetRowVisible(string label, bool visible)
    {
        foreach (Entry e in entries)
        {
            if (e != null && e.label == label) e.hidden = !visible;
        }
        Rebuild();
    }

    // ── Binding resolution ────────────────────────────────────────────────────

    private string ResolveKey(Entry e)
    {
        string raw = "";

        if (e.action != null && e.action.action != null)
        {
            raw = e.action.action.GetBindingDisplayString(
                InputBinding.DisplayStringOptions.DontIncludeInteractions);

            // Multiple schemes come back pipe-separated — take the first.
            int pipe = raw.IndexOf('|');
            if (pipe > 0) raw = raw.Substring(0, pipe);
        }

        if (string.IsNullOrWhiteSpace(raw)) raw = e.manualKey;
        if (string.IsNullOrWhiteSpace(raw)) return "";

        raw = raw.Trim();
        if (prettifyBindings) raw = Prettify(raw);
        if (uppercaseKeys)    raw = raw.ToUpperInvariant();
        return raw;
    }

    /// <summary>Turns the Input System's verbose names into something a HUD can show.</summary>
    private static string Prettify(string s)
    {
        s = s.Replace("Left Button", "LMB")
             .Replace("Right Button", "RMB")
             .Replace("Middle Button", "MMB")
             .Replace("Left Shift", "LSHIFT")
             .Replace("Right Shift", "RSHIFT")
             .Replace("Left Ctrl", "CTRL")
             .Replace("Right Ctrl", "CTRL")
             .Replace("Left Alt", "ALT")
             .Replace("Press ", "");

        // "W/A/S/D" comes through as a composite like "W/A/S/D" already; collapse
        // the long form the Input System sometimes emits.
        s = s.Replace("Up/Left/Down/Right", "WASD")
             .Replace("W/A/S/D", "WASD");

        return s;
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        // A controller plugged in or out means every glyph may have changed.
        if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed ||
            change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected)
        {
            Rebuild();
            Flash();
        }
    }
}
