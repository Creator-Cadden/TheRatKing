using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One row in the objective list on the right of the screen: a line of text and
/// a row of key icons that turn from white to green as you do the thing.
///
/// The card owns nothing but its own look and its own slide. ObjectiveListUI
/// decides where it sits and when it leaves; the card just eases toward whatever
/// anchored position it was last given, which is what makes the "a card leaves
/// and the ones below slide up into its slot" behaviour fall out for free —
/// the list re-numbers the rows, every card gets a new target, everyone glides.
///
/// CARD PREFAB (build once, RectTransform pivot + anchor = top-right):
///   ObjectiveCard          RectTransform · CanvasGroup · ObjectiveCardUI
///     ├ Background         Image
///     ├ Label              TMP_Text
///     ├ Counter            TMP_Text   (e.g. "1 / 3"; hidden when not needed)
///     └ Icons              HorizontalLayoutGroup
///         ├ Icon_0 … Icon_5  Image   (drag all six into Icon Slots)
/// </summary>
public class ObjectiveCardUI : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform rect;
    public CanvasGroup   group;
    public TMP_Text      label;
    public TMP_Text      counter;
    [Tooltip("Every icon Image on the card, left to right. Unused ones are hidden.")]
    public Image[]       iconSlots;

    [Tooltip("The GREEN FILL — the part that moves. Its Image Type does not " +
             "matter: the width is driven by the RectTransform, not fillAmount.")]
    public Image         progressBar;

    [Tooltip("The faint strip behind the fill, so an empty bar reads as 'nothing " +
             "yet' instead of vanishing. Optional — leave empty for fill only.")]
    public Image         progressTrack;

    [Header("Icon sizing")]
    [Tooltip("Every icon is drawn at this height; its width follows the sprite's " +
             "aspect. Without this, a 3:1 SPACE bar squashed into a square slot " +
             "renders as a sliver — which is what a 28×28 slot with Preserve " +
             "Aspect does to a wide key.")]
    public float iconHeight = 30f;
    [Tooltip("Ceiling on one icon's width, so a very wide key can't push the row " +
             "off the card.")]
    public float maxIconWidth = 140f;

    [Header("Bar look (used when the bar is built in code)")]
    public Color trackColor = new Color(1f, 1f, 1f, 0.12f);
    public Color fillColor  = new Color(0.30f, 0.85f, 0.35f, 1f);
    public float barHeight  = 5f;
    public float barInset   = 10f;
    public float barBottom  = 6f;

    [Header("Feel")]
    [Tooltip("How fast the card eases to its slot. Higher = snappier.")]
    public float lerpSpeed = 12f;
    [Tooltip("How fast the green bar chases its target. This is what makes a " +
             "quarter-step (one of WASD) glide instead of jumping.")]
    public float progressLerpSpeed = 9f;
    [Tooltip("Scale punch when an icon turns green.")]
    public float iconPopScale = 1.35f;
    public float iconPopDecay = 6f;

    /// <summary>Id the list looks this card up by.</summary>
    public string Id { get; private set; }
    /// <summary>True once it's been told to leave — the list stops counting it.</summary>
    public bool   IsExiting { get; private set; }
    /// <summary>True once it has slid far enough off-screen to be destroyed.</summary>
    public bool   ReadyToDestroy { get; private set; }

    private Vector2 _target;
    private Sprite[] _spriteIdle;
    private Sprite[] _spriteDone;
    private bool[]   _done;
    private float[]  _pop;
    private Color    _colIdle = Color.white;
    private Color    _colDone = Color.green;
    private int      _used;
    private float    _exitX;
    private float    _progressTarget = -1f;
    private float    _progressShown;

    /// <summary>
    /// The fill's RectTransform. THIS is what animates, not Image.fillAmount.
    ///
    /// Why: Image.fillAmount only does anything when the Image has a sprite.
    /// Image.OnPopulateMesh bails out to the plain Graphic full-rect quad the
    /// moment activeSprite is null, so a Filled image with an empty Sprite field
    /// draws 100% wide forever and silently ignores fillAmount — a static green
    /// bar, no matter how correct Type / Fill Method / Origin are. That was the
    /// bug. Stretching the rect works with no sprite, no atlas and no import
    /// settings, so the bar can't break again on art that isn't there.
    /// </summary>
    private RectTransform _fillRect;

    // ── Setup ─────────────────────────────────────────────────────────────────

    public void Init(string id, string text,
                     List<Sprite> idleSprites, List<Sprite> doneSprites,
                     Color idleColor, Color doneColor,
                     Vector2 slot, float enterOffsetX, float lerp)
    {
        Id        = id;
        lerpSpeed = lerp;
        _colIdle  = idleColor;
        _colDone  = doneColor;

        if (rect  == null) rect  = GetComponent<RectTransform>();
        if (group == null) group = GetComponent<CanvasGroup>();

        if (label != null) label.text = text;
        SetCounter("");

        EnsureBar();
        SetProgress(-1f);   // hidden until someone actually pushes progress

        int n = idleSprites != null ? idleSprites.Count : 0;
        if (iconSlots != null) n = Mathf.Min(n, iconSlots.Length);
        _used       = n;
        _spriteIdle = new Sprite[n];
        _spriteDone = new Sprite[n];
        _done       = new bool[n];
        _pop        = new float[n];

        if (iconSlots != null)
        {
            for (int i = 0; i < iconSlots.Length; i++)
            {
                if (iconSlots[i] == null) continue;
                bool used = i < n;
                iconSlots[i].gameObject.SetActive(used);
                if (!used) continue;

                _spriteIdle[i] = idleSprites[i];
                _spriteDone[i] = (doneSprites != null && i < doneSprites.Count) ? doneSprites[i] : null;
                iconSlots[i].sprite = _spriteIdle[i];
                iconSlots[i].color  = _colIdle;
                iconSlots[i].rectTransform.localScale = Vector3.one;
                ApplyIconSize(iconSlots[i], _spriteIdle[i]);
            }
        }

        // Start off to the right of its slot and glide in.
        _target = slot;
        if (rect != null) rect.anchoredPosition = slot + new Vector2(enterOffsetX, 0f);
        if (group != null) group.alpha = 1f;
    }

    /// <summary>
    /// Guarantees a working bar: builds the track and fill if the prefab has
    /// none, and — the part that matters — forces the fill's RectTransform into
    /// the one configuration the animation relies on.
    ///
    /// Runs on EVERY card, not just ones missing a bar, because a prefab wired
    /// by hand or by an older version of the setup tool is exactly how the fill
    /// ends up centred, or stretched to full width, or set to Filled with no
    /// sprite. Normalising here means the prefab can be in any state and the
    /// bar still behaves.
    /// </summary>
    private void EnsureBar()
    {
        RectTransform host = rect != null ? rect : GetComponent<RectTransform>();
        if (host == null) return;

        // ── The translucent track ────────────────────────────────────────────
        if (progressTrack == null)
        {
            Transform found = host.Find("ProgressTrack");
            GameObject trackGO = found != null
                ? found.gameObject
                : new GameObject("ProgressTrack", typeof(RectTransform));
            if (found == null) trackGO.transform.SetParent(host, false);

            RectTransform tr = trackGO.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 0f);
            tr.pivot     = new Vector2(0.5f, 0f);
            tr.offsetMin = new Vector2(barInset, barBottom);
            tr.offsetMax = new Vector2(-barInset, barBottom + barHeight);

            progressTrack = trackGO.GetComponent<Image>();
            if (progressTrack == null) progressTrack = trackGO.AddComponent<Image>();
        }

        if (progressTrack == null) return;
        progressTrack.color         = trackColor;
        progressTrack.type          = Image.Type.Simple;
        progressTrack.raycastTarget = false;

        // ── The green fill, always a child of the track ──────────────────────
        if (progressBar == null)
        {
            Transform f = progressTrack.transform.Find("Fill");
            GameObject fillGO = f != null
                ? f.gameObject
                : new GameObject("Fill", typeof(RectTransform));
            if (f == null) fillGO.transform.SetParent(progressTrack.transform, false);

            progressBar = fillGO.GetComponent<Image>();
            if (progressBar == null) progressBar = fillGO.AddComponent<Image>();
        }

        progressBar.color         = fillColor;
        progressBar.raycastTarget = false;
        // Simple on purpose. Filled would need a sprite to mean anything, and
        // the width comes from the rect now — see the note on _fillRect.
        progressBar.type          = Image.Type.Simple;

        _fillRect = progressBar.rectTransform;
        ResetFillRect();
        ApplyFill(0f);
    }

    /// <summary>
    /// Pins everything about the fill's rect except the one number the animation
    /// drives (anchorMax.x). With sizeDelta and anchoredPosition at zero, the
    /// rect is exactly the anchor rect, so width == anchorMax.x × track width and
    /// the left edge stays put — growth runs left to right, which is the whole ask.
    /// </summary>
    private void ResetFillRect()
    {
        if (_fillRect == null) return;
        _fillRect.pivot            = new Vector2(0.5f, 0.5f);
        _fillRect.anchorMin        = new Vector2(0f, 0f);
        _fillRect.anchoredPosition = Vector2.zero;
        _fillRect.sizeDelta        = Vector2.zero;
    }

    /// <summary>Draw the bar at 0–1 of the track's width.</summary>
    private void ApplyFill(float t)
    {
        t = Mathf.Clamp01(t);
        if (_fillRect != null)
        {
            _fillRect.anchorMin        = new Vector2(0f, 0f);
            _fillRect.anchorMax        = new Vector2(t, 1f);
            _fillRect.anchoredPosition = Vector2.zero;
            _fillRect.sizeDelta        = Vector2.zero;
        }
        // Kept in step so the bar still reads correctly if a sprite is ever
        // assigned and the Type switched back to Filled.
        if (progressBar != null) progressBar.fillAmount = t;
    }

    // ── Driven by the list ────────────────────────────────────────────────────

    public void SetSlot(Vector2 slot)
    {
        if (IsExiting) return;
        _target = slot;
    }

    public void SetCounter(string text)
    {
        if (counter == null) return;
        bool show = !string.IsNullOrEmpty(text);
        counter.gameObject.SetActive(show);
        if (show) counter.text = text;
    }

    /// <summary>
    /// Target 0–1 for the bar. Negative hides it — that's how a card with nothing
    /// to measure stays clean instead of showing an empty bar. The fill EASES to
    /// this value rather than snapping, so a held key reads as continuous motion
    /// and a discrete step (one of four directions) glides a quarter across.
    /// </summary>
    public void SetProgress(float t)
    {
        bool show = t >= 0f;

        if (progressTrack != null && progressTrack.gameObject.activeSelf != show)
            progressTrack.gameObject.SetActive(show);

        if (progressBar != null)
        {
            if (progressBar.gameObject.activeSelf != show) progressBar.gameObject.SetActive(show);
            // A freshly shown bar starts EMPTY and grows, so the motion is the
            // feedback. (It used to snap to the current value, which on a card
            // that appears mid-hold looked like a bar that never moves.)
            if (_progressTarget < 0f && show)
            {
                _progressShown = 0f;
                ApplyFill(0f);
            }
        }

        _progressTarget = show ? Mathf.Clamp01(t) : -1f;
    }

    /// <summary>White → green on one icon. Idempotent; only pops the first time.</summary>
    public void MarkIcon(int index, bool done)
    {
        if (_done == null || index < 0 || index >= _used) return;
        if (_done[index] == done) return;

        _done[index] = done;
        if (iconSlots == null || index >= iconSlots.Length || iconSlots[index] == null) return;

        if (done)
        {
            if (_spriteDone[index] != null)
            {
                iconSlots[index].sprite = _spriteDone[index];
                ApplyIconSize(iconSlots[index], _spriteDone[index]);
            }
            iconSlots[index].color = _colDone;
            _pop[index] = 1f;
        }
        else
        {
            iconSlots[index].sprite = _spriteIdle[index];
            iconSlots[index].color  = _colIdle;
            ApplyIconSize(iconSlots[index], _spriteIdle[index]);
        }
    }

    /// <summary>
    /// Height is fixed, width comes from the sprite. SPACE is three keys wide and
    /// CTRL/TAB/SHIFT two; forcing them all into one square is what made them
    /// look tiny next to the single-letter keys.
    /// </summary>
    private void ApplyIconSize(Image img, Sprite sprite)
    {
        if (img == null || sprite == null) return;

        float h = Mathf.Max(8f, iconHeight);
        float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
        float w = Mathf.Min(h * aspect, Mathf.Max(h, maxIconWidth));

        img.preserveAspect = true;
        img.rectTransform.sizeDelta = new Vector2(w, h);

        // A LayoutElement wins over sizeDelta inside a layout group, so keep the
        // two in step rather than fighting.
        var le = img.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le != null) { le.preferredWidth = w; le.preferredHeight = h; }
    }

    /// <summary>Light every icon at once — used when a card completes.</summary>
    public void MarkAllIcons()
    {
        for (int i = 0; i < _used; i++) MarkIcon(i, true);
    }

    /// <summary>Slide off to the right and die. The list re-layouts immediately.</summary>
    public void Exit(float exitOffsetX)
    {
        if (IsExiting) return;
        IsExiting = true;
        _exitX    = exitOffsetX;
        _target   = _target + new Vector2(exitOffsetX, 0f);
    }

    // ── Motion ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (rect == null) return;

        // Exponential ease — frame-rate independent, unlike a raw Lerp(a,b,speed*dt).
        float t = 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime);
        rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, _target, t);

        if (IsExiting)
        {
            float remaining = Mathf.Abs(_target.x - rect.anchoredPosition.x);
            if (group != null)
                group.alpha = Mathf.Clamp01(remaining / Mathf.Max(1f, _exitX));
            if (remaining < _exitX * 0.08f) ReadyToDestroy = true;
        }

        // Green bar eases toward its target. Snapping the last hair closed stops
        // an exponential ease from parking at 0.998 forever on a finished task.
        if (_fillRect != null && _progressTarget >= 0f)
        {
            float pt = 1f - Mathf.Exp(-Mathf.Max(0.01f, progressLerpSpeed) * Time.unscaledDeltaTime);
            _progressShown = Mathf.Lerp(_progressShown, _progressTarget, pt);
            if (Mathf.Abs(_progressTarget - _progressShown) < 0.002f) _progressShown = _progressTarget;
            ApplyFill(_progressShown);
        }

        // Icon pops.
        if (_pop == null || iconSlots == null) return;
        for (int i = 0; i < _used && i < iconSlots.Length; i++)
        {
            if (_pop[i] <= 0f || iconSlots[i] == null) continue;
            _pop[i] = Mathf.MoveTowards(_pop[i], 0f, iconPopDecay * Time.unscaledDeltaTime);
            float s = 1f + (iconPopScale - 1f) * _pop[i];
            iconSlots[i].rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }
}
