using UnityEngine;
using TMPro;

/// <summary>
/// Per-entity component: when this entity takes damage, spawn a floating
/// number above it that drifts upward, fades out, and self-destroys.
///
/// Setup:
///   1. Drop on the Player prefab and every enemy prefab.
///   2. Requires EntityStats on the same GameObject (auto-found).
///   3. No prefab to author — the number is created entirely at runtime
///      from a TextMeshPro 3D component, so it picks up your pixelization
///      automatically (it's rendered through the same Main Camera as the
///      world).
///
/// Tuning:
///   • Adjust spawnOffset.y to lift the number above the entity's head.
///   • lowColor / highColor + lowDamage / highDamage define a colour ramp
///     so big crits visibly stand out from chip damage.
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class DamageNumberSpawner : MonoBehaviour
{
    [Header("Spawn Position")]
    [Tooltip("World-space offset from the entity's transform where the number appears.")]
    public Vector3 spawnOffset = new Vector3(0f, 1.6f, 0f);

    [Tooltip("Random XZ jitter applied to each number so multiple hits don't stack.")]
    public float scatterRadius = 0.35f;

    [Header("Animation")]
    [Tooltip("How long the number stays on screen before being destroyed.")]
    public float lifetime = 1.2f;

    [Tooltip("Upward speed (units/sec) of the floating number.")]
    public float floatSpeed = 1.5f;

    [Tooltip("Optional sideways drift over the lifetime (units total).")]
    public float horizontalDrift = 0.25f;

    [Header("Style")]
    [Tooltip("TMP font size — world units. Try 4-8 depending on world scale.")]
    public float fontSize = 5f;

    [Tooltip("Color used when the hit is at or below lowDamage.")]
    public Color lowColor  = new Color(1f, 1f, 1f, 1f);

    [Tooltip("Color used when the hit is at or above highDamage.")]
    public Color highColor = new Color(1f, 0.35f, 0.1f, 1f);

    public int lowDamage  = 5;
    public int highDamage = 40;

    [Tooltip("If true, the number always faces the camera (recommended).")]
    public bool billboardToCamera = true;

    [Header("Debug")]
    public bool verbose = false;

    private EntityStats _stats;

    void Start()
    {
        _stats = GetComponent<EntityStats>();
        if (_stats == null)
        {
            Debug.LogError($"[DamageNumberSpawner] {gameObject.name} has no EntityStats.");
            enabled = false;
            return;
        }
        _stats.onDamageTaken.AddListener(OnDamage);
    }

    void OnDestroy()
    {
        if (_stats != null) _stats.onDamageTaken.RemoveListener(OnDamage);
    }

    private void OnDamage(int amount)
    {
        if (amount <= 0) return;
        Spawn(amount);
    }

    private void Spawn(int amount)
    {
        // Build a fresh GameObject + TMP component (no prefab needed).
        var go = new GameObject($"DamageNumber_{amount}");
        Vector3 jitter = Random.insideUnitSphere * scatterRadius;
        jitter.y = 0f;
        go.transform.position = transform.position + spawnOffset + jitter;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text             = amount.ToString();
        tmp.fontSize         = fontSize;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.color            = LerpDamageColor(amount);
        tmp.fontStyle        = FontStyles.Bold;
        tmp.outlineWidth     = 0.2f;
        tmp.outlineColor     = Color.black;

        // Driver that animates and destroys the number
        var anim = go.AddComponent<DamageNumberAnim>();
        anim.Init(tmp, lifetime, floatSpeed, horizontalDrift, billboardToCamera);

        if (verbose)
            Debug.Log($"[DamageNumberSpawner] {gameObject.name} spawned '{amount}' number.");
    }

    private Color LerpDamageColor(int amount)
    {
        float t = highDamage > lowDamage
            ? Mathf.InverseLerp(lowDamage, highDamage, amount)
            : 0f;
        return Color.Lerp(lowColor, highColor, t);
    }
}

/// <summary>
/// One-shot per-number animator. Lives only as long as its number does.
/// Internal — created by DamageNumberSpawner at runtime, not added by hand.
/// </summary>
public class DamageNumberAnim : MonoBehaviour
{
    private TextMeshPro _tmp;
    private float       _lifetime;
    private float       _floatSpeed;
    private float       _drift;
    private bool        _billboard;

    private float       _elapsed;
    private Vector3     _driftDir;
    private Color       _baseColor;

    public void Init(TextMeshPro tmp, float lifetime, float floatSpeed,
                     float horizontalDrift, bool billboard)
    {
        _tmp        = tmp;
        _lifetime   = lifetime;
        _floatSpeed = floatSpeed;
        _drift      = horizontalDrift;
        _billboard  = billboard;
        _baseColor  = tmp.color;

        // Pick a random sideways direction at spawn time
        float angle = Random.Range(0f, Mathf.PI * 2f);
        _driftDir   = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t   = _elapsed / Mathf.Max(0.0001f, _lifetime);

        // Float up + drift sideways (small)
        Vector3 pos = transform.position;
        pos += Vector3.up * _floatSpeed * Time.deltaTime;
        pos += _driftDir * (_drift * Time.deltaTime / Mathf.Max(0.0001f, _lifetime));
        transform.position = pos;

        // Fade out over lifetime
        if (_tmp != null)
        {
            Color c = _baseColor;
            c.a = Mathf.Clamp01(1f - t);
            _tmp.color = c;
        }

        // Face the camera
        if (_billboard)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 fwd = cam.transform.rotation * Vector3.forward;
                Vector3 up  = cam.transform.rotation * Vector3.up;
                transform.rotation = Quaternion.LookRotation(fwd, up);
            }
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
