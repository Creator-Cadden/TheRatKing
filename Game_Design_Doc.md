# The Rat King — Game Design Doc

> Working design doc. Not formal — just what we need in one place. Update freely.
>
> **Tabs to make** (each = one page/tab in Notion or Google Docs):
> 1. Overview & Pitch *(includes synopsis + hook/loop)*
> 2. Gameplay
> 3. Controls
> 4. Combat — *(intentionally blank — overhaul pending)*
> 5. Tower & Floors
> 6. Room Types & Pacing
> 7. Weapons & Progression
> 8. Enemies
> 9. Bosses
> 10. Leveling & Upgrades
> 11. Art & Theme
> 12. Vision for This Quarter
> 13. Quarter Roadmap (Weeks 1–4)
> 14. Pricing & Rewards *(see Pricing_and_Rewards.md)*
> 15. Marketing / Kickstarter *(see Kickstarter_Promo_Plan.md)*
> 16. Open Questions / TODO

---

## 1. Overview & Pitch

**What it is:** A silly action-RPG **tower crawler** with platforming and (soon) overhauled combat. Play a young rat climbing through floors of enemies to reach the Rat King. The core vibe is **silliness first** — lean into the humor, then make the mechanics as fun as possible around it.

**Genre / tags:** Action RPG · Tower Crawler · Platformer · Singleplayer · 3D · Comedy/Silly

**One-liner:** *"Climb the tower. Get buff. Fight your way to the Rat King."*

**Reference games (TBD — finalize later):**
- Soulslike game — *(TBD)*
- *(space for more)*
- *(space for more)*

**Team:** 2 people. **Engine:** Unity 6.

### Synopsis

Follow a young rat as he enters **the King's Gambit** — slash your way through floor after floor of enemies as you try to reach the Rat King at the top. Gather **stat upgrades randomly** as you wander the floors and grow stronger. But careful: the further you climb and the stronger you get… will the enemies keep pace with you?

*(Silly, light tone throughout — the premise is a rat entering a ridiculous gauntlet to challenge the king.)*

### Hook / Loop

**Core loop:** Enter a room → fight → upgrade → repeat → boss.

**Main hook: silliness.** That's the identity — focus on it. Everything else (mechanics, feel, variety) should be fun, but the humor is what makes it *this* game.

**Ways to mix up the loop:**
- **Mini-bosses** between floors or mid-floor to break the rhythm.
- **Mini-games** as an alternate change-of-pace room (if feasible).
- Silly upgrades, silly enemies, silly reactions — humor baked into the mechanics, not just the art.

---

## 2. Gameplay

Very simple by design. When you enter a room, a **wave of evil rats** rushes you. Attack them to clear the room. Spend **experience to upgrade yourself** — either in real time during the fight or after the room is cleared — then move on to the next room, and eventually the next floor.

- Clear-to-advance, linear.
- XP → upgrades (some random stat gains per the synopsis).
- Simple to pick up; depth comes from combat feel + upgrade variety.

*(This tab will expand as systems get built out.)*

---

## 3. Controls

- **WASD** — movement
- **Left click** — attack
- **Right click** — aim
- **Space** — jump
- **Shift** — sprint
- **Combos / context moves:** different inputs combine into different attacks. Example: **Jump + Attack = air attack.**

*(Combo list to expand as the combat overhaul defines moves.)*

---

## 4. Combat

*(Intentionally left blank — combat is getting a full overhaul from the current version. Fill in after the redesign.)*

---

## 5. Tower & Floors

**Structure:** Linear vertical climb. 5–6 floors, each ending in a boss. Each floor = several rooms + one boss room; room count increases as you climb.

**Floor themes:**
- **Floor 1 — Sewer:** classic sewer, brick tubing, circular walls.
- **Floor 2 — Mushrooms:** heavy platforming; bounce/spore potential.
- **Floor 3 — Roots & Wood:** climb toward a dead root in the center; verticality.
- **Floor 4 — TBD.**
- **Floors 5–6 — TBD.**
- **Boss rooms:** colosseum-style arenas in the shared brick tubing.

**Unifying look:** everything sits in a sewer/brick-tube structure, each floor adding its own biome.

---

## 6. Room Types & Pacing

**Principle:** intensity rises and falls in a rhythm — tension, release, bigger tension, boss. Not a flat wall of fights.

**Room types:** combat · combat + platforming · pure platforming (breathers + hidden rewards) · rest/utility (vendor / NPC / upgrade) · elite/optional · boss · *(optional)* mini-game room.

**Sample floor curve:** easy combat → light platforming → combat+platforming → big brawl → **rest/vendor** → platforming breather (hidden branch) → elite fight → **boss**.

