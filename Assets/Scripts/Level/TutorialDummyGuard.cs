using UnityEngine;

/// <summary>
/// Makes a tutorial enemy unkillable while it's still teaching something.
/// Added at runtime by TutorialManager when a wave is marked invulnerable.
///
/// It doesn't block damage — hits land, damage numbers pop, the health bar
/// drops, hit reactions play — it just pins the floor at 1 HP so the drill can't
/// end early. TutorialManager calls <see cref="Release"/> on the step that's
/// meant to finish it off, and from then on it dies normally, which is what pays
/// out XP and gold.
/// </summary>
[DisallowMultipleComponent]
public class TutorialDummyGuard : MonoBehaviour
{
    [Tooltip("Health it can never drop below while guarded.")]
    public int floor = 1;

    private EntityStats _stats;

    void Awake()
    {
        _stats = GetComponent<EntityStats>();
        if (_stats != null) _stats.HealthFloor = Mathf.Max(1, floor);
    }

    void OnDestroy()
    {
        // Paranoia: these are spawned copies that get destroyed with the wave,
        // but never leave a floor set on something that outlives the tutorial.
        if (_stats != null) _stats.HealthFloor = 0;
    }

    /// <summary>Stop protecting it — the next hit kills normally.</summary>
    public void Release()
    {
        if (_stats != null) _stats.HealthFloor = 0;
    }
}
