using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// DEBUG TOOL — kills every living enemy in the scene so you can test level
/// progression (gates, transitions, boss bar) without fighting through.
/// Add to any GameObject (e.g. the same one as WeaponSwapDebug) and press the
/// kill key in Play mode, or wire KillAll() to a UI Button's OnClick.
/// Disable the component (checkbox) or remove it for shipping builds.
/// </summary>
public class KillAllEnemiesDebug : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Key that kills every living enemy in the scene (Input System).")]
    public Key killKey = Key.K;

    [Header("Options")]
    [Tooltip("If off, EnemyXPDrop is stripped before the kill so the player " +
             "gains no XP from debug kills. Leave on to also test XP/level-up flow.")]
    public bool grantXP = true;

    [Tooltip("Log how many enemies were killed.")]
    public bool verbose = true;

    void Update()
    {
        if (Keyboard.current == null) return;
        if (Keyboard.current[killKey].wasPressedThisFrame)
            KillAll();
    }

    /// <summary>
    /// Kills every non-player EntityStats via TakeDamage, so all normal death
    /// flow runs: onDeath events, EncounterController gate counting, death fade.
    /// Also works on the boss. Public so a UI Button can call it directly.
    /// </summary>
    public void KillAll()
    {
        EntityStats[] all = FindObjectsByType<EntityStats>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        int killed = 0;
        foreach (EntityStats es in all)
        {
            if (es == null || es.isPlayer || es.IsDead) continue;

            if (!grantXP)
            {
                // DestroyImmediate (not Destroy) — Destroy is deferred to end of
                // frame, so the XP listener would still fire when TakeDamage kills
                // the enemy below. Immediate destruction runs EnemyXPDrop.OnDestroy
                // now, which unsubscribes it from onDeath first. OK in a debug tool.
                EnemyXPDrop xp = es.GetComponent<EnemyXPDrop>();
                if (xp != null) DestroyImmediate(xp);
            }

            es.TakeDamage(999999);
            killed++;
        }

        if (verbose)
            Debug.Log($"[KillAllEnemiesDebug] Killed {killed} enemies.");
    }
}
