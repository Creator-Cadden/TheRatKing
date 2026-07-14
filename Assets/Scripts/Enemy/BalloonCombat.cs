using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Balloon rat combat — Tier 2 only: a floating zoner whose single attack is a
/// telegraphed ground circle. Sequence: hover-chase → windup (circle decal
/// fades in on the ground below, puff-up anim) → DROP (fast descend, damage
/// resolves in the circle at impact) → GROUNDED (vulnerable — this is melee's
/// answer to an air enemy) → rise back up → cooldown.
/// Hover is driven by the NavMeshAgent's baseOffset (the AirGrunt pattern), or
/// by an assignable body child if you prefer to keep the agent untouched.
/// Damage/radius/windup/cooldown come from the stat block's FIRST Decal Attack
/// entry (shape Circle).
/// </summary>
public class BalloonCombat : EnemyCombatBase
{
    [Header("Hover / Drop")]
    [Tooltip("Optional: the visible balloon mesh child. If assigned, hover/drop " +
             "moves this child's local Y and the NavMeshAgent is left alone. " +
             "If empty, the agent's baseOffset is animated instead (AirGrunt style).")]
    public Transform body;

    [Tooltip("Hover height above the ground while chasing.")]
    public float hoverHeight = 2.5f;

    [Tooltip("Height while grounded after the drop (0 = flat on the floor).")]
    public float groundedHeight = 0.3f;

    [Tooltip("Seconds the drop takes — fast, it's the strike.")]
    public float dropDuration = 0.25f;

    [Tooltip("Seconds it sits on the ground after the drop — the VULNERABLE " +
             "window where melee weapons can reach it. The punish reward.")]
    public float groundedDuration = 1.5f;

    [Tooltip("Seconds to float back up to hover height.")]
    public float riseDuration = 0.8f;

    [Header("Animation Triggers (silently skipped if missing)")]
    public string windupTrigger = "Windup";   // puff up / strain
    public string dropTrigger   = "Drop";     // deflate slam
    public string riseTrigger   = "Rise";     // reinflate

    [Header("Decal Indicator")]
    [Tooltip("Color while the circle fades in during windup.")]
    public Color windupColor  = new Color(1f, 0.15f, 0.1f, 0.55f);

    [Tooltip("Color flashed at the moment of impact.")]
    public Color executeColor = new Color(1f, 0.6f, 0f, 0.75f);

    [Tooltip("Vertical offset of the decal from the detected ground point. " +
             "Negative values push it lower if it floats.")]
    public float indicatorYOffset = 0.03f;

    // (Ground detection uses the shared groundLayers mask + raycast helpers
    //  from EnemyCombatBase.)

    // ── State machine ──
    private enum Phase { Hover, Windup, Drop, Grounded, Rise }
    private Phase _phase = Phase.Hover;

    private float _phaseEndTime;
    private bool  _hitResolved;
    private NavMeshAgent _agent;

    // ── Indicator ──
    private GameObject   _indicator;
    private MeshFilter   _indicatorFilter;
    private MeshRenderer _indicatorRenderer;
    private Material     _indicatorMat;
    private float        _builtRadius = -1f;

    public override bool IsBusy => _phase != Phase.Hover;

    // The balloon's single decal entry (first in the stat block's list).
    private DecalAttackConfig Decal =>
        (_sb != null && _sb.hasDecalAttack &&
         _sb.decalAttacks != null && _sb.decalAttacks.Length > 0)
            ? _sb.decalAttacks[0] : null;

    public override float CurrentAttackReach => Decal?.Reach ?? 2.5f;

    protected override void Awake()
    {
        base.Awake();
        _agent = GetComponent<NavMeshAgent>();
    }

    public override CombatDebugState DecalDebugState
    {
        get
        {
            if (_phase == Phase.Windup)   return CombatDebugState.Windup;
            if (_phase == Phase.Drop)     return CombatDebugState.Strike;
            if (_phase == Phase.Grounded ||
                _phase == Phase.Rise)     return CombatDebugState.Recover;
            if (Decal != null &&
                Time.time < _lastAttackTime + Decal.cooldown) return CombatDebugState.Cooldown;
            return CombatDebugState.None;
        }
    }

