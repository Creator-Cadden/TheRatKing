# Encounter Gate Setup

One new script: **`EncounterController.cs`**. Drop it on an empty in any room. Hand it a list of enemies + a gate transform. When the enemies are dead, the gate lowers.

## The setup in 30 seconds

1. In your scene Hierarchy: **right-click → Create Empty**. Name it `RoomEncounter` (or whatever fits the room).
2. Position it somewhere in the room. Position doesn't matter for logic — only for gizmos.
3. **Add Component → Encounter Controller**.
4. Drag every enemy in the room into **Tracked Enemies** in the Inspector (one at a time, or multi-select in Hierarchy and drag).
5. Drag your gate model/parent transform into **Gate Transform**.
6. Leave **Open Action** as `Lower (slide downward)`.
7. Save the scene.

Hit Play. Kill all the enemies. Gate slides down. Player walks through to the LevelTransition trigger beyond it.

## Inspector fields explained

### Tracked Enemies
The list of enemies that belong to this encounter. **Drag every enemy from the scene into this list.** The script subscribes to each enemy's `onDeath` event at Start.

Enemies that are already dead at scene Start (shouldn't happen, but) are ignored. Empty slots in the list are also ignored — no error.

### Auto Populate (optional)
Tick this if you'd rather have the script find enemies automatically. It will:

- Run an `OverlapSphere` of `Auto Populate Radius` (default 25 units) around this GameObject at Start.
- Add every `EntityStats` found to the tracked list (skipping the player).
- Respects the layer mask.

Useful if you have rooms with shifting enemy spawns. The downside: any non-encounter enemies who wander into the radius get tracked too. For deterministic encounters, prefer the manual list.

### Gate Transform
The thing that physically blocks the way. Could be:
- A solid wall mesh
- A drop-bar
- A door pair
- An empty parent containing multiple gate pieces (they'll all move together)

### Open Action
Three modes:

| Mode | Effect |
|---|---|
| **Lower (slide downward)** | Smoothly moves the gate down by `Lower Distance` over `Lower Duration` seconds, using `Lower Curve` for easing. Recommended for visible gates. |
| **Deactivate** | Calls `SetActive(false)` instantly. Use for instant-disappear gates or invisible barriers. |
| **Event Only** | Doesn't touch the gate at all — only fires `onAllDefeated`. Use this if you want fully custom behaviour wired through the UnityEvent (rotate the gate, play a cinematic, whatever). |

### Lower Distance / Duration / Curve
Only used when `Open Action = Lower (slide downward)`.

- **Distance**: how far down the gate slides, in world units. Make this big enough that the gate is fully below the player's collision capsule.
- **Duration**: how long the slide takes (seconds). 1.5s default feels dramatic without being annoying.
- **Curve**: AnimationCurve defaulting to ease-in-out. Set to linear for constant speed, or hand-tune for bounce / overshoot / etc.

### Deactivate After Lower
When the lower animation finishes, also SetActive(false) the gate's GameObject. Saves a tiny bit of render/physics cost since the gate is offscreen anyway. Default on — turn it off only if you want the gate to physically remain below the floor (e.g. for a reverse "raise back up" later).

### Events

| Event | Fires when |
|---|---|
| **On Enemy Killed (int)** | Every time a tracked enemy dies. Argument = remaining alive count. Useful for "X enemies remaining" HUD. |
| **On All Defeated** | Once when the last enemy dies. Wire whatever flourishes you want — VFX burst, sound effect, screen shake, save trigger, etc. |

## Recommended layout in your room

```
[ENEMIES] <─── kill order ─────  [PLAYER STARTS HERE]

                          [GATE]      ← blocks the way until cleared
                              ↓        (sliding into the floor)
                       [LEVEL TRANSITION TRIGGER]
                              ↓
                       (player walks through after gate lowers)
```

Place the LevelTransition trigger BEHIND the gate's resting position. When the gate is up, the player can't reach the trigger. When the gate drops down (and optionally deactivates), the trigger becomes accessible.

If the LevelTransition trigger physically overlaps the gate, the gate's collider shouldn't block the LevelTransition's trigger collider — the gate likely has a mesh + collider on the gate mesh, while the LevelTransition has only a trigger collider. Triggers don't physically block each other.

## Gizmos in the Scene view

Select the RoomEncounter object and you'll see:

- **Yellow lines** from controller to every tracked enemy (with a small sphere at each)
- **Cyan line** from controller to the gate
- **Faded cyan line + box** showing where the gate will end up after lowering
- **Orange wire sphere** showing the Auto-Populate radius (only if Auto Populate is on)

Great for visually verifying you've wired the right enemies and the gate will drop to the right place.

## Multiple rooms

Each room gets its own `EncounterController` empty with its own enemy list and its own gate. They don't interfere — kill enemies in room A and only gate A drops.

If you want a multi-stage encounter (kill wave 1, gate opens; player walks in; kill wave 2, second gate opens), use multiple EncounterControllers chained — wire the first one's `onAllDefeated` to **activate** the next room's enemies (or to spawn them).

## Boss arenas

Drop a single `EncounterController` with just the boss in `Tracked Enemies`. When the boss dies, the exit gate lowers.

## Sanity checks before you press Play

- Every enemy in your list has `EntityStats` (they do if they have your standard enemy components).
- The gate transform has a collider that physically blocks the player.
- The gate's start position is visible to the player so they can see it lowering.
- The `Lower Distance` is at least the height of the gate + 1-2 units of clearance.
- Verbose ON if you want console logs of every kill + the gate-opens event.

## Console output with Verbose on

```
[EncounterController] 5/5 enemies alive at start.
[EncounterController] Enemy died — 4 remaining.
[EncounterController] Enemy died — 3 remaining.
[EncounterController] Enemy died — 2 remaining.
[EncounterController] Enemy died — 1 remaining.
[EncounterController] Enemy died — 0 remaining.
[EncounterController] All enemies down — opening gate.
```

If you don't see the "All enemies down" line after killing what looks like everything, check the Tracked Enemies list — there might be one in there that isn't dying (a stray reference, an off-screen one).

## Edge cases handled

- **Enemy reference gets destroyed mid-game**: `null` checks in the loop, no crashes.
- **Empty enemy list**: Resolve fires immediately at Start, gate opens on scene load.
- **Player enters a cleared room from a save**: same — `_aliveCount` is 0 at Start, gate is already open.
- **Trying to gate-lower without a gate transform**: warning log, no crash.
- **Same enemy added twice**: subscribed only once (UnityEvent prevents duplicates).
