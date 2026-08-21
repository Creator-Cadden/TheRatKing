using System;
using UnityEngine;

/// <summary>
/// Runtime lookup for "the current player" so UI and other scripts never need a
/// serialized scene reference (which can't be saved on a prefab, hence the
/// un-appliable override). The player registers itself via PlayerRegistrar the
/// moment it spawns; everything else reads from here or listens for OnPlayerReady.
///
/// Consumer pattern:
///   void OnEnable()  { PlayerRegistry.OnPlayerReady += Bind; if (PlayerRegistry.HasPlayer) Bind(PlayerRegistry.Player); }
///   void OnDisable() { PlayerRegistry.OnPlayerReady -= Bind; }
///   void Bind(GameObject p) { _stats = PlayerRegistry.Stats; ... }
/// </summary>
public static class PlayerRegistry
{
    public static GameObject  Player { get; private set; }
    public static Transform   Root   => Player != null ? Player.transform : null;
    public static EntityStats Stats  { get; private set; }
    public static XPSystem    XP     { get; private set; }
    public static bool        HasPlayer => Player != null;

    /// <summary>Fired when a player registers (scene load / spawn). Also safe to
    /// just poll HasPlayer in Start.</summary>
    public static event Action<GameObject> OnPlayerReady;
    /// <summary>Fired when the current player unregisters (scene unload / destroy).</summary>
    public static event Action OnPlayerGone;

    public static void Register(GameObject player)
    {
        if (player == null) return;
        Player = player;
        Stats  = player.GetComponent<EntityStats>();
        XP     = player.GetComponent<XPSystem>();
        OnPlayerReady?.Invoke(player);
    }

    public static void Unregister(GameObject player)
    {
        if (Player != player) return;
        Player = null; Stats = null; XP = null;
        OnPlayerGone?.Invoke();
    }
}
