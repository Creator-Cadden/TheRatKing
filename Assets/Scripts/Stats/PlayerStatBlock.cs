using UnityEngine;

// Right-click in Project -> Create -> Rat King -> Player Stat Block
/// <summary>
/// ScriptableObject with every player tuning knob: level-up gains, floor caps,
/// per-weapon strength multipliers / toughness bonuses / speed fractions,
/// stamina costs and regen. One shared asset, referenced by the Player prefab via EntityStats.
/// </summary>
[CreateAssetMenu(fileName = "PlayerStatBlock", menuName = "Rat King/Player Stat Block")]
public class PlayerStatBlock : BaseStatBlock
{
    // ── PLAYER CORE (moved off BaseStatBlock — enemies don't use these) ──

    [Header("Player Core")]
    [Tooltip("Pool spent on dodges and sprinting")]
    public int baseStamina = 50;

    [Tooltip("Movement speed and attack recovery rate")]
    public int baseSpeed = 5;

    // ── LEVELING ──

    [Header("Per Level Point")]
    [Tooltip("+HP gained each time the player puts a point into Health")]
    public int healthPerPoint = 10;

    [Tooltip("+Strength gained each time the player puts a point into Strength")]
    public int strengthPerPoint = 2;

    [Tooltip("+Stamina pool gained each time the player puts a point into Stamina")]
    public int staminaPerPoint = 8;

    [Tooltip("+Speed gained each time the player puts a point into Speed")]
    public int speedPerPoint = 1;

    [Header("Floor Level Caps")]
    public int floorOneCap   = 5;
    public int floorTwoCap   = 10;
    public int floorThreeCap = 15;

    // ── IMPACT (stagger stat — offense only) ──
    // Reaction on hit = Impact − enemy Toughness (0–5 tiers):
    //   below 0 → shrug (nothing) · exactly 0 → flinch (delays a basic windup)
    //   · +1 or more → stagger (cancels action + lockout + knockback).
    // Decal (Tier 2) attacks can NEVER be cancelled — staggers only DELAY their
    // windup (capped). True decal interruption is reserved for future perks.

    [Header("Impact (per weapon: basic / special = jump-charged)")]
    public int bladeImpactBasic    = 1;
    public int bladeImpactSpecial  = 2;
    public int hammerImpactBasic   = 3;
    public int hammerImpactSpecial = 4;
    public int bowImpactBasic      = 1;
    public int bowImpactSpecial    = 2;

    // ── ATTACK SPEED — per-weapon base cooldown, Speed-scaled ──
    // Effective cooldown = base × (1 − 4% × Speed points above base), never
    // below the per-weapon floor. One system for ALL weapons (design doc).

    // Bases start DELIBERATE so investing Speed has real purpose — the floor is
    // roughly the old "fast" value, i.e. a full Speed build EARNS the fast blade
    // instead of starting with it. (−4%/pt: floor reached around 11-12 points.)
    [Header("Attack Speed (base cooldown seconds / floor)")]
    public float bladeAttackCooldown  = 0.55f;
    public float bladeCooldownFloor   = 0.30f;
    public float hammerAttackCooldown = 1.10f;
    public float hammerCooldownFloor  = 0.65f;
    public float bowAttackCooldown    = 0.75f;
    public float bowCooldownFloor     = 0.45f;

    // ── WEAPON DAMAGE — Souls-style formula: damage = weaponBase + Strength × multiplier.
    // The flat base makes each Strength point a smooth % gain instead of doubling
    // damage at low Strength (the old no-base formula forced crazy multipliers). ──

    [Header("Blade")]
    [Tooltip("Flat base damage added before Strength scaling. Blade dmg = this + Strength × multiplier.")]
    public int bladeBaseDamage = 5;

    [Tooltip("Blade damage gained per point of Strength.")]
    public int bladeStrengthMultiplier = 1;

    [Header("Hammer")]
    [Tooltip("Flat base damage added before Strength scaling. Hammer dmg = this + Strength × multiplier.")]
    public int hammerBaseDamage = 30;

    [Tooltip("Hammer damage gained per point of Strength.")]
    public int hammerStrengthMultiplier = 2;

    [Tooltip("Fraction of normal move speed while hammer is equipped. 0.667 = one third reduction.")]
    public float hammerMoveSpeedFraction = 0.667f;

    [Tooltip("UNUSED — replaced by the per-weapon Attack Speed section above " +
             "(hammerAttackCooldown 0.80s). Kept so asset data isn't lost.")]
    public float hammerAttackSpeedFraction = 0.5f;

    [Header("Bow")]
    [Tooltip("Flat base damage added before Strength scaling. Bow quick shot = this + Strength × multiplier.")]
    public int bowBaseDamage = 0;

    [Tooltip("Bow damage gained per point of Strength.")]
    public int bowStrengthMultiplier = 1;

    [Tooltip("Charged aimed shot multiplies damage by this. Default 3 = triple damage.")]
    public float bowChargedMultiplier = 3f;

    [Tooltip("Fraction of normal move speed while aiming the bow. 0.667 = one third reduction.")]
    public float bowAimMoveSpeedFraction = 0.667f;

    // ── ACTION STAMINA COSTS ──

    [Header("Action Stamina Costs")]
    [Tooltip("Stamina drained per second while sprinting")]
    public float sprintStaminaPerSecond = 5f;

    [Tooltip("Flat stamina cost per roll/dodge. The roll fires with ANY stamina " +
             "remaining (drains to 0 if there isn't enough) — only a fully empty " +
             "bar blocks it.")]
    public int rollStaminaCost = 15;

    [Tooltip("UNUSED — jumping is free since the July 2026 playtest. " +
             "Kept so existing stat block assets don't lose serialized data.")]
    public int jumpStaminaCost = 5;

    // Attack stamina costs (design doc). All consumed via UseStaminaPartial —
    // any stamina lets the attack fire (drains to 0), only an empty bar blocks.
    [Header("Attack Stamina Costs")]
    [Tooltip("Blade jump attack cost (basic slashes are free).")]
    public int bladeJumpStaminaCost = 10;

    [Tooltip("Hammer basic swing cost — every heavy swing costs.")]
    public int hammerSwingStaminaCost = 10;

    [Tooltip("Hammer jump slam cost.")]
    public int hammerSlamStaminaCost = 20;

    [Tooltip("Bow charged shot cost (≥60% draw; quick releases are free).")]
    public int bowChargedStaminaCost = 10;

    // ── STAMINA REGEN ──

    [Header("Stamina Regen")]
    [Tooltip("Stamina recovered per second after the regen delay")]
    public float staminaRegenRate = 15f;

    [Tooltip("Seconds after last stamina use before regen kicks in")]
    public float staminaRegenDelay = 1.2f;
}
