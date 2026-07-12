using UnityEngine;

/// <summary>
/// Enemy attack logic: windup telegraph (cone / circle / rectangle indicator),
/// then a shape hit-check against the player. Shape + numbers come from the
/// EnemyStatBlock; Captains override the shape each attack via CaptainCombat.
/// EnemyAI reads CurrentAttackReach to know how close to chase.
/// </summary>
public class EnemyCombat : MonoBehaviour
{
    [Header("Combat Setup")]
    public LayerMask  playerLayer;
    public Transform  attackOrigin;

    [Header("Attack Indicator")]
    [Tooltip("Color shown during the windup telegraph. Alpha fades in over attackWindupTime.")]
    public Color windupColor  = new Color(1f, 0.15f, 0.1f, 0.55f);

    [Tooltip("Color flashed at the moment the hit fires.")]
    public Color executeColor = new Color(1f, 0.6f, 0f, 0.75f);

    [Header("Debug")]
    public bool verboseAttackLog = false;

    // ── References ─────────────────────────────────────────────────────
    private EntityStats    _selfStats;
    private EntityStats    _playerStats;
    private EnemyStatBlock _sb;
    private Animator       _animator;
    private Transform      _player;

    // ── State ──────────────────────────────────────────────────────────
    private bool  _isAttacking;
    private bool  _isWindingUp;
    private float _attackStartTime;
    private float _windupStartTime;
    private float _lastAttackTime;

    // ── Rotation lock ──────────────────────────────────────────────────
    private Quaternion _lockedRotation;
    public  bool       IsRotationLocked => _isWindingUp || _isAttacking;

    // ── Runtime shape override ─────────────────────────────────────────
    // Lets a controller (e.g. CaptainCombat) swap which attack shape this
    // EnemyCombat uses on a per-attack basis without editing the SO.
    // Set to null to fall back to the stat block's configured shape.
    public AttackShape? RuntimeShapeOverride;

    /// <summary>
    /// The shape this enemy will actually use for its next/current attack.
    /// </summary>
    public AttackShape ActiveShape =>
        RuntimeShapeOverride.HasValue ? RuntimeShapeOverride.Value :
        (_sb != null ? _sb.attackShape : AttackShape.Cone);

    /// <summary>
    /// Reach distance for the currently-active attack shape.
    /// EnemyAI reads this instead of EnemyStatBlock.AttackReach so dynamic
    /// shape switching (Captain) actually changes the chase/attack range.
    /// </summary>
    public float CurrentAttackReach
    {
        get
        {
            if (_sb == null) return 1.8f;
            return ActiveShape switch
            {
                AttackShape.Circle    => _sb.circleRadius,
                AttackShape.Rectangle => _sb.rectLength,
                _                     => _sb.attackRadius,   // Cone
            };
        }
    }

    /// <summary>
    /// Fires the moment a new windup is about to start. Lets controllers
    /// (CaptainCombat, future variants) pick a fresh shape just-in-time.
    /// </summary>
    public System.Action OnBeforeWindup;

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
    public bool       IsBusy            => _isAttacking || _isWindingUp;
    public Vector3    HitOriginPosition => HitOrigin.position;
    private Transform HitOrigin         => attackOrigin != null ? attackOrigin : transform;

