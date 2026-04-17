using UnityEngine;

public class EnemyCombat : MonoBehaviour
{
    [Header("Combat Setup")]
    public LayerMask playerLayer;
    public Transform attackOrigin;

    [Header("Attack Indicator")]
    public Color windupColor  = new Color(1f, 0.15f, 0.1f, 0.55f);
    public Color executeColor = new Color(1f, 0.6f,  0f,   0.75f);

    [Header("Debug")]
    public bool verboseAttackLog = false;

    private EntityStats    _selfStats;
    private EntityStats    _playerStats;
    private EnemyStatBlock _sb;
    private Animator       _animator;
    private Transform      _player;

    private bool  _isAttacking;
    private bool  _isWindingUp;
    private float _attackStartTime;
    private float _windupStartTime;
    private float _lastAttackTime;

    private GameObject            _indicator;
    private MeshFilter            _indicatorFilter;
    private MeshRenderer          _indicatorRenderer;
    private MaterialPropertyBlock _mpb;

    private float _cachedRadius;
    private float _cachedAngle;
    private float _cachedHeight;

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

        // Safety timeout
        if (_isAttacking && Time.time >= _attackStartTime + _sb.attackAnimTimeout)
        {
            Debug.LogWarning($"[EnemyCombat] {gameObject.name} attack timed out.");
            _isAttacking = false;
            ShowIndicator(false);
        }

        // Windup phase
        if (_isWindingUp)
        {
            float t = (Time.time - _windupStartTime) / _sb.attackWindupTime;
            t = Mathf.Clamp01(t);

            // Fade in only (NO scaling)
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
        if (distToPlayer > _sb.stopRange + 0.4f)              return;
        if (Time.time < _lastAttackTime + _sb.attackCooldown) return;

        _lastAttackTime  = Time.time;
        _isWindingUp     = true;
        _windupStartTime = Time.time;

        BuildAndShowIndicator();
        SetIndicatorColor(windupColor);
    }

    private void ExecuteAttack()
    {
        if (_player == null) return;

        _isWindingUp     = false;
        _isAttacking     = true;
        _attackStartTime = Time.time;

        // Face player
        Vector3 lookDir = _player.position - transform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        // Flash execute color
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

        if (!CheckConeHit()) return;

        int damage = Random.Range(_sb.attackDamageMin, _sb.attackDamageMax + 1)
                   + _selfStats.Strength * _sb.attackStrengthBonus;

        _playerStats.TakeDamage(damage);

        if (verboseAttackLog)
            Debug.Log($"[EnemyCombat] {gameObject.name} hit player for {damage}");
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

    private bool CheckConeHit()
    {
        Vector3 o      = HitOrigin.position;
        Vector3 bottom = o - Vector3.up * (_sb.attackHeight * 0.5f);
        Vector3 top    = o + Vector3.up * (_sb.attackHeight * 0.5f);

        foreach (Collider hit in Physics.OverlapCapsule(bottom, top, _sb.attackRadius, playerLayer))
        {
            Vector3 dir   = (hit.transform.position - o).normalized;
            float   angle = Vector3.Angle(transform.forward, dir);
            if (angle <= _sb.attackAngle * 0.5f) return true;
        }
        return false;
    }

    private void BuildAndShowIndicator()
    {
        if (_sb == null) return;

        if (_indicator == null)
        {
            _indicator = new GameObject("AttackIndicator");
            _indicator.transform.SetParent(HitOrigin, false);
            _indicator.transform.localPosition = Vector3.zero;
            _indicator.transform.localRotation = Quaternion.identity;

            _indicatorFilter   = _indicator.AddComponent<MeshFilter>();
            _indicatorRenderer = _indicator.AddComponent<MeshRenderer>();

            Material mat = new Material(FindIndicatorShader());
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend",   0);
            mat.SetFloat("_ZWrite",  0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;

            _indicatorRenderer.material = mat;
            _indicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _indicatorRenderer.receiveShadows    = false;

            _cachedRadius = -1f;
        }

        bool needsRebuild = !Mathf.Approximately(_cachedRadius, _sb.attackRadius)
                         || !Mathf.Approximately(_cachedAngle,  _sb.attackAngle)
                         || !Mathf.Approximately(_cachedHeight, _sb.attackHeight);

        if (needsRebuild)
        {
            _indicatorFilter.sharedMesh = BuildConePrismSolidMesh(
                _sb.attackRadius, _sb.attackAngle, _sb.attackHeight, 32);

            _cachedRadius = _sb.attackRadius;
            _cachedAngle  = _sb.attackAngle;
            _cachedHeight = _sb.attackHeight;
        }

        ShowIndicator(true);
    }

    private void ShowIndicator(bool visible)
    {
        if (_indicator != null) _indicator.SetActive(visible);
    }

    private void SetIndicatorColor(Color c)
    {
        if (_indicatorRenderer == null) return;
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_Color",     c);
        _indicatorRenderer.SetPropertyBlock(_mpb);
    }

    private static Mesh BuildConePrismSolidMesh(float radius, float angleDeg, float height, int segments)
    {
        float halfH = height * 0.5f;
        float halfAngle = angleDeg * 0.5f * Mathf.Deg2Rad;

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        Vector3[] topArc    = new Vector3[segments + 1];
        Vector3[] bottomArc = new Vector3[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float a = Mathf.Lerp(-halfAngle, halfAngle, t);

            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

            Vector3 forwardOffset = Vector3.forward * (radius * 0.5f);

            topArc[i]    = dir * radius + Vector3.up * halfH - forwardOffset;
            bottomArc[i] = dir * radius - Vector3.up * halfH - forwardOffset;
        }

        for (int i = 0; i < segments; i++)
        {
            int start = verts.Count;

            verts.Add(bottomArc[i]);
            verts.Add(bottomArc[i + 1]);
            verts.Add(topArc[i]);
            verts.Add(topArc[i + 1]);

            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 1);

            tris.Add(start + 1);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Shader FindIndicatorShader()
    {
        return Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Standard");
    }
}