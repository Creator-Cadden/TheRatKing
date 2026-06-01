using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// "Kill all the enemies in this room and the gate opens" controller.
///
/// Drop on an empty GameObject anywhere in the scene. Either drag enemies
/// into the Tracked Enemies list, or enable Auto Populate and the script
/// will scan for enemies within a radius at Start.
///
/// When every tracked enemy is dead, the Gate Transform animates
/// downward (default) or deactivates, freeing the path to whatever the
/// gate was blocking (typically a LevelTransition trigger).
///
/// Usage:
///   1. Add this component to an empty GameObject named e.g. "RoomEncounter".
///   2. Drag every enemy in the room into Tracked Enemies (the easy way)
///      OR set Auto Populate = on and parent the empty above the room.
///   3. Drag the gate model/parent into Gate Transform.
///   4. Choose Lower (default) or Deactivate as the Open Action.
///   5. (Optional) wire <see cref="onAllDefeated"/> to a UnityEvent for extra
///      reactions (sound effect, particle burst, log, etc.).
/// </summary>
public class EncounterController : MonoBehaviour
{
    public enum OpenAction
    {
        [InspectorName("Lower (slide downward)")]
        LowerDown,
        [InspectorName("Deactivate (SetActive false)")]
        Deactivate,
        [InspectorName("Custom UnityEvent only (don't touch the gate)")]
        EventOnly,
    }

    // ─────────────────────────────────────────
    [Header("Tracked Enemies")]
    [Tooltip("Drag every enemy that belongs to this encounter here. " +
             "When all are dead, the gate opens. Empty slots are ignored.")]
    public List<EntityStats> trackedEnemies = new List<EntityStats>();

    [Header("Auto-Populate (optional)")]
    [Tooltip("If on, the script finds every EntityStats inside autoPopulateRadius " +
             "at Start and adds them to trackedEnemies.")]
    public bool autoPopulate = false;

    [Tooltip("Radius around THIS GameObject used for auto-populate, in world units.")]
    public float autoPopulateRadius = 25f;

    [Tooltip("Layer mask for the auto-populate sphere check.")]
    public LayerMask autoPopulateLayer = ~0;

    // ─────────────────────────────────────────
    [Header("Gate")]
    [Tooltip("The gate model/parent that physically blocks the way. " +
             "Animated or deactivated when the encounter is cleared.")]
    public Transform gateTransform;

    [Tooltip("What happens to the gate when all enemies are defeated.")]
    public OpenAction openAction = OpenAction.LowerDown;

    [Tooltip("How far down (in world units) the gate slides. Only used " +
             "when Open Action = Lower.")]
    public float lowerDistance = 5f;

    [Tooltip("How long the lower animation takes, in seconds. " +
             "Only used when Open Action = Lower.")]
    public float lowerDuration = 1.5f;

