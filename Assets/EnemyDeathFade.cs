using System.Collections;
using UnityEngine;

public class EnemyDeathFade : MonoBehaviour
{
    [Header("Fade Settings")]
    public float delayBeforeFade = 2f;     // seconds to wait after death
    public float fadeDuration    = 1.5f;   // how long the fade takes
    public bool  destroyOnDone   = true;   // set false if you want to pool

    // ── Hook for later: assign a custom death effect in the Inspector ──
    [Header("Optional Effect (hook up later)")]
    [Tooltip("Particle system, VFX Graph, etc. Played when fade begins.")]
    public GameObject deathEffectPrefab;

    private EntityStats  _stats;
    private Renderer[]   _renderers;
    private Material[][] _originalMaterials;
    private Material[][] _fadeMaterials;

    void Start()
    {
        _stats = GetComponent<EntityStats>();
        _stats.onDeath.AddListener(OnDeath);

        // Grab every renderer on this enemy (mesh, skinned mesh, etc.)
        _renderers = GetComponentsInChildren<Renderer>();
    }

    private void OnDeath()
    {
        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        // ── Placeholder: swap in your custom animation / effect here later ──
        if (deathEffectPrefab != null)
            Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);

        yield return new WaitForSeconds(delayBeforeFade);

        SetupFadeMaterials();

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            SetAlpha(alpha);
            yield return null;
        }

        if (destroyOnDone)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    // Swaps all materials to transparent copies so we can fade them
    private void SetupFadeMaterials()
    {
        _fadeMaterials = new Material[_renderers.Length][];

        for (int r = 0; r < _renderers.Length; r++)
        {
            var mats = _renderers[r].materials;
            _fadeMaterials[r] = new Material[mats.Length];

            for (int m = 0; m < mats.Length; m++)
            {
                // Clone the material so we don't affect other enemies
                Material copy = new Material(mats[m]);

                // URP transparent setup
                copy.SetFloat("_Surface",  1f);
                copy.SetFloat("_ZWrite",   0f);
                copy.SetInt("_SrcBlend",   (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                copy.SetInt("_DstBlend",   (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                copy.renderQueue = 3000;

                _fadeMaterials[r][m] = copy;
            }

            _renderers[r].materials = _fadeMaterials[r];
        }
    }

    private void SetAlpha(float alpha)
    {
        if (_fadeMaterials == null) return;

        for (int r = 0; r < _fadeMaterials.Length; r++)
            foreach (var mat in _fadeMaterials[r])
            {
                Color c = mat.color;
                c.a = alpha;
                mat.color = c;

                // URP also needs _BaseColor
                if (mat.HasProperty("_BaseColor"))
                {
                    Color bc = mat.GetColor("_BaseColor");
                    bc.a = alpha;
                    mat.SetColor("_BaseColor", bc);
                }
            }
    }
}