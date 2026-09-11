using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton that owns ALL cursor lock / visibility / artwork state for the game.
///
/// Two independent things live here:
///   1. LOCK STATE — whether the OS cursor is locked to the centre (gameplay) or
///      free (menus). Systems call Request/Release with an owner string; the
///      cursor unlocks while ANY owner needs it. Unchanged from before.
///   2. LOOK — which cursor artwork is showing. Styles are registered per
///      <see cref="CursorState"/>; the highest-priority state any owner has
///      requested wins. Supports multi-frame animated cursors.
///
/// Setup: put this on the Cursormanager prefab, fill the Styles list with one
/// entry per state you have art for, and leave the rest empty (they fall back to
/// Default). Import every cursor PNG as Texture Type = Cursor.
/// </summary>
public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    // ── Cursor states ─────────────────────────────────────────────────────────

    /// <summary>Ordered LOW → HIGH priority. A higher state always wins.</summary>
    public enum CursorState
    {
        Default = 0,   // the plain rat-paw pointer
        Aim     = 1,   // bow drawn / targeting
        Hover   = 2,   // over a button or interactable
        Press   = 3,   // mouse held down on a button
        Blocked = 4,   // can't afford it / locked
        Busy    = 5    // loading, saving — beats everything
    }

    [System.Serializable]
    public class CursorStyle
    {
        public CursorState state = CursorState.Default;

        [Tooltip("One texture = static cursor. Two or more = animated, played in order.")]
        public Texture2D[] frames;

        [Tooltip("Pixel offset of the click point from the image's TOP-LEFT corner. " +
                 "For a paw/arrow pointing up-left this is near (0,0); for a crosshair " +
                 "it's the exact centre of the image.")]
        public Vector2 hotspot = Vector2.zero;

        [Tooltip("Frames per second. Only used when there's more than one frame.")]
        public float fps = 12f;

        [Tooltip("Ignore this style without deleting it (handy while iterating).")]
        public bool disabled = false;

        public bool IsValid => !disabled && frames != null && frames.Length > 0 && frames[0] != null;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Custom Cursor — legacy single texture")]
    [Tooltip("Kept for backwards compatibility. If the Styles list below has a " +
             "Default entry, that wins and this is ignored.")]
    public Texture2D cursorTexture;

    [Tooltip("Hotspot for the legacy texture above, measured from its top-left.")]
    public Vector2 cursorHotspot = new Vector2(24f, 5f);

    [Header("Cursor Styles (preferred)")]
    [Tooltip("One entry per state you have art for. Missing states fall back to Default.")]
    public List<CursorStyle> styles = new List<CursorStyle>();

    [Header("Behaviour")]
    [Tooltip("CursorMode.Auto lets the OS draw it (zero latency, but the OS may cap " +
             "the size at 32×32 on some platforms). ForceSoftware makes Unity draw it, " +
             "which allows any size but adds one frame of lag. Auto is almost always right.")]
    public CursorMode cursorMode = CursorMode.Auto;

    [Tooltip("Reset every state request when a new scene loads. Prevents a stuck " +
             "'Busy' cursor if something forgot to release.")]
    public bool clearStatesOnSceneLoad = true;

    [Header("Debug — read only")]
    [SerializeField] private string _activeRequests = "none";
    [SerializeField] private CursorState _activeState = CursorState.Default;

    // ── Private state ─────────────────────────────────────────────────────────

    // Which systems currently want the cursor visible + unlocked.
    private readonly HashSet<string> _requests = new HashSet<string>();

    // Which systems currently want a particular cursor LOOK.
    private readonly Dictionary<string, CursorState> _stateRequests =
        new Dictionary<string, CursorState>();

    private readonly Dictionary<CursorState, CursorStyle> _styleLookup =
        new Dictionary<CursorState, CursorStyle>();

    private CursorStyle _currentStyle;
    private int   _frameIndex;
    private float _frameTimer;
    private bool  _appliedOnce;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildStyleLookup();
        ApplyCursorTexture();
        ApplyCursorState();
    }

    void OnEnable()
    {
        if (clearStatesOnSceneLoad)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        if (clearStatesOnSceneLoad)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                               UnityEngine.SceneManagement.LoadSceneMode m)
    {
        _stateRequests.Clear();
        RefreshCursorArt(force: true);
    }

    void Update()
    {
        AnimateCurrentStyle();
    }

    // ── Style registry ────────────────────────────────────────────────────────

    private void BuildStyleLookup()
    {
        _styleLookup.Clear();
        foreach (CursorStyle s in styles)
        {
            if (s == null || !s.IsValid) continue;
            _styleLookup[s.state] = s;   // last valid entry for a state wins
        }

        // Fold the legacy single texture in as Default when no Default style exists.
        if (!_styleLookup.ContainsKey(CursorState.Default) && cursorTexture != null)
        {
            _styleLookup[CursorState.Default] = new CursorStyle
            {
                state   = CursorState.Default,
                frames  = new[] { cursorTexture },
                hotspot = cursorHotspot,
                fps     = 0f
            };
        }
    }

    /// <summary>
    /// Re-reads the Styles list. Call after changing styles at runtime; you do NOT
    /// need this for normal play.
    /// </summary>
    public void RebuildStyles()
    {
        BuildStyleLookup();
        RefreshCursorArt(force: true);
    }

    // ── Public API — LOCK (unchanged, existing callers keep working) ───────────

    /// <summary>Register that 'owner' needs the cursor visible and unlocked.</summary>
    public static void Request(string owner)
    {
        if (Instance == null)
        {
            // No manager in this scene (e.g. a menu scene opened directly). Still
            // make the cursor usable so panels/buttons aren't dead — the request
            // system just isn't tracking owners here.
            Debug.LogWarning("[CursorManager] No CursorManager in scene — showing cursor directly.");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            return;
        }
        Instance._requests.Add(owner);
        Instance.ApplyCursorState();
    }

    /// <summary>Unregister 'owner' — the cursor locks again if nobody else needs it.</summary>
    public static void Release(string owner)
    {
        if (Instance == null) return;
        Instance._requests.Remove(owner);
        Instance.ApplyCursorState();
    }

    /// <summary>Release everything and force-lock. Call on scene load / restart.</summary>
    public static void ForceReset()
    {
        if (Instance == null) return;
        Instance._requests.Clear();
        Instance._stateRequests.Clear();
        Instance.ApplyCursorState();
        Instance.RefreshCursorArt(force: true);
    }

    /// <summary>True while at least one system is holding the cursor free.</summary>
    public static bool IsUnlocked => Instance != null && Instance._requests.Count > 0;

    // ── Public API — LOOK ─────────────────────────────────────────────────────

    /// <summary>
    /// Ask for a cursor look. Highest-priority active request wins, so a Hover
    /// request from a button can't be stomped by an Aim request from the bow.
    /// Always pair with <see cref="ClearState"/> using the same owner string.
    /// </summary>
    public static void SetState(string owner, CursorState state)
    {
        if (Instance == null || string.IsNullOrEmpty(owner)) return;

        if (Instance._stateRequests.TryGetValue(owner, out CursorState existing) && existing == state)
            return;

        Instance._stateRequests[owner] = state;
        Instance.RefreshCursorArt(force: false);
    }

    /// <summary>Drop this owner's cursor-look request.</summary>
    public static void ClearState(string owner)
    {
        if (Instance == null || string.IsNullOrEmpty(owner)) return;
        if (Instance._stateRequests.Remove(owner))
            Instance.RefreshCursorArt(force: false);
    }

    /// <summary>The look currently being drawn.</summary>
    public static CursorState CurrentState =>
        Instance != null ? Instance._activeState : CursorState.Default;

    /// <summary>Legacy entry point — applies the Default style (or legacy texture).</summary>
    public void ApplyCursorTexture()
    {
        BuildStyleLookup();
        RefreshCursorArt(force: true);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void ApplyCursorState()
    {
        bool needsCursor = _requests.Count > 0;

        Cursor.lockState = needsCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = needsCursor;

        _activeRequests = _requests.Count > 0 ? string.Join(", ", _requests) : "none";
    }

    /// <summary>Works out the winning state and swaps the artwork if it changed.</summary>
    private void RefreshCursorArt(bool force)
    {
        CursorState winner = CursorState.Default;
        foreach (var kv in _stateRequests)
            if (kv.Value > winner) winner = kv.Value;

        if (!force && winner == _activeState && _appliedOnce) return;

        _activeState = winner;

        // Fall back down the priority ladder until we find a style with art.
        CursorStyle style = null;
        for (int s = (int)winner; s >= 0; s--)
        {
            if (_styleLookup.TryGetValue((CursorState)s, out CursorStyle found))
            {
                style = found;
                break;
            }
        }

        _currentStyle = style;
        _frameIndex   = 0;
        _frameTimer   = 0f;
        _appliedOnce  = true;

        if (style == null)
        {
            // No art at all — hand the OS cursor back rather than showing nothing.
            Cursor.SetCursor(null, Vector2.zero, cursorMode);
            return;
        }

        Cursor.SetCursor(style.frames[0], style.hotspot, cursorMode);
    }

    private void AnimateCurrentStyle()
    {
        if (_currentStyle == null) return;
        if (_currentStyle.frames.Length < 2 || _currentStyle.fps <= 0f) return;
        if (!Cursor.visible) return;   // don't burn work while the cursor is locked away

        // unscaledDeltaTime so the cursor keeps animating while the game is paused.
        _frameTimer += Time.unscaledDeltaTime;
        float frameLength = 1f / _currentStyle.fps;
        if (_frameTimer < frameLength) return;

        _frameTimer -= frameLength;
        _frameIndex  = (_frameIndex + 1) % _currentStyle.frames.Length;

        Texture2D tex = _currentStyle.frames[_frameIndex];
        if (tex != null)
            Cursor.SetCursor(tex, _currentStyle.hotspot, cursorMode);
    }
}
