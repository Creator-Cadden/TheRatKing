using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// A slot-machine / odometer number readout. Each digit is its own vertical
/// "reel" of 0-9 that physically ROLLS upward as the value climbs — like
/// Stardew's money or a fruit machine settling. Fully self-contained: it builds
/// its reels (masked cells + digit strips) at runtime, so there's no prefab to
/// assemble. Drop it on a UI object, assign a TMP font, and call SetValue().
///
/// Layout: reels are right-aligned to this object's origin and grow leftward, so
/// anchor this object to the TOP-RIGHT of your HUD and the ones column stays put
/// as the number grows.
/// </summary>
[DisallowMultipleComponent]
public class SlotNumberDisplay : MonoBehaviour
{
    [Header("Font & Cell")]
    [Tooltip("TMP font asset for the digits. Leave null to use TMP's default.")]
    public TMP_FontAsset font;
    [Tooltip("Digit font size. Keep it ~0.85–0.9 × Cell Height.")]
    public float fontSize = 34f;
    [Tooltip("Width of one digit cell (px).")]
    public float cellWidth = 24f;
    [Tooltip("Height of one digit cell (px) = how far one row scrolls.")]
    public float cellHeight = 40f;
    public Color textColor = new Color(1f, 0.95f, 0.82f, 1f); // warm cream
    public FontStyles fontStyle = FontStyles.Bold;

    [Header("Digits")]
    [Tooltip("Minimum reels shown. 1 = no leading zeros; 3 = always at least '007'.")]
    public int minDigits = 1;

    [Header("Spin")]
    [Tooltip("Reel speed in digit-rows per second (the 'spin' rate).")]
    public float spinSpeed = 22f;
    [Tooltip("Caps how long a big spin can take; bigger gaps speed up to fit this.")]
    public float maxSpinTime = 0.7f;

    // ── runtime ──
    private readonly List<Reel> _reels = new List<Reel>();
    private int _target;
    private RectTransform _rt;

