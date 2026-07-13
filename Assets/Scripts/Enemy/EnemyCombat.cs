using UnityEngine;

/// <summary>
/// Decal (Tier 2) enemy combat: telegraphed shape indicator during windup,
/// then a shape hit-check. Fully data-driven from the stat block's Decal
/// Attacks list + cycle mode (Captain = 3 entries + Sequence, Tough dash =
/// 1 rect entry). Used by Tough/Captain prefabs; enemies with basic attacks
/// use their own EnemyCombatBase subclass (GruntCombat) instead or as well.
/// CaptainCombat is DEPRECATED — the cycle now lives in the stat block.
/// </summary>
public class EnemyCombat : EnemyCombatBase
{
    [Header("Attack Indicator")]
    [Tooltip("Color shown during the windup telegraph. Alpha fades in over attackWindupTime.")]
    public Color windupColor  = new Color(1f, 0.15f, 0.1f, 0.55f);

    [Tooltip("Color flashed at the moment the hit fires.")]
    public Color executeColor = new Color(1f, 0.6f, 0f, 0.75f);

    // ── State ──────────────────────────────────────────────────────────
    // (playerLayer, attackOrigin, verboseAttackLog, stat/player refs,
    //  _lastAttackTime and _lockedRotation now live in EnemyCombatBase.)
    private bool  _isAttacking;
    private bool  _isWindingUp;
    private float _attackStartTime;
    private float _windupStartTime;
    private float _nextAttackReadyTime;   // per-entry cooldown gate

    public override bool IsRotationLocked => _isWindingUp || _isAttacking;

    // ── Decal attack selection ─────────────────────────────────────────
    // The stat block holds the decal attack LIST + cycle mode. This tracks
    // which entry is active. Cycle advances just before each windup.
    private int _decalIndex = -1;

    /// <summary>The decal attack entry this enemy will use next/currently.
    /// Null when the stat block has no decal attacks configured.</summary>
    public DecalAttackConfig CurrentDecal
    {
        get
        {
            if (_sb == null || !_sb.hasDecalAttack ||
                _sb.decalAttacks == null || _sb.decalAttacks.Length == 0)
                return null;
            int i = Mathf.Clamp(Mathf.Max(0, _decalIndex), 0, _sb.decalAttacks.Length - 1);
            return _sb.decalAttacks[i];
        }
    }

    /// <summary>Shape of the current decal entry (Cone fallback).</summary>
    public AttackShape ActiveShape => CurrentDecal?.shape ?? AttackShape.Cone;

    /// <summary>Reach of the current decal entry — EnemyAI closes to this.</summary>
    public override float CurrentAttackReach => CurrentDecal?.Reach ?? 1.8f;

    /// <summary>Advance the decal index according to the stat block's cycle mode.
    /// Called just before each windup so reach is evaluated on the NEW entry.</summary>
    private void PickNextDecal()
    {
        int count = _sb.decalAttacks != null ? _sb.decalAttacks.Length : 0;
        if (count <= 1) { _decalIndex = 0; return; }

        switch (_sb.decalCycleMode)
        {
            case DecalCycleMode.Sequence:
                _decalIndex = (_decalIndex + 1) % count;
                break;
            case DecalCycleMode.Random:
                int j;
                do { j = Random.Range(0, count); }
                while (j == _decalIndex);
                _decalIndex = j;
                break;
            default:                       // None
                _decalIndex = 0;
                break;
        }

        if (verboseAttackLog)
            Debug.Log($"[EnemyCombat] {gameObject.name} next decal → " +
                      $"{_sb.decalAttacks[_decalIndex].name} ({_sb.decalAttacks[_decalIndex].shape})");
    }

    // ── Indicator ──────────────────────────────────────────────────────
    private GameObject            _indicator;
    private MeshFilter            _indicatorFilter;
    private MeshRenderer          _indicatorRenderer;
    private MaterialPropertyBlock _mpb;
    private Material              _indicatorMat;

