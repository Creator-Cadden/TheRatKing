using UnityEngine;
using TMPro;

/// <summary>
/// The single source of truth for every colour, font and 9-slice sprite in the
/// game's UI. Create ONE of these (Assets/Resources/UITheme.asset) via
/// Create ▸ Rat King ▸ UI Theme, fill it in, and every UI script can read it
/// through <see cref="Active"/> without an inspector reference.
///
/// Why: retheming the whole HUD becomes editing one asset instead of hunting
/// down 40 colour fields across 25 prefabs.
/// </summary>
[CreateAssetMenu(fileName = "UITheme", menuName = "Rat King/UI Theme", order = 0)]
public class UITheme : ScriptableObject
{
    // ── Static access ─────────────────────────────────────────────────────────

    private static UITheme _active;

    /// <summary>
    /// The theme in use. Loads Resources/UITheme.asset the first time it's asked
    /// for. Returns null (with a warning) if that asset doesn't exist yet — every
    /// caller must null-check, so the game still runs before you make the asset.
    /// </summary>
    public static UITheme Active
    {
        get
        {
            if (_active == null)
            {
                _active = Resources.Load<UITheme>("UITheme");
                if (_active == null)
                    Debug.LogWarning("[UITheme] No Resources/UITheme.asset found — " +
                                     "UI scripts will fall back to their inspector colours.");
            }
            return _active;
        }
    }

    /// <summary>Force a theme in at runtime (used by tests / theme switching).</summary>
    public static void SetActive(UITheme theme) => _active = theme;

    // ── Core palette ──────────────────────────────────────────────────────────
    // Values match the Rat King HUD style guide: flat fills, thick warm ink
    // outline, muted ochre gold. Sewer browns are the "paper" the UI sits on.

    [Header("Surfaces — sewer browns")]
    [Tooltip("Darkest brown. Panel backing / bar troughs.")]
    public Color surfaceDeep = Hex("#201910");
    [Tooltip("Mid brown. Default panel fill.")]
    public Color surface     = Hex("#2C221A");
    [Tooltip("Lighter brown. Raised rows, hover states, inset wells.")]
    public Color surfaceRaised = Hex("#3A2D22");

    [Header("Ink & text")]
    [Tooltip("The outline colour on every sprite, and text on light surfaces.")]
    public Color ink        = Hex("#17110E");
    [Tooltip("Primary text colour on dark surfaces.")]
    public Color textPrimary   = Hex("#EFE3C9");
    [Tooltip("Secondary / disabled text. Cream knocked back ~45%.")]
    public Color textMuted     = Hex("#9C8F79");

    [Header("Accents")]
    public Color gold        = Hex("#CF9E24");
    public Color goldLight   = Hex("#E9C255");
    public Color xpGreen     = Hex("#4FB35A");
    public Color health      = Hex("#D2453A");
    [Tooltip("Darker red used for the delayed damage trail behind the health fill.")]
    public Color healthChip  = Hex("#8E2A22");
    public Color stamina     = Hex("#D8B547");
    [Tooltip("Level-up spark only. Never use this for a whole panel.")]
    public Color sillyPink   = Hex("#E85E9C");

    [Header("Semantic")]
    public Color danger  = Hex("#D2453A");
    public Color success = Hex("#4FB35A");
    public Color warning = Hex("#E0932E");

    // ── Type ──────────────────────────────────────────────────────────────────

    [Header("Fonts")]
    [Tooltip("Display face (Chewy). Titles, boss names, LEVEL UP. Use sparingly.")]
    public TMP_FontAsset displayFont;
    [Tooltip("Body face (Fredoka). All labels, numbers, tooltips. Use tabular figures.")]
    public TMP_FontAsset bodyFont;

    [Header("Type scale (px @ 1080p reference)")]
    public float sizeDisplay = 56f;
    public float sizeTitle   = 32f;
    public float sizeBody    = 20f;
    public float sizeSmall   = 16f;
    public float sizeMicro   = 13f;

    // ── Sprites ───────────────────────────────────────────────────────────────

    [Header("9-slice sprites")]
    [Tooltip("Main panel frame. 96×96 source, 32px border all round.")]
    public Sprite panel;
    [Tooltip("Inset well / bar trough. 48×48 source, 16px border.")]
    public Sprite panelInset;
    [Tooltip("Button idle. 64×64 source, 20px border.")]
    public Sprite button;
    [Tooltip("Button hovered.")]
    public Sprite buttonHover;
    [Tooltip("Button pressed (shifted down 2px in the art itself).")]
    public Sprite buttonPressed;
    [Tooltip("Tooltip bubble. 48×48 source, 16px border.")]
    public Sprite tooltip;
    [Tooltip("A plain 1×1 white pixel. Used for solid fills and flashes.")]
    public Sprite solid;

    // ── Motion ────────────────────────────────────────────────────────────────

    [Header("Motion (seconds)")]
    [Tooltip("Hover/press response. Anything slower feels laggy.")]
    public float durationFast   = 0.08f;
    [Tooltip("Panel fades, bar catch-up.")]
    public float durationNormal = 0.22f;
    [Tooltip("Screen-level transitions, boss bar intro.")]
    public float durationSlow   = 0.5f;

    // ── Layout ────────────────────────────────────────────────────────────────

    [Header("Layout")]
    [Tooltip("Base spacing unit. Every gap and padding should be a multiple of this.")]
    public float spacingUnit = 8f;
    [Tooltip("Keep HUD elements at least this far from the screen edge (TV overscan).")]
    public float screenMargin = 48f;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Parse "#RRGGBB" or "#RRGGBBAA" into a Color. Falls back to magenta.</summary>
    public static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
    }

    /// <summary>Same colour at a different alpha — avoids the `var c = x; c.a = y;` dance.</summary>
    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    /// <summary>
    /// A shared 1×1 white sprite for solid fills, generated at runtime so the UI
    /// works before any art exists. Assign <see cref="solid"/> to override it.
    /// </summary>
    private static Sprite _runtimeSolid;
    public static Sprite SolidSprite
    {
        get
        {
            if (Active != null && Active.solid != null) return Active.solid;
            if (_runtimeSolid != null) return _runtimeSolid;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _runtimeSolid = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _runtimeSolid.name = "UITheme_RuntimeSolid";
            return _runtimeSolid;
        }
    }
}
