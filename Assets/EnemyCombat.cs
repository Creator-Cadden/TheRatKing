using UnityEngine;

public class EnemyCombat : MonoBehaviour
{
    [Header("Combat Setup")]
    public LayerMask playerLayer;
    public Transform attackOrigin;

    [Header("Attack Timing")]
    [Tooltip("Safety fallback if OnAttackEnd() Animation Event is never fired.")]
    public float attackAnimTimeout = 2.5f;
    [Tooltip("Delay before enemy commits to attack. Gives player dodge window.")]
    public float attackWindupTime = 0.3f;

    [Header("Attack Indicator")]
    public Color attackIndicatorColor = new Color(1f, 0.15f, 0.1f, 0.55f);
    public float attackIndicatorHeight = 0.01f;

    [Header("Debug")]
    public bool verboseAttackLog = false;

    private EntityStats _selfStats;
    private EntityStats _playerStats;
    private EnemyStatBlock _sb;
    private Animator _animator;
    private Transform _player;

    private bool _isAttacking;
    private bool _isWindingUp;
    private float _attackStartTime;
    private float _windupStartTime;
    private float _lastAttackTime;

    private GameObject _attackIndicator;
    private Renderer _attackIndicatorRenderer;
    private MeshFilter _attackIndicatorMeshFilter;
    private float _lastIndicatorRadius;
    private float _lastIndicatorAngle;

    public bool IsBusy => _isAttacking || _isWindingUp;
    public Vector3 HitOriginPosition => HitOrigin.position;
    private Transform HitOrigin => attackOrigin != null ? attackOrigin : transform;

    void Awake()
    {
        _selfStats = GetComponent<EntityStats>();
        _animator = GetComponentInChildren<Animator>();
        _sb = _selfStats != null ? _selfStats.enemyStatBlock : null;
    }

