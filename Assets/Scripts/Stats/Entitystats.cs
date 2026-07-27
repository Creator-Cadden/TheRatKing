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

    // The save GameManager applied to this instance (if any). Unity's sceneLoaded
    // event fires BETWEEN Awake and Start, so ApplySaveData runs before Start —
    // and InitStats would wipe it. Start re-applies this so the save always wins.
    private SaveData _appliedSave;

    // Speed: each point above base adds this to walk speed (sprint scales 1.33x)
    private const float SpeedBonusPerPoint = 0.5f;

    void Start()
    {
        if (BaseBlock == null)
        {
            Debug.LogError($"[EntityStats] {gameObject.name} has no stat block assigned!");
            return;
        }
        InitStats();

        // If a save was applied before Start ran, InitStats just reset the player
        // to base stats — re-apply the save on top. (Fixes stats/XP not carrying
        // over between levels.)
        if (_appliedSave != null)
            ApplySaveData(_appliedSave);
    }

    void Update()
    {
        if (isPlayer) RegenStamina();
    }

    // ── Init ──

    private void InitStats()
    {
        BaseStatBlock b = BaseBlock;

        MaxHealth = b.baseHealth;
        Strength  = b.baseStrength;

        if (isPlayer && playerStatBlock != null)
        {
            // Player owns Stamina & Speed. The player has NO Toughness — stagger
            // resistance is an enemy-only concept now, so it stays 0.
            MaxStamina    = playerStatBlock.baseStamina;
            Speed         = playerStatBlock.baseSpeed;
            BaseToughness = 0;
            Toughness     = 0;
        }
        else
        {
            // Enemies own Toughness. They have no stamina and move via
            // EnemyStatBlock.moveSpeed, so the generic Speed stat is unused here.
            MaxStamina    = 0;
            Speed         = 0;
            BaseToughness = enemyStatBlock != null ? enemyStatBlock.baseToughness : 0;
            Toughness     = BaseToughness;
        }

        // ── Per-floor enemy scaling ───────────────────────────────────
        // Enemies get harder the deeper the player is.
        // Floor 1 = baseline, Floor 2 = ×multiplier, Floor 3 = ×multiplier²
        // Player stats are never scaled this way. BOSSES never scale either
        // (ignoreFloorScaling) — their stats are hand-tuned exact numbers.
        if (!isPlayer && enemyStatBlock != null && !enemyStatBlock.ignoreFloorScaling)
        {
            int floor = (GameManager.Instance != null && GameManager.Instance.ActiveSave != null)
                ? GameManager.Instance.ActiveSave.currentFloor
                : 1;

            int floorsAbove = Mathf.Max(0, floor - 1);
            if (floorsAbove > 0)
            {
                float hpMult  = Mathf.Pow(enemyStatBlock.healthScalePerFloor,   floorsAbove);
                float strMult = Mathf.Pow(enemyStatBlock.strengthScalePerFloor, floorsAbove);
                float staMult = Mathf.Pow(enemyStatBlock.staminaScalePerFloor,  floorsAbove);

                MaxHealth  = Mathf.Max(1, Mathf.RoundToInt(MaxHealth  * hpMult));
                Strength   = Mathf.Max(0, Mathf.RoundToInt(Strength   * strMult));
                MaxStamina = Mathf.Max(0, Mathf.RoundToInt(MaxStamina * staMult));

                Debug.Log($"[EntityStats] {gameObject.name} scaled to Floor {floor} → " +
                          $"HP × {hpMult:F2}, STR × {strMult:F2}");
            }
        }

        CurrentHealth  = MaxHealth;
        CurrentStamina = MaxStamina;

        _staminaRegenAccumulator = 0f;

        if (isPlayer)
            NotifySpeedChanged();

        Debug.Log($"[EntityStats] {gameObject.name} ready — " +
                  $"HP:{CurrentHealth} STR:{Strength} STA:{MaxStamina} SPD:{Speed} TGH:{Toughness}");
    }

    // ── Leveling ──

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
                MaxStamina     += playerStatBlock.staminaPerPoint;
                CurrentStamina += playerStatBlock.staminaPerPoint;
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

        int baseSpd     = playerStatBlock != null ? playerStatBlock.baseSpeed : 5;
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

    // ── Weapons ──

    public void EquipWeapon(WeaponType weapon)
    {
        if (!isPlayer) return;
        EquippedWeapon = weapon;

        // Push move speed change (hammer penalty or removal of it)
        NotifySpeedChanged();

        // Push attack speed change to PlayerCombat
        PlayerCombat pc = GetComponent<PlayerCombat>();
        pc?.RecalculateAttackCooldown();

        onStatsChanged?.Invoke();
        Debug.Log($"[EntityStats] Equipped {weapon}.");
    }

    /// <summary>
    /// Souls-style damage formula: weaponBase + Strength × multiplier.
    /// The flat base keeps low-Strength damage sane and makes each point a
    /// smooth percentage gain instead of doubling damage early.
    /// </summary>
    public int CalculateWeaponDamage()
    {
        if (playerStatBlock == null) return 0;

        return EquippedWeapon switch
        {
            WeaponType.Blade  => playerStatBlock.bladeBaseDamage  + Strength * playerStatBlock.bladeStrengthMultiplier,
            WeaponType.Hammer => playerStatBlock.hammerBaseDamage + Strength * playerStatBlock.hammerStrengthMultiplier,
            WeaponType.Bow    => playerStatBlock.bowBaseDamage    + Strength * playerStatBlock.bowStrengthMultiplier,
            _                 => 0
        };
    }

    /// <summary>
    /// Impact of the equipped weapon (the stagger stat — offense only).
    /// special = jump/charged attacks (+1 tier over basic per the design doc).
    /// </summary>
    public int GetWeaponImpact(bool special)
    {
        if (playerStatBlock == null) return 1;
        return EquippedWeapon switch
        {
            WeaponType.Blade  => special ? playerStatBlock.bladeImpactSpecial  : playerStatBlock.bladeImpactBasic,
            WeaponType.Hammer => special ? playerStatBlock.hammerImpactSpecial : playerStatBlock.hammerImpactBasic,
            WeaponType.Bow    => special ? playerStatBlock.bowImpactSpecial    : playerStatBlock.bowImpactBasic,
            _                 => 1
        };
    }

    /// <summary>
    /// Charged bow shot — full aim-hold release.
    /// Damage = (bowBaseDamage + Strength × bowStrengthMultiplier) × bowChargedMultiplier
    /// </summary>
    public int CalculateChargedBowDamage()
    {
        if (playerStatBlock == null || EquippedWeapon != WeaponType.Bow) return 0;
        float charged = (playerStatBlock.bowBaseDamage
                         + Strength * playerStatBlock.bowStrengthMultiplier)
                        * playerStatBlock.bowChargedMultiplier;
        return Mathf.RoundToInt(charged);
    }

    // ── Stamina ──

    public bool UseStamina(int amount)
    {
        if (CurrentStamina < amount) return false;
        CurrentStamina           = Mathf.Max(0, CurrentStamina - amount);
        _lastStaminaUseTime      = Time.time;
        _staminaRegenAccumulator = 0f;
        return true;
    }

    /// <summary>
    /// Spends up to <paramref name="amount"/> stamina, succeeding as long as ANY
    /// stamina remains (drains to 0 if there isn't enough). Used by the dodge roll —
    /// playtest feedback: the player should always be able to burn their last sliver
    /// of stamina to escape.
    /// </summary>
    public bool UseStaminaPartial(int amount)
    {
        if (CurrentStamina <= 0) return false;
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

    // ── Health & Damage ──

    // Brief invulnerability window (player only) granted after being hit —
    // prevents multi-hit shredding while a hit-reaction plays.
    private float _invulnerableUntil = -999f;

    public bool IsInvulnerable => isPlayer && Time.time < _invulnerableUntil;

    /// <summary>Grant hit-reaction i-frames (player only, no effect on enemies).</summary>
    public void GrantInvulnerability(float seconds)
    {
        if (!isPlayer || seconds <= 0f) return;
        _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + seconds);
    }

    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        if (IsInvulnerable) return;   // hit-reaction i-frames
        int finalDamage = Mathf.Max(1, damage);
        CurrentHealth   = Mathf.Max(0, CurrentHealth - finalDamage);
        onDamageTaken?.Invoke(finalDamage);
        if (CurrentHealth <= 0) Die();
    }

    /// <summary>
    /// Fall/hazard damage that can NEVER kill — clamps so the player is left
    /// at 1 HP minimum. Bypasses i-frames (falling always costs). Fires
    /// onDamageTaken so the health bar and damage numbers react normally.
    /// </summary>
    public void TakeFallDamage(int damage)
    {
        if (IsDead) return;
        int final = Mathf.Min(Mathf.Max(1, damage), Mathf.Max(0, CurrentHealth - 1));
        if (final <= 0) return;   // already at 1 HP — the void can't finish you

        CurrentHealth -= final;
        onDamageTaken?.Invoke(final);
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

        Debug.Log($"[EntityStats] {gameObject.name} died. " +
                  $"onDeath listeners: {onDeath.GetPersistentEventCount()} persistent, " +
                  $"isPlayer: {isPlayer}");

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

    // ── Save / Load ──

    /// <summary>
    /// Restores runtime stat values from a SaveData object.
    /// Called by SaveSystem.ApplyToStats after scene load.
    /// </summary>
    public void ApplySaveData(SaveData data)
    {
        _appliedSave   = data;   // remembered so Start can re-apply after InitStats

        MaxHealth      = data.maxHealth;
        CurrentHealth  = data.currentHealth;
        Strength       = data.strength;
        MaxStamina     = data.maxStamina;
        CurrentStamina = data.currentStamina;
        Speed          = data.speed;
        CurrentFloor   = data.currentFloor;

        EquippedWeapon = (WeaponType)data.equippedWeapon;
        NotifySpeedChanged();

        onStatsChanged?.Invoke();

        Debug.Log($"[EntityStats] Save data applied — HP:{CurrentHealth}/{MaxHealth} STR:{Strength} SPD:{Speed}");
    }
}
