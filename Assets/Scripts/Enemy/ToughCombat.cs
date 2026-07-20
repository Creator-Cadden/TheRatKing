using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tough rat combat — both tiers. Up close: an unmarked swipe (Tier 1, same
/// moving-hitbox pattern as GruntCombat). At mid range: the RECT DASH-CHARGE
/// (Tier 2) — rect decal locks along its facing, then it commits and charges
/// down the corridor, OVERSHOOTING past the player into a chunky recovery
/// window (the punish reward for sidestepping). Mini version of the boss roll.
/// Naturally un-interruptible during the dash via its high Toughness — no
/// special armor code needed.
/// Swipe tuning = stat block Basic Attack. Dash tuning = stat block's FIRST
/// Decal Attack entry (shape Rectangle).
/// </summary>
public class ToughCombat : EnemyCombatBase
{
    [Header("Swipe (Tier 1) — moving hit volume")]
    [Tooltip("Where the swipe lands. Parent to the paw/head bone so the volume " +
             "moves with the animation. Falls back to Attack Origin, then root.")]
    public Transform attackPoint;

    [Tooltip("Radius of the swipe hit sphere.")]
    public float hitRadius = 1.0f;

    [Tooltip("Auto-resolve the swipe hit this long into the strike if no " +
             "OnAttackHitFrame animation event is wired. 0 = events only.")]
    public float fallbackHitDelay = 0.2f;

    [Tooltip("Auto-end the swipe this long after it starts if no OnAttackEnd " +
             "animation event is wired. 0 = events only.")]
    public float fallbackEndDelay = 0.6f;

    // Attack chaining (N basics → decal) is configured on the stat block:
    // EnemyStatBlock.basicAttacksBetweenDecals. 0 = range-driven dashing.

    [Header("Dash (Tier 2)")]
    [Tooltip("Won't dash at targets closer than this — point-blank dashes feel " +
             "dumb and skip the swipe's job. Ignored when a forced dash is due.")]
    public float minDashDistance = 3f;

    [Tooltip("Movement speed during the charge.")]
    public float dashSpeed = 12f;

    [Tooltip("How far PAST the decal's rect length the dash carries — the " +
             "overshoot that exposes its back after a sidestep.")]
    public float overshootDistance = 1.5f;

    [Tooltip("Seconds of vulnerable recovery after the dash ends. The punish window.")]
    public float dashRecoverDuration = 1.2f;

    [Tooltip("Radius of the body contact check while charging.")]
    public float dashHitRadius = 1.0f;

    [Header("Animation Triggers (silently skipped if missing)")]
    public string swipeWindupTrigger = "Windup";
    public string swipeTrigger       = "Attk";
    public string dashWindupTrigger  = "DashWindup";
    public string dashTrigger        = "Dash";

    [Header("Dash Decal Indicator")]
    public Color windupColor  = new Color(1f, 0.15f, 0.1f, 0.55f);
    public Color executeColor = new Color(1f, 0.6f, 0f, 0.75f);

    [Tooltip("Vertical offset of the decal from this enemy's pivot. If the decal " +
             "floats above the floor, push this down (negative values are fine — " +
             "e.g. -0.4 if the rat's pivot sits above ground level).")]
    public float indicatorYOffset = 0.03f;

    [Header("Gizmos")]
    public bool showGizmos = true;

    // ── State machine ──
    private enum Phase { Idle, SwipeWindup, SwipeStrike, DashWindup, Dashing, DashRecover }
    private Phase _phase = Phase.Idle;

    private float _phaseEndTime;
    private float _strikeStartTime;
    private bool  _swipeHitResolved;

    private float _lastSwipeTime = -999f;
    private float _lastDashTime  = -999f;
    private int   _swipesSinceDash;

    private Vector3 _dashEnd;
    private bool    _dashHitResolved;
    private NavMeshAgent _agent;

    // ── Indicator ──
    private GameObject   _indicator;
    private MeshFilter   _indicatorFilter;
    private MeshRenderer _indicatorRenderer;
    private Material     _indicatorMat;
    private float _builtW = -1f, _builtL = -1f;

    public override bool IsBusy => _phase != Phase.Idle;

    // Impact system: swipe windup is flinch-delayable; the dash is a DECAL —
    // windup can only be delayed (capped), the charge itself is unstoppable.
    public override bool IsInBasicWindup => _phase == Phase.SwipeWindup;
    public override bool IsInDecalAction => _phase == Phase.DashWindup || _phase == Phase.Dashing;

    public override void DelayCurrentWindup(float seconds)
    {
        if (_phase != Phase.SwipeWindup && _phase != Phase.DashWindup) return;
        _phaseEndTime += AccrueWindupDelay(seconds);
    }

    private BasicAttackConfig Basic => _sb != null ? _sb.basicAttack : null;

    private DecalAttackConfig Dash =>
        (_sb != null && _sb.hasDecalAttack &&
         _sb.decalAttacks != null && _sb.decalAttacks.Length > 0)
            ? _sb.decalAttacks[0] : null;

    private bool DashReady =>
        Dash != null && Time.time >= _lastDashTime + Dash.cooldown;

    // Chain mode: the stat block can lock the dash behind N completed swipes.
    private bool ChainMode     => _sb != null && _sb.basicAttacksBetweenDecals > 0;
    private bool DashUnlocked  => !ChainMode || _swipesSinceDash >= _sb.basicAttacksBetweenDecals;

    // When the dash is available (off cooldown AND unlocked by the chain),
    // EnemyAI may engage from dash range; otherwise it closes to swipe range.
    public override float CurrentAttackReach
    {
        get
        {
            float swipe = Basic != null ? Basic.reach : 1.7f;
            return (DashReady && DashUnlocked) ? Mathf.Max(swipe, Dash.Reach) : swipe;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        _agent = GetComponent<NavMeshAgent>();
    }

    public override CombatDebugState BasicDebugState
    {
        get
        {
            if (_phase == Phase.SwipeWindup)  return CombatDebugState.Windup;
            if (_phase == Phase.SwipeStrike)  return CombatDebugState.Strike;
            if (Basic != null &&
                Time.time < _lastSwipeTime + Basic.cooldown) return CombatDebugState.Cooldown;
            return CombatDebugState.None;
        }
    }

    public override CombatDebugState DecalDebugState
    {
        get
        {
            if (_phase == Phase.DashWindup)   return CombatDebugState.Windup;
            if (_phase == Phase.Dashing)      return CombatDebugState.Strike;
            if (_phase == Phase.DashRecover)  return CombatDebugState.Recover;
            if (Dash != null &&
                Time.time < _lastDashTime + Dash.cooldown) return CombatDebugState.Cooldown;
            return CombatDebugState.None;
        }
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
            case Phase.SwipeWindup:
                if (Time.time >= _phaseEndTime) BeginSwipeStrike();
                break;

            case Phase.SwipeStrike:
                if (!_swipeHitResolved && fallbackHitDelay > 0f &&
                    Time.time >= _strikeStartTime + fallbackHitDelay)
                    ResolveSwipeHit();

                if (fallbackEndDelay > 0f &&
                    Time.time >= _strikeStartTime + fallbackEndDelay)
                { _phase = Phase.Idle; break; }

                if (Time.time >= _strikeStartTime + _sb.attackAnimTimeout)
                    _phase = Phase.Idle;
                break;

            case Phase.DashWindup:
            {
                float windup = Dash != null ? Dash.windupTime : 0.8f;
                float t = Mathf.Clamp01(1f - (_phaseEndTime - Time.time) / Mathf.Max(0.0001f, windup));
                Color c = windupColor;
                c.a *= t;
                SetIndicatorColor(c);
                SnapIndicatorToGround();

                if (Time.time >= _phaseEndTime) BeginDash();
                break;
            }

            case Phase.Dashing:
            {
                // Charge along the locked direction.
                transform.position = Vector3.MoveTowards(
                    transform.position, _dashEnd, dashSpeed * Time.deltaTime);

                // Body contact damage — once per dash.
                if (!_dashHitResolved &&
                    PlayerOverlapsSphere(transform.position + Vector3.up * 0.6f, dashHitRadius))
                {
                    _dashHitResolved = true;
                    // Shove the player ALONG the charge direction (plus a hint
                    // of sideways-away) — hit by a truck, not tapped by one.
                    Vector3 push = transform.forward
                                 + (_player.position - transform.position).normalized * 0.4f;
                    DamagePlayer(RollDamage(Dash.damageMin, Dash.damageMax),
                                 decalHit: true, pushDir: push);
                }

                if (Vector3.SqrMagnitude(transform.position - _dashEnd) < 0.01f)
                    EndDash();
                break;
            }

            case Phase.DashRecover:
                // Vulnerable — back exposed. The sidestep reward.
                if (Time.time >= _phaseEndTime)
                    _phase = Phase.Idle;
                break;
        }
    }

    public override void TryStartAttack(float distToPlayer)
    {
        if (_sb == null || _player == null) return;
        if (_phase != Phase.Idle)           return;

        bool swipeReady = _sb.hasBasicAttack && Basic != null &&
                          Time.time >= _lastSwipeTime + Basic.cooldown;

        if (ChainMode)
        {
            // Strict rhythm: N swipes, then the dash is the NEXT attack
            // (min-distance waived so hugging can't suppress it). While the
            // dash is due but still cooling down, keep swiping.
            if (DashUnlocked && DashReady && distToPlayer <= Dash.Reach + 0.35f)
            {
                StartDashWindup();
                return;
            }

            if (swipeReady && distToPlayer <= Basic.reach + 0.35f)
                StartSwipe();
            return;
        }

        // Range-driven mode (basicAttacksBetweenDecals = 0):
        // swipe up close, dash whenever ready in the mid-range band.
        if (swipeReady && distToPlayer <= Basic.reach + 0.35f)
        {
            StartSwipe();
            return;
        }

        if (DashReady && distToPlayer >= minDashDistance &&
            distToPlayer <= Dash.Reach + 0.35f)
        {
            StartDashWindup();
        }
    }

    // ── Swipe ──

    private void StartSwipe()
    {
        _lastSwipeTime = Time.time;
        _swipesSinceDash++;
        _phase              = Phase.SwipeWindup;
        _phaseEndTime       = Time.time + Basic.windupTime;
        _windupDelayAccrued = 0f;

        FaceAndLockOntoPlayer();
        SetTriggerIfPresent(swipeWindupTrigger);
    }

    private void BeginSwipeStrike()
    {
        _phase            = Phase.SwipeStrike;
        _strikeStartTime  = Time.time;
        _swipeHitResolved = false;

        SetTriggerIfPresent(swipeTrigger);
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play(AudioManager.SoundType.EnemyAttk);
    }

    private void ResolveSwipeHit()
    {
        if (_swipeHitResolved) return;
        _swipeHitResolved = true;

        Transform point = attackPoint != null ? attackPoint : HitOrigin;
        if (PlayerOverlapsSphere(point.position, hitRadius))
            DamagePlayer(RollBasicAttackDamage(), decalHit: false);
    }

    // ── Dash ──

    private void StartDashWindup()
    {
        _lastDashTime       = Time.time;
        _swipesSinceDash    = 0;
        _phase              = Phase.DashWindup;
        _phaseEndTime       = Time.time + Dash.windupTime;
        _windupDelayAccrued = 0f;

        FaceAndLockOntoPlayer();   // direction locks NOW — sidestep after this wins
        if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();

        BuildIndicator();
        SnapIndicatorToGround();
        ShowIndicator(true);
        SetIndicatorColor(new Color(windupColor.r, windupColor.g, windupColor.b, 0f));
        SetTriggerIfPresent(dashWindupTrigger);
    }

    private void BeginDash()
    {
        _phase           = Phase.Dashing;
        _dashHitResolved = false;

        // Cap the dash so it can never leave the arena / navmesh (boss-roll trick).
        float   travel = Dash.rectLength + overshootDistance;
        Vector3 target = transform.position + transform.forward * travel;
        if (NavMesh.Raycast(transform.position, target, out NavMeshHit hit, NavMesh.AllAreas))
            target = hit.position;
        _dashEnd = target;

        // The agent fights manual movement — hand control to the script.
        if (_agent != null) _agent.enabled = false;

        SetIndicatorColor(executeColor);
        SetTriggerIfPresent(dashTrigger);
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play(AudioManager.SoundType.EnemyAttk);
    }

    private void EndDash()
    {
        if (_agent != null) _agent.enabled = true;
        ShowIndicator(false);
        _phase        = Phase.DashRecover;
        _phaseEndTime = Time.time + dashRecoverDuration;
    }

    // ── Contract plumbing ──

    public override void OnAttackHitFrame()
    {
        if (_phase == Phase.SwipeStrike) ResolveSwipeHit();
    }

    public override void OnAttackEnd()
    {
        if (_phase == Phase.SwipeStrike) _phase = Phase.Idle;
    }

    public override void CancelWindup()
    {
        if (_phase == Phase.SwipeWindup) _phase = Phase.Idle;
        else if (_phase == Phase.DashWindup)
        {
            _phase = Phase.Idle;
            ShowIndicator(false);
        }
    }

    public override void CancelAttackState()
    {
        if (_phase == Phase.Dashing && _agent != null) _agent.enabled = true;
        _phase = Phase.Idle;
        ShowIndicator(false);
    }

    // ── Indicator (rect along locked facing) ──

    private void BuildIndicator()
    {
        if (Dash == null) return;
        EnsureIndicatorObject();

        if (!Mathf.Approximately(_builtW, Dash.rectWidth) ||
            !Mathf.Approximately(_builtL, Dash.rectLength))
        {
            _indicatorFilter.sharedMesh = BuildRectMesh(Dash.rectWidth, Dash.rectLength);
            _builtW = Dash.rectWidth;
            _builtL = Dash.rectLength;
        }
    }

    private void SnapIndicatorToGround()
    {
        if (_indicator == null) return;
        // Attack Origin XZ, snapped down to the raycast-detected real floor.
        _indicator.transform.position = GroundSnap(HitOrigin.position)
                                      + Vector3.up * indicatorYOffset;
        _indicator.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    private void EnsureIndicatorObject()
    {
        if (_indicator != null) return;

        _indicator = new GameObject("DashIndicator");
        _indicatorFilter   = _indicator.AddComponent<MeshFilter>();
        _indicatorRenderer = _indicator.AddComponent<MeshRenderer>();

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

    private static Mesh BuildRectMesh(float width, float length)
    {
        float hw = width * 0.5f;
        var verts = new Vector3[]
        {
            new Vector3(-hw, 0f, 0f),
            new Vector3( hw, 0f, 0f),
            new Vector3(-hw, 0f, length),
            new Vector3( hw, 0f, length),
        };
        var tris = new int[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };
        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        // Swipe sphere
        Transform point = attackPoint != null ? attackPoint
                        : attackOrigin != null ? attackOrigin
                        : transform;
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(point.position, hitRadius);

        // Dash corridor
        var stats = GetComponent<EntityStats>();
        var sb = stats != null ? stats.enemyStatBlock : null;
        if (sb != null && sb.decalAttacks != null && sb.decalAttacks.Length > 0)
        {
            var d = sb.decalAttacks[0];
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
            Matrix4x4 m = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position,
                Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, 0.5f, d.rectLength * 0.5f),
                                new Vector3(d.rectWidth, 1f, d.rectLength));
            Gizmos.matrix = m;
        }
    }
}
