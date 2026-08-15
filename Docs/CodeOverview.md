# The Rat King — Code Overview

One-page map of every system for anyone new to the codebase. Deeper docs for each system live in this same `Docs/` folder (`*_Setup.md`). Unity 6 (6000.3.12f1), new Input System.

## The 30-second tour

Third-person action game. One Player prefab with three swappable weapons (Blade / Hammer / Bow). Kill every enemy in a room → gate lowers → walk into a LevelTransition trigger → next level. lvl5 is the boss (per-weapon intro cutscene, screen-space boss bar, minion waves). Progress saves automatically at the start of each scene — never mid-level.

## Folder map (`Assets/Scripts/`)

| Folder | Owns | Key scripts |
|---|---|---|
| `Core/` | Persistent singleton, scene flow, saves | **GameManager** (save slots, death/retry, scene loading, test mode), SceneTest (debug logger) |
| `Level/` | Per-scene progression logic | **EncounterController** (enemy list → gate opens), LevelTransition (trigger → next scene), BossCutsceneController, DeathPlane, DoorBlock (legacy gate, still in lvl1–3) |
| `Player/` | Everything on the Player prefab | **PlayerCombat** (input router) → BladeCombat / HammerCombat / BowController (+Arrow), PlayerMovement, WeaponModelSwapper, WeaponSwapDebug (keys 1/2/3), AttackRipple, AttackCooldownHUD, PlayerDirectionRing, SpawnPoint |
| `Enemy/` | Enemy brains + boss | **EnemyAI** (chase/aggro, drives any **EnemyCombatBase**); per-enemy combat scripts: **GruntCombat** (Tier 1 — windup + moving hit sphere at attackPoint, no decal), **EnemyCombat** (Tier 2 decals, data-driven from the stat block's Decal Attacks list + cycle mode — Tough/Captain); CaptainCombat (DEPRECATED — cycle moved to stat block); **FatRatBoss** (self-contained boss state machine); BossMinionSpawner; EnemyDeathFade; EnemyXPDrop (reads xpReward from stat block); AttackShape (shared enum) |
| `Stats/` | Numbers and health | **EntityStats** (HP/damage/death events for player AND enemies), BaseStatBlock → PlayerStatBlock / EnemyStatBlock (ScriptableObjects = all tuning numbers), health bars |
| `ScreenUI/` | HUD | BossHealthBarUI, StatMenuUI (Tab), XPSystem + XP/level-up indicators, DamageNumberSpawner, StaminaBarUI, CursorManager, CanvasCameraBinder |
| `MenusSaves/` | Menus + persistence | MainMenuUI, WeaponSelectUI, PauseMenu, DeathScreen, CreditsScreen, **SaveSystem** + SaveData (JSON per slot) |
| `Audio/` | Sound | AudioManager (singleton, SoundType enum) |

## The flows that matter

**Attack input** → `PlayerCombat.OnAttack` → branches on `EntityStats.EquippedWeapon` → BladeCombat / HammerCombat / BowController does the actual hit logic. PlayerCombat also fires `Attk`/`AirAttk` triggers on BOTH the rat body animator and the active weapon's animator (via `WeaponModelSwapper.ActiveWeaponAnimator`). The `Weapon` int animator param (0=Blade 1=Hammer 2=Bow) routes body animations per weapon.

**Damage** → attacker calls `EntityStats.TakeDamage` → events fan out: DamageNumberSpawner (floating number), health bars, EnemyXPDrop → XPSystem on death, EncounterController counts the kill. Knockback: if attacker Toughness > target Toughness, `EnemyAI.ApplyKnockback` pushes the enemy (per-weapon force − toughness reduction, values on EnemyStatBlock). Design intent: dodging over stagger-locking; only the hammer interrupts weak grunts.

**Scene / save** → LevelTransition trigger → `GameManager.TransitionToLevel` captures current stats into SaveData → loads scene → `OnSceneLoaded` re-finds the player, applies the save, writes checkpoint to disk. Saves happen ONLY here — no mid-level saving by design. Test mode (main menu → Test Arena) skips all save writes.

**Boss level (lvl5)** → BossCutsceneController picks the mp4 for the equipped weapon → freezes player, plays video → on end/skip calls `BossHealthBarUI.PlayIntro()` (bar grows in) → FatRatBoss runs its own state machine (pattern: Roll, Roll, Slam — telegraphed indicators, roll is NavMesh-clamped so it can't leave the arena) → BossMinionSpawner adds grunt waves at HP thresholds (75/50/25%) + on a timer.

## Gotchas a new programmer must know

- **Tuning numbers live in ScriptableObject assets** (PlayerStatBlock / EnemyStatBlock in the Project window), not in scripts. Editing an asset changes every prefab using it.
- **BossMinionSpawner sets fields on freshly-Instantiated minions BEFORE their `Start()` runs** (speedMultiplier, permanentlyAggroed). EnemyAI.Start() bakes these in — don't move that logic to Awake on the spawner side or later than Start on the AI side.
- **Player.prefab is a prefab VARIANT of PlayerBackup.prefab.** Never delete PlayerBackup.
- **Animator transitions: "Has Exit Time" must be OFF on all attack transitions** — leaving it on causes the delayed-attack bug (see `Animator_Architecture.md`).
- **Scene names are referenced by string** (GameManager fields, LevelTransition.nextSceneName). Renaming a scene file breaks transitions silently.
- **FatRatBoss is intentionally separate** from EnemyAI/EnemyCombat. Don't try to unify them.
- **There is no lvl4** — lvl3 transitions straight to lvl5. Intentional (for now).
- New scenes need: the MainLevelPrefabs prefabs dragged in (Player, CamRig, UIs, Pixelizer…), a baked NavMesh, a SpawnPoint-tagged empty, and an entry in Build Profiles' scene list.

## Debug tools

Keys 1/2/3 swap weapons at runtime (WeaponSwapDebug). Most scripts have a `verbose` Inspector toggle for console logging. Gizmos show attack ranges (select the Player/enemy in Scene view). Main menu → Test Arena button loads TestingArena with full stats and no save writes.
