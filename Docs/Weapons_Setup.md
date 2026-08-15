# Three-Weapon Player Setup

## Architecture decision: one Rat, three weapons (not three Rats)

The Player prefab stays a **single prefab**. Inside it are three weapon model GameObjects (Blade / Hammer / Bow) — only one is active at a time. `WeaponModelSwapper` listens to weapon changes and toggles which model is visible.

Three benefits:
- Save system already tracks `equippedWeapon` and re-equips on load — works unchanged.
- WeaponSelect already lets the player pick — works unchanged.
- Every Player tweak (Cinemachine, animator, stats) lives in one place. If you ever do three prefabs, every fix has to land three times.

## Files added

| Script | Purpose |
|---|---|
| `Arrow.cs` | Flying projectile. Travels along velocity, hits enemies via OnTriggerEnter, applies damage + stagger, self-destroys. |
| `BowController.cs` | Bow-specific attack logic — free-look shot with auto-target, aimed charged shot, jump-attack triple shot. |
| `WeaponModelSwapper.cs` | Toggles the right weapon model child and writes the Animator's `Weapon` int parameter. |

Plus a modification to `PlayerCombat.OnAttack` — when the equipped weapon is Bow, it delegates to `BowController`. Blade and Hammer keep the existing HitScan path unchanged.

---

## 1. Player prefab — add weapon model children

Open `Player.prefab`. Under whatever Mesh / Visual parent the rat has, add three child GameObjects:

```
Player
├── (rat mesh / animator)
│   └── Weapons              (empty, organizational)
│       ├── Blade            ← put blade model and any FX here
│       ├── Hammer           ← put hammer model here
│       └── Bow              ← put bow model here  (and the BowTip empty)
├── PlayerCombat
└── ...
```

Each weapon GameObject can hold:
- The mesh / renderer for the visible weapon
- Any per-weapon attached FX (trail, glow, etc.)

Only ONE of the three is active at runtime — the swapper handles toggling. In Editor, leave them all active so you can see and edit; the swapper will deactivate the inactive two as soon as Play starts.

## 2. Add the WeaponModelSwapper component

On the Player root: **Add Component → Weapon Model Swapper**. Drag the three weapon GameObjects into:

- `Blade Model` → your Blade GameObject
- `Hammer Model` → your Hammer GameObject
- `Bow Model` → your Bow GameObject

Set `Weapon Animator` if your weapon-bearing Animator isn't found automatically (most often it auto-detects the Animator on a child).

If your Animator has a **`Weapon`** int parameter, the swapper will write to it on every weapon change. Use that in Animator state machine transitions to pick the right attack animation per weapon. (Hammer slash, blade slash, bow draw etc.)

If your Animator doesn't have a `Weapon` parameter yet, add one — Animator window → Parameters tab → `+` → Int → name it `Weapon`.

## 3. Author the Arrow prefab

`Assets/Prefabs/` → right-click → **Create → Prefab** (or just drag from scene → Project).

The arrow prefab needs:

```
Arrow                    (empty root)
  ├── Mesh / Visual      (the arrow model — child so it can have offset/scale)
  ├── Collider           (SphereCollider radius 0.05 or CapsuleCollider — "Is Trigger" ON)
  └── Rigidbody          (Is Kinematic ON, Use Gravity OFF) — needed for trigger events
  └── Arrow.cs script
```

- The script handles all motion — do NOT add a non-kinematic Rigidbody.
- Set `Enemy Layer` on the Arrow prefab to the same layer your enemies use.
- `Die On World Hit` ON → arrow disappears on walls.
- `Lifetime` = ~2.5s default — failsafe in case the arrow flies into the void.

Drop the finished prefab into Project. You'll wire it into BowController next.

## 4. Add the BowController component

On the Player root: **Add Component → Bow Controller**.

| Field | What |
|---|---|
| `Arrow Prefab` | Drag your Arrow prefab here |
| `Arrow Spawn Point` | An empty child of the rat at the bow tip / head — arrows spawn from here |
| `Arrow Speed` | 28 default — bump for faster arrows |
| `Arrow Lifetime` | 2.5s |
| `Enemy Layer` | Layer mask for arrow's hit detection (same as PlayerCombat.enemyLayer) |
| `Max Charge Time` | 1.4s — full hold to reach max damage |
| `Min Charge Multiplier` | 1.0 — damage at zero charge (basically a tap-fire) |
| `Max Charge Multiplier` | 3.0 — damage at full charge |
| `Auto Target Half Angle` | 18° — cone in front for free-look aim assist |
| `Auto Target Range` | 14 units |
| `Auto Target Strength` | 0.5 — 0 = none, 1 = direct lock |
| `Triple Shot Count` | 3 |
| `Triple Shot Interval` | 0.08s between shots |
| `Triple Shot Spread` | 6° random horizontal jitter |

### Arrow spawn point

Create an empty GameObject named `BowTip` (or `ArrowSpawn`) as a child of the rat's head or bow model. Position it where arrows visually emerge. The script reads its world position when spawning.

## 5. Existing scripts — nothing changes for Blade/Hammer

`PlayerCombat.OnAttack` checks the equipped weapon:
- **Blade** or **Hammer** → existing HitScan logic (same as before)
- **Bow** → delegates to `BowController`

