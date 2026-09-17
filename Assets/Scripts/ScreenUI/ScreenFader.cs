using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A plain black curtain you can pull across the screen. The tutorial uses it to
/// hide the player being teleported back to the start before a new phase, but
/// nothing here is tutorial-specific — level transitions and respawns can use it
/// too.
///
/// SETUP (once, in the tutorial scene or on MainLevelPrefabs):
///   Canvas (Screen Space - Overlay, Sort Order 999)
///     └ Image  "FadeQuad"  — pure black, stretched to fill, Raycast Target OFF
///        + CanvasGroup     (alpha 0, Interactable OFF, Blocks Raycasts OFF)
///        + ScreenFader     (drag the CanvasGroup into 'group')
/// Leave the GameObject ACTIVE — the CanvasGroup alpha is what hides it.
/// </summary>
[DisallowMultipleComponent]
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [Header("Refs")]
    [Tooltip("CanvasGroup on the full-screen black image.")]
    public CanvasGroup group;

    [Header("Defaults")]
    public float defaultFadeOut = 0.25f;
    public float defaultHold    = 0.15f;
    public float defaultFadeIn  = 0.35f;

    [Tooltip("Block clicks while the screen is black. Off is usually right for " +
             "gameplay fades so the pause menu still works.")]
    public bool blockRaycastsWhileBlack = false;

    /// <summary>True while a fade (or the black hold) is running.</summary>
    public bool IsFading { get; private set; }

    private Coroutine _routine;

    void Awake()
    {
        Instance = this;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha          = 0f;
            group.interactable   = false;
            group.blocksRaycasts = false;
        }
        else Debug.LogWarning("[ScreenFader] No CanvasGroup assigned — fades will no-op.");
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>
    /// Fade to black, run <paramref name="whileBlack"/> (teleports, spawns,
    /// resets — anything the player shouldn't see), then fade back in.
    /// Safe to call with a null action.
    /// </summary>
    public void FadeThrough(Action whileBlack, float fadeOut = -1f, float hold = -1f, float fadeIn = -1f)
    {
        if (group == null) { whileBlack?.Invoke(); return; }

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeThroughRoutine(
            whileBlack,
            fadeOut < 0f ? defaultFadeOut : fadeOut,
            hold    < 0f ? defaultHold    : hold,
            fadeIn  < 0f ? defaultFadeIn  : fadeIn));
    }

    /// <summary>Fade to black and stay there until something calls FadeIn().</summary>
    public void FadeOut(float duration = -1f)
    {
        if (group == null) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeTo(1f, duration < 0f ? defaultFadeOut : duration));
    }

    /// <summary>Fade back from black.</summary>
    public void FadeIn(float duration = -1f)
    {
        if (group == null) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeTo(0f, duration < 0f ? defaultFadeIn : duration));
    }

    /// <summary>Snap to fully black or fully clear with no animation.</summary>
    public void SetBlack(bool black)
    {
        if (group == null) return;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        group.alpha = black ? 1f : 0f;
        ApplyRaycastBlock(black);
        IsFading = false;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private IEnumerator FadeThroughRoutine(Action whileBlack, float fadeOut, float hold, float fadeIn)
    {
        IsFading = true;
        yield return FadeTo(1f, fadeOut, keepFlag: true);

        // Do the invisible work on the first fully-black frame.
        try { whileBlack?.Invoke(); }
        catch (Exception e) { Debug.LogError("[ScreenFader] whileBlack threw: " + e); }

        if (hold > 0f) yield return new WaitForSeconds(hold);

        yield return FadeTo(0f, fadeIn, keepFlag: true);
        IsFading = false;
        _routine = null;
    }

    private IEnumerator FadeTo(float target, float duration, bool keepFlag = false)
    {
        IsFading = true;
        ApplyRaycastBlock(target > 0.01f);

        float start = group.alpha;
        if (duration <= 0f)
        {
            group.alpha = target;
        }
        else
        {
            float t = 0f;
            while (t < duration)
            {
                // unscaledDeltaTime so a paused/hitstopped game still fades.
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
                yield return null;
            }
            group.alpha = target;
        }

        ApplyRaycastBlock(target > 0.01f);
        if (!keepFlag) { IsFading = false; _routine = null; }
    }

    private void ApplyRaycastBlock(bool black)
    {
        if (group == null) return;
        group.blocksRaycasts = blockRaycastsWhileBlack && black;
    }
}
