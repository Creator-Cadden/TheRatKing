using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Red arcs around the edge of the screen pointing at whatever just hit you.
///
/// In a third-person game with a camera the player is also steering, getting hit
/// from off-screen is the single most common "that felt unfair" moment. This is
/// the fix, and it's cheap.
///
/// Because EntityStats.onDamageTaken only carries an amount, enemy attack code
/// calls this directly with the attacker's position:
///     DamageDirectionIndicator.ShowFrom(transform.position);
/// Add that one line to EnemyCombatBase wherever it lands a hit on the player.
/// </summary>
public class DamageDirectionIndicator : MonoBehaviour
{
    public static DamageDirectionIndicator Instance { get; private set; }

    [Header("Player")]
    public string playerTag = "Player";

    [Header("Arc look")]
    [Tooltip("Sprite for one arc — a wedge or curved bar, transparent at the tips. " +
             "Leave empty for a plain rectangle (works, looks worse).")]
    public Sprite arcSprite;

    [Tooltip("Size of one arc in canvas pixels.")]
    public Vector2 arcSize = new Vector2(190f, 46f);

    [Tooltip("Distance from screen centre, in canvas pixels. Push this out past " +
             "your crosshair so it doesn't crowd the middle of the screen.")]
    public float arcRadius = 280f;

    public Color arcColor = new Color(0.85f, 0.16f, 0.12f, 1f);

    [Header("Timing")]
    public float holdTime = 0.55f;
    public float fadeTime = 0.7f;
    [Range(0f, 1f)] public float peakAlpha = 0.85f;

    [Header("Limits")]
    [Tooltip("Most arcs on screen at once. Extra hits refresh the nearest existing arc " +
             "instead of stacking, so a flurry doesn't fill the screen with red.")]
    public int maxArcs = 4;

    [Tooltip("Two hits within this many degrees reuse the same arc.")]
    public float mergeAngle = 25f;

    [Header("Accessibility")]
    [Tooltip("Scale opacity by the player's Screen Shake setting.")]
    public bool respectShakeSetting = true;

    // ── Private ───────────────────────────────────────────────────────────────

    private class Arc
    {
        public RectTransform rt;
        public Image  image;
        public float  angle;     // degrees, 0 = straight ahead
        public float  age;
    }

    private readonly List<Arc> _arcs = new List<Arc>();
    private Transform _player;
    private Camera    _cam;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        float opacity = respectShakeSetting ? Mathf.Max(0.3f, GameSettings.ScreenShake) : 1f;

        for (int i = _arcs.Count - 1; i >= 0; i--)
        {
            Arc a = _arcs[i];
            if (a.rt == null) { _arcs.RemoveAt(i); continue; }

            a.age += Time.deltaTime;

            float alpha = a.age < holdTime
                ? peakAlpha
                : (fadeTime > 0f ? peakAlpha * (1f - (a.age - holdTime) / fadeTime) : 0f);

            Color c = arcColor;
            c.a = Mathf.Clamp01(alpha) * opacity;
            a.image.color = c;

            if (a.age >= holdTime + fadeTime)
            {
                Destroy(a.rt.gameObject);
                _arcs.RemoveAt(i);
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Flash an arc pointing at this world position. Safe if no indicator exists.</summary>
    public static void ShowFrom(Vector3 worldPosition)
    {
        if (Instance == null) return;
        Instance.Flash(worldPosition);
    }

    private void Flash(Vector3 worldPosition)
    {
        EnsureRefs();
        if (_cam == null || _player == null) return;

        // Angle of the attacker relative to where the CAMERA is looking, flattened
        // to the ground plane. 0 = ahead, +90 = to the right.
        Vector3 toSource = worldPosition - _player.position;
        toSource.y = 0f;
        if (toSource.sqrMagnitude < 0.0001f) return;

        Vector3 forward = _cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return;

        float angle = Vector3.SignedAngle(forward.normalized, toSource.normalized, Vector3.up);

        // Merge with a nearby existing arc rather than stacking.
        foreach (Arc existing in _arcs)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(existing.angle, angle)) <= mergeAngle)
            {
                existing.age   = 0f;
                existing.angle = angle;
                Place(existing);
                return;
            }
        }

        if (_arcs.Count >= maxArcs)
        {
            // Recycle the oldest.
            Arc oldest = _arcs[0];
            foreach (Arc a in _arcs) if (a.age > oldest.age) oldest = a;
            oldest.age   = 0f;
            oldest.angle = angle;
            Place(oldest);
            return;
        }

        _arcs.Add(BuildArc(angle));
    }

    // ── Building ──────────────────────────────────────────────────────────────

    private Arc BuildArc(float angle)
    {
        var go = new GameObject("DamageArc", typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = arcSize;

        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.sprite = arcSprite != null ? arcSprite : UITheme.SolidSprite;
        img.color  = UITheme.WithAlpha(arcColor, 0f);

        var arc = new Arc { rt = rt, image = img, angle = angle, age = 0f };
        Place(arc);
        return arc;
    }

    /// <summary>Push the arc out to its radius and rotate it to face outward.</summary>
    private void Place(Arc arc)
    {
        // Screen space: 0° = up (ahead), clockwise positive to match SignedAngle.
        float rad = arc.angle * Mathf.Deg2Rad;
        var offset = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * arcRadius;

        arc.rt.anchoredPosition = offset;
        arc.rt.localRotation    = Quaternion.Euler(0f, 0f, -arc.angle);
    }

    private void EnsureRefs()
    {
        if (_player == null)
        {
            GameObject go = GameObject.FindWithTag(playerTag);
            if (go != null) _player = go.transform;
        }
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
    }
}
