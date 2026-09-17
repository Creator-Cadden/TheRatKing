using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows the melee chain as it happens: one pip per hit in the chain, filling as
/// you land them, with a thin bar draining to show how long you have before the
/// chain lapses. The last pip is called out as the finisher, because that's the
/// hit that actually carries the extra damage and impact — without a display,
/// the chain is invisible and players never learn it exists.
///
/// It builds its own pips at runtime, so there's nothing to wire: drop it on an
/// empty RectTransform inside a Canvas. It reads BladeCombat / HammerCombat
/// directly and hides itself when the bow is equipped (no chain) or the chain
/// has lapsed.
/// </summary>
public class ComboCounterUI : MonoBehaviour
{
    [Header("Optional refs (found automatically if empty)")]
    public BladeCombat  blade;
    public HammerCombat hammer;
    public EntityStats  playerStats;

    [Header("Optional label")]
    [Tooltip("Shown when the next hit is the finisher. Leave empty for pips only.")]
    public TMP_Text finisherLabel;
    public string finisherText = "FINISHER";

    [Header("Pips")]
    public float pipSize    = 22f;
    public float pipSpacing = 8f;
    public Color pipEmpty   = new Color(1f, 1f, 1f, 0.22f);
    public Color pipFilled  = new Color(0.81f, 0.62f, 0.14f, 1f);
    public Color pipFinisher = new Color(1f, 0.42f, 0.18f, 1f);
    [Tooltip("Sprite for a pip. Leave empty for a plain square.")]
    public Sprite pipSprite;

    [Header("Window bar")]
    [Tooltip("Thin bar that drains as the chain window closes. 0 = no bar.")]
    public float barHeight = 4f;
    public Color barColor  = new Color(1f, 1f, 1f, 0.55f);

    [Header("Feel")]
    public float fadeSpeed = 10f;
    [Tooltip("Scale punch on a pip as it fills.")]
    public float popScale = 1.5f;
    public float popDecay = 5f;

    private CanvasGroup _group;
    private RectTransform _rect;
    private RectTransform _pipRow;
    private Image _bar;
    private readonly List<Image> _pips = new List<Image>();
    private readonly List<float> _pops = new List<float>();

    private int   _builtFor = -1;   // chain length the pips were built for
    private int   _lastProgress;
    private float _alpha;

    void Awake()
    {
        _rect  = GetComponent<RectTransform>();
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.interactable   = false;
        _group.blocksRaycasts = false;
        _group.alpha          = 0f;

        var row = new GameObject("Pips", typeof(RectTransform));
        row.transform.SetParent(transform, false);
        _pipRow = row.GetComponent<RectTransform>();
        _pipRow.anchorMin = _pipRow.anchorMax = _pipRow.pivot = new Vector2(0.5f, 0.5f);

        if (barHeight > 0f)
        {
            var bar = new GameObject("Window", typeof(RectTransform));
            bar.transform.SetParent(transform, false);
            _bar = bar.AddComponent<Image>();
            _bar.color         = barColor;
            _bar.raycastTarget = false;
            _bar.type          = Image.Type.Filled;
            _bar.fillMethod    = Image.FillMethod.Horizontal;

            RectTransform br = _bar.rectTransform;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0.5f);
            br.sizeDelta = new Vector2(pipSize * 4f, barHeight);
        }

