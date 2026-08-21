using UnityEngine;

/// <summary>
/// A single "XP orb" — a small vibrant, glowing ball that bursts out of a dying
/// enemy, arcs outward under gravity for a beat, then homes in on the player and
/// grants its share of XP on arrival (which makes the XP bar tick up).
///
/// Spawned in a burst by <see cref="SpawnBurst"/>; you never add this by hand.
/// Orbs are created UNPARENTED at a world position so they survive the enemy's
/// death / fade destruction. Fully self-contained — it builds its own sphere and
/// a bright material at runtime, so there is NO prefab to wire up. Mirrors the
/// self-contained spirit of DamageNumberSpawner.
/// </summary>
[DisallowMultipleComponent]
public class XPOrb : MonoBehaviour
{
    // Vibrant defaults (SpawnBurst can override per-enemy).
    public static readonly Color DefaultColor = new Color(0.35f, 1f, 0.55f, 1f); // XP green
    public const float DefaultSize = 0.28f;

    [Header("Burst (scatter out)")]
    [Tooltip("Upward launch speed when the orb pops out of the enemy.")]
    public float burstUpSpeed   = 4.5f;
    [Tooltip("Sideways launch speed (random direction) on spawn.")]
    public float burstSideSpeed = 3.5f;
    [Tooltip("Gravity applied during the scatter phase.")]
    public float gravity        = -14f;
    [Tooltip("Seconds the orb scatters before it starts seeking the player.")]
    public float scatterTime    = 0.35f;

    [Header("Seek (fly to player)")]
    [Tooltip("Homing speed the instant seeking begins.")]
    public float seekStartSpeed = 3f;
    [Tooltip("How fast homing speed ramps up (units/sec^2). Orbs accelerate inward.")]
    public float seekAccel      = 34f;
    [Tooltip("Distance to the player at which the orb is collected.")]
    public float collectRadius  = 0.45f;
    [Tooltip("Safety net: orb self-collects after this long even if it never arrives.")]
    public float maxLifetime    = 4f;

    // ── Runtime state ──
    private Transform _target;
    private XPSystem  _xp;
    private int       _amount;
    private Vector3   _vel;
    private float     _age;
    private float     _seekSpeed;
    private bool      _seeking;
    private Transform _tf;

    /// <summary>
    /// Spawn a burst of orbs at <paramref name="position"/> that fly to
    /// <paramref name="target"/> and, between them, grant exactly
    /// <paramref name="totalXP"/> to <paramref name="xp"/> as they land.
    /// </summary>
    public static void SpawnBurst(Vector3 position, int totalXP, Transform target,
                                  XPSystem xp, int count = 0,
                                  Color? color = null, float size = 0f)
    {
        if (totalXP <= 0 || target == null || xp == null) return;

        // Orb count scales with the reward; never more orbs than XP to share.
        if (count <= 0) count = Mathf.Clamp(Mathf.RoundToInt(totalXP / 4f), 3, 12);
        count = Mathf.Clamp(count, 1, totalXP);

        Color c = color ?? DefaultColor;
        float s = size  > 0f ? size : DefaultSize;

        int baseShare = totalXP / count;
        int remainder = totalXP - baseShare * count;   // hand the leftover out one-by-one

        for (int i = 0; i < count; i++)
        {
            int share = baseShare + (i < remainder ? 1 : 0);
            CreateOne(position, share, target, xp, c, s);
        }
    }

    private static void CreateOne(Vector3 position, int share, Transform target,
                                  XPSystem xp, Color color, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "XPOrb";

        // Pure visual — no physics collider, movement is fully scripted.
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.position   = position + Vector3.up * 0.5f;
        go.transform.localScale = Vector3.one * size;

        ApplyOrbMaterial(go.GetComponent<Renderer>(), color);

        go.AddComponent<XPOrb>().Init(share, target, xp);
    }

    private void Init(int amount, Transform target, XPSystem xp)
    {
        _amount    = amount;
        _target    = target;
        _xp        = xp;
        _tf        = transform;
        _seekSpeed = seekStartSpeed;

        // Launch: up + a random sideways kick — a satisfying little fountain.
        float ang = Random.Range(0f, Mathf.PI * 2f);
        _vel = Vector3.up * burstUpSpeed
             + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * burstSideSpeed;
    }

    void Update()
    {
        _age += Time.deltaTime;

        if (_target == null)
        {
            // Player gone — grant now so XP is never silently lost.
            Collect();
            return;
        }

        if (!_seeking)
        {
            // Scatter phase: arc out under gravity.
            _vel.y += gravity * Time.deltaTime;
            _tf.position += _vel * Time.deltaTime;
            if (_age >= scatterTime) _seeking = true;
        }
        else
        {
            // Seek phase: accelerate toward the player.
            _seekSpeed += seekAccel * Time.deltaTime;
            Vector3 toTarget = TargetPoint() - _tf.position;
            float dist = toTarget.magnitude;
            if (dist <= collectRadius) { Collect(); return; }

            _tf.position += toTarget / Mathf.Max(dist, 0.0001f) * _seekSpeed * Time.deltaTime;
        }

        // Gentle spin so the ball reads as alive.
        _tf.Rotate(Vector3.up, 220f * Time.deltaTime, Space.World);

        if (_age >= maxLifetime) Collect();
    }

    // Aim at the player's mid-body, not their feet.
    private Vector3 TargetPoint() => _target.position + Vector3.up * 0.8f;

    private void Collect()
    {
        // Grant silently — the single "+N XP from {enemy}" popup is fired once at
        // the kill by EnemyXPDrop, so orbs must NOT each spam the feed.
        if (_xp != null && _amount > 0) _xp.AddXP(_amount, "", false);
        Destroy(gameObject);
    }

    // ── Runtime material: reads as a bright, vibrant orb across render pipelines
    //    (URP Unlit → Unlit/Color → Sprites/Default → Standard emission fallback). ──
    private static void ApplyOrbMaterial(Renderer r, Color color)
    {
        if (r == null) return;

        Shader shader = FindOrbShader();
        if (shader == null) return;

        var mat = new Material(shader);

        // Set whichever colour property the chosen shader actually exposes.
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);

        // If we landed on a lit/Standard shader, drive emission so it still glows.
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
        }

        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private static Shader FindOrbShader()
    {
        string[] names =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default",
            "Standard",
        };
        foreach (var n in names)
        {
            Shader s = Shader.Find(n);
            if (s != null) return s;
        }
        return null;
    }
}
