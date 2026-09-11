using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reference-counted game freeze. Any system that needs the game paused
/// (pause menu, stats panel, …) calls <see cref="Request"/> with a unique owner
/// string; <c>Time.timeScale</c> is 0 while ANY owner holds a freeze and returns
/// to 1 only when the LAST one releases.
///
/// This is what lets menus STACK safely: with "Escape always opens pause", you
/// can open the stats panel (freeze "statmenu"), press Escape to open pause
/// (freeze "pause"), then close pause again — the game stays frozen because the
/// stats panel still holds its freeze. If both menus set Time.timeScale directly,
/// closing one would wrongly un-freeze while the other is still open.
///
/// Static + self-contained; no GameObject needed. Death and scene loads call
/// <see cref="ReleaseAll"/> so a freeze can never leak across scenes.
/// </summary>
public static class GameFreeze
{
    private static readonly HashSet<string> _owners = new HashSet<string>();

    /// <summary>True while at least one owner is holding the game frozen.</summary>
    public static bool IsFrozen => _owners.Count > 0;

    /// <summary>Register that 'owner' needs the game frozen.</summary>
    public static void Request(string owner)
    {
        if (string.IsNullOrEmpty(owner)) return;
        if (_owners.Add(owner)) Apply();
    }

    /// <summary>Unregister 'owner' — time resumes only if nobody else needs it.</summary>
    public static void Release(string owner)
    {
        if (string.IsNullOrEmpty(owner)) return;
        if (_owners.Remove(owner)) Apply();
    }

    /// <summary>Clear every freeze and resume time. Call on scene load / reset.</summary>
    public static void ReleaseAll()
    {
        if (_owners.Count == 0) { Time.timeScale = 1f; return; }
        _owners.Clear();
        Apply();
    }

    private static void Apply()
    {
        Time.timeScale = _owners.Count > 0 ? 0f : 1f;
    }

    // Domain-reload-safe reset so a stale freeze from a previous play session
    // (with "Enter Play Mode → Reload Domain" disabled) can't carry over.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _owners.Clear();
        Time.timeScale = 1f;
    }
}
