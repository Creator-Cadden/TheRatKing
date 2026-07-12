# Boss Minion Spawner Setup

Add `BossMinionSpawner.cs` as a sibling component to `FatRatBoss`. Drag your grunt prefabs into its list and tune the trigger style.

## What it adds to the fight

- **Periodic waves** — every N seconds while the boss is alive, a small group of grunts joins the fight
- **HP-threshold waves** — at 75%, 50%, 25% (configurable), a larger wave appears as the boss gets pushed
- **Smart positioning** — minions spawn at random angles around the boss on the NavMesh, never inside walls
- **Population cap** — if the player can't kill them fast enough, the spawner pauses until they catch up

End result: the player has to balance attacking the boss with managing crowd control.

## 1. Add the component

1. Open `FatRatBoss.prefab` in Prefab Mode.
2. On the root → **Add Component → Boss Minion Spawner**.
3. Save the prefab.

## 2. Wire the minion prefabs

In the Inspector → `Minion Prefabs` field:

1. Set the array size to however many enemy types you want in the pool (e.g. `3`).
2. Drag your grunt prefabs in:
   - `GruntCone.prefab`
   - `GruntCircle.prefab`
   - `GruntRectangle.prefab`

Each time a minion is spawned, the script picks one at random from this list. Mix in tougher enemies if you want occasional Tough rats in the boss arena — just add them to the array.

## 3. Tune the triggers

The Inspector has two trigger styles. You can enable one, the other, or both.

### HP Thresholds (recommended)

| Field | Default | Effect |
|---|---|---|
| `HP Thresholds` | `[0.75, 0.5, 0.25]` | Waves fire when boss HP drops below each value (75%, 50%, 25%) |
| `HP Threshold Wave Count` | `2` | Number of minions per HP-threshold wave |

Each threshold fires **once per fight**. So the player gets three escalation moments — the fight gets harder as the boss takes damage.

**Tuning suggestions:**
- Fewer thresholds, more minions per wave → big dramatic waves (`[0.6, 0.3]` with count `4`)
- More thresholds, fewer minions per wave → constant pressure (`[0.85, 0.7, 0.55, 0.4, 0.25]` with count `1`)
- Single threshold at half HP → "phase 2 begins" feel (`[0.5]` with count `5`)

### Interval Spawning

| Field | Default | Effect |
|---|---|---|
| `Enable Interval Spawn` | `ON` | Toggles interval triggers entirely |
| `Spawn Interval` | `20s` | Seconds between waves |
| `Require Damage To Start` | `ON` | Wait for the boss to be hit before the timer starts |
| `Interval Wave Count` | `1` | Number of minions per interval wave |

So the default behavior: once the player starts the fight, every 20 seconds a single minion joins. Combined with HP thresholds, this gives constant background pressure plus escalation spikes.

**If you want only HP thresholds (no interval):**
- Turn `Enable Interval Spawn` OFF.

**If you want only interval (no HP thresholds):**
- Clear the `HP Thresholds` array to size 0.

## 4. Spawn geometry