    public void ConfigureRuntime(Transform player, EntityStats playerStats, Transform fallbackAttackOrigin, LayerMask fallbackPlayerLayer, bool verbose)
    {
        _player = player;
        _playerStats = playerStats;
        verboseAttackLog = verbose;

        if (attackOrigin == null && fallbackAttackOrigin != null)
            attackOrigin = fallbackAttackOrigin;

        if (playerLayer.value == 0 && fallbackPlayerLayer.value != 0)
            playerLayer = fallbackPlayerLayer;

        if (_selfStats == null) _selfStats = GetComponent<EntityStats>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_sb == null && _selfStats != null) _sb = _selfStats.enemyStatBlock;
    }

    public void Tick()
    {
        if (_sb == null) return;

        if (_isAttacking && Time.time >= _attackStartTime + attackAnimTimeout)
        {
            Debug.LogWarning($"[EnemyCombat] {gameObject.name} attack timed out after {attackAnimTimeout}s.");
            _isAttacking = false;
            SetAttackIndicatorVisible(false);
        }

        if (_isWindingUp)
        {
            UpdateAttackIndicator();
            if (Time.time >= _windupStartTime + attackWindupTime)
                ExecuteAttack();
        }
    }

    public void TryStartAttack(float distToPlayer)
    {
        if (_sb == null || _player == null) return;
        if (_isAttacking || _isWindingUp) return;

        float attackThreshold = _sb.stopRange + 0.4f;
        if (distToPlayer > attackThreshold) return;
        if (Time.time < _lastAttackTime + _sb.attackCooldown) return;

        _lastAttackTime = Time.time;
        _isWindingUp = true;
        _windupStartTime = Time.time;

        SetAttackIndicatorVisible(true);
        UpdateAttackIndicator();
    }

    private void ExecuteAttack()
    {
        if (_player == null) return;

        _isWindingUp = false;
        _isAttacking = true;
        _attackStartTime = Time.time;

        Vector3 lookDir = (_player.position - transform.position);
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        bool hasAttackTrigger = false;
        if (_animator != null)
        {
            foreach (var param in _animator.parameters)
            {
                if (param.name == "Attk") { hasAttackTrigger = true; break; }
            }
        }

        if (hasAttackTrigger)
        {
            _animator.SetTrigger("Attk");
        }
        else
        {
            OnAttackHitFrame();
            _isAttacking = false;
            SetAttackIndicatorVisible(false);
        }
    }

    public void OnAttackHitFrame()
    {
        if (_sb == null || _playerStats == null || _selfStats == null) return;

        bool hit = _sb.attackShape == AttackShape.Sphere
            ? CheckSphereHit()
            : CheckConeHit();

        if (!hit) return;

        int baseDamage = Random.Range(_sb.attackDamageMin, _sb.attackDamageMax + 1);
        int strengthBonus = _selfStats.Strength * _sb.attackStrengthBonus;
        int totalDamage = baseDamage + strengthBonus;
        _playerStats.TakeDamage(totalDamage);

        if (verboseAttackLog)
            Debug.Log($"[EnemyCombat] {gameObject.name} hit player for {totalDamage}");
    }

    public void OnAttackEnd()
    {
        _isAttacking = false;
        SetAttackIndicatorVisible(false);
    }

    public void CancelAttackState()
    {
        _isWindingUp = false;
        _isAttacking = false;
        SetAttackIndicatorVisible(false);
    }

    public void CancelWindup()
    {
        _isWindingUp = false;
        if (!_isAttacking) SetAttackIndicatorVisible(false);
    }

    private bool CheckSphereHit()
    {
        Collider[] hits = Physics.OverlapSphere(HitOrigin.position, _sb.attackRadius, playerLayer);
        return hits.Length > 0;
    }

    private bool CheckConeHit()
    {
        Collider[] hits = Physics.OverlapSphere(HitOrigin.position, _sb.attackRadius, playerLayer);
        foreach (Collider hit in hits)
        {
            Vector3 dir = (hit.transform.position - HitOrigin.position).normalized;
            float angle = Vector3.Angle(transform.forward, dir);
            if (angle <= _sb.attackAngle / 2f) return true;
        }
        return false;
    }

    private void EnsureAttackIndicator()
    {
        if (_attackIndicator != null) return;

        _attackIndicator = new GameObject("AttackIndicator");
        _attackIndicator.transform.SetParent(transform, true);
        _attackIndicatorMeshFilter = _attackIndicator.AddComponent<MeshFilter>();
        _attackIndicatorRenderer = _attackIndicator.AddComponent<MeshRenderer>();
        _attackIndicatorRenderer.material = new Material(FindIndicatorShader());
        _attackIndicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _attackIndicatorRenderer.receiveShadows = false;
        _attackIndicator.SetActive(false);
    }

    private void SetAttackIndicatorVisible(bool visible)
    {
        EnsureAttackIndicator();
        _attackIndicator.SetActive(visible);
    }

    private void UpdateAttackIndicator()
    {
        if (_attackIndicator == null || !_attackIndicator.activeSelf || _sb == null) return;

        Transform source = attackOrigin != null ? attackOrigin : transform;
        Vector3 pos = source.position + Vector3.up * attackIndicatorHeight;
        _attackIndicator.transform.position = pos;

        Vector3 forward = Vector3.ProjectOnPlane(source.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        float yaw = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y;
        _attackIndicator.transform.rotation = Quaternion.Euler(90f, yaw, 0f);

        float radius = Mathf.Max(0.2f, _sb.attackRadius);
        float angle = _sb.attackShape == AttackShape.Cone
            ? Mathf.Clamp(_sb.attackAngle, 10f, 170f)
            : 95f;

        if (!Mathf.Approximately(_lastIndicatorRadius, radius) || !Mathf.Approximately(_lastIndicatorAngle, angle))
        {
            _attackIndicatorMeshFilter.sharedMesh = BuildConeSectorMesh(radius, angle, 28);
            _lastIndicatorRadius = radius;
            _lastIndicatorAngle = angle;
        }

        _attackIndicatorRenderer.material.color = attackIndicatorColor;
    }

    private static Shader FindIndicatorShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        return shader;
    }

    private static Mesh BuildConeSectorMesh(float radius, float angleDeg, int segments)
    {
        Mesh mesh = new Mesh();
        int vertCount = segments + 2;
        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;
        float halfAngle = angleDeg * 0.5f;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float a = Mathf.Lerp(-halfAngle, halfAngle, t) * Mathf.Deg2Rad;
            vertices[i + 1] = new Vector3(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, 0f);
        }

        int tri = 0;
        for (int i = 0; i < segments; i++)
        {
            triangles[tri++] = 0;
            triangles[tri++] = i + 1;
            triangles[tri++] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
