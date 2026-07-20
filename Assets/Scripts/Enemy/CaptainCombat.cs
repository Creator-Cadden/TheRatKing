using UnityEngine;

/// <summary>
/// Captain combat — the per-level miniboss. Both tiers, chained:
/// N filler swipes (Tier 1, stat block Basic Attack) between each decal attack,
/// with the decal cycling through the stat block's list (Sequence = the
/// learnable rotation: swipe, swipe, CONE, swipe, swipe, CIRCLE, swipe,
/// swipe, RECT, repeat). Chain count = EnemyStatBlock.basicAttacksBetweenDecals.
/// Decal attacks resolve in place with a shape hit-check (cone / circle / rect)
/// and each entry has its own damage, windup, and recoverTime — give the big
/// cone the longest recovery so flanking it earns a real punish window.
/// </summary>
public class CaptainCombat : EnemyCombatBase
{
    [Header("Swipe (Tier 1) — moving hit volume")]
    [Tooltip("Where the filler swipe lands. Parent to the paw/head bone. " +
             "Falls back to Attack Origin, then root.")]
    public Transform attackPoint;

    [Tooltip("Radius of the swipe hit sphere.")]
    public float hitRadius = 1.1f;

    [Tooltip("Auto-resolve the swipe hit this long into the strike if no " +
             "OnAttackHitFrame animation event is wired. 0 = events only.")]
    public float fallbackHitDelay = 0.2f;

    [Tooltip("Auto-end the swipe this long after it starts if no OnAttackEnd " +
             "animation event is wired. 0 = events only.")]
    public float fallbackEndDelay = 0.7f;

    [Header("Decal Strike")]
    [Tooltip("Seconds the decal stays on screen (execute color) after the windup " +
             "ends — the hit resolves during this window, then recovery starts.")]
    public float decalStrikeDuration = 0.4f;

    [Tooltip("Delay into the strike before the hit check fires (sync with the " +
             "attack animation's impact moment).")]
    public float decalHitDelay = 0.1f;

    [Header("Animation Triggers (silently skipped if missing)")]
    public string swipeWindupTrigger = "Windup";
    public string swipeTrigger       = "Attk";
    public string decalWindupTrigger = "DecalWindup";
    public string decalTrigger       = "DecalAttk";

    [Header("Decal Indicator")]
    public Color windupColor  = new Color(1f, 0.15f, 0.1f, 0.55f);
    public Color executeColor = new Color(1f, 0.6f, 0f, 0.75f);

    [Tooltip("Vertical offset of the decal from the detected ground point.")]
    public float indicatorYOffset = 0.03f;

    // ── State machine ──
    private enum Phase { Idle, SwipeWindup, SwipeStrike, DecalWindup, DecalStrike, DecalRecover }
    private Phase _phase = Phase.Idle;

    private float _phaseEndTime;
    private float _strikeStartTime;
    private bool  _swipeHitResolved;
    private bool  _decalHitResolved;

    private float _lastSwipeTime = -999f;
    private float _lastDecalTime = -999f;
    private int   _basicsSinceDecal;

    // The entry that will be used for the NEXT decal attack. Precomputed so
    // CurrentAttackReach is stable while EnemyAI approaches.
    private int _pendingIndex = 0;
    private DecalAttackConfig _currentDecal;   // entry being executed right now

    // ── Indicator ──
    private GameObject   _indicator;
    private MeshFilter   _indicatorFilter;
    private MeshRenderer _indicatorRenderer;
    private Material     _indicatorMat;
    private AttackShape  _builtShape = (AttackShape)(-1);
    private float _builtA = -1f, _builtB = -1f;

    public override bool IsBusy => _phase != Phase.Idle;

    // Impact system: filler swipe windup is flinch-delayable; the rotation
    // moves are DECALS — windup delayable (capped), strike untouchable.
    public override bool IsInBasicWindup => _phase == Phase.SwipeWindup;
    public override bool IsInDecalAction => _phase == Phase.DecalWindup || _phase == Phase.DecalStrike;

    public override void DelayCurrentWindup(float seconds)
    {
        if (_phase != Phase.SwipeWindup && _phase != Phase.DecalWindup) return;
        _phaseEndTime += AccrueWindupDelay(seconds);
    }

    private BasicAttackConfig Basic => _sb != null ? _sb.basicAttack : null;

    private DecalAttackConfig Pending =>
        (_sb != null && _sb.hasDecalAttack &&
         _sb.decalAttacks != null && _sb.decalAttacks.Length > 0)
            ? _sb.decalAttacks[Mathf.Clamp(_pendingIndex, 0, _sb.decalAttacks.Length - 1)]
            : null;

    private bool ChainMode     => _sb != null && _sb.basicAttacksBetweenDecals > 0;
    private bool DecalUnlocked => !ChainMode || _basicsSinceDecal >= _sb.basicAttacksBetweenDecals;
    private bool DecalReady    => Pending != null && Time.time >= _lastDecalTime + Pending.cooldown;

