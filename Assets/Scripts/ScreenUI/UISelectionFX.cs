using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Adds "selected" flair to a button: a secondary spaced-out border that lights
/// up, plus little diamonds that orbit and shimmer around it. Reacts to BOTH
/// mouse hover and keyboard/controller selection. Fully self-contained — builds
/// its own visuals at runtime (no sprites needed) and uses unscaled time.
/// Drop onto any button. Pairs fine with UIButtonJuice / UIMenuEntrance.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UISelectionFX : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("Secondary border")]
    public bool  showBorder      = true;
    [Tooltip("Lighter accent colour for the border frame.")]
    public Color borderColor     = new Color(1f, 0.90f, 0.55f, 1f);
    [Tooltip("Gap between the button edge and the border.")]
    public float borderSpacing   = 10f;
    public float borderThickness = 3f;

    [Header("Shimmer diamonds")]
    public bool  showDiamonds  = true;
    public int   diamondCount  = 8;
    public float diamondSize   = 13f;
    public Color diamondColor  = new Color(1f, 0.95f, 0.70f, 1f);
    [Tooltip("How far outside the border the diamonds sit.")]
    public float diamondOrbit  = 14f;
    [Tooltip("Twinkle speed.")]
    public float shimmerSpeed  = 3.2f;
    [Tooltip("Gentle rotation of the whole diamond ring (deg/sec).")]
    public float ringDriftSpeed = 18f;

    [Header("Fade")]
    public float fadeSpeed = 12f;

    private RectTransform _rt;
    private RectTransform _fxRoot;
    private CanvasGroup   _fxGroup;
    private RectTransform[] _diamonds;
    private float[]         _phases, _baseAngles;
    private bool _hover, _selected, _built;

    void Awake() { _rt = (RectTransform)transform; Build(); }

    void OnDisable()
    {
        _hover = _selected = false;
        if (_fxGroup != null) _fxGroup.alpha = 0f;
    }

    private void Build()
    {
        if (_built) return; _built = true;

        var rootGO = new GameObject("SelectionFX");
        _fxRoot = rootGO.AddComponent<RectTransform>();
        _fxRoot.SetParent(_rt, false);
        _fxRoot.anchorMin = Vector2.zero; _fxRoot.anchorMax = Vector2.one;
        _fxRoot.offsetMin = new Vector2(-borderSpacing, -borderSpacing);
        _fxRoot.offsetMax = new Vector2( borderSpacing,  borderSpacing);
        _fxRoot.localScale = Vector3.one;

        _fxGroup = rootGO.AddComponent<CanvasGroup>();
        _fxGroup.alpha = 0f;
        _fxGroup.interactable = false;
        _fxGroup.blocksRaycasts = false;

        if (showBorder)
        {
            MakeEdge("Top",    new Vector2(0,1), new Vector2(1,1), new Vector2(0.5f,1), new Vector2(0f, borderThickness));
            MakeEdge("Bottom", new Vector2(0,0), new Vector2(1,0), new Vector2(0.5f,0), new Vector2(0f, borderThickness));
            MakeEdge("Left",   new Vector2(0,0), new Vector2(0,1), new Vector2(0,0.5f), new Vector2(borderThickness, 0f));
            MakeEdge("Right",  new Vector2(1,0), new Vector2(1,1), new Vector2(1,0.5f), new Vector2(borderThickness, 0f));
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
                _baseAngles[i] = (i / (float)diamondCount) * Mathf.PI * 2f
                                 + Random.Range(-0.1f, 0.1f);
            }
        }
    }

    private void MakeEdge(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_fxRoot, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = size;                 // 0 on the stretched axis
        rt.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = borderColor;
        img.raycastTarget = false;
    }

    private RectTransform MakeDiamond(int i)
    {
        var go = new GameObject("Diamond" + i);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_fxRoot, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(diamondSize, diamondSize);
        rt.localRotation = Quaternion.Euler(0, 0, 45f);   // square → diamond
        var img = go.AddComponent<Image>();
        img.color = diamondColor;
        img.raycastTarget = false;
        return rt;
    }

    void Update()
    {
        bool active = _hover || _selected;

        float k = 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime);
        _fxGroup.alpha = Mathf.Lerp(_fxGroup.alpha, active ? 1f : 0f, k);

        if (_fxGroup.alpha < 0.001f) return;   // nothing to animate while hidden

        if (_diamonds != null)
        {
            Vector2 half = _fxRoot.rect.size * 0.5f;
            float rx = half.x + diamondOrbit;
            float ry = half.y + diamondOrbit;
            float drift = Time.unscaledTime * ringDriftSpeed * Mathf.Deg2Rad;

            for (int i = 0; i < _diamonds.Length; i++)
            {
                float ang = _baseAngles[i] + drift;
                _diamonds[i].anchoredPosition = new Vector2(Mathf.Cos(ang) * rx,
                                                            Mathf.Sin(ang) * ry);

                float sh = 0.35f + 0.65f * (0.5f + 0.5f *
                           Mathf.Sin(Time.unscaledTime * shimmerSpeed + _phases[i]));
                var img = _diamonds[i].GetComponent<Image>();
                var c = diamondColor; c.a = diamondColor.a * sh; img.color = c;

                _diamonds[i].localRotation = Quaternion.Euler(0, 0, 45f + drift * Mathf.Rad2Deg);
                _diamonds[i].localScale = Vector3.one * (0.75f + 0.4f * sh);
            }
        }
    }

    public void OnPointerEnter(PointerEventData e) => _hover = true;
    public void OnPointerExit (PointerEventData e) => _hover = false;
    public void OnSelect      (BaseEventData e)    => _selected = true;
    public void OnDeselect    (BaseEventData e)    => _selected = false;
}
