using UnityEngine;

/// <summary>
/// Swaps the active weapon model on the Player when EquipWeapon is called,
/// and pushes a "Weapon" int parameter into the Animator so animations can
/// branch per weapon (0 = Blade, 1 = Hammer, 2 = Bow, -1 = None).
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

    [Header("Rat Body Animator")]
    [Tooltip("The RAT BODY animator (controls running, jumping, attacking pose). " +
             "If set, this Animator gets a 'Weapon' int parameter written every " +
             "time the weapon changes. Leave null to auto-find one via " +
             "GetComponentInChildren<Animator>().")]
    public Animator weaponAnimator;

    [Tooltip("Name of the int parameter on the rat body animator that controls " +
             "which weapon's animation set is active.")]
    public string weaponAnimParam = "Weapon";

    [Header("Per-Weapon Animators (optional)")]
    [Tooltip("Animator on the BLADE model itself — drives the blade-specific " +
             "swing animation (the sword tilt, glow, trail, etc.). PlayerCombat " +
             "fires its triggers via ActiveWeaponAnimator. Leave null if your " +
             "blade has no separate animator.")]
    public Animator bladeAnimator;

    [Tooltip("Animator on the HAMMER model itself. Leave null if your hammer " +
             "doesn't have a separate animator yet — PlayerCombat will silently skip it.")]
    public Animator hammerAnimator;

    [Tooltip("Animator on the BOW model itself. Drives the draw / release / " +
             "string animations. Leave null if not yet set up.")]
    public Animator bowAnimator;

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

    /// <summary>
    /// Force a refresh manually (e.g. after a runtime weapon swap from a pickup).
    /// </summary>
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

    /// <summary>
    /// Returns the Animator that belongs to the currently-equipped weapon
    /// (or null if that weapon has no separate animator wired up).
    /// </summary>
    public Animator ActiveWeaponAnimator
    {
        get
        {
            if (_stats == null) _stats = GetComponent<EntityStats>();
            if (_stats == null) return null;

            return _stats.EquippedWeapon switch
            {
                EntityStats.WeaponType.Blade  => bladeAnimator,
                EntityStats.WeaponType.Hammer => hammerAnimator,
                EntityStats.WeaponType.Bow    => bowAnimator,
                _                             => null,
            };
        }
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
