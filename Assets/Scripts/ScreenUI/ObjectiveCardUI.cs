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

    [Header("Icon sizing")]
    [Tooltip("Every icon is drawn at this height; its width follows the sprite's " +
             "aspect. Without this, a 3:1 SPACE bar squashed into a square slot " +
             "renders as a sliver — which is what a 28×28 slot with Preserve " +
             "Aspect does to a wide key.")]
    public float iconHeight = 30f;
    [Tooltip("Ceiling on one icon's width, so a very wide key can't push the row " +
             "off the card.")]
    public float maxIconWidth = 140f;

    [Header("Feel")]
    [Tooltip("How fast the card eases to its slot. Higher = snappier.")]
    public float lerpSpeed = 12f;
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
