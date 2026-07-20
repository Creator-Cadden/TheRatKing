using UnityEngine;

/// <summary>
/// Grunt combat — Tier 1 only: no decal, no shapes. When in reach: short windup
/// (Windup anim trigger + body language IS the telegraph), then a bite/scratch
/// that hits a small sphere at <see cref="attackPoint"/>. Low damage, short
/// cooldown, repeat. Parent the attack point to the jaw/claw bone so the hit
/// volume travels with the attack animation (Souls-style moving hitbox).
/// Tuning (damage, windup, cooldown, reach) lives on the EnemyStatBlock asset.
/// </summary>
public class GruntCombat : EnemyCombatBase
{
    [Header("Attack Point (movable hit volume)")]
    [Tooltip("Where the bite/scratch actually lands. Assign a child transform — " +
             "ideally parented to the head/claw bone so the volume moves with the " +
             "attack animation. Falls back to Attack Origin, then the enemy root.")]
    public Transform attackPoint;

    [Tooltip("Radius of the hit sphere around the attack point.")]
    public float hitRadius = 0.9f;

    [Header("Animation")]
    [Tooltip("Trigger fired at windup start (rear-back pose). Silently ignored " +
             "if the animator doesn't have it yet.")]
    public string windupTrigger = "Windup";

    [Tooltip("Trigger fired when the strike begins after the windup.")]
    public string attackTrigger = "Attk";

    [Tooltip("If > 0 and no OnAttackHitFrame animation event arrives, the hit " +
             "auto-resolves this many seconds into the strike. Lets the grunt " +
             "work before animation events are authored. Set 0 once the attack " +
             "clip has a proper hit-frame event.")]
    public float fallbackHitDelay = 0.2f;

    [Tooltip("If > 0 and no OnAttackEnd animation event arrives, the strike ends " +
             "this many seconds after it starts (roughly the attack animation " +
             "length). Without this, un-evented attacks wait for the full " +
             "attackAnimTimeout (~2.5s) and the grunt feels frozen between bites. " +
             "Set 0 once the attack clip has a proper end event.")]
    public float fallbackEndDelay = 0.6f;

    [Header("Gizmos")]
    public bool showHitGizmo = true;

    // (State ball visuals moved to the reusable EnemyStateDebugBalls component —
    //  add that alongside this script to see windup/strike/recover/cooldown.)

    // ── State machine ──
    private enum Phase { Idle, WindingUp, Striking }
    private Phase _phase = Phase.Idle;

    private float _windupEndTime;
    private float _strikeStartTime;
    private bool  _hitResolved;

    private EnemyAI _ai;   // for post-attack-pause state reporting

    public override bool IsBusy => _phase != Phase.Idle;

    // Impact system: grunt has no decal; its basic windup can be flinch-delayed.
    public override bool IsInBasicWindup => _phase == Phase.WindingUp;

    public override void DelayCurrentWindup(float seconds)
    {
        if (_phase != Phase.WindingUp) return;
        _windupEndTime += AccrueWindupDelay(seconds);
    }

    // All tuning comes from the stat block's Basic Attack section.
    private BasicAttackConfig Basic => _sb != null ? _sb.basicAttack : null;

    public override float CurrentAttackReach => Basic != null ? Basic.reach : 1.6f;

    private Transform HitPoint =>
        attackPoint != null ? attackPoint : HitOrigin;

    public override void Tick()
    {
        if (_sb == null) return;

        HoldLockedRotation();

        switch (_phase)
        {
            case Phase.WindingUp:
                if (Time.time >= _windupEndTime)
                    BeginStrike();
                break;

            case Phase.Striking:
                // Fallback hit if no animation event is wired yet.
                if (!_hitResolved && fallbackHitDelay > 0f &&
                    Time.time >= _strikeStartTime + fallbackHitDelay)
                    OnAttackHitFrame();

                // Fallback end if no OnAttackEnd animation event is wired yet —
                // otherwise the grunt sits busy until attackAnimTimeout and the
                // whole attack cycle balloons to ~4s.
                if (fallbackEndDelay > 0f &&
                    Time.time >= _strikeStartTime + fallbackEndDelay)
                {
                    OnAttackEnd();
                    break;
                }

                // Safety net — never get stuck if the anim never fires OnAttackEnd.
                if (Time.time >= _strikeStartTime + _sb.attackAnimTimeout)
                {
                    if (verboseAttackLog)
                        Debug.LogWarning($"[GruntCombat] {gameObject.name} strike timed out.");
                    OnAttackEnd();
                }
                break;
        }
    }

    public override void TryStartAttack(float distToPlayer)
    {
        if (_sb == null || _player == null || Basic == null)     return;
        if (!_sb.hasBasicAttack)                                 return;
        if (_phase != Phase.Idle)                                return;
        if (Time.time < _lastAttackTime + Basic.cooldown)        return;
        if (distToPlayer > CurrentAttackReach + 0.35f)           return;

        _lastAttackTime      = Time.time;
        _phase               = Phase.WindingUp;
        _windupEndTime       = Time.time + Basic.windupTime;
        _windupDelayAccrued  = 0f;

        FaceAndLockOntoPlayer();
        SetTriggerIfPresent(windupTrigger);
    }

    private void BeginStrike()
    {
        _phase           = Phase.Striking;
        _strikeStartTime = Time.time;
        _hitResolved     = false;

        SetTriggerIfPresent(attackTrigger);
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play(AudioManager.SoundType.EnemyAttk);
    }

    public override void OnAttackHitFrame()
    {
        if (_hitResolved || _phase != Phase.Striking) return;
        _hitResolved = true;

        if (PlayerOverlapsSphere(HitPoint.position, hitRadius))
            DamagePlayer(RollBasicAttackDamage(), decalHit: false);
    }

    public override void OnAttackEnd()
    {
        _phase = Phase.Idle;
    }

    public override void CancelWindup()
    {
        if (_phase == Phase.WindingUp)
            _phase = Phase.Idle;
    }

    public override void CancelAttackState()
    {
        _phase = Phase.Idle;
    }

    protected override void Awake()
    {
        base.Awake();
        _ai = GetComponent<EnemyAI>();
    }

    public override CombatDebugState BasicDebugState
    {
        get
        {
            if (_selfStats != null && _selfStats.IsDead)     return CombatDebugState.None;
            if (_phase == Phase.WindingUp)                   return CombatDebugState.Windup;
            if (_phase == Phase.Striking)                    return CombatDebugState.Strike;
            if (_ai != null && _ai.IsInPostAttackPause)      return CombatDebugState.Recover;
            if (Basic != null &&
                Time.time < _lastAttackTime + Basic.cooldown) return CombatDebugState.Cooldown;
            return CombatDebugState.None;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!showHitGizmo) return;

        Transform point = attackPoint != null ? attackPoint
                        : attackOrigin != null ? attackOrigin
                        : transform;

        // Red sphere = where the bite lands. Move the attack point / adjust
        // hitRadius until this sits right at the claw's strike position.
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(point.position, hitRadius);
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.15f);
        Gizmos.DrawSphere(point.position, hitRadius);
    }
}
