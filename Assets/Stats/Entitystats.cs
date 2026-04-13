using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Lives on the Player and every enemy/boss.
/// Player needs a PlayerStatBlock. Enemies need an EnemyStatBlock.
/// Both share BaseStatBlock for the five core stats.
/// </summary>
public class EntityStats : MonoBehaviour
{
    [Header("Is This The Player?")]
    public bool isPlayer = false;

    [Header("Stat Block")]
    [Tooltip("Drag a PlayerStatBlock here if isPlayer is true")]
    public PlayerStatBlock playerStatBlock;

    [Tooltip("Drag an EnemyStatBlock here if isPlayer is false")]
    public EnemyStatBlock enemyStatBlock;

    // Convenience property — whichever block is relevant
    public BaseStatBlock BaseBlock => isPlayer
        ? (BaseStatBlock)playerStatBlock
        : (BaseStatBlock)enemyStatBlock;

    // ── Events ──
    public UnityEvent             onDeath;
    public UnityEvent<int>        onDamageTaken;
    public UnityEvent<int>        onHeal;
    public UnityEvent             onLevelUp;

    // ── Runtime Stats ──
    public int CurrentHealth   { get; private set; }
    public int MaxHealth       { get; private set; }
    public int CurrentStamina  { get; private set; }
    public int MaxStamina      { get; private set; }
    public int Strength        { get; private set; }
    public int Speed           { get; private set; }
    public int BaseToughness   { get; private set; }
    public int Toughness       { get; private set; }

    // ── Leveling (player only) ──
    public int CurrentLevel { get; private set; } = 0;
    public int CurrentFloor { get; private set; } = 1;
    public int LevelCap     => GetLevelCap();

    // ── Equipped Weapon (player only) ──
    public enum WeaponType { None, Blade, Hammer, Bow }
    public WeaponType EquippedWeapon { get; private set; } = WeaponType.Blade;

    private bool  _isDead;
    private float _lastStaminaUseTime;

    // ─────────────────────────────────────────
    void Start()
    {
        if (BaseBlock == null)
        {
            Debug.LogError($"[EntityStats] {gameObject.name} has no stat block assigned! " +
                           $"Drag a {(isPlayer ? "PlayerStatBlock" : "EnemyStatBlock")} into the slot.");
            return;
        }

        InitStats();
    }

    void Update()
    {
        if (isPlayer) RegenStamina();
    }

    // ─────────────────────────────────────────
    // Init
    // ─────────────────────────────────────────

    private void InitStats()
    {
        BaseStatBlock b = BaseBlock;

        MaxHealth     = b.baseHealth;
        Strength      = b.baseStrength;
        MaxStamina    = b.baseStamina;
        Speed         = b.baseSpeed;
        BaseToughness = b.baseToughness;
        Toughness     = BaseToughness;

        CurrentHealth  = MaxHealth;
        CurrentStamina = MaxStamina;

        if (isPlayer) ApplyWeaponToughnessBonus();

        Debug.Log($"[EntityStats] {gameObject.name} ready — " +
                  $"HP:{CurrentHealth} STR:{Strength} STA:{MaxStamina} SPD:{Speed} TGH:{Toughness}");
    }

    // ─────────────────────────────────────────
    // Leveling — player only
    // ─────────────────────────────────────────

    /// <summary>
    /// Call when the player earns a level point.
    /// Returns false if already at the floor cap.
    /// </summary>
    public bool GainLevel()
    {
        if (!isPlayer) return false;

        if (CurrentLevel >= LevelCap)
        {
            Debug.Log("[EntityStats] At level cap — beat the boss to continue.");
            return false;
        }

        CurrentLevel++;
        Debug.Log($"[EntityStats] Level up — now {CurrentLevel}/{LevelCap}");
        onLevelUp?.Invoke();
        return true;
    }

    /// <summary>
    /// Spend a level point on a chosen stat.
    /// Valid inputs: "health", "strength", "stamina", "speed"
    /// </summary>
    public void SpendPoint(string stat)
    {
        if (!isPlayer || playerStatBlock == null) return;

        switch (stat.ToLower())
        {
            case "health":
                MaxHealth     += playerStatBlock.healthPerPoint;
                CurrentHealth += playerStatBlock.healthPerPoint;
                Debug.Log($"[EntityStats] Health → {MaxHealth}");
                break;

            case "strength":
                Strength += playerStatBlock.strengthPerPoint;
                Debug.Log($"[EntityStats] Strength → {Strength}");
                break;

            case "stamina":
                MaxStamina     += playerStatBlock.staminaPerPoint;
                CurrentStamina += playerStatBlock.staminaPerPoint;
                Debug.Log($"[EntityStats] Stamina → {MaxStamina}");
                break;

            case "speed":
                Speed += playerStatBlock.speedPerPoint;
                Debug.Log($"[EntityStats] Speed → {Speed}");
                break;

            default:
                Debug.LogWarning($"[EntityStats] Unknown stat '{stat}'. Use: health / strength / stamina / speed");
                break;
        }
    }

