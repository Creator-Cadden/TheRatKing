using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Auto-created full-screen overlay for menu juice: a quick colour FLASH and
/// screen-lerp FADE / TRANSITION effects. No setup needed — the first call
/// spawns its own top-most canvas. Scene-local (destroyed on scene load) so a
/// fade-to-black never leaks into the next scene.
///
///   MenuFX.FadeIn();                       // fade the screen in from black
///   MenuFX.Flash(Color.white);             // quick flash (used on click)
///   MenuFX.Transition(() => SwapPanels());  // fade out, swap, fade back in
///   MenuFX.FadeOutThen(() => LoadScene());  // fade to black, then act
/// </summary>
[DefaultExecutionOrder(20000)]
public class MenuFX : MonoBehaviour
{
    private static MenuFX _instance;
    private static MenuFX Instance
    {
        get { if (_instance == null) Create(); return _instance; }
    }

    private Image      _overlay;   // black — fades
    private Image      _flash;     // white — quick flashes
    private Image      _wipe;      // black — slides across for transitions
    private Coroutine  _fadeRoutine, _flashRoutine;

    private static void Create()
    {
        var go = new GameObject("MenuFX");
        _instance = go.AddComponent<MenuFX>();
        _instance.Build();
    }

    void OnDestroy() { if (_instance == this) _instance = null; }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // above all menu UI

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _overlay = MakeFullScreenImage("Overlay", Color.black);
        _flash   = MakeFullScreenImage("Flash",   Color.white);

