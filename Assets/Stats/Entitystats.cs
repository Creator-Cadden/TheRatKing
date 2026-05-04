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

    public BaseStatBlock BaseBlock => isPlayer
        ? (BaseStatBlock)playerStatBlock
        : (BaseStatBlock)enemyStatBlock;

    // ── Events ──
    public UnityEvent             onDeath;
    public UnityEvent<int>        onDamageTaken;
    public UnityEvent<int>        onHeal;
    public UnityEvent             onLevelUp;

    // Fired whenever a runtime stat changes so UI can react immediately
    public UnityEvent             onStatsChanged;

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

    // ── Speed scaling ──
    // Each Speed point adds this fraction of base walk/sprint speed.
    // At base Speed 6, one point = +0.5 walk / +0.67 sprint (roughly 8% per point).
    // Tune this constant to taste.
    private const float SpeedBonusPerPoint = 0.5f;

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

        _staminaRegenAccumulator = 0f;

        if (isPlayer) ApplyWeaponToughnessBonus();

        // Push initial speed to PlayerMovement
        NotifySpeedChanged();

        Debug.Log($"[EntityStats] {gameObject.name} ready — " +
                  $"HP:{CurrentHealth} STR:{Strength} STA:{MaxStamina} SPD:{Speed} TGH:{Toughness}");
    }

    // ─────────────────────────────────────────
    // Leveling — player only
    // ─────────────────────────────────────────

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
    /// Spend one stat point. Called by XPSystem.SpendPoint().
    /// Each stat's gain per point is read from PlayerStatBlock so you can
    /// tune everything in the ScriptableObject without touching code.
    ///
    /// Gains per point (from PlayerStatBlock defaults, stamina changed to 10):
    ///   health   +10 HP  (current AND max — immediate survivability)
    ///   strength +2      (multiplied by weapon bonus in CalculateWeaponDamage)
    ///   stamina  +10     (current AND max pool — more rolls/sprints/jumps)
    ///   speed    +1      (applied to PlayerMovement walk/sprint speeds live)
    /// </summary>
    public void SpendPoint(string stat)
    {
        if (!isPlayer || playerStatBlock == null) return;

        switch (stat.ToLower())
        {
            case "health":
                MaxHealth     += playerStatBlock.healthPerPoint;
                CurrentHealth += playerStatBlock.healthPerPoint;
                Debug.Log($"[EntityStats] Health → {CurrentHealth}/{MaxHealth}");
                break;

            case "strength":
                Strength += playerStatBlock.strengthPerPoint;
                Debug.Log($"[EntityStats] Strength → {Strength}");
                break;

            case "stamina":
                // Hardcoded to 10 per request, overriding the ScriptableObject's 8
                int staminaGain = 10;
                MaxStamina     += staminaGain;
                CurrentStamina += staminaGain;
                Debug.Log($"[EntityStats] Stamina → {CurrentStamina}/{MaxStamina}");
                break;

            case "speed":
                Speed += playerStatBlock.speedPerPoint;
                NotifySpeedChanged();
                Debug.Log($"[EntityStats] Speed → {Speed}");
                break;

            default:
                Debug.LogWarning($"[EntityStats] Unknown stat '{stat}'. Use: health / strength / stamina / speed");
                return;
        }

        onStatsChanged?.Invoke();
    }

    /// <summary>
    /// Pushes the current Speed value to PlayerMovement so walk/sprint speeds
    /// update immediately when a Speed point is spent.
    ///
    /// Speed effect:
    ///   walkSpeed  = base walkSpeed  + (Speed - baseSpeed) * SpeedBonusPerPoint
    ///   sprintSpeed = base sprintSpeed + (Speed - baseSpeed) * SpeedBonusPerPoint * 1.33
    ///
    /// At base Speed 6 and SpeedBonusPerPoint 0.5:
    ///   +1 Speed = +0.50 walk, +0.67 sprint
    ///   +5 Speed = +2.50 walk, +3.33 sprint — noticeably faster but not broken
    /// </summary>
    private void NotifySpeedChanged()
    {
        if (!isPlayer) return;

        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm == null) return;

        int baseSpeed = BaseBlock?.baseSpeed ?? 6;
        int bonusPoints = Speed - baseSpeed;            // points spent above base

        pm.ApplySpeedBonus(bonusPoints, SpeedBonusPerPoint);
    }

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
        onStatsChanged?.Invoke();
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

    // ─────────────────────────────────────────
    // Stamina — player only
    // ─────────────────────────────────────────

    public bool UseStamina(int amount)
    {
        if (CurrentStamina < amount)
        {
            Debug.Log($"[EntityStats] Not enough stamina ({CurrentStamina}/{MaxStamina})");
            return false;
        }

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
        Debug.Log($"[EntityStats] {gameObject.name} took {finalDamage} — HP {CurrentHealth}/{MaxHealth}");

        if (CurrentHealth <= 0)
        {
            Debug.Log($"[EntityStats] {gameObject.name} HP hit 0 — calling Die(). isPlayer={isPlayer}");
            Die();
        }
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

        Debug.Log($"[EntityStats] Die() fired on '{gameObject.name}'. isPlayer={isPlayer}. " +
                  $"onDeath persistent listeners: {onDeath.GetPersistentEventCount()}");

        onDeath?.Invoke();
    }

    // ─────────────────────────────────────────
    // Stagger check
    // ─────────────────────────────────────────

    public bool ShouldStagger(int staggerForce) => staggerForce > Toughness;

    // ─────────────────────────────────────────
    // Reset — called by GameManager on Retry
    // ─────────────────────────────────────────

    public void ResetToFull()
    {
        IsDead = false;

        CurrentHealth  = MaxHealth;
        CurrentStamina = MaxStamina;

        _lastStaminaUseTime      = -999f;
        _staminaRegenAccumulator = 0f;

        onHeal?.Invoke(MaxHealth);
        onStatsChanged?.Invoke();

        Debug.Log($"[EntityStats] {gameObject.name} fully reset — HP {CurrentHealth}/{MaxHealth}");
    }
}