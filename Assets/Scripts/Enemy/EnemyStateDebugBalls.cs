using UnityEngine;

/// <summary>
/// DEBUG TOOL — floats up to two small colored balls above an enemy showing
/// its combat state, read from any EnemyCombatBase script(s) on the same
/// GameObject. BASIC attack ball sits slightly LEFT, DECAL attack ball
/// slightly RIGHT — each toggleable separately.
/// Colors: yellow = windup, red = strike, cyan = recover (punish window),
/// green = on cooldown. Hidden when that attack is ready/idle.
/// Add alongside GruntCombat / ToughCombat / BalloonCombat / EnemyCombat.
/// Turn both toggles OFF (or remove the component) for real builds.
/// </summary>
public class EnemyStateDebugBalls : MonoBehaviour
{
    [Header("Toggles")]
    [Tooltip("Show the BASIC (Tier 1) attack state ball — left side.")]
    public bool showBasicBall = true;

    [Tooltip("Show the DECAL (Tier 2) attack state ball — right side.")]
    public bool showDecalBall = true;

    [Header("Placement")]
    [Tooltip("Height above the enemy's pivot.")]
    public float height = 2.2f;

    [Tooltip("Sideways offset — basic ball at −this, decal ball at +this, " +
             "relative to the enemy's facing.")]
    public float sideOffset = 0.35f;

    [Tooltip("Ball diameter in world units.")]
    public float ballSize = 0.25f;

    [Header("Colors")]
    public Color windupColor   = Color.yellow;
    public Color strikeColor   = Color.red;
    public Color recoverColor  = Color.cyan;
    public Color cooldownColor = Color.green;

    private EnemyCombatBase[] _combats;
    private EntityStats       _stats;

    private GameObject _basicBall, _decalBall;
    private Material   _basicMat,  _decalMat;

    void Awake()
    {
        _combats = GetComponents<EnemyCombatBase>();
        _stats   = GetComponent<EntityStats>();
    }

    void OnDestroy()
    {
        if (_basicBall != null) Destroy(_basicBall);
        if (_decalBall != null) Destroy(_decalBall);
        if (_basicMat  != null) Destroy(_basicMat);
        if (_decalMat  != null) Destroy(_decalMat);
    }

    void LateUpdate()
    {
        bool dead = _stats != null && _stats.IsDead;

        // An enemy can have multiple combat scripts in theory — take the most
        // interesting (non-None) state reported for each tier.
        var basicState = EnemyCombatBase.CombatDebugState.None;
        var decalState = EnemyCombatBase.CombatDebugState.None;

        if (!dead && _combats != null)
        {
            foreach (var c in _combats)
            {
                if (c == null) continue;
                if (basicState == EnemyCombatBase.CombatDebugState.None)
                    basicState = c.BasicDebugState;
                if (decalState == EnemyCombatBase.CombatDebugState.None)
                    decalState = c.DecalDebugState;
            }
        }

        UpdateBall(ref _basicBall, ref _basicMat, "BasicStateBall",
                   showBasicBall, basicState, -sideOffset);
        UpdateBall(ref _decalBall, ref _decalMat, "DecalStateBall",
                   showDecalBall, decalState, +sideOffset);
    }

    private void UpdateBall(ref GameObject ball, ref Material mat, string name,
                            bool enabled, EnemyCombatBase.CombatDebugState state,
                            float side)
    {
        if (!enabled || state == EnemyCombatBase.CombatDebugState.None)
        {
            if (ball != null) ball.SetActive(false);
            return;
        }

        if (ball == null)
        {
            ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = name;
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(transform, worldPositionStays: false);
            ball.transform.localScale = Vector3.one * ballSize;

            Shader shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color");
            mat = new Material(shader);

            var rend = ball.GetComponent<MeshRenderer>();
            rend.material          = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows    = false;
        }

        ball.SetActive(true);
        ball.transform.position = transform.position
                                + Vector3.up * height
                                + transform.right * side;

        mat.color = state switch
        {
            EnemyCombatBase.CombatDebugState.Windup   => windupColor,
            EnemyCombatBase.CombatDebugState.Strike   => strikeColor,
            EnemyCombatBase.CombatDebugState.Recover  => recoverColor,
            _                                         => cooldownColor,
        };
    }
}