**Rules:** rest/vendor room shortly before the boss; reward tough fights with a breather; let each floor's gimmick live in both combat AND platforming.

---

## 7. Weapons & Progression

- **3 weapon types** now, more later.
- **Unlocked as you climb** — beat bosses or find them.
- Weapon variety is the main lever for run-to-run variety; make each feel distinct.

*(Fill in: the 3 current weapon types + how each plays.)*

---

## 8. Enemies

- Waves of **evil rats** rush the player on room entry.
- Room-locked, **wave-based spawns** (not scattered/pre-placed).
- Mix archetypes: swarmers, tanky bruisers, ranged pokers.
- Enemies pursue/pressure the player.
- New enemy type per floor, taught in that floor's warm-up room.
- Keep them **silly** in look and behavior.

*(Fill in: enemy roster per floor.)*

---

## 9. Bosses

- **5–6 total**, one per floor, in colosseum arenas.
- Optional **mini-bosses** to mix up the loop.
- Each boss tests what the floor taught (mushroom → spores, roots → climb, etc.).
- Payoff/climax of each floor. Lean into silly boss personalities.

*(Fill in: boss concepts per floor.)*

---

## 10. Leveling & Upgrades

- **Kills grant XP.** Spend it to upgrade — in real time or after clearing a room.
- **Random stat upgrades** gathered while wandering floors (per synopsis) — adds light run variety.
- Make leveling **feel good**: clear feedback + a real power spike.
- Tie upgrades into weapon-unlock progression so climbing = getting stronger.
- Open question: do enemies **scale** with the player? (The synopsis teases this — decide how it works.)

*(Fill in: stat list, upgrade pool, whether upgrades are chosen or random.)*

---

## 11. Art & Theme

- **Intentionally simple graphics** — fun + vibe over fidelity.
- **Unifying look:** sewer / brick-tube structure throughout.
- **Per-floor identity:** sewer → mushrooms → roots/wood → (TBD) → colosseum boss arenas.
- Keep the rat-kingdom theme and **silly tone** consistent across enemies, NPCs, and vendors.
- UI/VFX don't need to be final this quarter (see Vision).

---

## 12. Vision for This Quarter

Small team, so the scope this quarter is **relatively small and focused**. The main goal is a **functional game with a start scene and a game-over scene** — a complete, playable loop end to end. UI and VFX do **not** need to be final; the target is something **worth showing** by the end of the quarter. A big workstream this quarter is the **combat overhaul** (see the blank Combat tab).

---

## 13. Quarter Roadmap (Weeks 1–4)

> Suggested sprint plan toward "functional game, start → game over." Adjust to fit — these are starting points, not gospel.

**Week 1 — Foundations**
- Start scene + main menu (rough is fine).
- Core movement (WASD, jump, sprint) feeling right.
- One test room with one enemy you can fight (placeholder combat).

**Week 2 — The Loop**
- Room-lock + **wave spawner** (enemies come in waves, exits lock until cleared).
- Clear-to-advance between rooms.
- Basic upgrade on room clear (even one stat).

**Week 3 — Floor & Fail State**
- Chain rooms into a full playable floor → placeholder boss.
- **Game-over scene** + restart flow.
- Rough UI: health + XP/level readout.

**Week 4 — Overhaul & Showable Build**
- **Combat overhaul** pass (replace current combat).
- Polish the full loop: start → play a floor → win/lose → game over.
- Bugfix + buffer. End with a build **worth showing**.

*(Combat overhaul may span more than Week 4 — treat it as a parallel workstream if needed.)*

---

## 14. Pricing & Rewards

See **Pricing_and_Rewards.md**. Steam: $10 launch → $12 after content → $15 if randomization/replay added. Kickstarter tiers: $5 / $6 / $8 / $15 / $30 / $50 / $100. Backers always pay less than release.

---

## 15. Marketing / Kickstarter

See **Kickstarter_Promo_Plan.md**. Cancel the current campaign, relaunch when ready (need demo, 60–90s trailer, email list, launch-day audience). Discord + demo are free.

---

## 16. Open Questions / TODO

- [ ] **Combat overhaul** — design the new combat (fills the blank Combat tab).
- [ ] Do enemies **scale** with the player? How?
- [ ] Are upgrades **random, chosen, or both**? Define the stat/upgrade pool.
- [ ] Decide **Floor 4 theme** (and floors 5–6).
- [ ] Define the **3 current weapon types** + how each plays.
- [ ] **Mini-bosses / mini-games** — in or out for this quarter?
- [ ] Build **enemy roster** + **boss concepts** per floor.
- [ ] Quarter goal: functional game, **start scene + game-over scene**, showable build.