| Field | Default | Effect |
|---|---|---|
| `Min Spawn Distance` | `4` | Closest minions can spawn to the boss (don't spawn ON the boss) |
| `Max Spawn Distance` | `10` | Farthest minions can spawn |
| `Navmesh Sample Radius` | `2` | How forgiving the NavMesh check is — bigger handles uneven floors |
| `Spawn Attempts` | `6` | Retries per minion if first random position misses the NavMesh |
| `Max Alive Minions` | `5` | Population cap. Waves pause when this is hit until some die. |

## 5. NavMesh requirement

This is critical. The spawner uses `NavMesh.SamplePosition` to validate candidate positions. **If your boss arena doesn't have a NavMesh baked, the spawner will fail every attempt and log warnings.**

To check:
1. Open your boss arena scene.
2. Open **Window → AI → Navigation** (or your version's equivalent).
3. Look at the Scene view — the navigable area should be highlighted in blue.
4. If there's no blue overlay, click the **Bake** tab and bake the NavMesh.

The min/max spawn ring (orange/red circles in the gizmo) should both be inside the blue NavMesh overlay. If part of the outer ring extends outside the arena, the spawner will retry until it finds a valid point inside.

## 6. Encounter and gate interaction

Your `EncounterController` (the room gate) only tracks specific enemies. **The minions spawned by this script are NOT in that list**, so they don't block gate opening — the gate fires when the boss dies regardless of how many minions are still alive.

This is usually what you want — once the boss is down, the player gets to escape even if a couple of minions are still scurrying around. If you'd rather force the player to clear everything, you'd need to extend EncounterController to also track dynamically-spawned enemies (tell me if you want this and I can wire it).

## 7. Gizmos for tuning

Select the boss in the Scene view (with the spawner component attached). You'll see two circles:

- **Inner orange ring** = `Min Spawn Distance`. No minions spawn closer than this.
- **Outer red ring** = `Max Spawn Distance`. No minions spawn farther than this.

Adjust the radii to fit your arena. If the outer ring extends past your arena walls, the NavMesh check will prevent spawns there but the warning logs can get noisy.

## 8. Testing

1. Set `Verbose` ON in the spawner Inspector.
2. Drop into TestingArena with the boss (or play through to lvl5).
3. Hit the boss — interval timer starts.
4. Wait ~20s → first wave should appear. Console:
   ```
   [BossMinionSpawner] Spawned 1/1 minions (1 alive total).
   ```
5. Damage the boss to 75% HP → HP-threshold wave fires. Console:
   ```
   [BossMinionSpawner] HP threshold 75% crossed (now 74%) — spawning wave.
   [BossMinionSpawner] Spawned 2/2 minions (3 alive total).
   ```
6. Kill the boss → spawner stops. Console:
   ```
   [BossMinionSpawner] Boss died — spawner stopped.
   ```

## 9. Tuning recipes

**"Sparse pressure" — minions are background threats, boss is the focus**
```
HP Thresholds: 0.5
HP Threshold Wave Count: 2
Enable Interval: ON
Spawn Interval: 25s
Interval Wave Count: 1
Max Alive Minions: 3
```

**"Crowd-control nightmare" — survive the swarm**
```
HP Thresholds: 0.85, 0.7, 0.55, 0.4, 0.25
HP Threshold Wave Count: 3
Enable Interval: ON
Spawn Interval: 12s
Interval Wave Count: 2
Max Alive Minions: 8
```

**"Phase 2 dramatic" — single midpoint surge, no constant adds**
```
HP Thresholds: 0.5
HP Threshold Wave Count: 6
Enable Interval: OFF
Max Alive Minions: 6
```

**"Endless wave" — get the boss down fast or be overrun**
```
HP Thresholds: (empty)
Enable Interval: ON
Spawn Interval: 8s
Interval Wave Count: 1
Max Alive Minions: 10
```

## 10. Common gotchas

| Symptom | Fix |
|---|---|
| Nothing spawns | Check `Minion Prefabs` array isn't empty AND NavMesh is baked in the arena |
| Spawner logs "No valid NavMesh point found" repeatedly | Either rebake NavMesh to cover the spawn ring, OR shrink Max Spawn Distance |
| Minions spawn but immediately fall through the floor | NavMesh sample is finding a position below the visible floor — increase `Navmesh Sample Radius` or bake the NavMesh with tighter agent radius |
| Same enemy type spawns every time | Multiple slots in `Minion Prefabs` are the same — vary the prefab list |
| Way too many minions on screen | Lower `Max Alive Minions` to 3 or 4 |
| HP threshold never triggers | Boss's `MaxHealth` is 0 — check EntityStats / EnemyStatBlock |

## 11. Boss feels too easy/hard?

The right tuning depends on how strong the player's loadout is at lvl5. Quick adjustments:

- **Too hard** → reduce `HP Threshold Wave Count` or increase `Spawn Interval`
- **Too easy** → add more HP thresholds, increase wave counts, or shrink interval

Watch playtests with Verbose ON and see when waves fire vs how the player handles them.

## 12. The component is portable

Even though the script is designed for FatRatBoss, it's actually generic — it just lives on any EntityStats. So you can drop it on Captain enemies or future mini-bosses too. Same configuration, same behavior. The "boss" terminology in the name is just for clarity.
