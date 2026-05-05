using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Lives on the Player and every enemy/boss.
/// Player needs a PlayerStatBlock. Enemies need an EnemyStatBlock.
/// </summary>
public class EntityStats : MonoBehaviour
{
    [Header("Is This The Player?")]
    public bool isPlayer = false;

    [Header("Stat Block")]
    public PlayerStatBlock playerStatBlock;
    public EnemyStatBlock  enemyStatBlock;

    public BaseStatBlock BaseBlock => isPlayer
        ? (BaseStatBlock)playerStatBlock
        : (BaseStatBlock)enemyStatBlock;

    // ── Events ──
    public UnityEvent          onDeath;
    public UnityEvent<int>     onDamageTaken;
    public UnityEvent<int>     onHeal;
    public UnityEvent          onLevelUp;
    public UnityEvent          onStatsChanged;

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

    public bool IsDead { get; private set; }

    private float _lastStaminaUseTime;
    private float _staminaRegenAccumulator;

    // Speed: each point above base adds this to walk speed (sprint scales 1.33x)
    private const float SpeedBonusPerPoint = 0.5f;

    // ─────────────────────────────────────────
    void Start()
    {
        if (BaseBlock == null)
        {
            Debug.LogError($"[EntityStats] {gameObject.name} has no stat block assigned!");
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

        _staminaRegenAccumulator = 0f;

        if (isPlayer)
        {
            ApplyWeaponToughnessBonus();
            NotifySpeedChanged();
        }

        Debug.Log($"[EntityStats] {gameObject.name} ready — " +
                  $"HP:{CurrentHealth} STR:{Strength} STA:{MaxStamina} SPD:{Speed} TGH:{Toughness}");
    }

    // ─────────────────────────────────────────
    // Leveling
    // ─────────────────────────────────────────

    public bool GainLevel()
    {
        if (!isPlayer) return false;
        if (CurrentLevel >= LevelCap)
        {
            Debug.Log("[EntityStats] At level cap.");
            return false;
        }
        CurrentLevel++;
        onLevelUp?.Invoke();
        return true;
    }

    public void SpendPoint(string stat)
    {
        if (!isPlayer || playerStatBlock == null) return;

        switch (stat.ToLower())
        {
            case "health":
                MaxHealth     += playerStatBlock.healthPerPoint;
                CurrentHealth += playerStatBlock.healthPerPoint;
                break;
            case "strength":
                Strength += playerStatBlock.strengthPerPoint;
                break;
            case "stamina":
                MaxStamina     += 10;
                CurrentStamina += 10;
                break;
            case "speed":
                Speed += playerStatBlock.speedPerPoint;
                NotifySpeedChanged();
                break;
            default:
                Debug.LogWarning($"[EntityStats] Unknown stat '{stat}'.");
                return;
        }

        onStatsChanged?.Invoke();
        Debug.Log($"[EntityStats] Spent point on {stat}.");
    }

    private void NotifySpeedChanged()
    {
        if (!isPlayer) return;

        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm == null) return;

        int baseSpd     = BaseBlock?.baseSpeed ?? 5;
        int bonusPoints = Speed - baseSpd;

        // Pass hammer fraction so movement knows to apply it on top of speed bonus
        float hammerFraction = (isPlayer && playerStatBlock != null && EquippedWeapon == WeaponType.Hammer)
            ? playerStatBlock.hammerMoveSpeedFraction
            : 1f;

        pm.ApplySpeedBonus(bonusPoints, SpeedBonusPerPoint, hammerFraction);
    }