        _wipe = MakeFullScreenImage("Wipe", Color.black);
        _wipe.color = Color.black;                                  // opaque black band
        _wipe.rectTransform.anchoredPosition = new Vector2(10000f, 0f);  // parked off-screen
    }

    private Image MakeFullScreenImage(string n, Color c)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(c.r, c.g, c.b, 0f);
        img.raycastTarget = false;      // never eats menu clicks
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    private static void SetAlpha(Image img, Color rgb, float a)
        => img.color = new Color(rgb.r, rgb.g, rgb.b, a);

    // ── Public API ───────────────────────────────────────────────

    public static void Flash(Color color, float duration = 0.22f, float peak = 0.5f)
    {
        var i = Instance;
        if (i._flashRoutine != null) i.StopCoroutine(i._flashRoutine);
        i._flashRoutine = i.StartCoroutine(i.FlashRoutine(color, duration, peak));
    }

    public static void FadeIn(float duration = 0.4f)
    {
        var i = Instance;
        if (i._fadeRoutine != null) i.StopCoroutine(i._fadeRoutine);
        i._fadeRoutine = i.StartCoroutine(i.FadeRoutine(1f, 0f, duration, null));
    }

    public static void Transition(Action midpoint, float halfDuration = 0.18f)
    {
        var i = Instance;
        if (i._fadeRoutine != null) i.StopCoroutine(i._fadeRoutine);
        i._fadeRoutine = i.StartCoroutine(i.TransitionRoutine(midpoint, halfDuration, Color.black));
    }

    public static void FadeOutThen(Action onComplete, float duration = 0.4f)
    {
        var i = Instance;
        if (i._fadeRoutine != null) i.StopCoroutine(i._fadeRoutine);
        i._fadeRoutine = i.StartCoroutine(i.FadeRoutine(0f, 1f, duration, onComplete));
    }

    /// <summary>Black band sweeps in from the left to cover the screen, swaps the
    /// panel while hidden, then sweeps off to the right.</summary>
    public static void Wipe(Action midpoint, float duration = 0.55f)
    {
        var i = Instance;
        if (i._fadeRoutine != null) i.StopCoroutine(i._fadeRoutine);
        i._fadeRoutine = i.StartCoroutine(i.WipeRoutine(midpoint, duration));
    }

    /// <summary>Opening title sequence: hold black while a "presents" line fades in
    /// and out, then call onReveal (menu activates + title animates) as the screen
    /// fades up from black.</summary>
    public static void PlayIntro(string presentsLine, float presentsHold, Action onReveal)
    {
        var i = Instance;
        if (i._fadeRoutine != null) i.StopCoroutine(i._fadeRoutine);
        i._fadeRoutine = i.StartCoroutine(i.IntroRoutine(presentsLine, presentsHold, onReveal));
    }

    // ── Routines (unscaled time so menus at timeScale 0 still animate) ──

    private IEnumerator FlashRoutine(Color color, float duration, float peak)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(_flash, color, Mathf.Lerp(peak, 0f, t / duration));
            yield return null;
        }
        SetAlpha(_flash, color, 0f);
    }

    private IEnumerator FadeRoutine(float from, float to, float duration, Action onComplete)
    {
        float t = 0f;
        SetAlpha(_overlay, Color.black, from);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(_overlay, Color.black, Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetAlpha(_overlay, Color.black, to);
        onComplete?.Invoke();
    }

    private IEnumerator TransitionRoutine(Action midpoint, float half, Color color)
    {
        float t = 0f;
        while (t < half)                       // cover the screen
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(_overlay, color, Mathf.Lerp(0f, 1f, t / half));
            yield return null;
        }
        SetAlpha(_overlay, color, 1f);

        midpoint?.Invoke();                    // swap while hidden

        t = 0f;
        while (t < half)                       // reveal the new screen
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(_overlay, color, Mathf.Lerp(1f, 0f, t / half));
            yield return null;
        }
        SetAlpha(_overlay, color, 0f);
    }

    private IEnumerator WipeRoutine(Action midpoint, float duration)
    {
        float w = ((RectTransform)transform).rect.width;
        if (w < 1f) w = Screen.width;
        var rt = _wipe.rectTransform;

        float half = duration * 0.5f;

        // Phase 1: slide in from off-left (-w) until it fully covers (0).
        float t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float p = EaseInOut(Mathf.Clamp01(t / half));
            rt.anchoredPosition = new Vector2(Mathf.Lerp(-w, 0f, p), 0f);
            yield return null;
        }
        rt.anchoredPosition = Vector2.zero;    // fully covered

        midpoint?.Invoke();                    // swap panels while hidden
        yield return null;                     // hold one covered frame

        // Phase 2: keep sweeping right until it exits off-screen (+w).
        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float p = EaseInOut(Mathf.Clamp01(t / half));
            rt.anchoredPosition = new Vector2(Mathf.Lerp(0f, w, p), 0f);
            yield return null;
        }
        rt.anchoredPosition = new Vector2(10000f, 0f);   // park off-screen
    }

    private static float EaseInOut(float x)
        => x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;

    private IEnumerator IntroRoutine(string line, float hold, Action onReveal)
    {
        SetAlpha(_overlay, Color.black, 1f);   // cover the screen immediately

        // Build the "presents" line on this (top-most) canvas so it sits above black.
        var go = new GameObject("PresentsLine");
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = line;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize  = 46f;
        tmp.color     = new Color(1f, 1f, 1f, 0f);
        var trt = tmp.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(1400f, 200f);
        trt.anchoredPosition = Vector2.zero;

        yield return FadeText(tmp, 0f, 1f, 0.7f);          // fade in
        float t = 0f;
        while (t < hold) { t += Time.unscaledDeltaTime; yield return null; }   // hold
        yield return FadeText(tmp, 1f, 0f, 0.5f);          // fade out
        Destroy(go);

        yield return null;
        onReveal?.Invoke();                                 // menu appears + title animates

        // Fade the black away to reveal it all.
        t = 0f; const float reveal = 0.6f;
        while (t < reveal)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(_overlay, Color.black, Mathf.Lerp(1f, 0f, t / reveal));
            yield return null;
        }
        SetAlpha(_overlay, Color.black, 0f);
    }

    private IEnumerator FadeText(TMP_Text tmp, float from, float to, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            var c = tmp.color; c.a = Mathf.Lerp(from, to, t / dur); tmp.color = c;
            yield return null;
        }
        var cc = tmp.color; cc.a = to; tmp.color = cc;
    }
}
