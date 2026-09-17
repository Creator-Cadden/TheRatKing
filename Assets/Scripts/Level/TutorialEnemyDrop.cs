using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Makes a spawned tutorial enemy DROP IN from above instead of popping into
/// existence. Added at runtime by TutorialManager when a wave spawns, so you
/// never have to put it on a prefab.
///
/// The problem it solves: a NavMeshAgent snaps its owner to the navmesh the
/// instant it's enabled, so you can't just spawn an enemy 8m up and let gravity
/// do the work. So: agent + AI + combat are held OFF, the transform is animated
/// straight down to the landing point, then everything is switched on in the
/// order EnemyAI expects (agent last, and only if the thing isn't already dead).
/// </summary>
[DisallowMultipleComponent]
public class TutorialEnemyDrop : MonoBehaviour
{
    /// <summary>Raised on this enemy once it has landed and the AI is live.</summary>
    public System.Action OnLanded;

    private NavMeshAgent    _agent;
    private EnemyAI         _ai;
    private EnemyCombatBase _combat;
    private Collider[]      _colliders;
    private EntityStats     _stats;

    private bool    _landed;
    private bool    _immovable;
    private Vector3 _anchor;

    /// <summary>True once the enemy has hit the floor and been switched on.</summary>
    public bool HasLanded => _landed;

    /// <summary>
    /// Called by TutorialManager right after Instantiate.
    /// </summary>
    /// <param name="groundPoint">Where the enemy should end up (the spawn point).</param>
    /// <param name="height">How far above that it starts.</param>
    /// <param name="speed">Fall speed in m/s.</param>
    /// <param name="passive">Never attack — a training dummy that can't hurt you.</param>
    /// <param name="stationary">Never move once it lands; it still turns and reacts.</param>
    /// <param name="immovable">Knockback moves it zero distance, and it's pinned to
    /// the landing spot as a last resort — a dummy can't be punched out of the room.</param>
    /// <param name="landFx">Optional prefab spawned at the landing point on impact.</param>
    public void Begin(Vector3 groundPoint, float height, float speed,
                      bool passive, bool stationary, bool immovable, GameObject landFx)
    {
        _immovable  = immovable;
        _anchor     = groundPoint;
        _agent     = GetComponent<NavMeshAgent>();
        _ai        = GetComponent<EnemyAI>();
        _combat    = GetComponent<EnemyCombatBase>();
        _stats     = GetComponent<EntityStats>();
        _colliders = GetComponentsInChildren<Collider>();

        // Hold everything. Order matters: the agent must go off BEFORE we move
        // the transform, or it will drag us back to the navmesh mid-fall.
        if (_agent  != null) _agent.enabled  = false;
        if (_ai     != null) _ai.enabled     = false;
        if (_combat != null) _combat.enabled = false;
        SetCollidersEnabled(false);

        // SNAP THE LANDING TO THE ACTUAL FLOOR.
        // Landing at the spawn point's exact Y is what made rats hover: a spawn
        // marker nudged even slightly above the ground left the enemy parked in
        // mid-air, and `immovable` then pinned it there for good.
        groundPoint = SnapToGround(groundPoint);
        _anchor     = groundPoint;

        transform.position = groundPoint + Vector3.up * Mathf.Max(0.1f, height);

        StartCoroutine(Fall(groundPoint, Mathf.Max(0.5f, speed), passive, stationary, landFx));
    }

    /// <summary>
    /// Finds the real floor under a point. Colliders are already disabled when
    /// this runs, so the enemy can't hit itself. Falls back to the navmesh, then
    /// to the point as given.
    /// </summary>
    private Vector3 SnapToGround(Vector3 point)
    {
        Vector3 from = point + Vector3.up * 3f;

        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 30f,
                            ~0, QueryTriggerInteraction.Ignore))
            return hit.point;

        if (NavMesh.SamplePosition(point, out NavMeshHit nav, 3f, NavMesh.AllAreas))
            return nav.position;

        Debug.LogWarning($"[TutorialEnemyDrop] No floor found under {point} — " +
                         "landing at the spawn point as given, which may leave " +
                         "the enemy in the air.");
        return point;
    }

    private IEnumerator Fall(Vector3 groundPoint, float speed, bool passive, bool stationary,
                             GameObject landFx)
    {
        // Accelerate a little on the way down — a constant-speed drop reads as
        // an elevator, not a landing.
        float v = speed * 0.45f;
        while (transform.position.y > groundPoint.y + 0.01f)
        {
            v = Mathf.MoveTowards(v, speed, speed * 2.5f * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, groundPoint, v * Time.deltaTime);
            yield return null;
        }
        transform.position = groundPoint;

        if (landFx != null) Instantiate(landFx, groundPoint, Quaternion.identity);
        CameraJuice.Shake(0.18f);   // small thud — remove if you don't want it

        SetCollidersEnabled(true);

        // Don't switch the brain on for something that died on the way down
        // (possible if the player shot it mid-drop).
        bool alive = _stats == null || !_stats.IsDead;
        if (alive)
        {
            if (_agent != null)
            {
                _agent.enabled = true;
                // Warp snaps cleanly onto the navmesh instead of sliding there.
                if (NavMesh.SamplePosition(groundPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    _agent.Warp(hit.position);
            }
            // The brain goes back on even for a dummy — EnemyAI is what runs the
            // hit reactions (flinch, stagger, the reaction debug text), and a
            // disabled MonoBehaviour can't start the coroutines those need. The
            // two flags below are what make it harmless and rooted instead.
            if (_ai != null)
            {
                _ai.doNotAttack  = passive;
                _ai.holdPosition = stationary;
                _ai.immovable    = _immovable;
                _ai.enabled      = true;
            }
            if (_combat != null) _combat.enabled = true;
        }

        _landed = true;
        OnLanded?.Invoke();
    }

    /// <summary>
    /// The backstop. EnemyAI.immovable already swallows knockback, but a dummy can
    /// also be nudged by the player's capsule, an anti-camping slide, or a physics
    /// push — and any of those, repeated, walks it out of the room. Pinning it
    /// after everything else has moved is the one place nothing can argue with.
    /// </summary>
    void LateUpdate()
    {
        if (!_immovable || !_landed) return;
        if (_stats != null && _stats.IsDead) return;   // let corpses fall over

        Vector3 p = transform.position;
        if ((p - _anchor).sqrMagnitude < 0.0004f) return;   // already there
        transform.position = _anchor;
    }

    private void SetCollidersEnabled(bool on)
    {
        if (_colliders == null) return;
        foreach (var c in _colliders)
        {
            // Never touch trigger volumes (hurtboxes/aggro spheres) — only the
            // solid body colliders would let the player shove a falling rat.
            if (c == null || c.isTrigger) continue;
            c.enabled = on;
        }
    }
}
