# Enemy Combat Rework Plan — "More Motion, Real Windups"

Goal: enemy attacks should be read from the enemy's BODY (Souls-style animation telegraphs), not from a floor decal on a motionless rat. Detection math (cone/circle/rect overlap checks) stays exactly as-is — only the sequencing, motion, and presentation change.

## The Two-Tier Telegraph Grammar (design decision, July 2026)

Every attack in the game belongs to exactly one tier. The rule: **decals mark attacks whose danger zone is decoupled from the attacker's body.**

- **Tier 1 — no decal (basic attacks):** melee swipes at body range. Telegraph = windup animation + audio cue. Player reads the enemy's body and dodges on reaction. (Same as FFXIV auto-attacks having no marker.)
- **Tier 2 — decal (specials / AoEs):** area attacks, charges, and ground-targeted effects. Telegraph = shape decal (+ animation for personality). Decal says "don't read the body — MOVE."

Consistency is sacred: never put a decal on a weak poke, never skip it on a lethal AoE. And every Tier 1 attack MUST have a distinct audio cue, because off-camera enemies are unreadable in third person without sound.

### Enemy roster mapped to the tiers

| Enemy | Tier 1 (no decal) | Tier 2 (decal) | Notes |
|---|---|---|---|
| Grunt | Scratch/lunge | — | 100% body-read. The rhythm enemy. |
| Tough rat | Close-range swipe | **Rect dash-charge** | Bull charge: paw-scrape windup, rect decal flashes down the corridor, knockback-immune during dash, OVERSHOOTS past the player → chunky recovery = punish window. Mini version of the boss roll → trains players for lvl5. Use at 4–8m / when kited; swipe up close. |
| Captain (per-level miniboss) | Fast low-damage swipe (filler — makes melee range never free; NEVER fires during post-move recovery, or it breaks the punish windows) | Fixed rotation: far 90° cone → circle slam → rect lunge | The "shapes guy" and the midterm exam. Cone (long reach, quarter-circle) punishes backpedaling → forces lateral dodge/flank, longest recovery = punish window. Circle slam punishes hitbox-hugging. Rect lunge punishes kiting. Fixed order = learnable rotation (CaptainCombat Forward mode) — cracking the pattern IS the fight. Escalate per level: lvl1 slow rotation; lvl2 faster / delayed second cone swung back the other way; lvl3 randomizes order below 50% HP. Presentation: bigger silhouette (crown/cape), named health bar, aggro audio sting, guaranteed XP reward, gated room via EncounterController. Three visibly different windup poses so attentive players can pre-read the shape. |
| Balloon rat (planned, floats) | — | Ground-targeted AoE circles | All-decal is correct: airborne attacker, danger zone is on the floor. Still gets body language (puff up, wobble, strained squeak) for personality + timing. Role = zoning while grunts pressure. Must have an answer for all 3 weapons (e.g. descends into melee reach when attacking). Cap simultaneous decal-painters per fight. |
| FatRatBoss | — (fast grunt minions ARE its tier-1 pressure — FFXIV model: boss does mechanics, adds force movement) | Roll/roll/slam rotation + NEW reactive stomp | Stays all-decal (final exam of decal reading). NEW: anti-hug stomp — small circle decal around its body, FAST windup (~0.4s), modest damage; fires reactively when the player sits in melee range too long (currently hugging the boss is completely safe — roll passes by, slam's 1.2s airtime lets you walk out of the 3.5m circle). This is Asylum Demon's butt-slam translated into decal grammar. Optional final-phase (<25% HP): rotation ~20% faster, double roll becomes triple. DS1 first-boss formula for reference: charge (=roll) + leap slam (=slam) + close-range answer (=stomp), 3-5 moves total. |

Consolidation note: the ToughCircle/ToughCone/ToughRectangle prefab variants should collapse into ONE Tough with the swipe + dash kit — shape variety is now the Captain's job. Level-design caution: rect dashes can be undodgeable in narrow corridors; place toughs accordingly.

### The curriculum structure (why this roster works)

Each enemy teaches one boss pattern; the boss (lvl5) is the final exam:

- Tough's rect dash = mini boss ROLL → teaches sidestep-the-corridor
- Balloon rat's ground circle (drops down with windup area, then vulnerable) = mini boss SLAM → teaches leave-the-zone + punish-the-recovery
- Captain's rotation = the midterm — combines all shapes, teaches pattern recognition
- Miniboss principle (from Souls/Hades/MonHun): tougher = MORE DECISIONS, not more HP. Stat bumps are seasoning; new mixups, punish windows, and pattern reads are the meal. Avoid pure damage sponges.

## Current flow (why it feels robotic)

1. EnemyAI chases → hard-stops at stopRange
2. `EnemyCombat.TryStartAttack` → faces player, instantly locks rotation, shows the shape decal fading in
3. Enemy stands in IDLE the whole windup — no windup animation exists
4. Timer expires → `ExecuteAttack` → attack anim + sound finally fire
5. Animation event → `OnAttackHitFrame()` → shape overlap check → damage

Problems: no body-language telegraph, full stop kills momentum, decal is the only threat signal, timing is constant, instant rotation lock makes dodging trivial.

## Phase 1 — Animation-first windup (do first, biggest payoff)

- New `Windup` trigger on enemy animators, fired inside `TryStartAttack`
- Each enemy gets a windup pose/clip: rear back, claw raised, weight shift
- Windup audio cue at windup START (hiss/screech) — currently sound only plays at execute
- Animator: Blend Tree → Windup (Has Exit Time OFF), Windup → Attack driven by the existing execute timing
- Hit continues to land via the attack clip's animation event → `OnAttackHitFrame()` (already implemented)
- Ideally: `attackWindupTime` per stat block matches the windup clip length so code timer and animation agree

## Phase 2 — Motion into the attack

- Allow windup to begin while still approaching (slightly outside final reach)
- During windup: forward drift at ~30% move speed instead of a full stop (`windupDriftSpeed`)
- During the scratch's active frames: short lunge forward (`lungeSpeed`, `lungeDistance`) so the attack carries momentum — the rectangle attack especially should BE a lunge
- Rotation: track the player at limited turn rate for the first ~60% of windup, then hard-lock (`trackTurnSpeed`, `lockAtWindupFraction`). Sidestep after the lock = clean earned dodge

## Phase 3 — Demote the indicator

- Grunt basic scratch: decal OFF by default. Telegraph = windup anim + sound + brief red emission flash on the enemy's material
- Keep decals only where animation can't communicate the area: Captain's large shapes, circle AoEs, boss roll rectangle + slam circle (those already work well)
- Where kept: sharp flash in the last ~40% of windup, not a slow fade-in (lingering decals read as floor UI)
- New indicator mode per stat block: `Always / FlashOnly / Hidden`

## Phase 4 — Timing variety

- Windup duration jitter: ×Random(0.85–1.25) per attack
- `delayChance`: small chance to hold the windup pose an extra 0.2–0.4s (punishes panic-dodging — the Souls "delayed swing")
- Cooldown jitter so groups don't attack in lockstep

## Phase 5 — Impact feel

- Hitstop ~0.06s when an enemy attack connects with the player (brief timeScale dip or animator pause)
- Small camera nudge/shake on player hit
- (Player-side hitstop on melee connects would also help — separate task)

## What stays unchanged

- Shape overlap hit checks (cheap, reliable — only their trigger source changes from timer to animation event)
- EnemyStatBlock as the data home (new fields added, nothing removed)
- CaptainCombat shape cycling, knockback system, EncounterController, FatRatBoss (boss already has good telegraphs)

## New EnemyStatBlock fields

`windupDriftSpeed`, `lungeSpeed`, `lungeDistance`, `trackTurnSpeed`, `lockAtWindupFraction`, `windupTimeJitter`, `delayChance`, `indicatorMode`

## Unity-side work (not code)

- Author/assign windup clips per enemy (grunt, tough, captain) — hammer-windup-style anticipation poses
- Add `Windup` trigger + transitions to each enemy Animator Controller (Has Exit Time OFF on entry)
- Verify each attack clip has the `OnAttackHitFrame` / `OnAttackEnd` animation events
- New windup audio clip(s) in AudioManager

## Suggested order + testing

1. Phase 1 on ONE grunt prefab in TestingArena → playtest feel
2. Phase 2 drift + lunge on same grunt → check dodge timing still feels fair
3. Phase 3 decal-off for that grunt → confirm attacks are still readable without it
4. Roll out to tough/captain, then Phases 4–5
5. Re-run the feedback form playtest and compare notes
