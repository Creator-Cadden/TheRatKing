using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Queued corner notifications: "Rat Coin ×25", "Blade unlocked", "Checkpoint
/// reached", "Floor 2". One line at a time, oldest first, each sliding in,
/// holding, then fading.
///
/// Call it from anywhere:
///     ToastNotifier.Show("Checkpoint reached");
///     ToastNotifier.Show("Hammer unlocked", "Press 2 to equip", ToastKind.Good);
///
/// Put this on an empty RectTransform inside your HUD canvas, anchored where you
/// want the stack to appear (top-centre and bottom-right both read well). Lines
/// are built at runtime, so it works before any UI art exists.
/// </summary>
public class ToastNotifier : MonoBehaviour
{
    public enum ToastKind { Neutral, Good, Bad, Gold }

    public static ToastNotifier Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Layout")]
    [Tooltip("How many toasts can be on screen at once. Extras wait in the queue.")]
    public int maxVisible = 3;

    [Tooltip("Width of a toast line, in canvas pixels.")]
    public float width = 360f;

    [Tooltip("Height of a toast line.")]
    public float height = 56f;

    [Tooltip("Vertical gap between stacked toasts.")]
    public float spacing = 8f;

    [Tooltip("Which way new toasts stack. -1 = downwards (anchor at top), " +
             "+1 = upwards (anchor at bottom).")]
    public int stackDirection = -1;

    [Header("Motion")]
    [Tooltip("Distance the toast slides in from, in pixels. Positive = from the right.")]
    public float slideDistance = 60f;
    public float fadeInTime  = 0.18f;
    public float holdTime    = 2.6f;
    public float fadeOutTime = 0.45f;

    [Header("Style")]
    [Tooltip("Optional 9-slice background. Leave empty to use a flat rounded box " +
             "built at runtime from the theme colours.")]
    public Sprite backgroundSprite;

    [Tooltip("Font for the main line. Leave empty to use the theme's body font.")]
    public TMP_FontAsset font;

    public float titleFontSize = 20f;
    public float subFontSize   = 14f;

    [Tooltip("Left padding inside a toast, and the width reserved for the accent stripe.")]
    public float accentStripeWidth = 5f;

    // ── Private ───────────────────────────────────────────────────────────────

    private class Toast
    {
        public RectTransform rt;
        public CanvasGroup   group;
        public float         age;
        public float         lifetime;
        public float         targetY;
        public float         currentY;
    }

    private readonly List<Toast> _live    = new List<Toast>();
    private readonly Queue<(string title, string sub, ToastKind kind)> _pending
        = new Queue<(string, string, ToastKind)>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Two HUDs in one scene — keep the first, drop the duplicate quietly.
            enabled = false;
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // Promote queued toasts as slots free up.
        while (_pending.Count > 0 && _live.Count < maxVisible)
        {
            var next = _pending.Dequeue();
            Spawn(next.title, next.sub, next.kind);
        }

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Toast t = _live[i];
            if (t.rt == null) { _live.RemoveAt(i); continue; }

            t.age += Time.unscaledDeltaTime;   // unscaled so toasts work while paused

            // Alpha envelope: fade in → hold → fade out.
            float alpha;
            if (t.age < fadeInTime)
                alpha = fadeInTime > 0f ? t.age / fadeInTime : 1f;
            else if (t.age < fadeInTime + holdTime)
                alpha = 1f;
            else
                alpha = fadeOutTime > 0f
                    ? 1f - (t.age - fadeInTime - holdTime) / fadeOutTime
                    : 0f;

            t.group.alpha = Mathf.Clamp01(alpha);

            // Slide in from the side, easing out.
            float slideT = fadeInTime > 0f ? Mathf.Clamp01(t.age / fadeInTime) : 1f;
            float ease   = 1f - Mathf.Pow(1f - slideT, 3f);
            float x      = Mathf.Lerp(slideDistance, 0f, ease);

            // Ease toward the assigned stack slot so the list closes up smoothly.
            t.currentY = Mathf.Lerp(t.currentY, t.targetY, Time.unscaledDeltaTime * 12f);
            t.rt.anchoredPosition = new Vector2(x, t.currentY);

            if (t.age >= t.lifetime)
            {
                Destroy(t.rt.gameObject);
                _live.RemoveAt(i);
                RestackSlots();
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Show(string title) => Show(title, "", ToastKind.Neutral);

    public static void Show(string title, ToastKind kind) => Show(title, "", kind);

    /// <summary>Queue a toast. Safe to call when no ToastNotifier exists — it just logs.</summary>
    public static void Show(string title, string subtitle, ToastKind kind = ToastKind.Neutral)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        if (Instance == null)
        {
            Debug.Log($"[Toast] {title} — {subtitle}");
            return;
        }
        Instance._pending.Enqueue((title, subtitle, kind));
    }

