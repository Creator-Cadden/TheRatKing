# Hammer Combat Setup

A new per-weapon controller — `HammerCombat.cs` — that mirrors the BowController pattern. Active only when the equipped weapon is Hammer. Owns all its own knobs: reach, timing, slam radius, cooldowns (NOT affected by the Speed stat).

## Architecture

```
PlayerCombat (router)
   ├── Bow equipped     → delegates to BowController
   ├── Hammer equipped  → delegates to HammerCombat        ← NEW
   └── Blade equipped   → existing HitScan path (the fallback default)
```

PlayerCombat becomes a thin router. Each weapon has its own controller for any logic that's weapon-specific.

## What's different about the hammer now

| Mechanic | Blade (PlayerCombat) | Hammer (HammerCombat) |
|---|---|---|
| Reach | `basicAttackRadius` 2.0 | `swingRadius` 3.5 — **bigger reach** |
| Arc | `basicAttackAngle` 60° | `swingAngle` 80° |
| Cooldown | Base 1.0s − Speed reductions | Fixed `swingCooldown` 1.4s (no Speed effect) |
| Windup | None (instant hit) | `swingWindup` 0.15s (telegraphed) |
| Jump attack | 360° spin (existing) | 360° **slam** with own radius + damage mult |
| Jump cooldown | Shared `jumpAttackCooldown` | Independent `slamCooldown` 2.0s |
| Slam damage | Same as basic | `slamDamageMultiplier` 1.5× |

**Speed stat no longer affects the hammer's cooldown.** Players can invest Speed for platforming and movement without accidentally turning the hammer into a fast weapon.

## Setup on the Player prefab

1. Open `Player.prefab` (Prefab Mode).
2. On the root → **Add Component → Hammer Combat**.
3. In the Inspector, the only mandatory wiring:
   - **Enemy Layer** — drag the same enemy layer mask you used on PlayerCombat
   - **Attack Origin** — auto-pulled from PlayerCombat if you leave it null
4. Save the prefab.

That's it. The script auto-finds PlayerCombat.attackOrigin in Awake if its own slot is empty.

## Inspector knobs

### Basic Swing
| Field | Default | Effect |
|---|---|---|
| `Swing Radius` | 3.5 | Forward reach. Bigger = more outranges enemies. |
| `Swing Angle` | 80° | Sweep arc width. Wider = hits more targets per swing. |
| `Swing Height` | 0.7 | Vertical hit volume |
| `Swing Cooldown` | 1.4s | Fixed delay between swings — **no Speed reduction** |
| `Swing Windup` | 0.15s | Visual telegraph time before the hit resolves |

### Jump Slam
| Field | Default | Effect |
|---|---|---|
| `Slam Radius` | 4.5 | Circular AoE radius around the player at impact |
| `Slam Height` | 1.2 | Vertical hit volume — taller helps catch enemies on slopes |
| `Slam Cooldown` | 2.0s | Fixed slam cooldown — separate from swing cooldown |
| `Slam Windup` | 0.1s | Tiny moment before impact lands |
| `Slam Damage Multiplier` | 1.5 | Multiplier on top of base hammer damage |

### Stagger
| Field | Default | Effect |
|---|---|---|
| `Stagger Force` | 8 | Same threshold as PlayerCombat's hammerStaggerForce. Interrupts enemies with Toughness < 8. |

## What I changed in PlayerCombat

- Added `_hammer` cache (looked up via `GetComponent<HammerCombat>()` in Awake).
- `OnAttack` now branches: Bow → BowController, **Hammer → HammerCombat**, Blade → existing path.
- The hammer branch only fires triggers + sets `_hasJumpAttacked` if `HammerCombat.TryBasicSwing` / `TryJumpSlam` actually fired (i.e. cooldown was ready).

The old PlayerCombat.RecalculateAttackCooldown logic stays — but it doesn't run for hammer attacks anymore because HammerCombat manages its own cooldowns. So the speed-driven cooldown adjustment is just dormant when hammer is equipped.

## How it plays now

- **Hammer + grunt swarm** → wide swing, multiple grunts in arc each cleave, all staggered by the +8 stagger force. Same 1.4s rhythm regardless of your Speed.
- **Hammer + Tough rat** → swing connects (3.5 reach hits before the rat's attack windup completes), Tough takes damage but isn't interrupted (Toughness 8 ≥ stagger force 8). Real exchange.
- **Hammer + jump slam** → leap, press LMB → 4.5 radius circle slam hits everything around the impact, 1.5× damage, slam cooldown 2s before you can do it again. Great for crowd control / opening.
- **Hammer + Speed stat** → Speed makes you run faster + dodge faster + platform reliably. Does NOT change attack rhythm. Build Speed freely without worrying about combat tempo.

## Tuning tips

If hammer feels too slow or too fast:
- Bump `Swing Cooldown` down to 1.1s for a snappier hammer
- Bump it up to 1.7s for a heavier "commit to the swing" feel
- `Slam Cooldown` is independent — tune separately

If the swing reach feels wrong:
- Compare to blade's 2.0 reach. Hammer at 3.5 should feel noticeably longer
- 4.0+ feels like a polearm, may be too much
- 3.0 feels like "barely longer" — bump if you want clear weapon identity

If the slam hits TOO many enemies:
- Drop `Slam Radius` to 3.5
- Or raise `Slam Damage Multiplier` slightly so it stays valuable even with fewer hits

If the stagger feels off:
- `Stagger Force` 8 = interrupts everyone Toughness 0-7. Most grunts (T 1-2) and Bait. Tough/Captain (T 8) push through.
- Set to 9 → also interrupts Tough rats. (Probably too strong unless you scale up enemy Toughness elsewhere.)
- Set to 6 → only Bait and weakest grunts get interrupted. Hammer is "heavy but trades blows with enemies."

## Animations

Animation triggers fire the same as before:
- Basic swing → `Attk` on rat body animator + hammer's own animator
- Jump slam → `AirAttk` on rat body animator + hammer's own animator

The `Weapon` int parameter on the rat body animator stays at 1 while hammer is equipped, so your Animator State Machine can branch to hammer-specific animations for both triggers.

If you want a distinct animation trigger name for the slam (e.g. `HammerSlam` instead of `AirAttk`), say the word and I'll add a configurable trigger name on HammerCombat.

## Gizmos to verify the geometry

Select the Player in the scene with `Show Gizmos` ON in HammerCombat:

- **Purple cone** = basic swing reach + angle
- **Magenta cylinder** = slam radius (360° around the player)

You can confirm the swing reach extends past blade's red cone and the slam circle is wide enough to feel impactful.

## Quick test loop

1. Hit Play in TestingArena with grunts.
2. Press `2` to equip Hammer (WeaponSwapDebug).
3. Walk up to a grunt → press LMB. Watch the windup → hit lands → grunt staggers.
4. Surround yourself with 3 grunts → jump → press LMB → slam connects all three with 1.5× damage.
5. Try investing Speed in Stat Menu (Tab) → confirm hammer cooldown is STILL ~1.4s (not faster).
6. Walk up to a Tough rat → swing → damage applies but Tough rat keeps attacking (Toughness 8 ≥ stagger 8). You must dodge his windups.

If anything feels wrong tell me which value, and we can dial it.