    /// <summary>
    /// Call when the player beats a floor boss to unlock the next 5 level points.
    /// </summary>
    public void AdvanceFloor()
    {
        if (!isPlayer) return;
        CurrentFloor = Mathf.Min(CurrentFloor + 1, 3);
        Debug.Log($"[EntityStats] Floor {CurrentFloor} — level cap now {LevelCap}");
    }

    private int GetLevelCap()
    {
        if (playerStatBlock == null) return 0;
        return CurrentFloor switch
        {
            1 => playerStatBlock.floorOneCap,
            2 => playerStatBlock.floorTwoCap,
            _ => playerStatBlock.floorThreeCap
        };
    }

    // ─────────────────────────────────────────
    // Weapons — player only
    // ─────────────────────────────────────────

    public void EquipWeapon(WeaponType weapon)
    {
        if (!isPlayer) return;
        EquippedWeapon = weapon;
        ApplyWeaponToughnessBonus();
        Debug.Log($"[EntityStats] Equipped {weapon} — Toughness now {Toughness}");
    }

    private void ApplyWeaponToughnessBonus()
    {
        if (playerStatBlock == null) return;

        int bonus = EquippedWeapon switch
        {
            WeaponType.Blade  => playerStatBlock.bladeToughnessBonus,
            WeaponType.Hammer => playerStatBlock.hammerToughnessBonus,
            WeaponType.Bow    => playerStatBlock.bowToughnessBonus,
            _                 => 0
        };

        Toughness = BaseToughness + bonus;
    }

    /// <summary>
    /// Returns damage for one hit with the currently equipped weapon.
    /// Bow ignores Strength. Blade and Hammer scale with it.
    /// </summary>
    public int CalculateWeaponDamage()
    {
        if (playerStatBlock == null) return 0;

        int baseDmg;
        int strengthBonus;

        switch (EquippedWeapon)
        {
            case WeaponType.Blade:
                baseDmg       = Random.Range(playerStatBlock.bladeDamageMin,  playerStatBlock.bladeDamageMax  + 1);
                strengthBonus = Strength * playerStatBlock.bladeStrengthBonus;
                break;

            case WeaponType.Hammer:
                baseDmg       = Random.Range(playerStatBlock.hammerDamageMin, playerStatBlock.hammerDamageMax + 1);
                strengthBonus = Strength * playerStatBlock.hammerStrengthBonus;
                break;

            case WeaponType.Bow:
                baseDmg       = Random.Range(playerStatBlock.bowDamageMin,    playerStatBlock.bowDamageMax    + 1);
                strengthBonus = 0;
                break;

            default:
                return 0;
        }

        return baseDmg + strengthBonus;
    }

    /// <summary>
    /// Returns the stamina cost for one use of the equipped weapon.
    /// </summary>
    public int GetWeaponStaminaCost()
    {
        if (playerStatBlock == null) return 0;

        return EquippedWeapon switch
        {
            WeaponType.Blade  => playerStatBlock.bladeStaminaCost,
            WeaponType.Hammer => playerStatBlock.hammerStaminaCost,
            WeaponType.Bow    => playerStatBlock.bowStaminaCost,
            _                 => 0
        };
    }

    // ─────────────────────────────────────────
    // Stamina — player only
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns true if the cost was paid. False if not enough stamina.
    /// </summary>
    public bool UseStamina(int amount)
    {
        if (CurrentStamina < amount)
        {
            Debug.Log($"[EntityStats] Not enough stamina ({CurrentStamina}/{amount} needed)");
            return false;
        }

        CurrentStamina      = Mathf.Max(0, CurrentStamina - amount);
        _lastStaminaUseTime = Time.time;
        return true;
    }

    private void RegenStamina()
    {
        if (playerStatBlock == null) return;
        if (CurrentStamina >= MaxStamina) return;
        if (Time.time < _lastStaminaUseTime + playerStatBlock.staminaRegenDelay) return;

        CurrentStamina = Mathf.Min(MaxStamina,
            CurrentStamina + Mathf.RoundToInt(playerStatBlock.staminaRegenRate * Time.deltaTime));
    }

    // ─────────────────────────────────────────
    // Health & Damage
    // ─────────────────────────────────────────

    public void TakeDamage(int damage)
    {
        if (_isDead) return;

        int finalDamage = Mathf.Max(1, damage);
        CurrentHealth   = Mathf.Max(0, CurrentHealth - finalDamage);

        onDamageTaken?.Invoke(finalDamage);
        Debug.Log($"[EntityStats] {gameObject.name} took {finalDamage} — HP {CurrentHealth}/{MaxHealth}");

        if (CurrentHealth <= 0) Die();
    }

    public void Heal(int amount)
    {
        if (_isDead) return;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        onHeal?.Invoke(amount);
    }

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;
        Debug.Log($"[EntityStats] {gameObject.name} died.");
        onDeath?.Invoke();
    }

    public bool IsDead => _isDead;

    // ─────────────────────────────────────────
    // Stagger check
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns true if the hit's stagger force exceeds this entity's Toughness.
    /// </summary>
    public bool ShouldStagger(int staggerForce) => staggerForce > Toughness;
}