    private class Reel
    {
        public GameObject     root;
        public RectTransform  strip;   // holds 0..10, moves in y
        public float          phase;   // continuous position in rows (0..10)
        public float          target;  // where it's heading (>= phase, forward-only)
    }

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        EnsureReels(Mathf.Max(1, minDigits));
        SetValueImmediate(_target);
    }

    /// <summary>Is any reel still rolling? (Use to keep the HUD visible while it spins.)</summary>
    public bool IsSpinning
    {
        get
        {
            for (int i = 0; i < _reels.Count; i++)
                if (Mathf.Abs(_reels[i].target - _reels[i].phase) > 0.01f) return true;
            return false;
        }
    }

    /// <summary>Roll to a new value (animated).</summary>
    public void SetValue(int value)
    {
        value = Mathf.Max(0, value);
        _target = value;
        EnsureReels(Mathf.Max(minDigits, DigitCount(value)));
        AssignTargets(value, animate: true);
    }

    /// <summary>Snap to a value with no spin (init / load).</summary>
    public void SetValueImmediate(int value)
    {
        value = Mathf.Max(0, value);
        _target = value;
        EnsureReels(Mathf.Max(minDigits, DigitCount(value)));
        AssignTargets(value, animate: false);
    }

    void Update()
    {
        for (int i = 0; i < _reels.Count; i++)
        {
            Reel r = _reels[i];
            if (Mathf.Abs(r.target - r.phase) <= 0.0001f) continue;

            float gap   = r.target - r.phase;
            float speed = Mathf.Max(spinSpeed, Mathf.Abs(gap) / Mathf.Max(0.0001f, maxSpinTime));
            r.phase = Mathf.MoveTowards(r.phase, r.target, speed * Time.unscaledDeltaTime);

            if (Mathf.Abs(r.target - r.phase) <= 0.0001f)
            {
                // Settle, then keep phase bounded to [0,10) so it never drifts large.
                float norm = Mod(r.target, 10f);
                r.phase = norm; r.target = norm;
            }
            ApplyReel(r);
        }
    }

    // ── targets ──

    private void AssignTargets(int value, bool animate)
    {
        for (int i = 0; i < _reels.Count; i++)
        {
            int place = _reels.Count - 1 - i;   // reel 0 = leftmost = highest place
            int digit = DigitAt(value, place);
            Reel r = _reels[i];

            if (!animate)
            {
                r.phase = digit; r.target = digit;
                ApplyReel(r);
                continue;
            }

            // Smallest position >= current phase whose digit matches → forward-only spin.
            float baseFloor = Mathf.Floor(r.phase);
            float candidate = baseFloor + Mod(digit - baseFloor, 10f);
            while (candidate < r.phase - 1e-3f) candidate += 10f;
            r.target = candidate;
        }
    }

    // ── build / rebuild reels ──

    private void EnsureReels(int count)
    {
        count = Mathf.Max(1, count);
        if (_reels.Count == count) { LayoutReels(); return; }

        // Simplest robust approach: tear down and rebuild, preserving nothing but
        // the target value (digit counts change rarely — only crossing 10/100/…).
        for (int i = 0; i < _reels.Count; i++)
            if (_reels[i].root != null) Destroy(_reels[i].root);
        _reels.Clear();

        for (int i = 0; i < count; i++)
            _reels.Add(BuildReel(i));

        LayoutReels();
    }

    private Reel BuildReel(int index)
    {
        // Viewport: a masked cell, one digit tall.
        var vp = new GameObject($"Reel{index}", typeof(RectTransform), typeof(RectMask2D));
        var vpRt = vp.GetComponent<RectTransform>();
        vpRt.SetParent(transform, false);
        vpRt.anchorMin = vpRt.anchorMax = new Vector2(1f, 0.5f); // right-aligned column
        vpRt.pivot     = new Vector2(1f, 0.5f);
        vpRt.sizeDelta = new Vector2(cellWidth, cellHeight);

        // Strip: anchored to the viewport top; slides in y to reveal a digit.
        var strip = new GameObject("Strip", typeof(RectTransform));
        var stripRt = strip.GetComponent<RectTransform>();
        stripRt.SetParent(vpRt, false);
        stripRt.anchorMin = stripRt.anchorMax = new Vector2(0.5f, 1f);
        stripRt.pivot     = new Vector2(0.5f, 1f);
        stripRt.sizeDelta = new Vector2(cellWidth, cellHeight * 11f);
        stripRt.anchoredPosition = Vector2.zero;

        // Eleven glyphs 0..9 plus a trailing 0 so 9→0 wraps seamlessly.
        for (int d = 0; d <= 10; d++)
        {
            var g = new GameObject($"D{d}", typeof(RectTransform));
            var gRt = g.GetComponent<RectTransform>();
            gRt.SetParent(stripRt, false);
            gRt.anchorMin = gRt.anchorMax = new Vector2(0.5f, 1f);
            gRt.pivot     = new Vector2(0.5f, 1f);
            gRt.sizeDelta = new Vector2(cellWidth, cellHeight);
            gRt.anchoredPosition = new Vector2(0f, -d * cellHeight);

            var tmp = g.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text          = (d % 10).ToString();
            tmp.fontSize      = fontSize;
            tmp.color         = textColor;
            tmp.fontStyle     = fontStyle;
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
        }

        return new Reel { root = vp, strip = stripRt, phase = 0f, target = 0f };
    }

    private void LayoutReels()
    {
        int count = _reels.Count;
        for (int i = 0; i < count; i++)
        {
            var vpRt = _reels[i].root.GetComponent<RectTransform>();
            // Rightmost reel at x=0, each earlier reel one cell further left.
            vpRt.anchoredPosition = new Vector2(-(count - 1 - i) * cellWidth, 0f);
        }
    }

    private void ApplyReel(Reel r)
    {
        r.strip.anchoredPosition = new Vector2(0f, r.phase * cellHeight);
    }

    // ── helpers ──
    private static int DigitCount(int v) => v < 10 ? 1 : Mathf.FloorToInt(Mathf.Log10(v)) + 1;
    private static int DigitAt(int value, int place)
    {
        for (int i = 0; i < place; i++) value /= 10;
        return value % 10;
    }
    private static float Mod(float a, float m) => ((a % m) + m) % m;
}
