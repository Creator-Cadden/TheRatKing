using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One tooltip bubble, reused by everything. Put this on a RectTransform that is
/// a DIRECT CHILD of your top-most canvas (so it draws over every panel) and set
/// the canvas's Sort Order high.
///
/// Anything can raise it:
///     TooltipSystem.Show("Strength", "Raises damage with every weapon.\n+2 damage per point.");
///     TooltipSystem.Hide();
///
/// Usually you don't call it directly — you put a <see cref="TooltipTrigger"/> on
/// the thing being hovered and fill in the text there.
/// </summary>
public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem Instance { get; private set; }

    [Header("References (auto-built if left empty)")]
    public RectTransform panel;
    public CanvasGroup   canvasGroup;
    public Image         background;
    public TMP_Text      titleLabel;
    public TMP_Text      bodyLabel;

    [Header("Layout")]
    [Tooltip("Max width before the body text wraps.")]
    public float maxWidth = 320f;
    [Tooltip("Padding inside the bubble.")]
    public Vector2 padding = new Vector2(14f, 10f);
    [Tooltip("Offset from the cursor. Positive X puts it to the right of the pointer.")]
    public Vector2 cursorOffset = new Vector2(18f, -18f);
    [Tooltip("Keep the bubble this far from the screen edge before it flips sides.")]
    public float edgeMargin = 12f;

    [Header("Motion")]
    public float fadeInTime  = 0.10f;
    public float fadeOutTime = 0.08f;
    [Tooltip("Follow the cursor while shown. Turn OFF to pin it where it appeared.")]
    public bool followCursor = true;

    [Header("Style")]
    [Tooltip("9-slice bubble sprite. Empty = flat panel from the theme colours.")]
    public Sprite bubbleSprite;
    public float titleFontSize = 20f;
    public float bodyFontSize  = 16f;

    private bool   _visible;
    private float  _alpha;
    private Canvas _canvas;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        _canvas  = GetComponentInParent<Canvas>();

        if (panel == null) BuildRuntimeBubble();
        if (canvasGroup == null) canvasGroup = panel.GetComponent<CanvasGroup>()
                                            ?? panel.gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha          = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable   = false;
        panel.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void LateUpdate()
    {
        // Fade
        float target = _visible ? 1f : 0f;
        float speed  = _visible
            ? (fadeInTime  > 0f ? Time.unscaledDeltaTime / fadeInTime  : 1f)
            : (fadeOutTime > 0f ? Time.unscaledDeltaTime / fadeOutTime : 1f);

        _alpha = Mathf.MoveTowards(_alpha, target, speed);
        canvasGroup.alpha = _alpha;

        if (!_visible && _alpha <= 0f)
        {
            if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
            return;
        }

        if (_visible && followCursor) PositionAtCursor();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Show(string title, string body)
    {
        if (Instance == null) return;
        Instance.ShowInternal(title, body);
    }

    public static void Hide()
    {
        if (Instance == null) return;
        Instance._visible = false;
    }

    private void ShowInternal(string title, string body)
    {
        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        bool hasBody  = !string.IsNullOrWhiteSpace(body);
        if (!hasTitle && !hasBody) return;

        if (titleLabel != null)
        {
            titleLabel.gameObject.SetActive(hasTitle);
            titleLabel.text = title;
        }
        if (bodyLabel != null)
        {
            bodyLabel.gameObject.SetActive(hasBody);
            bodyLabel.text = body;
        }

        panel.gameObject.SetActive(true);
        _visible = true;

        // Force a layout pass so the size is correct on the very first frame,
        // otherwise the bubble pops at the wrong size for one frame.
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        PositionAtCursor();
    }

    // ── Positioning ───────────────────────────────────────────────────────────

    private void PositionAtCursor()
    {
        Vector2 screenPos = GetPointerScreenPosition();
        Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _canvas.worldCamera
            : null;

        RectTransform parentRect = panel.parent as RectTransform;
        if (parentRect == null) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenPos, cam, out Vector2 local))
            return;

        Vector2 size = panel.rect.size;
        Vector2 pos  = local + cursorOffset;

        // Flip to the other side of the cursor rather than running off screen.
        Rect bounds = parentRect.rect;
        if (pos.x + size.x > bounds.xMax - edgeMargin)
            pos.x = local.x - cursorOffset.x - size.x;
        if (pos.y - size.y < bounds.yMin + edgeMargin)
            pos.y = local.y - cursorOffset.y + size.y;

        pos.x = Mathf.Clamp(pos.x, bounds.xMin + edgeMargin, bounds.xMax - edgeMargin - size.x);
        pos.y = Mathf.Clamp(pos.y, bounds.yMin + edgeMargin + size.y, bounds.yMax - edgeMargin);

        panel.anchoredPosition = pos;
    }

    private static Vector2 GetPointerScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        // This project runs the Input System package only, so never touch the
        // legacy UnityEngine.Input here — it throws at runtime in that mode.
        return UnityEngine.InputSystem.Mouse.current != null
            ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
            : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    // ── Runtime construction ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a usable bubble out of nothing so tooltips work before you've made
    /// the art. Replace by assigning the fields in the inspector.
    /// </summary>
    private void BuildRuntimeBubble()
    {
        UITheme theme = UITheme.Active;

        var go = new GameObject("TooltipBubble", typeof(RectTransform), typeof(CanvasGroup));
        panel = go.GetComponent<RectTransform>();
        panel.SetParent(transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot     = new Vector2(0f, 1f);

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)padding.x, (int)padding.x,
                                        (int)padding.y, (int)padding.y);
        layout.spacing = 4f;
        layout.childControlWidth   = true;
        layout.childControlHeight  = true;
        layout.childForceExpandWidth  = false;
        layout.childForceExpandHeight = false;

        background = go.AddComponent<Image>();
        background.raycastTarget = false;
        if (bubbleSprite != null)
        {
            background.sprite = bubbleSprite;
            background.type   = Image.Type.Sliced;
            background.color  = Color.white;
        }
        else
        {
            background.sprite = UITheme.SolidSprite;
            background.color  = theme != null
                ? UITheme.WithAlpha(theme.surfaceDeep, 0.97f)
                : new Color(0.12f, 0.10f, 0.06f, 0.97f);
        }

        titleLabel = MakeLabel("Title", titleFontSize, FontStyles.Bold,
                               theme != null ? theme.goldLight : new Color(0.91f, 0.76f, 0.33f));
        bodyLabel  = MakeLabel("Body",  bodyFontSize,  FontStyles.Normal,
                               theme != null ? theme.textPrimary : new Color(0.94f, 0.89f, 0.79f));
    }

    private TMP_Text MakeLabel(string name, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(panel, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize      = size;
        tmp.fontStyle     = style;
        tmp.color         = color;
        tmp.raycastTarget = false;
        tmp.alignment     = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        if (UITheme.Active != null && UITheme.Active.bodyFont != null)
            tmp.font = UITheme.Active.bodyFont;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = maxWidth - padding.x * 2f;

        return tmp;
    }
}
