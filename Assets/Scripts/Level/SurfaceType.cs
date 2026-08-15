using UnityEngine;

/// <summary>
/// The kinds of ground the rat can step on. Add new ones here as needed.
/// </summary>
public enum SurfaceKind
{
    Stone,
    Wood,
    Plastic,
    WoodShavings,
    Water,
    Dirt,
    Mushroom,
    Brick,        // later
    HollowPipe,   // later
}

/// <summary>
/// Tags a ground object with its surface kind so the footstep system can pick
/// the right sound. Drop it on a ground object — OR on a PARENT that groups many
/// same-surface objects (the footstep raycast resolves it via GetComponentInParent,
/// so one tag on a parent covers all its children). Untagged ground falls back to
/// the footstep system's default surface, so you only need to tag the exceptions.
///
/// Tips for tagging lots of objects fast:
///  • Group objects of one surface under an empty and put ONE SurfaceType on it.
///  • Or multi-select many objects, Add Component ▸ Surface Type, set the dropdown
///    once — Unity applies it to every selected object at once.
/// It's purely logical — nothing to do with the object's Material or color.
/// </summary>
public class SurfaceType : MonoBehaviour
{
    [Tooltip("What this surface sounds like underfoot.")]
    public SurfaceKind surface = SurfaceKind.Stone;

    /// <summary>
    /// Resolve the surface kind from a collider a footstep raycast hit. Searches
    /// parents so a tag on a group root still applies. Returns <paramref name="fallback"/>
    /// when nothing is tagged.
    /// </summary>
    public static SurfaceKind Resolve(Collider col, SurfaceKind fallback = SurfaceKind.Stone)
    {
        if (col == null) return fallback;
        SurfaceType st = col.GetComponentInParent<SurfaceType>();
        return st != null ? st.surface : fallback;
    }
}