So no further wiring needed for Blade/Hammer. The hammer's slower attack speed comes from `PlayerStatBlock.hammerAttackSpeedFraction` which `RecalculateAttackCooldown` already applies.

## 6. Animator setup (the user already has animations)

The user noted: animations for all weapons already exist; hammer is mostly the blade; the jump-shoot animation pitches the rat ~45° down. Two ways to plug them in:

### Option A — same triggers, branch by Weapon int
- Keep `Attk` and `AirAttk` triggers unchanged.
- In the Animator state machine, transition from the base state on `Attk` trigger, but use the `Weapon` int parameter as an additional condition:
  - `Weapon == 0` → Blade Attack state
  - `Weapon == 1` → Hammer Attack state
  - `Weapon == 2` → Bow Draw state
- Same idea for `AirAttk`: Blade air spin / Hammer air spin / Bow downward triple.

### Option B — distinct triggers per weapon
- Add `BowAttk`, `BowAirAttk` triggers separately.
- In `PlayerCombat`, set the appropriate trigger based on equipped weapon. Easy code change if you prefer.

Option A is recommended — it keeps PlayerCombat code simpler and uses the already-existing trigger names.

## How the bow inputs flow at runtime

```
LMB pressed (grounded, not aiming)
    PlayerCombat.OnAttack → bow path → cooldown OK?
        → set "Attk" trigger
        → BowController.FreeLookShot()
            → optional auto-target nudge
            → SpawnArrow(direction, damage)

LMB pressed (in air)
    PlayerCombat.OnAttack → bow path → bow.JumpTripleShot()
        → fires 3 arrows ~0.08s apart along rat's forward
          (rat's forward is the animation's tilted-down pose)

RMB held, LMB pressed (grounded, aiming)
    PlayerCombat.OnAttack → bow path → bow.BeginAimedShot()
        → IsCharging = true
        → CurrentChargeFraction = 0..1 over maxChargeTime
    LMB released
        → BowController.Update detects release via Attack action
        → FireChargedAimedShot(chargeFraction)
        → SpawnArrow(forward, damage × lerp(min, max, fraction))
```

## HUD charge bar (optional, easy to add)

`BowController.CurrentChargeFraction` is a 0..1 public property. Drag a UI Image (fillType = Filled) and set its fill amount each frame:

```csharp
fillImage.fillAmount = bow.CurrentChargeFraction;
fillImage.enabled    = bow.IsCharging;
```

Stick it next to the AttackCooldown HUD on the MainUI canvas. Hide it when `IsCharging` is false.

## Test checklist

1. **Blade** (existing) — LMB swings, mid-air LMB does the spin attack. Confirm unchanged.
2. **Hammer** — swap via WeaponSelect (or runtime via PlayerCombat.EquipHammer in a test scene). Confirm swing animation plays, slower cooldown, bigger stagger.
3. **Bow free-look** — equip bow, run around, LMB → arrow fires forward. If an enemy is roughly in front, the arrow nudges toward them.
4. **Bow aimed basic** — RMB to aim, quick LMB tap → arrow fires straight forward (no auto-target while aiming).
5. **Bow aimed charged** — RMB hold + LMB hold for 1.4s+ → release → arrow fires for ~3× damage. Test partial holds (0.5s) for partial damage.
6. **Bow jump triple** — equip bow, jump, LMB mid-air → 3 arrows fan out toward the ground in front (animation pitches rat down). Each ~0.08s apart.
7. **Weapon model** — verify only the equipped weapon's model is visible at any time. Swap via WeaponSelect screen and confirm the model swaps too.

## Gotchas

- **Arrow doesn't damage enemies** → check Arrow's `Enemy Layer` and that enemies are on that layer. Also confirm the enemy has `EntityStats` (the arrow looks for it via `GetComponentInParent`).
- **Arrow goes through walls** → set `Die On World Hit` ON. Also ensure your walls have colliders.
- **Arrow hits the player on spawn** → push `arrowSpawnPoint` further forward away from the player's collider. Arrow has a `CompareTag("Player")` early-out as a safety.
- **No "Weapon" parameter on Animator** → either add it (Animator → Parameters → +Int → "Weapon"), or change the `Weapon Anim Param` field on WeaponModelSwapper to whatever your existing parameter is called.
- **Charged shot doesn't fire on release** → BowController looks up the `Attack` InputAction from the PlayerInput component. If your action is named something else, change the lookup string in BowController.Start.

## Quick recap of every component the Player needs now

```
Player.prefab
├── (existing) CharacterController, EntityStats, XPSystem, PlayerMovement, PlayerCombat
├── DamageNumberSpawner                ← from feedback UI pass
├── WeaponModelSwapper                 ← NEW (3 weapon model refs)
├── BowController                      ← NEW (arrow prefab, spawn point)
├── Weapons (empty)
│   ├── Blade        (model + FX)
│   ├── Hammer       (model + FX)
│   └── Bow          (model + FX + BowTip empty)
└── BowTip                             ← child empty marking arrow spawn position
```

That's the whole rig. Once it's wired, swapping weapons is a single-line call (`stats.EquipWeapon(...)`) and everything — model, animator, attack logic — follows.