    public void AdvanceFloor()
    {
        if (!isPlayer) return;
        CurrentFloor = Mathf.Min(CurrentFloor + 1, 3);
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
    // Weapons
    // ─────────────────────────────────────────

    public void EquipWeapon(WeaponType weapon)
    {
        if (!isPlayer) return;
        EquippedWeapon = weapon;
        ApplyWeaponToughnessBonus();

        // Push move speed change (hammer penalty or removal of it)
        NotifySpeedChanged();

        // Push attack speed change to PlayerCombat
        PlayerCombat pc = GetComponent<PlayerCombat>();
        pc?.RecalculateAttackCooldown();

        onStatsChanged?.Invoke();
        Debug.Log($"[EntityStats] Equipped {weapon} — Toughness:{Toughness}");
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
    /// Damage formula:
    ///   Blade  = Strength * bladeStrengthMultiplier  (no flat base)
    ///   Hammer = Strength * hammerStrengthMultiplier (no flat base, heavy penalty to speed)
    ///   Bow    = Random(bowDamageMin, bowDamageMax)  (no strength scaling, ranged)
    /// </summary>
    public int CalculateWeaponDamage()
    {
        if (playerStatBlock == null) return 0;

        return EquippedWeapon switch
        {
            WeaponType.Blade  => Strength * playerStatBlock.bladeStrengthMultiplier,
            WeaponType.Hammer => Strength * playerStatBlock.hammerStrengthMultiplier,
            WeaponType.Bow    => Strength * playerStatBlock.bowStrengthMultiplier,
            _                 => 0
        };
    }

    /// <summary>
    /// Charged bow shot — full aim-hold release.
    /// Damage = Strength * bowStrengthMultiplier * bowChargedMultiplier
    /// </summary>
    public int CalculateChargedBowDamage()
    {
        if (playerStatBlock == null || EquippedWeapon != WeaponType.Bow) return 0;
        float charged = Strength * playerStatBlock.bowStrengthMultiplier
                        * playerStatBlock.bowChargedMultiplier;
        return Mathf.RoundToInt(charged);
    }

    // ─────────────────────────────────────────
    // Stamina
    // ─────────────────────────────────────────

    public bool UseStamina(int amount)
    {
        if (CurrentStamina < amount) return false;
        CurrentStamina           = Mathf.Max(0, CurrentStamina - amount);
        _lastStaminaUseTime      = Time.time;
        _staminaRegenAccumulator = 0f;
        return true;
    }

    public bool UseStaminaPerSecond(float amountPerSecond)
    {
        if (CurrentStamina <= 0) return false;

        _staminaRegenAccumulator -= amountPerSecond * Time.deltaTime;

        if (_staminaRegenAccumulator <= -1f)
        {
            int drain                 = Mathf.FloorToInt(-_staminaRegenAccumulator);
            _staminaRegenAccumulator += drain;
            CurrentStamina            = Mathf.Max(0, CurrentStamina - drain);
        }

        _lastStaminaUseTime = Time.time;
        return CurrentStamina > 0;
    }

    private void RegenStamina()
    {
        if (playerStatBlock == null) return;
        if (CurrentStamina >= MaxStamina) return;
        if (Time.time < _lastStaminaUseTime + playerStatBlock.staminaRegenDelay) return;

        _staminaRegenAccumulator += playerStatBlock.staminaRegenRate * Time.deltaTime;

        int wholePoints = Mathf.FloorToInt(_staminaRegenAccumulator);
        if (wholePoints > 0)
        {
            _staminaRegenAccumulator -= wholePoints;
            CurrentStamina = Mathf.Min(MaxStamina, CurrentStamina + wholePoints);
        }
    }

    // ─────────────────────────────────────────
    // Health & Damage
    // ─────────────────────────────────────────

    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        int finalDamage = Mathf.Max(1, damage);
        CurrentHealth   = Mathf.Max(0, CurrentHealth - finalDamage);
        onDamageTaken?.Invoke(finalDamage);
        if (CurrentHealth <= 0) Die();
    }

    public void Heal(int amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        onHeal?.Invoke(amount);
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;
        onDeath?.Invoke();
    }

    public bool ShouldStagger(int staggerForce) => staggerForce > Toughness;

    public void ResetToFull()
    {
        IsDead         = false;
        CurrentHealth  = MaxHealth;
        CurrentStamina = MaxStamina;

        _lastStaminaUseTime      = -999f;
        _staminaRegenAccumulator = 0f;

        onHeal?.Invoke(MaxHealth);
        onStatsChanged?.Invoke();
    }
}