    // ── Mesh cache ─────────────────────────────────────────────────────
    private AttackShape _lastBuiltShape = (AttackShape)(-1);
    private float _cachedRadius;
    private float _cachedAngle;
    private float _cachedCircleR;
    private float _cachedRectW;
    private float _cachedRectL;

    // ── Public ─────────────────────────────────────────────────────────
    public override bool IsBusy => _isAttacking || _isWindingUp;

    protected override void Awake()
    {
        base.Awake();
        _mpb = new MaterialPropertyBlock();
    }

    void OnDestroy()
    {
        if (_indicator != null)    Destroy(_indicator);
        if (_indicatorMat != null) Destroy(_indicatorMat);
    }

    void OnDrawGizmosSelected()
    {
        var sb = _sb;
        if (sb == null)
        {
            var stats = GetComponent<EntityStats>();
            if (stats != null) sb = stats.enemyStatBlock;
        }
        if (sb == null) return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, sb.aggroRange);

        Gizmos.color = new Color(1f, 0.9f, 0f, 0.45f);
        Gizmos.DrawWireSphere(transform.position, sb.stopRange);
    }

    public override void Tick()
    {
        if (_sb == null) return;

        if (_isAttacking && Time.time >= _attackStartTime + _sb.attackAnimTimeout)
        {
            Debug.LogWarning($"[EnemyCombat] {gameObject.name} attack timed out.");
            _isAttacking = false;
            ShowIndicator(false);
        }

        if (IsRotationLocked)
            transform.rotation = _lockedRotation;

        if ((_isWindingUp || _isAttacking) && _indicator != null && _indicator.activeSelf)
            SnapIndicatorToOrigin();

        if (_isWindingUp)
        {
            float windup = CurrentDecal != null ? CurrentDecal.windupTime : 0.6f;
            float t = Mathf.Clamp01((Time.time - _windupStartTime) / Mathf.Max(0.0001f, windup));
            Color c = windupColor;
            c.a *= t;
            SetIndicatorColor(c);

            if (Time.time >= _windupStartTime + windup)
                ExecuteAttack();
        }
    }

    public override void TryStartAttack(float distToPlayer)
    {
        if (_sb == null || _player == null)                   return;
        if (!_sb.hasDecalAttack)                              return;
        if (_isAttacking || _isWindingUp)                     return;
        if (Time.time < _nextAttackReadyTime)                 return;

        // Advance the cycle FIRST so reach is evaluated on the new entry.
        PickNextDecal();
        if (CurrentDecal == null)                             return;

        if (distToPlayer > CurrentAttackReach + 0.35f)        return;

        // Cooldown is per-entry — a big cone can have a longer recovery than
        // the quick rect lunge. Timed from windup start (same as before).
        _nextAttackReadyTime = Time.time + CurrentDecal.cooldown;
        _isWindingUp     = true;
        _windupStartTime = Time.time;

        FaceAndLockOntoPlayer();

        BuildIndicator();
        SnapIndicatorToOrigin();
        ShowIndicator(true);
        SetIndicatorColor(new Color(windupColor.r, windupColor.g, windupColor.b, 0f));
    }

    private void ExecuteAttack()
    {
        if (_player == null) return;

        _isWindingUp     = false;
        _isAttacking     = true;
        _attackStartTime = Time.time;

        _animator.SetTrigger("Bite");

        AudioManager.Instance.Play(AudioManager.SoundType.EnemyAttk);

        SetIndicatorColor(executeColor);

        bool hasAttackTrigger = false;
        if (_animator != null)
            foreach (var p in _animator.parameters)
                if (p.name == "Attk") { hasAttackTrigger = true; break; }

        if (hasAttackTrigger)
        {
            _animator.SetTrigger("Attk");
        }
        else
        {
            OnAttackHitFrame();
            _isAttacking = false;
            ShowIndicator(false);
        }
    }

    public override void OnAttackHitFrame()
    {
        if (_sb == null || _playerStats == null || _selfStats == null) return;

        DecalAttackConfig atk = CurrentDecal;
        if (atk == null) return;

        bool hit = atk.shape switch
        {
            AttackShape.Circle    => CheckCircleHit(atk),
            AttackShape.Rectangle => CheckRectHit(atk),
            _                     => CheckConeHit(atk),
        };

        if (!hit) return;

        int damage = RollDamage(atk.damageMin, atk.damageMax);
        _playerStats.TakeDamage(damage);

        if (verboseAttackLog)
            Debug.Log($"[EnemyCombat] {gameObject.name} hit player for {damage} ({atk.name}/{atk.shape})");
    }

    public override void OnAttackEnd()
    {
        _isAttacking = false;
        ShowIndicator(false);
    }

    public override void CancelAttackState()
    {
        _isWindingUp = false;
        _isAttacking = false;
        ShowIndicator(false);
    }

    public override void CancelWindup()
    {
        _isWindingUp = false;
        if (!_isAttacking) ShowIndicator(false);
    }

    // ── Hit detection ──

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

    // ── Indicator ──

    private void BuildIndicator()
    {
        DecalAttackConfig atk = CurrentDecal;
        if (atk == null) return;

        EnsureIndicatorObject();

        bool needsRebuild = _lastBuiltShape != atk.shape
            || !Mathf.Approximately(_cachedRadius,  atk.coneRadius)
            || !Mathf.Approximately(_cachedAngle,   atk.coneAngle)
            || !Mathf.Approximately(_cachedCircleR, atk.circleRadius)
            || !Mathf.Approximately(_cachedRectW,   atk.rectWidth)
            || !Mathf.Approximately(_cachedRectL,   atk.rectLength);

        if (needsRebuild)
        {
            _indicatorFilter.sharedMesh = atk.shape switch
            {
                AttackShape.Circle    => BuildDiskMesh(atk.circleRadius, 48),
                AttackShape.Rectangle => BuildRectMesh(atk.rectWidth, atk.rectLength),
                _                     => BuildConeMesh(atk.coneRadius, atk.coneAngle, 32),
            };

            _lastBuiltShape = atk.shape;
            _cachedRadius   = atk.coneRadius;
            _cachedAngle    = atk.coneAngle;
            _cachedCircleR  = atk.circleRadius;
            _cachedRectW    = atk.rectWidth;
            _cachedRectL    = atk.rectLength;
        }
    }

    private void SnapIndicatorToOrigin()
    {
        if (_indicator == null) return;
        _indicator.transform.position = new Vector3(transform.position.x, HitOrigin.position.y, transform.position.z);
        _indicator.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    private void EnsureIndicatorObject()
    {
        if (_indicator != null) return;

        _indicator = new GameObject("AttackIndicator");
        _indicator.transform.localScale = Vector3.one;

        _indicatorFilter   = _indicator.AddComponent<MeshFilter>();
        _indicatorRenderer = _indicator.AddComponent<MeshRenderer>();

        // ── Shader selection ─────────────────────────────────────────────
        // "Sprites/Default" is ALWAYS included in URP builds — it never
        // strips and supports alpha transparency out of the box.
        // "Universal Render Pipeline/Unlit" goes purple in builds unless
        // manually added to Project Settings > Graphics > Always Included Shaders.
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            // Fallback chain for edge cases
            shader = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Unlit/Transparent")
                  ?? Shader.Find("Unlit/Color");
        }

        _indicatorMat            = new Material(shader);
        _indicatorMat.renderQueue = 3000;

        // Sprites/Default reads _Color for tint and handles blending itself.
        // Set an initial color so it isn't invisible before the first Tick.
        _indicatorMat.color = new Color(windupColor.r, windupColor.g, windupColor.b, 0f);

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
        if (_indicatorRenderer == null || _indicatorMat == null) return;

        // Sprites/Default uses material.color directly — set it on the
        // live instance so the change is visible immediately in builds.
        _indicatorMat.color = c;

        // Also push via property block for URP/Unlit fallback path.
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_Color",     c);
        _indicatorRenderer.SetPropertyBlock(_mpb);
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
}
