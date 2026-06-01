using UnityEngine;

/// <summary>
/// Swaps the active weapon model on the Player when EquipWeapon is called,
/// and pushes a "Weapon" int parameter into the Animator so animations can
/// branch per weapon (0 = Blade, 1 = Hammer, 2 = Bow, -1 = None).
///
/// Setup:
///   1. Under the Player prefab, give each weapon its own model GameObject
///      (e.g. a child empty named "Blade" holding the blade mesh, same for
///      "Hammer" and "Bow").
///   2. Add this component to the Player root.
///   3. Drag each weapon model into the Inspector slots.
///   4. If your Animator has a "Weapon" int parameter, the animator
///      auto-switches between attack states.
///   5. Hook the rat's Animator into the Inspector (or leave null to use
///      GetComponentInChildren).
///
/// The component subscribes to <see cref="EntityStats.onStatsChanged"/>
/// which already fires from EquipWeapon, so no extra wiring needed —
/// every time a save loads, weapon-select confirms, or a future swap
/// fires, this picks it up.
/// </summary>
[RequireComponent(typeof(EntityStats))]
public class WeaponModelSwapper : MonoBehaviour
{
    [Header("Weapon Model Roots")]
    [Tooltip("GameObject that holds the visible blade model.")]
    public GameObject bladeModel;

    [Tooltip("GameObject that holds the visible hammer model.")]
    public GameObject hammerModel;

    [Tooltip("GameObject that holds the visible bow model.")]
    public GameObject bowModel;

    [Header("Animator")]
    [Tooltip("If set, this Animator gets a 'Weapon' int parameter written " +
             "every time the weapon changes. Leave null to auto-find one " +
             "via GetComponentInChildren<Animator>().")]
    public Animator weaponAnimator;

    [Tooltip("Name of the int parameter on the Animator that controls which " +
             "weapon's animation set is active.")]
    public string weaponAnimParam = "Weapon";

    [Header("Debug")]
    public bool verbose = false;

    private EntityStats _stats;

    void Awake()
    {
        _stats = GetComponent<EntityStats>();
        if (weaponAnimator == null)
            weaponAnimator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        if (_stats != null)
        {
            _stats.onStatsChanged.RemoveListener(Refresh);
            _stats.onStatsChanged.AddListener(Refresh);
        }
        // Apply once at enable so the initial state is correct
        Refresh();
    }

    void OnDisable()
    {
        if (_stats != null)
            _stats.onStatsChanged.RemoveListener(Refresh);
    }

    void Start()
    {
        // EntityStats.InitStats sets the weapon in Start; do one more pass
        // in case OnEnable ran before InitStats.
        Refresh();
    }

    /// <summary>Force a refresh manually (e.g. after a runtime weapon swap from a pickup).</summary>
    public void Refresh()
    {
        if (_stats == null) _stats = GetComponent<EntityStats>();
        if (_stats == null) return;

        EntityStats.WeaponType w = _stats.EquippedWeapon;

        SetActiveSafe(bladeModel,  w == EntityStats.WeaponType.Blade);
        SetActiveSafe(hammerModel, w == EntityStats.WeaponType.Hammer);
        SetActiveSafe(bowModel,    w == EntityStats.WeaponType.Bow);

        if (weaponAnimator != null && HasParam(weaponAnimator, weaponAnimParam))
        {
            int value = w switch
            {
                EntityStats.WeaponType.Blade  => 0,
                EntityStats.WeaponType.Hammer => 1,
                EntityStats.WeaponType.Bow    => 2,
                _                             => -1,
            };
            weaponAnimator.SetInteger(weaponAnimParam, value);
        }

        if (verbose) Debug.Log($"[WeaponModelSwapper] Active weapon → {w}");
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private static bool HasParam(Animator anim, string name)
    {
        foreach (var p in anim.parameters)
            if (p.name == name) return true;
        return false;
    }
}