    void Start()
    {
        ApplyHover(hoverHeight);
    }

    void OnDestroy()
    {
        if (_indicator != null)    Destroy(_indicator);
        if (_indicatorMat != null) Destroy(_indicatorMat);
    }

    public override void Tick()
    {
        if (_sb == null) return;

        HoldLockedRotation();

        switch (_phase)
        {
            case Phase.Windup:
            {
                // Circle fades in on the ground — the decal promise.
                float windup = Decal != null ? Decal.windupTime : 0.9f;
                float t = Mathf.Clamp01(1f - (_phaseEndTime - Time.time) / Mathf.Max(0.0001f, windup));
                Color c = windupColor;
                c.a *= t;
                SetIndicatorColor(c);
                SnapIndicatorToGround();

                if (Time.time >= _phaseEndTime) BeginDrop();
                break;
            }

            case Phase.Drop:
            {
                float t = DropProgress();
                ApplyHover(Mathf.Lerp(hoverHeight, groundedHeight, t));

                if (Time.time >= _phaseEndTime)
                {
                    ResolveImpact();
                    _phase        = Phase.Grounded;
                    _phaseEndTime = Time.time + groundedDuration;
                }
                break;
            }

            case Phase.Grounded:
                // Sitting duck — melee punish window.
                if (Time.time >= _phaseEndTime)
                {
                    _phase        = Phase.Rise;
                    _phaseEndTime = Time.time + riseDuration;
                    SetTriggerIfPresent(riseTrigger);
                }
                break;

            case Phase.Rise:
            {
                float t = 1f - Mathf.Clamp01((_phaseEndTime - Time.time) / Mathf.Max(0.0001f, riseDuration));
                ApplyHover(Mathf.Lerp(groundedHeight, hoverHeight, t));

                if (Time.time >= _phaseEndTime)
                {
                    ApplyHover(hoverHeight);
                    _phase = Phase.Hover;
                }
                break;
            }
        }
    }

    public override void TryStartAttack(float distToPlayer)
    {
        if (_sb == null || _player == null || Decal == null)   return;
        if (!_sb.hasDecalAttack)                               return;
        if (_phase != Phase.Hover)                             return;
        if (Time.time < _lastAttackTime + Decal.cooldown)      return;
        if (distToPlayer > CurrentAttackReach + 0.35f)         return;

        _lastAttackTime = Time.time;
        _phase          = Phase.Windup;
        _phaseEndTime   = Time.time + Decal.windupTime;
        _hitResolved    = false;

        FaceAndLockOntoPlayer();
        if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();

        BuildIndicator();
        SnapIndicatorToGround();
        ShowIndicator(true);
        SetIndicatorColor(new Color(windupColor.r, windupColor.g, windupColor.b, 0f));
        SetTriggerIfPresent(windupTrigger);
    }

    private void BeginDrop()
    {
        _phase        = Phase.Drop;
        _phaseEndTime = Time.time + dropDuration;
        SetIndicatorColor(executeColor);
        SetTriggerIfPresent(dropTrigger);
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play(AudioManager.SoundType.EnemyAttk);
    }

    private void ResolveImpact()
    {
        if (_hitResolved || Decal == null) return;
        _hitResolved = true;

        // Same anchor as the decal visual — what you see is what hits.
        Vector3 center = GroundCenter() + Vector3.up * (Decal.height * 0.5f);
        if (PlayerOverlapsSphere(center, Decal.circleRadius))
            DamagePlayer(RollDamage(Decal.damageMin, Decal.damageMax));

        // Indicator's job is done the moment the hit lands.
        ShowIndicator(false);
    }