    public override float CurrentAttackReach
    {
        get
        {
            float swipe = Basic != null ? Basic.reach : 1.8f;
            return (DecalReady && DecalUnlocked && Pending != null)
                ? Mathf.Max(swipe, Pending.Reach)
                : swipe;
        }
    }

    // ── Debug state balls ──

    public override CombatDebugState BasicDebugState
    {
        get
        {
            if (_phase == Phase.SwipeWindup) return CombatDebugState.Windup;
            if (_phase == Phase.SwipeStrike) return CombatDebugState.Strike;
            if (Basic != null &&
                Time.time < _lastSwipeTime + Basic.cooldown) return CombatDebugState.Cooldown;
            return CombatDebugState.None;
        }
    }

    public override CombatDebugState DecalDebugState
    {
        get
        {
            if (_phase == Phase.DecalWindup)  return CombatDebugState.Windup;
            if (_phase == Phase.DecalStrike)  return CombatDebugState.Strike;
            if (_phase == Phase.DecalRecover) return CombatDebugState.Recover;
            if (!DecalUnlocked || !DecalReady) return CombatDebugState.Cooldown;
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

            case Phase.DecalWindup:
            {
                float windup = _currentDecal != null ? _currentDecal.windupTime : 1f;
                float t = Mathf.Clamp01(1f - (_phaseEndTime - Time.time) / Mathf.Max(0.0001f, windup));
                Color c = windupColor;
                c.a *= t;
                SetIndicatorColor(c);
                SnapIndicatorToGround();

                if (Time.time >= _phaseEndTime) BeginDecalStrike();
                break;
            }

            case Phase.DecalStrike:
                if (!_decalHitResolved &&
                    Time.time >= _strikeStartTime + decalHitDelay)
                    ResolveDecalHit();

                if (Time.time >= _strikeStartTime + decalStrikeDuration)
                {
                    ShowIndicator(false);
                    _phase        = Phase.DecalRecover;
                    _phaseEndTime = Time.time +
                        (_currentDecal != null ? _currentDecal.recoverTime : 1f);
                }
                break;

            case Phase.DecalRecover:
                // Vulnerable — the punish window after the big move.
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

        // Chain rhythm: once N swipes are done, the NEXT attack is the decal.
        if (DecalUnlocked && DecalReady && Pending != null &&
            distToPlayer <= Pending.Reach + 0.35f)
        {
            StartDecal();
            return;
        }

        if (swipeReady && distToPlayer <= Basic.reach + 0.35f)
            StartSwipe();
    }

    // ── Swipe ──

    private void StartSwipe()
    {
        _lastSwipeTime = Time.time;
        _basicsSinceDecal++;
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

    // ── Decal attack ──

    private void StartDecal()
    {
        _currentDecal       = Pending;
        _lastDecalTime      = Time.time;
        _basicsSinceDecal   = 0;
        _phase              = Phase.DecalWindup;
        _phaseEndTime       = Time.time + _currentDecal.windupTime;
        _decalHitResolved   = false;
        _windupDelayAccrued = 0f;

        // Precompute which entry comes NEXT so reach is stable while EnemyAI
        // approaches for the next rotation step.
        AdvancePendingIndex();

        FaceAndLockOntoPlayer();
        BuildIndicator(_currentDecal);
        SnapIndicatorToGround();
        ShowIndicator(true);
        SetIndicatorColor(new Color(windupColor.r, windupColor.g, windupColor.b, 0f));
        SetTriggerIfPresent(decalWindupTrigger);
    }

    private void AdvancePendingIndex()
    {
        int count = _sb.decalAttacks != null ? _sb.decalAttacks.Length : 0;
        if (count <= 1) { _pendingIndex = 0; return; }

        switch (_sb.decalCycleMode)
        {
            case DecalCycleMode.Sequence:
                _pendingIndex = (_pendingIndex + 1) % count;
                break;
            case DecalCycleMode.Random:
                int j;
                do { j = Random.Range(0, count); }
                while (j == _pendingIndex);
                _pendingIndex = j;
                break;
            default:
                _pendingIndex = 0;
                break;
        }
    }

    private void BeginDecalStrike()
    {
        _phase           = Phase.DecalStrike;
        _strikeStartTime = Time.time;

        SetIndicatorColor(executeColor);
        SetTriggerIfPresent(decalTrigger);
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play(AudioManager.SoundType.EnemyAttk);
    }

    private void ResolveDecalHit()
    {
        if (_decalHitResolved || _currentDecal == null) return;
        _decalHitResolved = true;

        bool hit = _currentDecal.shape switch
        {
            AttackShape.Circle    => CheckCircleHit(_currentDecal),
            AttackShape.Rectangle => CheckRectHit(_currentDecal),
            _                     => CheckConeHit(_currentDecal),
        };

        if (hit)
            DamagePlayer(RollDamage(_currentDecal.damageMin, _currentDecal.damageMax), decalHit: true);
    }

    private bool CheckConeHit(DecalAttackConfig atk)
    {
        Vector3 o         = HitOriginPosition;
        float   halfAngle = atk.coneAngle * 0.5f;
        Vector3 bottom    = o - Vector3.up * (atk.height * 0.5f);
        Vector3 top       = o + Vector3.up * (atk.height * 0.5f);

        foreach (Collider hit in Physics.OverlapCapsule(bottom, top, atk.coneRadius, playerLayer))
        {
            Vector3 toTarget = hit.transform.position - o;
            toTarget.y = 0f;
            if (Vector3.Angle(transform.forward, toTarget.normalized) <= halfAngle)
                return true;
        }
        return false;
    }

    private bool CheckCircleHit(DecalAttackConfig atk)
    {
        Vector3 o      = HitOriginPosition;
        Vector3 bottom = o - Vector3.up * (atk.height * 0.5f);
        Vector3 top    = o + Vector3.up * (atk.height * 0.5f);
        return Physics.OverlapCapsule(bottom, top, atk.circleRadius, playerLayer).Length > 0;
    }

    private bool CheckRectHit(DecalAttackConfig atk)
    {
        Vector3 originXZ = new Vector3(transform.position.x, HitOriginPosition.y, transform.position.z);
        Vector3 center   = originXZ + transform.forward * (atk.rectLength * 0.5f);
        Vector3 halfExt  = new Vector3(atk.rectWidth * 0.5f, atk.height * 0.5f, atk.rectLength * 0.5f);
        return Physics.OverlapBox(center, halfExt, transform.rotation, playerLayer).Length > 0;
    }

    // ── Contract plumbing ──

    public override void OnAttackHitFrame()
    {
        if      (_phase == Phase.SwipeStrike) ResolveSwipeHit();
        else if (_phase == Phase.DecalStrike) ResolveDecalHit();
    }

    public override void OnAttackEnd()
    {
        if (_phase == Phase.SwipeStrike) _phase = Phase.Idle;
    }

    public override void CancelWindup()
    {
        if (_phase == Phase.SwipeWindup) _phase = Phase.Idle;
        else if (_phase == Phase.DecalWindup)
        {
            _phase = Phase.Idle;
            ShowIndicator(false);
        }
    }

    public override void CancelAttackState()
    {
        _phase = Phase.Idle;
        ShowIndicator(false);
    }

    // ── Indicator ──

    private void BuildIndicator(DecalAttackConfig atk)
    {
        if (atk == null) return;
        EnsureIndicatorObject();

        // Rebuild the mesh when the shape or its key dimensions changed.
        float a = atk.shape == AttackShape.Circle ? atk.circleRadius : atk.coneRadius;
        float b = atk.shape == AttackShape.Rectangle ? atk.rectLength : atk.coneAngle;
        if (atk.shape == AttackShape.Rectangle) a = atk.rectWidth;

        if (_builtShape != atk.shape ||
            !Mathf.Approximately(_builtA, a) || !Mathf.Approximately(_builtB, b))
        {
            _indicatorFilter.sharedMesh = atk.shape switch
            {
                AttackShape.Circle    => BuildDiskMesh(atk.circleRadius, 48),
                AttackShape.Rectangle => BuildRectMesh(atk.rectWidth, atk.rectLength),
                _                     => BuildConeMesh(atk.coneRadius, atk.coneAngle, 32),
            };
            _builtShape = atk.shape;
            _builtA = a;
            _builtB = b;
        }
    }

    private void SnapIndicatorToGround()
    {
        if (_indicator == null) return;
        _indicator.transform.position = GroundSnap(HitOrigin.position)
                                      + Vector3.up * indicatorYOffset;
        _indicator.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    private void EnsureIndicatorObject()
    {
        if (_indicator != null) return;

        _indicator = new GameObject("CaptainDecalIndicator");
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

    // ── Mesh builders ──

    private static Mesh BuildConeMesh(float radius, float angleDeg, int segments)
    {
        float halfAngle = angleDeg * 0.5f * Mathf.Deg2Rad;
        var verts = new Vector3[segments + 2];
        var tris  = new int[segments * 6];
        verts[0] = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float a = Mathf.Lerp(-halfAngle, halfAngle, t);
            verts[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
        }

        for (int i = 0; i < segments; i++)
        {
            int b = i * 6;
            tris[b + 0] = 0; tris[b + 1] = i + 1; tris[b + 2] = i + 2;
            tris[b + 3] = 0; tris[b + 4] = i + 2; tris[b + 5] = i + 1;
        }

        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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
        Transform point = attackPoint != null ? attackPoint
                        : attackOrigin != null ? attackOrigin
                        : transform;
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(point.position, hitRadius);
    }
}