    [Tooltip("How the lower animation eases. Defaults to a smooth ease-in-out.")]
    public AnimationCurve lowerCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("If on AND Open Action = Lower, deactivate the gate GameObject " +
             "after the slide finishes (saves draw calls + physics queries).")]
    public bool deactivateAfterLower = true;

    // ─────────────────────────────────────────
    [Header("Events")]
    [Tooltip("Fires once per enemy killed in this encounter. " +
             "Argument = remaining alive count.")]
    public UnityEvent<int> onEnemyKilled;

    [Tooltip("Fires when every tracked enemy is dead. Wire VFX, sound, " +
             "screen shake, save trigger, anything here.")]
    public UnityEvent onAllDefeated;

    // ─────────────────────────────────────────
    [Header("Debug")]
    public bool verbose = false;

    // ─────────────────────────────────────────
    // Runtime state
    // ─────────────────────────────────────────
    private int _aliveCount;
    private bool _resolved;             // true once onAllDefeated has fired
    private Coroutine _lowerRoutine;

    public int AliveCount => _aliveCount;
    public int TotalCount => trackedEnemies != null ? trackedEnemies.Count : 0;
    public bool IsCleared => _resolved;

    // ─────────────────────────────────────────

    void Start()
    {
        if (autoPopulate) AutoPopulate();
        Bind();
    }

    void OnDestroy()
    {
        Unbind();
    }

    private void AutoPopulate()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position,
                                                autoPopulateRadius,
                                                autoPopulateLayer);
        foreach (var c in hits)
        {
            var es = c.GetComponentInParent<EntityStats>();
            if (es == null || es.isPlayer) continue;
            if (!trackedEnemies.Contains(es))
                trackedEnemies.Add(es);
        }

        if (verbose)
            Debug.Log($"[EncounterController] Auto-populated {trackedEnemies.Count} enemies.");
    }

    private void Bind()
    {
        _aliveCount = 0;
        foreach (var es in trackedEnemies)
        {
            if (es == null) continue;
            if (es.IsDead)  continue;
            es.onDeath.AddListener(OnEnemyDied);
            _aliveCount++;
        }

        if (verbose)
            Debug.Log($"[EncounterController] {_aliveCount}/{trackedEnemies.Count} enemies alive at start.");

        // Edge case: zero alive at start (somebody pre-cleared the room).
        if (_aliveCount <= 0) Resolve();
    }

    private void Unbind()
    {
        if (trackedEnemies == null) return;
        foreach (var es in trackedEnemies)
        {
            if (es == null) continue;
            es.onDeath.RemoveListener(OnEnemyDied);
        }
    }

    // ─────────────────────────────────────────

    private void OnEnemyDied()
    {
        _aliveCount = Mathf.Max(0, _aliveCount - 1);
        onEnemyKilled?.Invoke(_aliveCount);

        if (verbose)
            Debug.Log($"[EncounterController] Enemy died — {_aliveCount} remaining.");

        if (_aliveCount <= 0) Resolve();
    }

    private void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        if (verbose) Debug.Log("[EncounterController] All enemies down — opening gate.");

        onAllDefeated?.Invoke();
        ExecuteOpenAction();
    }

    private void ExecuteOpenAction()
    {
        if (gateTransform == null && openAction != OpenAction.EventOnly)
        {
            Debug.LogWarning("[EncounterController] gateTransform is null — " +
                             "set it in the Inspector or switch Open Action to Event Only.");
            return;
        }

        switch (openAction)
        {
            case OpenAction.LowerDown:
                if (_lowerRoutine != null) StopCoroutine(_lowerRoutine);
                _lowerRoutine = StartCoroutine(LowerGateRoutine());
                break;

            case OpenAction.Deactivate:
                gateTransform.gameObject.SetActive(false);
                break;

            case OpenAction.EventOnly:
                // onAllDefeated already fired — nothing else to do
                break;
        }
    }

    private IEnumerator LowerGateRoutine()
    {
        Vector3 start  = gateTransform.position;
        Vector3 end    = start + Vector3.down * lowerDistance;

        float elapsed = 0f;
        while (elapsed < lowerDuration)
        {
            elapsed += Time.deltaTime;
            float t = lowerCurve.Evaluate(Mathf.Clamp01(elapsed / lowerDuration));
            gateTransform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }
        gateTransform.position = end;

        if (deactivateAfterLower)
            gateTransform.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────
    // Gizmos
    // ─────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (autoPopulate)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, autoPopulateRadius);
        }

        // Draw a line from this controller to every tracked enemy
        Gizmos.color = new Color(1f, 1f, 0f, 0.7f);
        if (trackedEnemies != null)
        {
            foreach (var es in trackedEnemies)
            {
                if (es == null) continue;
                Gizmos.DrawLine(transform.position, es.transform.position);
                Gizmos.DrawWireSphere(es.transform.position, 0.4f);
            }
        }

        // Draw a line from controller to gate
        if (gateTransform != null)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.7f);
            Gizmos.DrawLine(transform.position, gateTransform.position);
            if (openAction == OpenAction.LowerDown)
            {
                Vector3 endPos = gateTransform.position + Vector3.down * lowerDistance;
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.3f);
                Gizmos.DrawLine(gateTransform.position, endPos);
                Gizmos.DrawWireCube(endPos, Vector3.one);
            }
        }
    }
}
