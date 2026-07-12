using UnityEngine;
using System.Collections;
using TMPro;

/// <summary>
/// Level-up feedback driven by FOUR TMP_Text references only.
/// No panel root GameObjects, no CanvasGroups. Each text fades in/out
/// independently by writing its color.alpha.
/// </summary>
public class LevelUpIndicator : MonoBehaviour
{
    [Header("Burst — 'LEVEL UP!'")]
    [Tooltip("Big celebratory text. Plays for a moment, then fades out.")]
    public TMP_Text burstMainText;

    [Tooltip("Subtitle under the big text.")]
    public TMP_Text burstSubText;

    [Header("Reminder — 'Unspent points'")]
    [Tooltip("Shows the number of unspent points. Sticks around until they're all spent.")]
    public TMP_Text reminderMainText;

    [Tooltip("Subtitle under the reminder, e.g. 'Press TAB to spend'.")]
    public TMP_Text reminderSubText;

    [Header("Burst Content")]
    public string burstMainMessage = "LEVEL UP!";
    public string burstSubMessage  = "Press TAB to spend stat points";

    [Header("Reminder Content")]
    [Tooltip("{0} = number of unspent points.")]
    public string reminderMainFormat = "Unspent points: {0}";
    public string reminderSubMessage = "Press TAB to spend";

    [Header("Timing")]
    [Tooltip("How long the BURST stays at full opacity before fading out.")]
    public float burstHoldDuration = 2.5f;

    [Tooltip("Fade-in time for both pairs.")]
    public float fadeInDuration = 0.25f;

    [Tooltip("Fade-out time for both pairs.")]
    public float fadeOutDuration = 0.4f;

    [Header("Player")]
    public string playerTag = "Player";

    // ── Private ──
    private XPSystem  _xp;
    private Coroutine _burstRoutine;
    private Coroutine _reminderRoutine;


    void Awake()
    {
        SetPairAlpha(burstMainText,    burstSubText,    0f);
        SetPairAlpha(reminderMainText, reminderSubText, 0f);
    }

    void Start()
    {
        BindPlayer();
    }

    void OnEnable()
    {
        if (_xp == null) BindPlayer();
    }

    void OnDestroy()
    {
        if (_xp != null)
        {
            _xp.onLevelUp.RemoveListener(OnLevelUp);
            _xp.onStatPointSpent.RemoveListener(OnPointSpent);
        }
    }


    private void BindPlayer()
    {
        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null) return;

        _xp = player.GetComponent<XPSystem>();
        if (_xp == null) return;

        _xp.onLevelUp.RemoveListener(OnLevelUp);
        _xp.onLevelUp.AddListener(OnLevelUp);

        _xp.onStatPointSpent.RemoveListener(OnPointSpent);
        _xp.onStatPointSpent.AddListener(OnPointSpent);

        // If a save loaded with points still unspent, show the reminder.
        if (_xp.UnspentPoints > 0) ShowReminder();
    }

    // ── Burst — "LEVEL UP!" ──

    private void OnLevelUp()
    {
        if (_burstRoutine != null) StopCoroutine(_burstRoutine);
        _burstRoutine = StartCoroutine(BurstSequence());
    }

    private IEnumerator BurstSequence()
    {
        if (burstMainText != null) burstMainText.text = burstMainMessage;
        if (burstSubText  != null) burstSubText.text  = burstSubMessage;

        // Fade in
        yield return FadePair(burstMainText, burstSubText, 0f, 1f, fadeInDuration);

        // Hold
        yield return new WaitForSecondsRealtime(burstHoldDuration);

        // Fade out
        yield return FadePair(burstMainText, burstSubText, 1f, 0f, fadeOutDuration);

        // After the burst, show the persistent reminder if points are unspent.
        if (_xp != null && _xp.UnspentPoints > 0)
            ShowReminder();
    }

    // ── Reminder — "Unspent points: N" ──

    private void OnPointSpent(int remaining)
    {
        if (remaining <= 0)
            HideReminder();
        else
            ShowReminder();
    }

    private void ShowReminder()
    {
        if (reminderMainText != null)
        {
            reminderMainText.text = _xp != null
                ? string.Format(reminderMainFormat, _xp.UnspentPoints)
                : reminderMainFormat;
        }
        if (reminderSubText != null)
            reminderSubText.text = reminderSubMessage;

        // If already at full alpha, just update the text — no flicker.
        if (PairAlpha(reminderMainText, reminderSubText) >= 0.99f) return;

        if (_reminderRoutine != null) StopCoroutine(_reminderRoutine);
        _reminderRoutine = StartCoroutine(
            FadePair(reminderMainText, reminderSubText,
                     PairAlpha(reminderMainText, reminderSubText), 1f, fadeInDuration));
    }

    private void HideReminder()
    {
        if (_reminderRoutine != null) StopCoroutine(_reminderRoutine);
        _reminderRoutine = StartCoroutine(
            FadePair(reminderMainText, reminderSubText,
                     PairAlpha(reminderMainText, reminderSubText), 0f, fadeOutDuration));
    }

    // ── Alpha helpers (operate on two TMP_Texts in lockstep) ──

    private static IEnumerator FadePair(TMP_Text a, TMP_Text b, float from, float to, float seconds)
    {
        if (seconds <= 0f)
        {
            SetPairAlpha(a, b, to);
            yield break;
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(from, to, t / seconds);
            SetPairAlpha(a, b, alpha);
            yield return null;
        }
        SetPairAlpha(a, b, to);
    }

    private static void SetPairAlpha(TMP_Text a, TMP_Text b, float alpha)
    {
        if (a != null) { var c = a.color; c.a = alpha; a.color = c; }
        if (b != null) { var c = b.color; c.a = alpha; b.color = c; }
    }

    /// <summary>
    /// Returns the higher of the two texts' current alpha (or 0 if both null).
    /// </summary>
    private static float PairAlpha(TMP_Text a, TMP_Text b)
    {
        float aa = a != null ? a.color.a : 0f;
        float bb = b != null ? b.color.a : 0f;
        return Mathf.Max(aa, bb);
    }
}
