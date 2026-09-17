using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// The centre explanation panel. Owns one job: show a block of tutorial text
/// so it can actually be read, and get out of the way on its own.
///
/// Three things make it readable where a bare TMP_Text wasn't:
///
///   1. A solid panel behind the words, so text never sits on top of a lit
///      floor or a moving rat.
///   2. Long text is PAGED instead of dumped. A paragraph break starts a new
///      page, and a paragraph longer than Words Per Page is split again on
///      sentence boundaries — so a wall of text becomes three short beats
///      whatever the author typed.
///   3. Pages advance THEMSELVES after a read time worked out from their word
///      count. The continue key skips ahead early; it is never required to
///      make progress. That's what takes the flow from "press E, press E,
///      press E" down to zero presses for a player happy to read.
///
/// Everything here runs on unscaled time, because the tutorial freezes the
/// game for its freeze-and-show beats and a paused clock would leave the text
/// on screen forever.
/// </summary>
public class TutorialPromptUI : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Fades the whole thing. Auto-found on this object if empty.")]
    public CanvasGroup   group;
    [Tooltip("The panel that slides. Auto-found on this object if empty.")]
    public RectTransform panel;
    [Tooltip("The dark plate behind the words. Built in code if empty.")]
    public Image         panelBackground;
    public TMP_Text      body;
    [Tooltip("\"[E] next\" / \"[E] continue\".")]
    public TMP_Text      continueHint;
    [Tooltip("\"2 / 3\" so the player knows more is coming. Optional.")]
    public TMP_Text      pageLabel;
    [Tooltip("Thin bar draining toward the automatic page turn. Optional — its " +
             "width is driven by the RectTransform, so it needs no sprite.")]
    public Image         pageTimerFill;

    [Header("Look")]
    public Color backgroundColor = new Color(0.04f, 0.04f, 0.05f, 0.88f);
    [Tooltip("How fast the panel fades. Higher = snappier.")]
    public float fadeSpeed    = 14f;
    [Tooltip("The panel rises this many pixels as it fades in.")]
    public float riseDistance = 20f;

    [Header("Reading pace")]
    [Tooltip("Seconds of read time per word. 0.34 is a relaxed ~175 words a " +
             "minute — slow enough for a player who is also watching a rat.")]
    public float secondsPerWord  = 0.34f;
    [Tooltip("Added to every page, so a three-word line still lands.")]
    public float baseSeconds     = 1.1f;
    public float minPageSeconds  = 2.2f;
    [Tooltip("Ceiling on one page. If a page wants longer than this, it should " +
             "have been two pages — which is what Words Per Page is for.")]
    public float maxPageSeconds  = 10f;
    [Tooltip("Split a paragraph longer than this into sentence-sized pages.")]
    public int   wordsPerPage    = 34;
    [Tooltip("Off makes every page wait for the key. Leave ON.")]
    public bool  autoAdvancePages = true;

    [Header("Keys")]
    public Key continueKey = Key.E;

    /// <summary>True while the panel is up (including its fade out).</summary>
    public bool IsShowing { get; private set; }

    /// <summary>
    /// True once the last page has been consumed. The tutorial polls this
    /// instead of running its own clock, so paging and step completion can
    /// never disagree about whether the text has been seen.
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <summary>Pages left to read, including the current one.</summary>
    public int PagesRemaining => Mathf.Max(0, _pages.Count - _pageIndex);

    private readonly List<string> _pages = new List<string>();
    private int   _pageIndex;
    private float _pageClock;
    private float _pageDuration;
    private bool  _holdOnLastPage;
    private bool  _awaitingKey;
    private float _alphaTarget;
    private Vector2 _restPos;
    private bool  _restCaptured;

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        if (panel == null) panel = GetComponent<RectTransform>();

        CaptureRest();
        BuildBackgroundIfMissing();

        group.alpha          = 0f;
        group.blocksRaycasts = false;
        group.interactable   = false;
        _alphaTarget         = 0f;
    }

    private void CaptureRest()
    {
        if (_restCaptured || panel == null) return;
        _restPos      = panel.anchoredPosition;
        _restCaptured = true;
    }

    /// <summary>
    /// Gives the panel a plate to sit on if the scene didn't. A spriteless
    /// Image still renders a solid rect, so this needs no art.
    /// </summary>
    private void BuildBackgroundIfMissing()
    {
        if (panelBackground == null && panel != null)
        {
            Transform found = panel.Find("PromptBackground");
            GameObject go = found != null
                ? found.gameObject
                : new GameObject("PromptBackground", typeof(RectTransform));

            if (found == null)
            {
                go.transform.SetParent(panel, false);
                go.transform.SetAsFirstSibling();   // behind the text
            }

            RectTransform r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;

            panelBackground = go.GetComponent<Image>();
            if (panelBackground == null) panelBackground = go.AddComponent<Image>();
        }

        if (panelBackground != null)
        {
            panelBackground.color         = backgroundColor;
            panelBackground.type          = Image.Type.Simple;
            panelBackground.raycastTarget = false;
        }
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Put text up. Splits it into pages and starts reading.
    /// </summary>
    /// <param name="text">The whole explanation. Blank lines start new pages.</param>
    /// <param name="holdOnLastPage">
    /// True makes the LAST page wait for the continue key instead of closing on
    /// its own. Reserve it for beats the player must acknowledge — a
    /// freeze-and-show — so the key keeps meaning something.
    /// </param>
    public void Show(string text, bool holdOnLastPage)
    {
        CaptureRest();
        BuildBackgroundIfMissing();

        Paginate(text);

        _pageIndex      = 0;
        _holdOnLastPage = holdOnLastPage;
        _awaitingKey    = false;
        IsFinished      = _pages.Count == 0;
        IsShowing       = _pages.Count > 0;
        _alphaTarget    = IsShowing ? 1f : 0f;

        if (!IsShowing) { Hide(); return; }

        // Start below the rest position so the fade in has a little lift.
        if (panel != null) panel.anchoredPosition = _restPos - new Vector2(0f, riseDistance);

        ApplyPage();
    }

    /// <summary>Take it down now, whatever page it was on.</summary>
    public void Hide()
    {
        IsShowing    = false;
        _awaitingKey = false;
        _alphaTarget = 0f;
        _pages.Clear();
        _pageIndex = 0;
        if (continueHint != null) continueHint.gameObject.SetActive(false);
        if (pageLabel    != null) pageLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// Jump straight past everything still queued — used when a phase is
    /// skipped, so the panel doesn't keep narrating a section that's over.
    /// </summary>
    public void FinishNow()
    {
        IsFinished = true;
        Hide();
    }

    // ── Paging ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Text → pages. A blank line is an explicit break; anything still longer
    /// than Words Per Page is cut again at sentence ends so no page is a wall.
    /// </summary>
    private void Paginate(string text)
    {
        _pages.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;

        string normalised = text.Replace("\r\n", "\n").Replace("\r", "\n");

        foreach (string chunk in normalised.Split(new[] { "\n\n" }, System.StringSplitOptions.None))
        {
            string paragraph = chunk.Trim();
            if (paragraph.Length == 0) continue;

            if (CountWords(paragraph) <= wordsPerPage) { _pages.Add(paragraph); continue; }

            // Too long — rebuild it a sentence at a time, starting a new page
            // whenever the budget is spent. Splitting mid-sentence would read
            // far worse than a slightly over-budget page, so the check happens
            // BEFORE appending, never inside a sentence.
            var current = new System.Text.StringBuilder();
            int  words  = 0;

            foreach (string sentence in SplitSentences(paragraph))
            {
                int w = CountWords(sentence);
                if (words > 0 && words + w > wordsPerPage)
                {
                    _pages.Add(current.ToString().Trim());
                    current.Length = 0;
                    words = 0;
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(sentence);
                words += w;
            }

            if (current.Length > 0) _pages.Add(current.ToString().Trim());
        }
    }

    /// <summary>
    /// Sentence pieces, each keeping its own terminator. Deliberately simple:
    /// it breaks after . ! ? followed by whitespace. "Mr. Rat" would split
    /// early, which costs a page break and nothing else.
    /// </summary>
    private static List<string> SplitSentences(string paragraph)
    {
        var  result = new List<string>();
        int  start  = 0;

        for (int i = 0; i < paragraph.Length; i++)
        {
            char c = paragraph[i];
            bool terminator = c == '.' || c == '!' || c == '?';
            if (!terminator) continue;

            // Run past "?!" or "..." so they stay with their sentence.
            int end = i;
            while (end + 1 < paragraph.Length &&
                   (paragraph[end + 1] == '.' || paragraph[end + 1] == '!' || paragraph[end + 1] == '?'))
                end++;

            bool atEnd = end + 1 >= paragraph.Length;
            if (!atEnd && !char.IsWhiteSpace(paragraph[end + 1])) { i = end; continue; }

            string piece = paragraph.Substring(start, end - start + 1).Trim();
            if (piece.Length > 0) result.Add(piece);
            start = end + 1;
            i     = end;
        }

        string tail = start < paragraph.Length ? paragraph.Substring(start).Trim() : "";
        if (tail.Length > 0) result.Add(tail);
        if (result.Count == 0) result.Add(paragraph);
        return result;
    }

    private static int CountWords(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        int n = 0;
        bool inWord = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)) { inWord = false; continue; }
            if (!inWord) { n++; inWord = true; }
        }
        return n;
    }

    /// <summary>Draw the current page and work out how long it gets.</summary>
    private void ApplyPage()
    {
        if (_pageIndex < 0 || _pageIndex >= _pages.Count) return;

        string page = _pages[_pageIndex];
        if (body != null) body.text = page;

        _pageClock    = 0f;
        _pageDuration = Mathf.Clamp(baseSeconds + CountWords(page) * secondsPerWord,
                                    minPageSeconds, maxPageSeconds);

        bool lastPage = _pageIndex >= _pages.Count - 1;

        if (pageLabel != null)
        {
            bool show = _pages.Count > 1;
            pageLabel.gameObject.SetActive(show);
            if (show) pageLabel.text = $"{_pageIndex + 1} / {_pages.Count}";
        }

        if (continueHint != null)
        {
            continueHint.gameObject.SetActive(true);
            continueHint.text = lastPage
                ? $"[{KeyName()}] continue"
                : $"[{KeyName()}] next";
        }
    }

    private string KeyName() => continueKey.ToString().ToUpperInvariant();

    /// <summary>Move to the next page, or finish if that was the last.</summary>
    private void Advance()
    {
        if (_pageIndex < _pages.Count - 1) { _pageIndex++; ApplyPage(); return; }

        if (_holdOnLastPage && !_awaitingKey)
        {
            // Last page of a beat the player must acknowledge: stop the clock
            // and wait. This is the one place the key is load-bearing.
            _awaitingKey = true;
            if (continueHint != null)
            {
                continueHint.gameObject.SetActive(true);
                continueHint.text = $"Press [{KeyName()}] to continue";
            }
            return;
        }

        IsFinished = true;
        Hide();
    }

    // ── Tick ──────────────────────────────────────────────────────────────────

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Fade and lift. Runs even when not showing, so the panel eases out.
        if (group != null)
        {
            float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, fadeSpeed) * dt);
            group.alpha = Mathf.Lerp(group.alpha, _alphaTarget, k);
            if (_alphaTarget <= 0f && group.alpha < 0.002f) group.alpha = 0f;

            if (panel != null && _restCaptured)
            {
                Vector2 want = _alphaTarget > 0f ? _restPos
                                                 : _restPos - new Vector2(0f, riseDistance);
                panel.anchoredPosition = Vector2.Lerp(panel.anchoredPosition, want, k);
            }
        }

        if (!IsShowing || _pages.Count == 0) return;

        Keyboard kb      = Keyboard.current;
        bool     pressed = kb != null && kb[continueKey].wasPressedThisFrame;

        if (_awaitingKey)
        {
            if (pageTimerFill != null) SetTimerFill(0f);
            if (pressed) { IsFinished = true; Hide(); }
            return;
        }

        _pageClock += dt;

        if (pageTimerFill != null)
            SetTimerFill(1f - Mathf.Clamp01(_pageClock / Mathf.Max(0.01f, _pageDuration)));

        if (pressed) { Advance(); return; }
        if (autoAdvancePages && _pageClock >= _pageDuration) Advance();
    }

    /// <summary>
    /// Width via the anchor, not Image.fillAmount — fillAmount is ignored on an
    /// Image with no sprite, which is the same trap the objective card bar fell
    /// into. This way the bar needs no art at all.
    /// </summary>
    private void SetTimerFill(float t)
    {
        RectTransform r = pageTimerFill.rectTransform;
        r.anchorMin        = new Vector2(0f, 0f);
        r.anchorMax        = new Vector2(Mathf.Clamp01(t), 1f);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta        = Vector2.zero;
    }
}
