using UnityEngine;

/// <summary>
/// Emits dust puffs for movement — running, rolling, jumping, and landing —
/// by reacting to PlayerMovement's events. Assign a dust ParticleSystem to each
/// slot (a PREFAB is fine — the script instantiates its own runtime copies, so
/// Emit works). The systems should have Play On Awake OFF and Looping OFF, and
/// Simulation Space = World. Put this on the Player.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class MovementParticles : MonoBehaviour
{
    [Header("Dust systems (prefab or scene instance)")]
    public ParticleSystem runDust;
    public ParticleSystem rollDust;
    public ParticleSystem jumpPuff;
    public ParticleSystem landPuff;

    [Header("Spawn origin")]
    [Tooltip("An empty at the rat's feet. Falls back to just below the player if empty.")]
    public Transform feet;
    public float feetDropIfUnset = 0.5f;

    [Header("Run dust")]
    public float runMinSpeed = 3f;
    public float runInterval = 0.18f;
    public int   runBurst = 2;

    [Header("Jump / Roll bursts")]
    public int jumpBurst = 10;
    public int rollBurst = 14;

    [Header("Land burst (scales with fall speed)")]
    public float minLandSpeed = 3f;
    public int   landBurstMin = 8;
    public int   landBurstMax = 30;
    public float landHardSpeed = 18f;

    private PlayerMovement _move;
    private float          _lastRunEmit;

    // Runtime instances (so Emit works even when a PREFAB was assigned).
    private ParticleSystem _runPS, _rollPS, _jumpPS, _landPS;

    void Awake() => _move = GetComponent<PlayerMovement>();

    void Start()
    {
        _runPS  = SpawnInstance(runDust);
        _rollPS = SpawnInstance(rollDust);
        _jumpPS = SpawnInstance(jumpPuff);
        _landPS = SpawnInstance(landPuff);
    }

    void OnEnable()
    {
        if (_move == null) return;
        _move.OnJumped      += HandleJump;
        _move.OnLanded      += HandleLand;
        _move.OnRollStarted += HandleRoll;
    }

    void OnDisable()
    {
        if (_move == null) return;
        _move.OnJumped      -= HandleJump;
        _move.OnLanded      -= HandleLand;
        _move.OnRollStarted -= HandleRoll;
    }

    void Update()
    {
        if (_move == null || _runPS == null) return;

        if (_move.IsGrounded && !_move.IsRolling && _move.HorizontalSpeed >= runMinSpeed
            && Time.time >= _lastRunEmit + runInterval)
        {
            _lastRunEmit = Time.time;
            EmitAt(_runPS, runBurst);
        }
    }

    private void HandleJump() => EmitAt(_jumpPS, jumpBurst);
    private void HandleRoll() => EmitAt(_rollPS, rollBurst);

    private void HandleLand(float fallSpeed)
    {
        if (fallSpeed < minLandSpeed) return;
        int count = Mathf.RoundToInt(Mathf.Lerp(landBurstMin, landBurstMax,
                        Mathf.InverseLerp(minLandSpeed, landHardSpeed, fallSpeed)));
        EmitAt(_landPS, count);
    }

    /// <summary>Instantiate a runtime copy so Emit is legal (prefabs can't be emitted).</summary>
    private ParticleSystem SpawnInstance(ParticleSystem src)
    {
        if (src == null) return null;
        ParticleSystem inst = Instantiate(src, transform);   // parented so it cleans up with the player
        inst.gameObject.name = src.name + " (runtime)";
        inst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return inst;
    }

    private void EmitAt(ParticleSystem ps, int count)
    {
        if (ps == null || count <= 0) return;
        ps.transform.position = FeetPos();
        ps.Emit(count);
    }

    private Vector3 FeetPos()
        => feet != null ? feet.position : transform.position + Vector3.down * feetDropIfUnset;
}
