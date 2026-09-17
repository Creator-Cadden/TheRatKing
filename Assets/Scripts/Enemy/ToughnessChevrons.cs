using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stacks a chevron over an enemy's head, one per point of TOUGHNESS, so armour
/// is readable at a glance instead of something the player has to infer from how
/// their hits land. Toughness 0 shows nothing — the absence of chevrons is the
/// information.
///
/// Drop it on any enemy prefab; it needs no wiring. The sprite falls back to
/// `Assets/Resources/UI/Chevron.png`, so an enemy with nothing assigned still
/// shows them. It reads EntityStats, which has already pulled toughness out of
/// the EnemyStatBlock by Start, and re-reads on stat changes in case a floor
/// modifier bumps it.
/// </summary>
[DisallowMultipleComponent]
public class ToughnessChevrons : MonoBehaviour
{
    [Header("Art")]
    [Tooltip("Leave empty to load Resources/UI/Chevron.")]
    public Sprite chevronSprite;
    public Color color = new Color(1f, 0.85f, 0.45f, 0.95f);

    [Header("Placement")]
    [Tooltip("Extra height above the top of the enemy's renderers.")]
    public float heightAboveHead = 0.45f;
    [Tooltip("World size of one chevron.")]
    public float chevronScale = 0.30f;
    [Tooltip("Vertical gap between chevrons.")]
    public float spacing = 0.16f;
    [Tooltip("Cap the stack so a very tough enemy doesn't grow a tower.")]
    public int maxChevrons = 6;

    [Header("Behaviour")]
    [Tooltip("Hide the whole stack past this distance. 0 = always visible.")]
    public float hideBeyondDistance = 35f;

    private EntityStats _stats;
    private Transform   _holder;
    private readonly List<SpriteRenderer> _chevrons = new List<SpriteRenderer>();
    private float  _headHeight;
    private int    _shown = -1;
    private Camera _cam;

    void Start()
    {
        _stats = GetComponent<EntityStats>();
        if (_stats == null)
        {
            Debug.LogWarning($"[ToughnessChevrons] No EntityStats on '{name}'.");
            enabled = false;
            return;
        }

        if (chevronSprite == null) chevronSprite = Resources.Load<Sprite>("UI/Chevron");
        if (chevronSprite == null)
        {
            Debug.LogWarning("[ToughnessChevrons] No chevron sprite — expected " +
                             "Assets/Resources/UI/Chevron.png (imported as a Sprite).");
            enabled = false;
            return;
        }

        _headHeight = MeasureHeadHeight();

        _holder = new GameObject("ChevronStack").transform;
        _holder.SetParent(transform, false);

        Rebuild();
        _stats.onStatsChanged.AddListener(Rebuild);

        if (_shown == 0)
            Debug.Log($"[ToughnessChevrons] '{name}' read toughness 0 at Start. " +
                      "If it has armour, EntityStats just hadn't initialised yet — " +
                      "LateUpdate will pick it up.");
    }

    void OnDestroy()
    {
        if (_stats != null) _stats.onStatsChanged.RemoveListener(Rebuild);
    }

    /// <summary>
    /// Renderer bounds rather than a hand-tuned offset, so the same component sits
    /// correctly over a grunt and over something twice its size.
    /// </summary>
    private float MeasureHeadHeight()
    {
        Renderer[] rs = GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 1.5f;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.max.y - transform.position.y;
    }

    private void Rebuild()
    {
        int want = Mathf.Clamp(ToughnessValue(), 0, Mathf.Max(0, maxChevrons));
        if (want == _shown) return;
        _shown = want;

        while (_chevrons.Count < want)
        {
            var go = new GameObject("Chevron" + _chevrons.Count);
            go.transform.SetParent(_holder, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = chevronSprite;
            sr.color  = color;
            // Draw over the rat rather than inside it.
            sr.sortingOrder = 100;
            _chevrons.Add(sr);
        }

        for (int i = 0; i < _chevrons.Count; i++)
        {
            bool used = i < want;
            _chevrons[i].gameObject.SetActive(used);
            if (!used) continue;

            _chevrons[i].transform.localPosition = new Vector3(0f, i * spacing, 0f);
            _chevrons[i].transform.localScale    = Vector3.one * chevronScale;
        }
    }

    private int ToughnessValue()
    {
        if (_stats == null) return 0;
        // Toughness is the live value; BaseToughness is the stat-block number.
        // Prefer the live one so floor scaling shows up.
        return Mathf.Max(_stats.Toughness, _stats.BaseToughness);
    }

    void LateUpdate()
    {
        if (_holder == null) return;

        // Re-check the count every frame rather than trusting Start order or the
        // onStatsChanged event. EntityStats reads toughness out of the stat block
        // in ITS Start, and Unity does not order Starts — so a chevron stack built
        // in our Start can easily have read 0 and then never been rebuilt, which
        // looks exactly like "the chevrons don't work". Rebuild() early-outs when
        // the number hasn't changed, so this costs one int compare.
        Rebuild();

        if (_cam == null) _cam = Camera.main;

        // One place decides visibility: are there any chevrons, and are we close
        // enough to bother drawing them.
        bool near = _cam == null || hideBeyondDistance <= 0f ||
                    (transform.position - _cam.transform.position).sqrMagnitude
                        <= hideBeyondDistance * hideBeyondDistance;
        bool show = _shown > 0 && near;
        if (_holder.gameObject.activeSelf != show) _holder.gameObject.SetActive(show);

        if (!show || _cam == null) return;

        _holder.position = transform.position + Vector3.up * (_headHeight + heightAboveHead);

        // Copying the camera's rotation outright is the billboard that doesn't
        // mirror or skew — pointing forward AT the camera flips the sprite.
        _holder.rotation = _cam.transform.rotation;
    }
}