    // These animation events aren't required for the balloon (timing is
    // physics-of-the-drop, not animation) but the contract needs them.
    public override void OnAttackHitFrame() => ResolveImpact();
    public override void OnAttackEnd()      { }

    public override void CancelWindup()
    {
        if (_phase != Phase.Windup) return;
        _phase = Phase.Hover;
        ShowIndicator(false);
    }

    public override void CancelAttackState()
    {
        // Knockback / death mid-attack: hide the decal and float back up
        // (unless dead — EnemyAI disables us then anyway).
        _phase = Phase.Hover;
        ShowIndicator(false);
        ApplyHover(hoverHeight);
    }

    // ── Hover plumbing ──

    private void ApplyHover(float h)
    {
        if (body != null)
        {
            Vector3 lp = body.localPosition;
            lp.y = h;
            body.localPosition = lp;
        }
        else if (_agent != null)
        {
            _agent.baseOffset = h;
        }
    }

    private float DropProgress() =>
        1f - Mathf.Clamp01((_phaseEndTime - Time.time) / Mathf.Max(0.0001f, dropDuration));

    /// <summary>The point on the ground directly below the balloon — the shared
    /// raycast helper finds the REAL floor; falls back to hover math if nothing
    /// is hit (e.g. flying over a pit).</summary>
    private Vector3 GroundCenter()
    {
        Vector3 anchor = HitOrigin.position;
        if (TryFindGroundY(anchor, out float y, hoverHeight + 6f))
            return new Vector3(anchor.x, y, anchor.z);

        // Fallback: computed hover math.
        Vector3 p = transform.position;
        if (body == null && _agent != null)
            p.y -= _agent.baseOffset;
        return new Vector3(anchor.x, p.y, anchor.z);
    }

    // ── Decal indicator (disk) ──

    private void BuildIndicator()
    {
        if (Decal == null) return;
        EnsureIndicatorObject();

        if (!Mathf.Approximately(_builtRadius, Decal.circleRadius))
        {
            _indicatorFilter.sharedMesh = BuildDiskMesh(Decal.circleRadius, 48);
            _builtRadius = Decal.circleRadius;
        }
    }

    private void SnapIndicatorToGround()
    {
        if (_indicator == null) return;
        // GroundCenter = Attack Origin XZ + raycast-detected floor height.
        _indicator.transform.position = GroundCenter() + Vector3.up * indicatorYOffset;
        _indicator.transform.rotation = Quaternion.identity;
    }

    private void EnsureIndicatorObject()
    {
        if (_indicator != null) return;

        _indicator = new GameObject("BalloonDropIndicator");
        _indicatorFilter   = _indicator.AddComponent<MeshFilter>();
        _indicatorRenderer = _indicator.AddComponent<MeshRenderer>();

        // Sprites/Default — always included in builds, supports alpha.
        Shader shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");
        _indicatorMat = new Material(shader) { renderQueue = 3000 };

        _indicatorRenderer.material          = _indicatorMat;
        _indicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _indicatorRenderer.receiveShadows    = false;
    }

    private void ShowIndicator(bool visible)
    {
        if (_indicator != null) _indicator.SetActive(visible);
    }

    private void SetIndicatorColor(Color c)
    {
        if (_indicatorMat != null) _indicatorMat.color = c;
    }

    private static Mesh BuildDiskMesh(float radius, int segments)
    {
        var verts = new Vector3[segments + 1];
        var tris  = new int[segments * 6];
        verts[0] = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments + 1;
            int b    = i * 6;
            tris[b + 0] = 0; tris[b + 1] = i + 1; tris[b + 2] = next;
            tris[b + 3] = 0; tris[b + 4] = next;  tris[b + 5] = i + 1;
        }

        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDrawGizmosSelected()
    {
        var stats = GetComponent<EntityStats>();
        var sb = stats != null ? stats.enemyStatBlock : null;
        if (sb == null || sb.decalAttacks == null || sb.decalAttacks.Length == 0) return;

        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, sb.decalAttacks[0].circleRadius);
    }
}
