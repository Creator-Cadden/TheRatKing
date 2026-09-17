using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Freeze and show." Stops the game and darkens the screen except for a lit
/// rectangle around one thing in the world — so when the tutorial says the word
/// STAGGER, the player is looking at the STAGGER label floating over the rat
/// that just took the hit, with nothing else competing for attention.
///
/// HOW THE HOLE WORKS. The shade is four solid quads — above, below, left and
/// right of the lit rectangle — rather than one full-screen quad. That matters
/// because the thing being shown is world-space 3D text (EnemyAI spawns a
/// TextMeshPro object above the enemy), and any screen-space overlay would
/// cover it no matter what order things are drawn in. Leaving an actual gap is
/// the only way the 3D scene shows through, and it needs no shader, no render
/// texture and no second camera, so it behaves the same under any pipeline.
///
/// The freeze goes through GameFreeze, so it stacks with the pause and stat
/// menus instead of fighting them over Time.timeScale. Input is NOT locked
/// here — the caller owns that, because only the caller knows what the player
/// should still be allowed to do afterwards.
/// </summary>
[DisallowMultipleComponent]
public class TutorialFocus : MonoBehaviour
{
    public static TutorialFocus Instance { get; private set; }

    private const string FREEZE_OWNER = "tutorialfocus";

    [Header("Refs — all built in code if left empty")]
    public Canvas        canvas;
    public RectTransform canvasRect;
    public Image         shadeTop, shadeBottom, shadeLeft, shadeRight;

    [Header("Look")]
    [Tooltip("Colour of the darkened area. The alpha is the target darkness.")]
    public Color shadeColor = new Color(0f, 0f, 0f, 0.80f);
    [Tooltip("How fast the shade fades in and out.")]
    public float fadeSpeed = 7f;
    [Tooltip("Above the objective cards would hide them; below the prompt panel " +
             "keeps the explanation readable. 500 sits between the two.")]
    public int   sortingOrder = 500;

    [Header("Spotlight")]
    [Tooltip("Size of the lit hole, in canvas units.")]
    public Vector2 cutoutSize = new Vector2(460f, 360f);
    [Tooltip("Metres above the target's pivot to centre the hole on. The reaction " +
             "label floats ~3.6m up, so aim high or the text sits outside the light.")]
    public float worldYOffset = 2.6f;
    [Tooltip("The hole starts this much bigger and closes to size — a small " +
             "movement that reads as the screen focusing rather than blinking.")]
    public float openOvershoot = 1.4f;

    /// <summary>True from the moment it's asked to open until it's asked to close.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True while anything is still drawn, including the fade out.</summary>
    public bool IsVisible => _t > 0.002f;

    private Transform _target;
    private bool      _fullScreen;
    private float     _t;
    private Camera    _cam;
    private bool      _frozen;

    void Awake()
    {
        if (Instance == null) Instance = this;
        Build();
        _t = 0f;
        ApplyAlpha(0f);
        SetQuadsActive(false);
    }

    void OnDestroy()
    {
        // A focus beat interrupted by a scene change must not leave the game
        // frozen — that would look exactly like a hard lock-up.
        ReleaseFreeze();
        if (Instance == this) Instance = null;
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>Darken everything but a window around <paramref name="target"/>.</summary>
    public void Open(Transform target) => Open(target, cutoutSize);

    /// <summary>As above, with a one-off hole size.</summary>
    public void Open(Transform target, Vector2 size)
    {
        cutoutSize  = size;
        _target     = target;
        _fullScreen = target == null;
        IsOpen      = true;
        SetQuadsActive(true);
        TakeFreeze();
    }

    /// <summary>Darken the whole screen — for a beat with nothing to point at.</summary>
    public void OpenFullScreen()
    {
        _target     = null;
        _fullScreen = true;
        IsOpen      = true;
        SetQuadsActive(true);
        TakeFreeze();
    }

    /// <summary>Let go: time resumes immediately, the shade fades out behind it.</summary>
    public void Close()
    {
        IsOpen  = false;
        _target = null;
        ReleaseFreeze();
    }

    private void TakeFreeze()
    {
        if (_frozen) return;
        _frozen = true;
        GameFreeze.Request(FREEZE_OWNER);
    }

    private void ReleaseFreeze()
    {
        if (!_frozen) return;
        _frozen = false;
        GameFreeze.Release(FREEZE_OWNER);
    }

    // ── Tick ──────────────────────────────────────────────────────────────────

    // LateUpdate so the hole is placed from the target's FINAL position this
    // frame. Placing it in Update would leave the light a frame behind anything
    // still being moved by other scripts.
    void LateUpdate()
    {
        float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, fadeSpeed) * Time.unscaledDeltaTime);
        _t = Mathf.Lerp(_t, IsOpen ? 1f : 0f, k);
        if (!IsOpen && _t < 0.002f)
        {
            _t = 0f;
            ApplyAlpha(0f);
            SetQuadsActive(false);
            return;
        }

