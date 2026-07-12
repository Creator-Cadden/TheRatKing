using UnityEngine;

/// <summary>
/// Captain enemy variant.
/// </summary>
[RequireComponent(typeof(EnemyCombat))]
public class CaptainCombat : MonoBehaviour
{
    public enum CycleMode { Forward, Random }

    [Header("Captain — Shape Switching")]
    [Tooltip("Forward = Cone → Circle → Rectangle → Cone... in a strict loop.\n" +
             "Random  = picks a different shape than the last one each attack.")]
    public CycleMode mode = CycleMode.Forward;

    [Tooltip("Verbose console logging when the shape changes.")]
    public bool verbose = false;

    private static readonly AttackShape[] Sequence =
    {
        AttackShape.Cone,
        AttackShape.Circle,
        AttackShape.Rectangle,
    };

    private EnemyCombat _combat;
    // Start one before the first index so the first OnBeforeWindup tick
    // advances us to Sequence[0] (Cone) for the very first attack.
    private int _index = -1;

    void Awake()
    {
        _combat = GetComponent<EnemyCombat>();
        if (_combat == null)
        {
            Debug.LogError($"[CaptainCombat] {gameObject.name} needs an EnemyCombat component.");
            enabled = false;
            return;
        }

        // Hook into EnemyCombat's pre-windup callback. EnemyCombat fires this
        // the moment a new attack is about to start so we can pick the shape
        // before reach is evaluated.
        _combat.OnBeforeWindup = PickNextShape;
    }

    void OnDestroy()
    {
        // Clear the hook so EnemyCombat doesn't hold a dead reference.
        if (_combat != null) _combat.OnBeforeWindup = null;
    }

    private void PickNextShape()
    {
        AttackShape next;

        if (mode == CycleMode.Random)
        {
            // Pick anything except the current one (avoids dull repeats).
            int j;
            do { j = Random.Range(0, Sequence.Length); }
            while (Sequence.Length > 1 && _index >= 0 && j == _index);
            _index = j;
        }
        else
        {
            // _index starts at -1 → first call lands on 0 (Cone), then 1, 2, 0, 1, 2…
            _index = (_index + 1) % Sequence.Length;
            if (_index < 0) _index += Sequence.Length;  // safety
        }

        next = Sequence[_index];
        _combat.RuntimeShapeOverride = next;

        if (verbose)
            Debug.Log($"[CaptainCombat] {gameObject.name} next attack shape → {next}");
    }
}
