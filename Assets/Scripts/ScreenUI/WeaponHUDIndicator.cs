using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows which of the three weapons is equipped. Small, always-on, bottom corner.
///
/// It matters more than it looks: Blade / Hammer / Bow have different reach,
/// cooldown and Impact, and the player's model is a tiny rat at distance. A
/// glanceable icon stops "why is my attack slow?" confusion after a swap.
///
/// Binds to EntityStats.onStatsChanged, which EquipWeapon already fires.
/// </summary>
public class WeaponHUDIndicator : MonoBehaviour
{
    [Header("Player")]
    public string playerTag = "Player";
    [Tooltip("Leave null — auto-found via the player tag.")]
    public EntityStats playerStats;

    [Header("Display")]
    [Tooltip("Icon swapped to match the weapon.")]
    public Image iconImage;
    [Tooltip("Optional name label, e.g. 'BLADE'.")]
    public TMP_Text nameLabel;
    [Tooltip("Optional stat line, e.g. 'DMG 24  ·  IMPACT 2'.")]
    public TMP_Text statLabel;

    [Header("Icons")]
    public Sprite bladeIcon;
    public Sprite hammerIcon;
    public Sprite bowIcon;

    [Header("Swap juice")]
    [Tooltip("Scale the icon punches to when the weapon changes.")]
    public float punchScale = 1.25f;
    public float punchRecover = 9f;

    [Header("Format")]
    [Tooltip("{0} = damage, {1} = impact.")]
    public string statFormat = "DMG {0}  ·  IMPACT {1}";
    [Tooltip("Show the stat line at all.")]
    public bool showStats = true;

    // ── Private ───────────────────────────────────────────────────────────────

    private EntityStats.WeaponType _lastWeapon = EntityStats.WeaponType.None;
    private float _punch;
    private RectTransform _iconRt;

    void Start()
    {
        if (iconImage != null) _iconRt = iconImage.rectTransform;
        BindPlayer();
        Refresh();
    }

    void OnDestroy()
    {
        if (playerStats != null) playerStats.onStatsChanged.RemoveListener(Refresh);
    }

    void Update()
    {
        if (playerStats == null) { BindPlayer(); return; }

        // Belt and braces — catch a weapon change even if the event didn't fire.
        if (playerStats.EquippedWeapon != _lastWeapon) Refresh();

        if (_iconRt != null && _punch > 0.001f)
        {
            _punch = Mathf.Lerp(_punch, 0f, Time.deltaTime * punchRecover);
            _iconRt.localScale = Vector3.one * (1f + _punch);
        }
    }

    private void BindPlayer()
    {
        if (playerStats != null) return;

        GameObject go = GameObject.FindWithTag(playerTag);
        if (go == null) return;

        playerStats = go.GetComponent<EntityStats>();
        if (playerStats != null) playerStats.onStatsChanged.AddListener(Refresh);
    }

    /// <summary>Re-read the equipped weapon and update every field. Safe to call anytime.</summary>
    public void Refresh()
    {
        if (playerStats == null) return;

        EntityStats.WeaponType w = playerStats.EquippedWeapon;
        bool changed = w != _lastWeapon;
        _lastWeapon  = w;

        if (iconImage != null)
        {
            Sprite s = w switch
            {
                EntityStats.WeaponType.Blade  => bladeIcon,
                EntityStats.WeaponType.Hammer => hammerIcon,
                EntityStats.WeaponType.Bow    => bowIcon,
                _                             => null
            };
            iconImage.sprite  = s;
            iconImage.enabled = s != null;
        }

        if (nameLabel != null) nameLabel.text = w.ToString().ToUpperInvariant();

        if (statLabel != null)
        {
            statLabel.gameObject.SetActive(showStats && w != EntityStats.WeaponType.None);
            if (showStats && w != EntityStats.WeaponType.None)
            {
                int dmg    = playerStats.CalculateWeaponDamage();
                int impact = playerStats.GetWeaponImpact(false);
                statLabel.text = string.Format(statFormat, dmg, impact);
            }
        }

        if (changed) _punch = punchScale - 1f;
    }
}
