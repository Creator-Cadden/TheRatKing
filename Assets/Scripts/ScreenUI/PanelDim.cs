using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// A full-screen dim layer that fades IN when its GameObject is enabled — the
/// "focus the panel" backdrop behind the pause and stats menus.
///
/// Setup: add a UI ▸ Image, stretch it to fill the Canvas, and make it the
/// FIRST (backmost) child INSIDE a panel root (so it turns on/off with the
/// panel). Put this script on it. No other wiring — it reads its own Image.
/// Because it lives inside the panel root, it appears/disappears with the panel;
/// the fade-IN plays on open. (Fade-out isn't shown because the root is
/// deactivated instantly on close — fine for a dim; the real URP blur upgrade
/// later can live on this same object.)
///
/// Uses unscaled time so it animates even though the game is frozen.
/// </summary>
[RequireComponent(typeof(Image))]
public class PanelDim : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("How dark the backdrop gets. 0.6 = 60% black.")]
    [Range(0f, 1f)] public float targetAlpha = 0.6f;
    public Color color = Color.black;

    [Header("Timing")]
    public float fadeInDuration = 0.15f;

    [Header("Input")]
    [Tooltip("Block clicks from reaching the game world behind the panel.")]
    public bool blockRaycasts = true;

    private Image _img;
    private Coroutine _fade;

    void Awake()
    {
        _img = GetComponent<Image>();
        _img.raycastTarget = blockRaycasts;
        SetAlpha(0f);
    }

    void OnEnable()
    {
        if (_img == null) _img = GetComponent<Image>();
        SetAlpha(0f);
        if (gameObject.activeInHierarchy)
        {
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeIn());
        }
    }

    void OnDisable()
    {
        // Reset so the next open starts from transparent.
        if (_fade != null) { StopCoroutine(_fade); _fade = null; }
        SetAlpha(0f);
    }

    private IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(0f, targetAlpha, fadeInDuration > 0f ? t / fadeInDuration : 1f));
            yield return null;
        }
        SetAlpha(targetAlpha);
    }

    private void SetAlpha(float a)
    {
        if (_img == null) return;
        Color c = color; c.a = a;
        _img.color = c;
    }
}