    void Awake()
    {
        _selfStats = GetComponent<EntityStats>();
        _animator  = GetComponentInChildren<Animator>();
        _sb        = _selfStats != null ? _selfStats.enemyStatBlock : null;
        _mpb       = new MaterialPropertyBlock();
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

    public void ConfigureRuntime(Transform player, EntityStats playerStats,
        Transform fallbackOrigin, LayerMask fallbackLayer, bool verbose)
    {
        _player          = player;
        _playerStats     = playerStats;
        verboseAttackLog = verbose;

        if (attackOrigin      == null && fallbackOrigin != null)   attackOrigin = fallbackOrigin;
        if (playerLayer.value == 0    && fallbackLayer.value != 0) playerLayer  = fallbackLayer;

        if (_selfStats == null) _selfStats = GetComponent<EntityStats>();
        if (_animator  == null) _animator  = GetComponentInChildren<Animator>();
        if (_sb == null && _selfStats != null) _sb = _selfStats.enemyStatBlock;
    }

    public void Tick()
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
            float t = Mathf.Clamp01((Time.time - _windupStartTime) / _sb.attackWindupTime);
            Color c = windupColor;
            c.a *= t;
            SetIndicatorColor(c);

            if (Time.time >= _windupStartTime + _sb.attackWindupTime)
                ExecuteAttack();
        }
    }

    public void TryStartAttack(float distToPlayer)
    {
        if (_sb == null || _player == null)                   return;
        if (_isAttacking || _isWindingUp)                     return;
        if (Time.time < _lastAttackTime + _sb.attackCooldown) return;

        // Pick the shape for THIS attack first — controllers (e.g. CaptainCombat)
        // hook in here to cycle/randomize before reach is evaluated.
        OnBeforeWindup?.Invoke();

        if (distToPlayer > CurrentAttackReach + 0.35f)        return;

        _lastAttackTime  = Time.time;
        _isWindingUp     = true;
        _windupStartTime = Time.time;

        Vector3 lookDir = _player.position - transform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir);
        _lockedRotation = transform.rotation;

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

    public void OnAttackHitFrame()
    {
        if (_sb == null || _playerStats == null || _selfStats == null) return;

        AttackShape shape = ActiveShape;
        bool hit = shape switch
        {
            AttackShape.Circle    => CheckCircleHit(),
            AttackShape.Rectangle => CheckRectHit(),
            _                     => CheckConeHit(),
        };

        if (!hit) return;

        int damage = Random.Range(_sb.attackDamageMin, _sb.attackDamageMax + 1)
                   + _selfStats.Strength * _sb.attackStrengthBonus;

        _playerStats.TakeDamage(damage);

        if (verboseAttackLog)
            Debug.Log($"[EnemyCombat] {gameObject.name} hit player for {damage} ({shape})");
    }

    public void OnAttackEnd()
    {
        _isAttacking = false;
        ShowIndicator(false);
    }

    public void CancelAttackState()
    {
        _isWindingUp = false;
        _isAttacking = false;
        ShowIndicator(false);
    }

    public void CancelWindup()
    {
        _isWindingUp = false;
        if (!_isAttacking) ShowIndicator(false);
    }

    // ── Hit detection ──

    private bool CheckConeHit()
    {
        Vector3 o         = HitOriginPosition;
        float   halfAngle = _sb.attackAngle * 0.5f;
        Vector3 bottom    = o - Vector3.up * (_sb.attackHeight * 0.5f);
        Vector3 top       = o + Vector3.up * (_sb.attackHeight * 0.5f);

        foreach (Collider hit in Physics.OverlapCapsule(bottom, top, _sb.attackRadius, playerLayer))
        {
            Vector3 toTarget = hit.transform.position - o;
            toTarget.y = 0f;
            if (Vector3.Angle(transform.forward, toTarget.normalized) <= halfAngle)
                return true;
        }
        return false;
    }

    private bool CheckCircleHit()
    {
        Vector3 o      = HitOriginPosition;
        Vector3 bottom = o - Vector3.up * (_sb.attackHeight * 0.5f);
        Vector3 top    = o + Vector3.up * (_sb.attackHeight * 0.5f);
        return Physics.OverlapCapsule(bottom, top, _sb.circleRadius, playerLayer).Length > 0;
    }

    private bool CheckRectHit()
    {
        Vector3 originXZ = new Vector3(transform.position.x, HitOriginPosition.y, transform.position.z);
        Vector3 center   = originXZ + transform.forward * (_sb.rectLength * 0.5f);
        Vector3 halfExt  = new Vector3(_sb.rectWidth * 0.5f, _sb.attackHeight * 0.5f, _sb.rectLength * 0.5f);
        return Physics.OverlapBox(center, halfExt, transform.rotation, playerLayer).Length > 0;
    }

    // ── Indicator ──

    private void BuildIndicator()
    {
        if (_sb == null) return;

        EnsureIndicatorObject();

        AttackShape shape = ActiveShape;

        bool needsRebuild = _lastBuiltShape != shape
            || !Mathf.Approximately(_cachedRadius,  _sb.attackRadius)
            || !Mathf.Approximately(_cachedAngle,   _sb.attackAngle)
            || !Mathf.Approximately(_cachedCircleR, _sb.circleRadius)
            || !Mathf.Approximately(_cachedRectW,   _sb.rectWidth)
            || !Mathf.Approximately(_cachedRectL,   _sb.rectLength);

        if (needsRebuild)
        {
            _indicatorFilter.sharedMesh = shape switch
            {
                AttackShape.Circle    => BuildDiskMesh(_sb.circleRadius, 48),
                AttackShape.Rectangle => BuildRectMesh(_sb.rectWidth, _sb.rectLength),
                _                     => BuildConeMesh(_sb.attackRadius, _sb.attackAngle, 32),
            };

            _lastBuiltShape = shape;
            _cachedRadius   = _sb.attackRadius;
            _cachedAngle    = _sb.attackAngle;
            _cachedCircleR  = _sb.circleRadius;
            _cachedRectW    = _sb.rectWidth;
            _cachedRectL    = _sb.rectLength;
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
