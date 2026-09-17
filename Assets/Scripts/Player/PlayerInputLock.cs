using UnityEngine;

/// <summary>
/// A global gate on player input, so scripted moments can take the controls
/// away without disabling components.
///
/// Why not just disable PlayerCombat / PlayerMovement: those components own
/// state the rest of the game reads every frame (grounded, stamina, aim yaw,
/// the camera priorities). Switching them off mid-frame leaves the camera
/// stuck in aim, the animator mid-swing and the controller convinced it's
/// still airborne. Gating at the input callbacks instead means the player
/// simply stops issuing commands while everything else keeps running.
///
/// Used by the tutorial to stop the player swinging at a dummy while an
/// explanation is on screen, and to hold the pad completely still during a
/// freeze-and-show beat. Nothing else needs to know it exists — the flags
/// default to false and are cleared on load.
/// </summary>
public static class PlayerInputLock
{
    /// <summary>Blocks attack presses. Releases still pass through, so a
    /// half-drawn bow can always finish its shot rather than sticking.</summary>
    public static bool AttackLocked  { get; set; }

    /// <summary>Blocks aiming, and drops any aim already held.</summary>
    public static bool AimLocked     { get; set; }

    /// <summary>Zeroes the move stick. Checked every frame, not just on the
    /// callback, because a key already held down sends no new event.</summary>
    public static bool MoveLocked    { get; set; }

    public static bool JumpLocked    { get; set; }
    public static bool SprintLocked  { get; set; }
    public static bool DashLocked    { get; set; }

    /// <summary>True if anything at all is currently held.</summary>
    public static bool AnyLocked =>
        AttackLocked || AimLocked || MoveLocked || JumpLocked || SprintLocked || DashLocked;

    /// <summary>Lock or unlock every action at once.</summary>
    public static void SetAll(bool locked)
    {
        AttackLocked = AimLocked = MoveLocked = locked;
        JumpLocked   = SprintLocked = DashLocked = locked;
    }

    /// <summary>
    /// Everything back to the player. Call this on scene load, on death and
    /// whenever a scripted sequence ends — including the failure paths, or a
    /// lock taken out for one beat silently follows the player into real play.
    /// </summary>
    public static void ClearAll() => SetAll(false);

    /// <summary>
    /// Locks the two things that make a player accidentally wreck a lesson:
    /// swinging at the thing being explained, and aiming the camera away from it.
    /// </summary>
    public static void LockCombat(bool locked)
    {
        AttackLocked = locked;
        AimLocked    = locked;
    }

    // Domain-reload-safe, same as GameFreeze. With "Enter Play Mode → Reload
    // Domain" turned off, a lock left set by the previous play session would
    // otherwise start the next one with the controls dead.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ClearAll();
}
