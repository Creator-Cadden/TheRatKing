using UnityEngine;

/// <summary>
/// Put this ONCE on the Player prefab. It registers the player into
/// PlayerRegistry the instant it enables, so any scene it's dropped into wires
/// itself up — no per-scene Inspector references, no un-appliable prefab
/// overrides. Runs early (negative execution order) so consumers can read the
/// player in their own Start.
/// </summary>
[DefaultExecutionOrder(-100)]
public class PlayerRegistrar : MonoBehaviour
{
    void OnEnable()  => PlayerRegistry.Register(gameObject);
    void OnDisable() => PlayerRegistry.Unregister(gameObject);
}