        ApplyAlpha(_t);
        LayoutQuads();
    }

    private void LayoutQuads()
    {
        if (canvasRect == null) return;

        Vector2 canvasSize = canvasRect.rect.size;
        float   W = canvasSize.x;
        float   H = canvasSize.y;

        // No target: cover the lot with a zero-width hole in the middle.
        if (_fullScreen || _target == null)
        {
            SetQuad(shadeLeft,   0f, 0f, W, H);
            SetQuad(shadeRight,  0f, 0f, 0f, 0f);
            SetQuad(shadeTop,    0f, 0f, 0f, 0f);
            SetQuad(shadeBottom, 0f, 0f, 0f, 0f);
            return;
        }

        if (_cam == null) _cam = Camera.main;
        if (_cam == null)
        {
            SetQuad(shadeLeft, 0f, 0f, W, H);
            SetQuad(shadeRight,  0f, 0f, 0f, 0f);
            SetQuad(shadeTop,    0f, 0f, 0f, 0f);
            SetQuad(shadeBottom, 0f, 0f, 0f, 0f);
            return;
        }

        Vector3 world  = _target.position + Vector3.up * worldYOffset;
        Vector3 screen = _cam.WorldToScreenPoint(world);

        // Behind the camera: don't try to light a point that isn't on screen.
        if (screen.z <= 0f)
        {
            SetQuad(shadeLeft, 0f, 0f, W, H);
            SetQuad(shadeRight,  0f, 0f, 0f, 0f);
            SetQuad(shadeTop,    0f, 0f, 0f, 0f);
            SetQuad(shadeBottom, 0f, 0f, 0f, 0f);
            return;
        }

        // Overlay canvas → pass no camera. Result is relative to the pivot, so
        // shift it to a bottom-left origin to match the quads' anchoring.
        Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                     ? canvas.worldCamera : null;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, new Vector2(screen.x, screen.y), uiCam, out Vector2 local);

        Vector2 pivot = canvasRect.pivot;
        float   cx    = local.x + W * pivot.x;
        float   cy    = local.y + H * pivot.y;

        // Overshoot closes to size as _t goes 0 → 1.
        float scale = Mathf.Lerp(Mathf.Max(1f, openOvershoot), 1f, _t);
        float hw    = Mathf.Max(8f, cutoutSize.x * scale) * 0.5f;
        float hh    = Mathf.Max(8f, cutoutSize.y * scale) * 0.5f;

        float xMin = Mathf.Clamp(cx - hw, 0f, W);
        float xMax = Mathf.Clamp(cx + hw, 0f, W);
        float yMin = Mathf.Clamp(cy - hh, 0f, H);
        float yMax = Mathf.Clamp(cy + hh, 0f, H);

        // Left and right run the full height; top and bottom only span the hole,
        // so the four never overlap and the shade stays one flat density.
        SetQuad(shadeLeft,   0f,   0f,   xMin,        H);
        SetQuad(shadeRight,  xMax, 0f,   W - xMax,    H);
        SetQuad(shadeTop,    xMin, yMax, xMax - xMin, H - yMax);
        SetQuad(shadeBottom, xMin, 0f,   xMax - xMin, yMin);
    }

    private static void SetQuad(Image img, float x, float y, float w, float h)
    {
        if (img == null) return;
        RectTransform r = img.rectTransform;
        r.anchorMin        = Vector2.zero;
        r.anchorMax        = Vector2.zero;
        r.pivot            = Vector2.zero;
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta        = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
    }

    private void ApplyAlpha(float t)
    {
        Color c = shadeColor;
        c.a = shadeColor.a * Mathf.Clamp01(t);
        if (shadeTop    != null) shadeTop.color    = c;
        if (shadeBottom != null) shadeBottom.color = c;
        if (shadeLeft   != null) shadeLeft.color   = c;
        if (shadeRight  != null) shadeRight.color  = c;
    }

    private void SetQuadsActive(bool on)
    {
        if (shadeTop    != null && shadeTop.gameObject.activeSelf    != on) shadeTop.gameObject.SetActive(on);
        if (shadeBottom != null && shadeBottom.gameObject.activeSelf != on) shadeBottom.gameObject.SetActive(on);
        if (shadeLeft   != null && shadeLeft.gameObject.activeSelf   != on) shadeLeft.gameObject.SetActive(on);
        if (shadeRight  != null && shadeRight.gameObject.activeSelf  != on) shadeRight.gameObject.SetActive(on);
    }

    // ── Self-build ────────────────────────────────────────────────────────────

    /// <summary>
    /// Makes its own canvas and four quads, so dropping this component on an
    /// empty GameObject is the whole setup. A spriteless Image draws a solid
    /// rect, so there's no art to import.
    /// </summary>
    private void Build()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        canvas.sortingOrder = sortingOrder;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
        }

        if (canvasRect == null) canvasRect = GetComponent<RectTransform>();

        shadeTop    = EnsureQuad(shadeTop,    "ShadeTop");
        shadeBottom = EnsureQuad(shadeBottom, "ShadeBottom");
        shadeLeft   = EnsureQuad(shadeLeft,   "ShadeLeft");
        shadeRight  = EnsureQuad(shadeRight,  "ShadeRight");
    }

    private Image EnsureQuad(Image existing, string name)
    {
        if (existing != null) { existing.raycastTarget = false; return existing; }

        Transform found = transform.Find(name);
        GameObject go = found != null ? found.gameObject
                                      : new GameObject(name, typeof(RectTransform));
        if (found == null) go.transform.SetParent(transform, false);

        Image img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.type          = Image.Type.Simple;
        img.color         = new Color(shadeColor.r, shadeColor.g, shadeColor.b, 0f);
        return img;
    }
}
