using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Button flair: an always-on secondary border (bronze at rest, brightens to
/// gold on hover), shimmering diamonds that orbit on hover/select, a hover tint
/// that turns the fill gold and the TEXT dark (readable), and a bright click
/// burst before the wipe. Reacts to mouse hover AND keyboard/controller select.
/// Renders its border + diamonds on a boosted sorting layer so adjacent buttons
/// don't clip them. Self-contained (builds its own visuals), uses unscaled time.
/// Set the Button's Transition to None so it doesn't fight the hover tint.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UISelectionFX : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler, IPointerClickHandler
{
    [Header("Border (spaced frame)")]
    public bool  showBorder       = true;
    [Tooltip("Border colour at rest (aged bronze).")]
    public Color borderRestColor  = new Color(0.43f, 0.35f, 0.20f, 1f);
    [Tooltip("Border colour when hovered/selected (gold). Diamonds + fill use this too.")]
    public Color borderColor      = new Color(1f, 0.83f, 0.48f, 1f);
    [Tooltip("Show the bronze border always (true) or only fade it in on hover (false).")]
    public bool  alwaysShowBorder = true;
    [Tooltip("How far the frame sits outside the button edge. Keep small (~4) so " +
             "it hugs the button.")]
    public float borderSpacing    = 4f;
    [Tooltip("Optional border sprite. Leave empty to reuse the BUTTON's own sprite " +
             "so the rounded corners match. (Sprite must be 9-sliced for the hollow " +
             "frame; most rounded UI sprites are.)")]
    public Sprite borderSprite;
    [Tooltip("Only used as a fallback if no sliced sprite is available.")]
    public float borderThickness  = 3f;

    [Header("Shimmer diamonds")]
    public bool  showDiamonds   = true;
    public int   diamondCount   = 8;
    public float diamondSize    = 13f;
    public Color diamondColor   = new Color(1f, 0.86f, 0.55f, 1f);
    public float diamondOrbit   = 14f;
    public float shimmerSpeed   = 3.2f;
    public float ringDriftSpeed = 18f;

    [Header("Render on top")]
    [Tooltip("Draw the border + diamonds this many sorting steps above the menu " +
             "so neighbouring buttons don't clip them.")]
    public int sortingBoost = 5;
    [Tooltip("Extra sorting added WHILE hovered/selected, so this button's frame + " +
             "diamonds rise above every other button's border.")]
    public int activeSortBoost = 10;

    [Header("Hover tint")]
    [Tooltip("Tint the button fill to the gold border colour on hover/select.")]
    public bool  tintButton    = true;
    [Tooltip("Tint the button TEXT to this darker colour on hover (readable on gold).")]
    public bool  tintLabel     = true;
    public Color hoverTextColor = new Color(0.16f, 0.13f, 0.08f, 1f);

    [Header("Click burst")]
    public bool  enableClickBurst = true;
    public float burstDuration    = 0.28f;
    public float burstPush        = 34f;
    public Color burstFlashColor  = Color.white;

    [Header("Fade")]
    public float fadeSpeed = 12f;

    private RectTransform _rt, _fxRoot, _diamondGroup;
    private CanvasGroup   _diamondCG;
    private Image[]       _border;
    private Canvas        _fxCanvas;
    private int           _baseSort;
    private RectTransform[] _diamonds;
    private float[]         _phases, _baseAngles;
    private bool _hover, _selected, _built;

    private Graphic  _btnGraphic; private Color _btnBase;
    private TMP_Text _label;      private Color _labelBase;
    private float    _burstTime, _amount;

    void Awake() { _rt = (RectTransform)transform; Build(); }

    void OnDisable()
    {
        _hover = _selected = false;
        _amount = 0f;
        if (_diamondCG  != null) _diamondCG.alpha = 0f;
        if (_btnGraphic != null) _btnGraphic.color = _btnBase;
        if (_label      != null) _label.color = _labelBase;
    }

    private void Build()
    {
        if (_built) return; _built = true;

        _btnGraphic = GetComponent<Graphic>();
        if (_btnGraphic != null) _btnBase = _btnGraphic.color;
        _label = GetComponentInChildren<TMP_Text>();
        if (_label != null) _labelBase = _label.color;

        var rootGO = new GameObject("SelectionFX");
        _fxRoot = rootGO.AddComponent<RectTransform>();
        _fxRoot.SetParent(_rt, false);
        _fxRoot.anchorMin = Vector2.zero; _fxRoot.anchorMax = Vector2.one;
        _fxRoot.offsetMin = new Vector2(-borderSpacing, -borderSpacing);
        _fxRoot.offsetMax = new Vector2( borderSpacing,  borderSpacing);
        _fxRoot.localScale = Vector3.one;

        // Own sorting so the frame + diamonds draw above sibling buttons. Made
        // RELATIVE to the menu canvas so a hard number can't be undercut by it.
        var cv = rootGO.AddComponent<Canvas>();
        cv.overrideSorting = true;
        var parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            cv.sortingLayerID = parentCanvas.sortingLayerID;
            cv.sortingOrder   = parentCanvas.sortingOrder + Mathf.Max(1, sortingBoost);
        }
        else cv.sortingOrder = sortingBoost;
        _fxCanvas = cv;
        _baseSort = cv.sortingOrder;

        // Diamonds live under a fading group; the border does not (always visible).
        var dgGO = new GameObject("Diamonds");
        _diamondGroup = dgGO.AddComponent<RectTransform>();
        _diamondGroup.SetParent(_fxRoot, false);
        _diamondGroup.anchorMin = Vector2.zero; _diamondGroup.anchorMax = Vector2.one;
        _diamondGroup.offsetMin = Vector2.zero; _diamondGroup.offsetMax = Vector2.zero;
        _diamondCG = dgGO.AddComponent<CanvasGroup>();
        _diamondCG.alpha = 0f; _diamondCG.interactable = false; _diamondCG.blocksRaycasts = false;

        if (showBorder)
        {
            Sprite spr = borderSprite;
            if (spr == null && _btnGraphic is Image bi) spr = bi.sprite;

            if (spr != null)
            {
                // Single rounded frame reusing the button's sprite → corners match.
                _border = new Image[] { MakeFrame(spr) };
            }
            else
            {
                // Fallback: straight 4-edge rectangle (no sprite available).
                _border = new Image[4];
                _border[0] = MakeEdge("Top",    new Vector2(0,1), new Vector2(1,1), new Vector2(0.5f,1), new Vector2(0f, borderThickness));
                _border[1] = MakeEdge("Bottom", new Vector2(0,0), new Vector2(1,0), new Vector2(0.5f,0), new Vector2(0f, borderThickness));
                _border[2] = MakeEdge("Left",   new Vector2(0,0), new Vector2(0,1), new Vector2(0,0.5f), new Vector2(borderThickness, 0f));
                _border[3] = MakeEdge("Right",  new Vector2(1,0), new Vector2(1,1), new Vector2(1,0.5f), new Vector2(borderThickness, 0f));
            }
        }

        if (showDiamonds && diamondCount > 0)
        {
            _diamonds   = new RectTransform[diamondCount];
            _phases     = new float[diamondCount];
            _baseAngles = new float[diamondCount];
            for (int i = 0; i < diamondCount; i++)
            {
                _diamonds[i]   = MakeDiamond(i);
                _phases[i]     = Random.Range(0f, Mathf.PI * 2f);
                _baseAngles[i] = (i / (float)diamondCount) * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
            }
        }
    }

    private Image MakeFrame(Sprite spr)
    {
        var go = new GameObject("Border");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_fxRoot, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;   // fills _fxRoot (button + spacing)
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite     = spr;
        img.type       = Image.Type.Sliced;
        img.fillCenter = false;                 // hollow → only the rounded edge shows
        img.raycastTarget = false;
        img.color = alwaysShowBorder ? borderRestColor
                                     : new Color(borderColor.r, borderColor.g, borderColor.b, 0f);
        return img;
    }

    private Image MakeEdge(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_fxRoot, false);      // direct child → not faded by diamond group
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = alwaysShowBorder ? borderRestColor : new Color(borderColor.r, borderColor.g, borderColor.b, 0f);
        img.raycastTarget = false;
        return img;
    }

    private RectTransform MakeDiamond(int i)
    {
        var go = new GameObject("Diamond" + i);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_diamondGroup, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(diamondSize, diamondSize);
        rt.localRotation = Quaternion.Euler(0, 0, 45f);
        var img = go.AddComponent<Image>();
        img.color = diamondColor;
        img.raycastTarget = false;
        return rt;
    }

    void Update()
    {
        bool active   = _hover || _selected;
        bool bursting = _burstTime > 0f;
        if (bursting) _burstTime -= Time.unscaledDeltaTime;

        // While active, lift this button's whole FX layer above every other one.
        if (_fxCanvas != null)
            _fxCanvas.sortingOrder = _baseSort + ((active || bursting) ? activeSortBoost : 0);

        float bp = bursting
            ? 1f - Mathf.Clamp01(_burstTime / Mathf.Max(0.0001f, burstDuration))
            : -1f;

        float k = 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime);
        _amount = Mathf.Lerp(_amount, (active || bursting) ? 1f : 0f, k);

        // Border: bronze → gold (or flash white on click).
        if (showBorder && _border != null)
        {
            Color bc;
            float ba;
            if (bp >= 0f)      { bc = Color.Lerp(burstFlashColor, borderColor, bp); ba = 1f; }
            else if (alwaysShowBorder) { bc = Color.Lerp(borderRestColor, borderColor, _amount); ba = 1f; }
            else               { bc = borderColor; ba = _amount; }
            bc.a = ba;
            for (int i = 0; i < _border.Length; i++) if (_border[i] != null) _border[i].color = bc;
        }

        if (_diamondCG != null) _diamondCG.alpha = (bp >= 0f) ? 1f : _amount;

        // Fill → gold on hover; flash on click.
        if (tintButton && _btnGraphic != null)
        {
            Color t = active ? borderColor : _btnBase;
            _btnGraphic.color = bp >= 0f ? Color.Lerp(burstFlashColor, t, bp)
                                         : Color.Lerp(_btnGraphic.color, t, k);
        }
        // Text → dark on hover; flash on click.
        if (tintLabel && _label != null)
        {
            Color t = active ? hoverTextColor : _labelBase;
            _label.color = bp >= 0f ? Color.Lerp(burstFlashColor, t, bp)
                                    : Color.Lerp(_label.color, t, k);
        }

        if (_diamonds != null && (_amount > 0.001f || bursting))
        {
            Vector2 half = _fxRoot.rect.size * 0.5f;
            float rx = half.x + diamondOrbit;
            float ry = half.y + diamondOrbit;
            float extra = bp >= 0f ? burstPush * (1f - (1f - bp) * (1f - bp)) : 0f;
            float drift = Time.unscaledTime * ringDriftSpeed * Mathf.Deg2Rad;

            for (int i = 0; i < _diamonds.Length; i++)
            {
                float ang = _baseAngles[i] + drift;
                _diamonds[i].anchoredPosition = new Vector2(Mathf.Cos(ang) * (rx + extra),
                                                            Mathf.Sin(ang) * (ry + extra));

                float sh = 0.35f + 0.65f * (0.5f + 0.5f *
                           Mathf.Sin(Time.unscaledTime * shimmerSpeed + _phases[i]));

                var img = _diamonds[i].GetComponent<Image>();
                if (bp >= 0f)
                {
                    var c = burstFlashColor; c.a = 1f; img.color = c;
                    _diamonds[i].localScale = Vector3.one * (1.5f - 0.7f * bp);
                }
                else
                {
                    var c = diamondColor; c.a = diamondColor.a * sh; img.color = c;
                    _diamonds[i].localScale = Vector3.one * (0.75f + 0.4f * sh);
                }
                _diamonds[i].localRotation = Quaternion.Euler(0, 0, 45f + drift * Mathf.Rad2Deg);
            }
        }
    }

    public void OnPointerEnter(PointerEventData e) => _hover = true;
    public void OnPointerExit (PointerEventData e) => _hover = false;
    public void OnSelect      (BaseEventData e)    => _selected = true;
    public void OnDeselect    (BaseEventData e)    => _selected = false;
    public void OnPointerClick(PointerEventData e) { if (enableClickBurst) _burstTime = burstDuration; }
}
