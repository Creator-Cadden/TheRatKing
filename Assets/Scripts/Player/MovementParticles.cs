using UnityEngine;

/// <summary>
/// Emits dust puffs for movement — running, rolling, jumping, and landing —
/// by reacting to PlayerMovement's events. Assign a dust ParticleSystem to each
/// slot (see the setup notes in chat); they're triggered via Emit(), so the
/// systems should have Play On Awake OFF and Looping OFF. Put this on the Player.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class MovementParticles : MonoBehaviour
{
    [Header("Dust systems (assign your puff prefabs / instances)")]
    public ParticleSystem runDust;
    public ParticleSystem rollDust;
    public ParticleSystem jumpPuff;
    public ParticleSystem landPuff;

    [Header("Spawn origin")]
    [Tooltip("Where puffs spawn — an empty at the rat's feet. Falls back to just " +
             "below the player if left empty.")]
    public Transform feet;
    public float feetDropIfUnset = 0.5f;

    [Header("Run dust")]
    [Tooltip("Min horizontal speed before running kicks up dust.")]
    public float runMinSpeed = 3f;
    [Tooltip("Seconds between run puffs.")]
    public float runInterval = 0.18f;
    public int   runBurst = 2;

    [Header("Jump / Roll bursts")]
    public int jumpBurst = 10;
    public int rollBurst = 14;

    [Header("Land burst (scales with fall speed)")]
    [Tooltip("Below this fall speed, no landing dust (a gentle step).")]
    public float minLandSpeed = 3f;
    public int   landBurstMin = 8;
    public int   landBurstMax = 30;
    [Tooltip("Fall speed that reaches the max burst.")]
    public float landHardSpeed = 18f;

    private PlayerMovement _move;
    private float          _lastRunEmit;

    void Awake() => _move = GetComponent<PlayerMovement>();

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
        if (_move == null || runDust == null) return;

        // Run dust: grounded, not rolling, moving fast enough — on an interval.
        if (_move.IsGrounded && !_move.IsRolling && _move.HorizontalSpeed >= runMinSpeed
            && Time.time >= _lastRunEmit + runInterval)
        {
            _lastRunEmit = Time.time;
            EmitAt(runDust, runBurst);
        }
    }

    private void HandleJump() => EmitAt(jumpPuff, jumpBurst);
    private void HandleRoll() => EmitAt(rollDust, rollBurst);

    private void HandleLand(float fallSpeed)
    {
        if (fallSpeed < minLandSpeed) return;
        int count = Mathf.RoundToInt(Mathf.Lerp(landBurstMin, landBurstMax,
                        Mathf.InverseLerp(minLandSpeed, landHardSpeed, fallSpeed)));
        EmitAt(landPuff, count);
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
