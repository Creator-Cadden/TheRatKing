using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Floating "+X XP from {enemy}" feed.
/// </summary>
public class XPGainIndicator : MonoBehaviour
{
    [Header("Feed Item")]
    [Tooltip("Optional — prefab used per line. If null, a plain TMP_Text is " +
             "created at runtime with the default font.")]
    public TMP_Text textPrefab;

    [Tooltip("Font size for the spawned text (only used when textPrefab is null).")]
    public float fontSize = 22f;

    [Tooltip("Color the text fades from.")]
    public Color startColor = new Color(1f, 0.95f, 0.3f, 1f);   // warm gold

    [Header("Animation")]
    [Tooltip("How long each line stays on screen before being destroyed.")]
    public float lifetime = 2.2f;

    [Tooltip("How far (pixels) each line floats up over its lifetime.")]
    public float floatDistance = 70f;

    [Header("Display")]
    [Tooltip("Format string. {0} = XP amount, {1} = source name. " +
             "If source is empty, '{2}' uses just the amount line.")]
    public string formatWithSource = "+{0} XP  from  {1}";
    public string formatNoSource   = "+{0} XP";

    [Header("Player")]
    public string playerTag = "Player";

    private XPSystem _xp;
    private RectTransform _rt;

    void Start()
    {
        _rt = GetComponent<RectTransform>();
        BindPlayer();
    }

    void OnEnable()
    {
        if (_xp == null) BindPlayer();
    }

    void OnDestroy()
    {
        if (_xp != null) _xp.onXPGainedFromSource.RemoveListener(OnGain);
    }

    private void BindPlayer()
    {
        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null) return;

        _xp = player.GetComponent<XPSystem>();
        if (_xp == null) return;

        _xp.onXPGainedFromSource.RemoveListener(OnGain);
        _xp.onXPGainedFromSource.AddListener(OnGain);
    }

    private void OnGain(int amount, string sourceName)
    {
        if (amount <= 0) return;

        TMP_Text line = CreateLine();
        if (line == null) return;

        line.text = string.IsNullOrEmpty(sourceName)
            ? string.Format(formatNoSource, amount)
            : string.Format(formatWithSource, amount, sourceName);
        line.color = startColor;

        StartCoroutine(AnimateLine(line));
    }

    private TMP_Text CreateLine()
    {
        if (textPrefab != null)
            return Instantiate(textPrefab, transform);

        // Build a minimal TMP_Text at runtime
        var go = new GameObject("XPLine");
        go.transform.SetParent(transform, worldPositionStays: false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0f, fontSize * 1.4f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize           = fontSize;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.color              = startColor;
        tmp.fontStyle          = FontStyles.Bold;
        tmp.enableAutoSizing   = false;
        tmp.raycastTarget      = false;
        return tmp;
    }

    private IEnumerator AnimateLine(TMP_Text line)
    {
        RectTransform rt = line.rectTransform;
        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos   = startPos + Vector2.up * floatDistance;
        Color   baseCol  = line.color;

        float elapsed = 0f;
        while (elapsed < lifetime && line != null)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / lifetime);

            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);

            Color c = baseCol;
            c.a = 1f - t;
            line.color = c;

            yield return null;
        }

        if (line != null) Destroy(line.gameObject);
    }
}