        if (finisherLabel != null) finisherLabel.gameObject.SetActive(false);
    }

    void Start()
    {
        if (blade  == null) blade  = FindFirstObjectByType<BladeCombat>();
        if (hammer == null) hammer = FindFirstObjectByType<HammerCombat>();
        if (playerStats == null)
        {
            var move = FindFirstObjectByType<PlayerMovement>();
            if (move != null) playerStats = move.GetComponent<EntityStats>();
        }
    }

    void Update()
    {
        // Which weapon owns a chain right now?
        int   length   = 0;
        int   progress = 0;
        bool  alive    = false;
        bool  finisher = false;
        float window   = 0f;

        var weapon = playerStats != null ? playerStats.EquippedWeapon
                                         : EntityStats.WeaponType.None;

        if (weapon == EntityStats.WeaponType.Blade && blade != null)
        {
            length = blade.ComboLength; progress = blade.ComboProgress;
            alive  = blade.ChainAlive;  finisher = blade.NextIsFinisher;
            window = blade.ChainWindow01;
        }
        else if (weapon == EntityStats.WeaponType.Hammer && hammer != null)
        {
            length = hammer.ComboLength; progress = hammer.ComboProgress;
            alive  = hammer.ChainAlive;  finisher = hammer.NextIsFinisher;
            window = hammer.ChainWindow01;
        }

        // The bow has no chain — and a dead chain shows nothing rather than a
        // row of empty pips, which would read as "you're missing something".
        float target = alive && length > 1 ? 1f : 0f;
        _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * fadeSpeed);
        _group.alpha = _alpha;

        if (target <= 0f && _alpha <= 0.001f)
        {
            _lastProgress = 0;
            if (finisherLabel != null && finisherLabel.gameObject.activeSelf)
                finisherLabel.gameObject.SetActive(false);
            return;
        }

        if (length != _builtFor) BuildPips(length);

        // Fill up to progress; the pip that would be the finisher is tinted hot.
        for (int i = 0; i < _pips.Count; i++)
        {
            bool filled  = i < progress;
            bool isLast  = i == _pips.Count - 1;
            _pips[i].color = filled
                ? (isLast ? pipFinisher : pipFilled)
                : (isLast && finisher ? pipFinisher : pipEmpty);
        }

        // Pop the pip that just filled.
        if (progress > _lastProgress)
            for (int i = _lastProgress; i < progress && i < _pops.Count; i++) _pops[i] = 1f;
        _lastProgress = progress;

        for (int i = 0; i < _pips.Count; i++)
        {
            if (_pops[i] <= 0f) continue;
            _pops[i] = Mathf.MoveTowards(_pops[i], 0f, popDecay * Time.deltaTime);
            float s = 1f + (popScale - 1f) * _pops[i];
            _pips[i].rectTransform.localScale = new Vector3(s, s, 1f);
        }

        if (_bar != null) _bar.fillAmount = window;

        if (finisherLabel != null)
        {
            if (finisherLabel.gameObject.activeSelf != finisher)
                finisherLabel.gameObject.SetActive(finisher);
            if (finisher) finisherLabel.text = finisherText;
        }
    }

    private void BuildPips(int length)
    {
        foreach (Image p in _pips) if (p != null) Destroy(p.gameObject);
        _pips.Clear();
        _pops.Clear();
        _builtFor = length;
        if (length <= 0) return;

        float totalW = length * pipSize + (length - 1) * pipSpacing;
        _pipRow.sizeDelta = new Vector2(totalW, pipSize);
        _pipRow.anchoredPosition = new Vector2(0f, barHeight > 0f ? pipSize * 0.5f : 0f);

        for (int i = 0; i < length; i++)
        {
            var go = new GameObject("Pip" + i, typeof(RectTransform));
            go.transform.SetParent(_pipRow, false);

            var img = go.AddComponent<Image>();
            img.sprite        = pipSprite;
            img.color         = pipEmpty;
            img.raycastTarget = false;
            if (pipSprite != null) img.preserveAspect = true;

            RectTransform r = img.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(pipSize, pipSize);
            r.anchoredPosition = new Vector2(i * (pipSize + pipSpacing), 0f);

            _pips.Add(img);
            _pops.Add(0f);
        }

        if (_bar != null)
        {
            _bar.rectTransform.sizeDelta        = new Vector2(totalW, barHeight);
            _bar.rectTransform.anchoredPosition = new Vector2(0f, -pipSize * 0.5f - 2f);
        }
    }
}
