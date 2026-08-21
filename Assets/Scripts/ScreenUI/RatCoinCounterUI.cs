using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Top-right Rat Coin counter. Listens to the player's CurrencySystem and, when
/// coins come in, drives a slot-machine reel roll-up (SlotNumberDisplay) plus a
/// pop + gold flash, then FADES AWAY when you're not earning — reappearing the
/// next time coins land. Spending also rolls the reels (no pop).
///
/// (The currency component is still called CurrencySystem in code; "Rat Coin" is
/// its in-game name, which is what this HUD shows.)
///
/// Wiring: put on a top-right HUD object (anchored top-right).
///   • slot       — a SlotNumberDisplay for the rolling reels (preferred).
///   • amountText  — a plain TMP fallback, used only if slot is empty.
///   • icon        — optional coin icon RectTransform; bumps on a gain.
///   • canvasGroup — auto-added if left empty; used for the fade.
/// Auto-binds to the Player (by tag).
/// </summary>
[DisallowMultipleComponent]
public class RatCoinCounterUI : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Preferred: reel display that physically rolls the digits.")]
    public SlotNumberDisplay slot;
    [Tooltip("Fallback plain number, used only when 'slot' is empty.")]
    public TMP_Text amountText;
    [Tooltip("Optional coin icon that bumps/spins when coins arrive.")]
    public RectTransform icon;
    [Tooltip("Fades the whole counter. Auto-added if left empty.")]
    public CanvasGroup canvasGroup;

    [Header("Player")]
    public string playerTag = "Player";

    [Header("Fallback text format")]
    public bool useThousandsSeparator = true;

    [Header("Fade (hide when idle)")]
    [Tooltip("Fade the counter out when no coins have come in recently.")]
    public bool  autoFade      = true;
    [Tooltip("Stay visible this long after the last gain / until the reels settle.")]
    public float holdAfterGain = 2.5f;
    public float fadeInSpeed   = 12f;
    public float fadeOutSpeed  = 2.5f;

    [Header("Juice")]
    [Tooltip("Scale the counter punches to on a gain, then settles back to 1.")]
    public float popScale = 1.16f;
    public float popTime  = 0.18f;
    [Tooltip("Fallback text colours (only used with amountText, not the reels).")]
    public Color normalColor = new Color(1f, 0.95f, 0.82f, 1f);
    public Color gainColor   = new Color(1f, 0.85f, 0.25f, 1f);

    // ── runtime ──
    private CurrencySystem _wallet;
    private int   _target;
    private float _shownFallback;      // used only in the no-slot path
    private float _alpha;
    private float _lastGainTime = -999f;
    private Coroutine _pop;
    private Transform _popTarget;

    void Start()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _popTarget = slot != null ? slot.transform
                   : amountText != null ? amountText.transform
                   : transform;

        BindPlayer();
        if (_wallet != null) { _target = _wallet.CurrentCurrency; _shownFallback = _target; }

        if (slot != null) slot.SetValueImmediate(_target);
        else RenderFallback(_target);

        _alpha = autoFade ? 0f : 1f;
        canvasGroup.alpha = _alpha;
    }

    void OnEnable() { if (_wallet == null) BindPlayer(); }

    void OnDestroy()
    {
        if (_wallet != null) _wallet.onCurrencyChanged.RemoveListener(OnChanged);
    }

    private void BindPlayer()
    {
        GameObject p = GameObject.FindWithTag(playerTag);
        if (p == null) return;
        _wallet = p.GetComponent<CurrencySystem>();
        if (_wallet == null) return;

        _wallet.onCurrencyChanged.RemoveListener(OnChanged);
        _wallet.onCurrencyChanged.AddListener(OnChanged);
        _target = _wallet.CurrentCurrency;
        _shownFallback = _target;
    }

    private void OnChanged(int newTotal)
    {
        bool gained = newTotal > _target;
        _target = newTotal;

        if (slot != null) slot.SetValue(newTotal);   // reels animate themselves

        if (gained)
        {
            _lastGainTime = Time.unscaledTime;
            Punch();
        }
        else
        {
            // Spending still wakes the counter briefly so the roll-down is seen.
            _lastGainTime = Time.unscaledTime;
        }
    }

    void Update()
    {
        if (_wallet == null) { BindPlayer(); if (_wallet == null) return; }

        // Fallback (no reels): roll the plain number toward the target.
        if (slot == null && Mathf.RoundToInt(_shownFallback) != _target)
        {
            float gap    = Mathf.Abs(_target - _shownFallback);
            float perSec = Mathf.Max(20f, gap / 0.6f);
            _shownFallback = Mathf.MoveTowards(_shownFallback, _target, perSec * Time.unscaledDeltaTime);
            RenderFallback(Mathf.RoundToInt(_shownFallback));
        }

        // Fade: visible while the reels spin (or number rolls) or within the hold.
        bool busy =
            (slot != null && slot.IsSpinning) ||
            (slot == null && Mathf.RoundToInt(_shownFallback) != _target) ||
            Time.unscaledTime < _lastGainTime + holdAfterGain;

        float targetAlpha = !autoFade ? 1f : (busy ? 1f : 0f);
        float sp = targetAlpha > _alpha ? fadeInSpeed : fadeOutSpeed;
        _alpha = Mathf.MoveTowards(_alpha, targetAlpha, sp * Time.unscaledDeltaTime);
        if (canvasGroup != null) canvasGroup.alpha = _alpha;
    }

    private void RenderFallback(int value)
    {
        if (amountText == null) return;
        amountText.text = useThousandsSeparator ? value.ToString("N0") : value.ToString();
    }

    private void Punch()
    {
        if (_pop != null) StopCoroutine(_pop);
        _pop = StartCoroutine(PopRoutine());
    }

    private IEnumerator PopRoutine()
    {
        Transform t = _popTarget != null ? _popTarget : transform;
        float half = Mathf.Max(0.0001f, popTime * 0.5f);

        float e = 0f;
        while (e < popTime)
        {
            e += Time.unscaledDeltaTime;
            float p = e / popTime;
            float s = e < half
                ? Mathf.Lerp(1f, popScale, e / half)
                : Mathf.Lerp(popScale, 1f, (e - half) / half);
            t.localScale = Vector3.one * s;

            if (slot == null && amountText != null)
                amountText.color = Color.Lerp(gainColor, normalColor, p);
            if (icon != null)
                icon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(p * Mathf.PI) * 16f);

            yield return null;
        }

        t.localScale = Vector3.one;
        if (slot == null && amountText != null) amountText.color = normalColor;
        if (icon != null) icon.localRotation = Quaternion.identity;
    }
}
