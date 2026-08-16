using UnityEngine;
using TMPro;

/// <summary>
/// Per-entity: on damage, spawns a floating number above it that POPS in, ARCS
/// up and out, scales its size by how big the hit was, then fades and self-destroys.
/// Fully self-contained (both classes live in this file).
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class DamageNumberSpawner : MonoBehaviour
{
    [Header("Spawn Position")]
    public Vector3 spawnOffset   = new Vector3(0f, 1.6f, 0f);
    public float   scatterRadius = 0.3f;

    [Header("Motion")]
    public float lifetime     = 1.1f;
    [Tooltip("Initial upward launch speed — the number tosses up then arcs down.")]
    public float arcUpSpeed   = 3.2f;
    [Tooltip("Initial sideways launch speed (random direction).")]
    public float arcSideSpeed = 1.6f;
    [Tooltip("Downward acceleration — gives the toss its arc.")]
    public float gravity      = -9f;

    [Header("Pop")]
    [Tooltip("Overshoot scale on spawn (1.3 = pops 30% bigger before settling).")]
    public float popScale    = 1.3f;
    [Tooltip("How long the pop-in takes.")]
    public float popDuration = 0.12f;

    [Header("Size by damage")]
    [Tooltip("Font size for a hit at/below lowDamage (a small tick).")]
    public float minFontSize = 4f;
    [Tooltip("Font size for a hit at/above highDamage (a chunky hit).")]
    public float maxFontSize = 9f;
    public int   lowDamage   = 5;
    public int   highDamage  = 40;

    [Header("Colour by damage")]
    public Color lowColor  = new Color(1f, 1f, 1f, 1f);
    public Color highColor = new Color(1f, 0.35f, 0.1f, 1f);

    public bool billboardToCamera = true;

    private EntityStats _stats;

    void Start()
    {
        _stats = GetComponent<EntityStats>();
        if (_stats == null) { enabled = false; return; }
        _stats.onDamageTaken.AddListener(OnDamage);
    }

    void OnDestroy()
    {
        if (_stats != null) _stats.onDamageTaken.RemoveListener(OnDamage);
    }

    private void OnDamage(int amount)
    {
        if (amount <= 0) return;

        var go = new GameObject($"DamageNumber_{amount}");
        Vector3 jitter = Random.insideUnitSphere * scatterRadius; jitter.y = 0f;
        go.transform.position = transform.position + spawnOffset + jitter;

        float t = highDamage > lowDamage ? Mathf.InverseLerp(lowDamage, highDamage, amount) : 0f;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text         = amount.ToString();
        tmp.fontSize     = Mathf.Lerp(minFontSize, maxFontSize, t);   // bigger hit = bigger number
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.color        = Color.Lerp(lowColor, highColor, t);
        tmp.fontStyle    = FontStyles.Bold;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;

        // Arc launch: up + random sideways.
        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector3 vel = Vector3.up * arcUpSpeed
                    + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * arcSideSpeed;

        go.AddComponent<DamageNumberAnim>()
          .Init(tmp, lifetime, vel, gravity, popScale, popDuration, billboardToCamera);
    }
}

/// <summary>
/// One-shot animator for a damage number: pop-in scale, arc via velocity +
/// gravity, fade out, camera billboard, self-destruct. Created at runtime by
/// DamageNumberSpawner — not added by hand.
/// </summary>
public class DamageNumberAnim : MonoBehaviour
{
    private TMP_Text _tmp;
    private float    _lifetime, _gravity, _popScale, _popDuration;
    private bool     _billboard;
    private Vector3  _vel, _baseScale;
    private Color    _baseColor;
    private float    _elapsed;

    public void Init(TMP_Text tmp, float lifetime, Vector3 initialVel, float gravity,
                     float popScale, float popDuration, bool billboard)
    {
        _tmp = tmp; _lifetime = lifetime; _vel = initialVel; _gravity = gravity;
        _popScale = popScale; _popDuration = popDuration; _billboard = billboard;
        _baseColor = tmp.color;
        _baseScale = transform.localScale;
        transform.localScale = _baseScale * 0.4f;   // start small for the pop
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = _elapsed / Mathf.Max(0.0001f, _lifetime);

        // Arc.
        _vel.y += _gravity * Time.deltaTime;
        transform.position += _vel * Time.deltaTime;

        // Pop-in overshoot, then settle to 1.
        float s;
        if (_popDuration > 0f && _elapsed < _popDuration)
        {
            float p = _elapsed / _popDuration;
            s = Mathf.Lerp(0.4f, _popScale, 1f - (1f - p) * (1f - p));
        }
        else
        {
            float p = Mathf.Clamp01((_elapsed - _popDuration) / 0.1f);
            s = Mathf.Lerp(_popScale, 1f, p);
        }
        transform.localScale = _baseScale * s;

        // Fade over the last 40%.
        if (_tmp != null)
        {
            Color c = _baseColor;
            c.a = _baseColor.a * (1f - Mathf.Clamp01(Mathf.InverseLerp(0.6f, 1f, t)));
            _tmp.color = c;
        }

        // Billboard.
        if (_billboard)
        {
            Camera cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(
                    cam.transform.rotation * Vector3.forward,
                    cam.transform.rotation * Vector3.up);
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