    /// <summary>Drop everything queued and on screen. Use on scene transitions.</summary>
    public static void ClearAll()
    {
        if (Instance == null) return;
        Instance._pending.Clear();
        foreach (Toast t in Instance._live)
            if (t.rt != null) Destroy(t.rt.gameObject);
        Instance._live.Clear();
    }

    // ── Building ──────────────────────────────────────────────────────────────

    private void Spawn(string title, string sub, ToastKind kind)
    {
        UITheme theme = UITheme.Active;

        var go = new GameObject($"Toast_{title}", typeof(RectTransform), typeof(CanvasGroup));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, stackDirection < 0 ? 1f : 0f);
        rt.pivot     = new Vector2(1f, stackDirection < 0 ? 1f : 0f);
        rt.sizeDelta = new Vector2(width, height);

        var group = go.GetComponent<CanvasGroup>();
        group.alpha          = 0f;
        group.blocksRaycasts = false;
        group.interactable   = false;

        // Background
        var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.SetParent(rt, false);
        Stretch(bgRt);
        var bg = bgGo.GetComponent<Image>();
        bg.raycastTarget = false;
        if (backgroundSprite != null)
        {
            bg.sprite = backgroundSprite;
            bg.type   = Image.Type.Sliced;
            bg.color  = Color.white;
        }
        else
        {
            bg.sprite = UITheme.SolidSprite;
            bg.color  = theme != null
                ? UITheme.WithAlpha(theme.surface, 0.94f)
                : new Color(0.17f, 0.13f, 0.10f, 0.94f);
        }

        // Accent stripe down the left edge — carries the kind at a glance.
        var stripeGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        var stripeRt = stripeGo.GetComponent<RectTransform>();
        stripeRt.SetParent(rt, false);
        stripeRt.anchorMin = new Vector2(0f, 0f);
        stripeRt.anchorMax = new Vector2(0f, 1f);
        stripeRt.pivot     = new Vector2(0f, 0.5f);
        stripeRt.sizeDelta = new Vector2(accentStripeWidth, 0f);
        stripeRt.anchoredPosition = Vector2.zero;
        var stripe = stripeGo.GetComponent<Image>();
        stripe.raycastTarget = false;
        stripe.sprite = UITheme.SolidSprite;
        stripe.color  = AccentFor(kind, theme);

        // Text
        bool hasSub = !string.IsNullOrWhiteSpace(sub);
        MakeLabel(rt, "Title", title,
                  titleFontSize,
                  theme != null ? theme.textPrimary : Color.white,
                  hasSub ? new Vector2(0f, height * 0.16f) : Vector2.zero,
                  FontStyles.Bold);

        if (hasSub)
        {
            MakeLabel(rt, "Sub", sub,
                      subFontSize,
                      theme != null ? theme.textMuted : new Color(0.75f, 0.72f, 0.64f),
                      new Vector2(0f, -height * 0.22f),
                      FontStyles.Normal);
        }

        var toast = new Toast
        {
            rt       = rt,
            group    = group,
            age      = 0f,
            lifetime = fadeInTime + holdTime + fadeOutTime,
            currentY = 0f
        };
        _live.Add(toast);
        RestackSlots();
        toast.currentY = toast.targetY;   // don't slide vertically on first appearance
    }

    private void MakeLabel(RectTransform parent, string name, string text,
                           float size, Color color, Vector2 offset, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Stretch(rt);
        rt.offsetMin = new Vector2(accentStripeWidth + 14f, 0f);
        rt.offsetMax = new Vector2(-14f, 0f);
        rt.anchoredPosition += offset;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = size;
        tmp.color         = color;
        tmp.fontStyle     = style;
        tmp.alignment     = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        tmp.overflowMode  = TextOverflowModes.Ellipsis;

        TMP_FontAsset f = font != null ? font
                        : (UITheme.Active != null ? UITheme.Active.bodyFont : null);
        if (f != null) tmp.font = f;
    }

    /// <summary>Re-assign each live toast its slot in the stack, newest nearest the anchor.</summary>
    private void RestackSlots()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            int slotFromAnchor = _live.Count - 1 - i;   // oldest sits furthest out
            _live[i].targetY = stackDirection * slotFromAnchor * (height + spacing);
        }
    }

    private static Color AccentFor(ToastKind kind, UITheme theme)
    {
        if (theme == null)
        {
            return kind switch
            {
                ToastKind.Good => new Color(0.31f, 0.70f, 0.35f),
                ToastKind.Bad  => new Color(0.82f, 0.27f, 0.23f),
                ToastKind.Gold => new Color(0.81f, 0.62f, 0.14f),
                _              => new Color(0.62f, 0.56f, 0.47f)
            };
        }
        return kind switch
        {
            ToastKind.Good => theme.xpGreen,
            ToastKind.Bad  => theme.danger,
            ToastKind.Gold => theme.gold,
            _              => theme.textMuted
        };
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
