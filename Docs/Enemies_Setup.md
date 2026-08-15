# Enemy Setup Reference

Three pieces in this pass:

1. **Persistent aggro** — already wired into every enemy via `EnemyStatBlock.damagedAggroDuration`. Defaults to 60s. Zero disables it.
2. **Captain prefab** — uses `CaptainCombat` alongside the regular components. Cycles cone → circle → rectangle.
3. **Fat Rat Boss prefab** — uses the new standalone `FatRatBoss` script (no EnemyAI / EnemyCombat).

---

## Grunt / Tough Rat (no changes from before)

Required on the GameObject:

- `EntityStats` (Is Player? **off**; `enemyStatBlock` = its EnemyStatBlock asset)
- `EnemyStatBlock` asset in Project — verify `damagedAggroDuration` is set to whatever feels right (60s default)
- `NavMeshAgent`
- `EnemyAI`
- `EnemyCombat`
- `EnemyDeathFade`, `EnemyXPDrop`, `EnemyHealthBar` as needed
- Animator with the usual triggers

All grunts and tough enemies automatically get the persistent-aggro upgrade — once hit, they chase for `damagedAggroDuration` seconds regardless of distance. Tune it per-enemy on each EnemyStatBlock asset.

## Captain (cycles attack shapes)

Same components as a regular enemy, **plus one**:

- `EntityStats`
- `EnemyStatBlock` asset (make a new one named e.g. `Captain_StatBlock`)
- `NavMeshAgent`
- `EnemyAI`
- `EnemyCombat`
- **`CaptainCombat`** ← new component
- Standard FX scripts as needed

### Configuring the Captain's EnemyStatBlock

Because the Captain cycles through all three shapes, its stat block needs reasonable values for **all three groups**:

- **Cone group**: `attackRadius`, `attackAngle`
- **Circle group**: `circleRadius`
- **Rectangle group**: `rectWidth`, `rectLength`

Set whatever makes sense for the Captain's flavor — e.g. wide cone (180°), big circle, long rectangle lunge.

The `attackShape` field on the stat block doesn't matter for Captains — `CaptainCombat` overrides it. (Setting it to the same value you want for the FIRST attack still works since `CaptainCombat` starts at index -1 and advances to 0 before the first attack.)

### CaptainCombat Inspector

- **Mode**: `Forward` (Cone → Circle → Rectangle → Cone…) or `Random` (avoids repeats).
- **Verbose**: log shape changes to console.

### How the dynamic reach works

`EnemyAI` now reads the *active* shape's reach from `EnemyCombat.CurrentAttackReach`, so the Captain will close to the right distance for whichever shape is queued. No extra wiring needed.

## Fat Rat Boss

This is a different beast — does **not** use EnemyAI/EnemyCombat. Use the new `FatRatBoss` script which handles its own state machine, movement, and attacks.

### Required components

- `EntityStats` (Is Player? **off**; `enemyStatBlock` = a boss-tier EnemyStatBlock asset with high HP and high Strength — used only for HP/death/strength scaling)
- `NavMeshAgent` (slow speed gets set from `moveSpeed` field in script)
- **`FatRatBoss`** ← new component
- Animator (optional but recommended) with these trigger names — the script calls them but tolerates missing ones:
  - `RollWindup` — ball-up animation
  - `Roll` — rolling animation
  - `JumpWindup` — crouching before jump
  - `InAir` — airborne pose
  - `Slam` — crash-down impact
  - `Death` — death state
  - Float param `Running` — drives walk animation (1 = running, 0 = idle)

### Optional setup

- An empty child `AttackOrigin` transform placed at the boss's center → drag into the `Attack Origin` slot. Without one, the script uses the boss's root transform position.

### Inspector key fields

Most defaults are reasonable starting points; tune to taste.

- **Detection & Movement**
  - `aggroRange` (15) — distance at which the boss notices you
  - `moveSpeed` (1.8) — slow ponderous chase
  - `stopRange` (4.5) — distance at which it stops to attack
- **Pre-Attack Pause**
  - `pauseDuration` (2s) — stands still this long before each windup
- **Attack Pattern**
  - `attackPattern` — default `[Roll, Roll, Slam]`. Edit the array to change the rhythm.
- **Roll Attack**
  - `rollWindupDuration` (1.5s), `rollSpeed` (14), `rollDistance` (12), `rollWidth` (1.8), `rollDamage` (35), `rollRecoverDuration` (1.2s)
- **Slam Attack**
  - `jumpWindupDuration` (0.45s), `jumpHeight` (6), `airDuration` (1.2s), `slamRadius` (3.5), `slamDamage` (50), `slamRecoverDuration` (1.4s)
- **References**
  - `playerLayer` — set to your player's collision layer. Without it the boss hits nothing.

### Attack flow

**Roll**
1. Stops, faces player, locks direction.
2. Rectangle indicator appears along the locked path, fades in over `rollWindupDuration`.
3. After windup, indicator goes orange and the boss commits — rolls straight along the rectangle at `rollSpeed` until it covers `rollDistance`.
4. Anyone whose collider overlaps the rolling body during the charge takes `rollDamage` (one-shot per roll — the boss can't hit you twice with the same roll).
5. Recover, then resume chase.

**Slam**
1. Stops, faces player.
2. `jumpWindupDuration` crouch.
3. At the moment it leaves the ground:
   - The slam circle locks onto the **player's current XZ**.
   - The boss visually rises in a parabolic arc.
4. After `airDuration`, the boss crashes onto the locked landing spot.
5. Damage resolves against anyone inside the circle radius **at impact** — leave the circle before the crash and you're safe.
6. Indicator fades during recovery, then resume chase.

### Tuning tips

- If players never get hit by the slam, lower `airDuration` so they have less time to dodge.
- If the roll feels too short, increase `rollDistance` — the rectangle indicator and actual roll length stay in sync.
- If the boss looks janky leaving the ground during the slam, raise `jumpHeight` for a more dramatic arc.

### Required scene setup

- Player tagged `"Player"` (same as all other enemies expect).
- Bake a NavMesh under the arena floor.
- Make sure `playerLayer` on the boss matches the layer your player's CharacterController collider is on.

---

## Quick prefab checklist

```
Grunt_Rat.prefab
  ├ EntityStats (enemy, with Grunt EnemyStatBlock)
  ├ NavMeshAgent
  ├ EnemyAI
  ├ EnemyCombat
  ├ Animator
  └ Mesh / collider / FX children

Tough_Rat.prefab
  └ same as Grunt, different EnemyStatBlock with bigger numbers

Captain_Rat.prefab
  ├ EntityStats (enemy, with Captain EnemyStatBlock — needs all 3 shape groups configured)
  ├ NavMeshAgent
  ├ EnemyAI
  ├ EnemyCombat
  ├ CaptainCombat                 ← new
  └ Animator + mesh + FX

FatRat_Boss.prefab
  ├ EntityStats (enemy, with Boss EnemyStatBlock for HP/Strength scaling)
  ├ NavMeshAgent
  ├ FatRatBoss                    ← new (replaces EnemyAI + EnemyCombat)
  └ Animator + mesh + FX (large scale)
```
