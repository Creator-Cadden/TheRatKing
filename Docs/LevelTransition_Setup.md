# Level Transition Setup

What you've got now:

- **`LevelTransition.cs`** — a trigger component you stick on a Cube / Plane / any collider.
- **`GameManager.TransitionToLevel(sceneName, floorMode)`** — new method called by the trigger. Captures the player's **current in-game stats** into the save, advances the floor, then loads the next scene.

The existing `OnSceneLoaded → SaveCheckpoint` flow then writes the new state to disk, so the player's save is now stamped with the new scene as the checkpoint.

You can't manually save — saves only fire at the start of each scene, which is what you wanted.

## How to set up a transition trigger

In the **scene where the level ends** (e.g. lvl1):

1. **Hierarchy → right-click → 3D Object → Cube** (or Plane, Capsule, whatever shape). Position and scale it at the doorway / level-end spot.
2. With it selected, in the Inspector:
   - On the **Collider** component, tick **`Is Trigger`** (so the player walks through it instead of bumping into it).
   - Optional: drag a translucent material onto it so you can see it in-game while testing. Remove the material for the shipping build.
3. **Add Component → Level Transition**. Fields to set:
   - **Next Scene Name**: type the destination scene name exactly as it appears in your Scenes folder, no `.unity` (e.g. `lvl2`).
   - **Floor Mode**:
     - `Advance` — usual forward transition (+1 floor, capped at 3). Default.
     - `Unchanged` — for a side room or zone change inside the same floor.
     - `Set To 1/2/3` — force a specific floor (backtracks or warps).
   - **Player Tag**: leave as `Player`.
   - **One Shot**: leave on. Prevents double-triggering if the player wobbles back and forth.
4. Make sure the destination scene is in **File → Build Profiles → Scene List** with its checkbox enabled. Otherwise SceneManager.LoadScene errors out.

## What happens at runtime

1. Player crosses the trigger.
2. `LevelTransition.OnTriggerEnter` fires once. It calls `GameManager.TransitionToLevel("lvl2", floorMode)`.
3. GameManager:
   a. Reads the player's current `EntityStats` and `XPSystem` (HP, max HP, strength, stamina, level, XP, weapon, etc.).
   b. Builds a new `SaveData` with the destination scene name and the new floor.
   c. Replaces `ActiveSave` with that.
   d. Calls `SceneManager.LoadScene("lvl2")`.
4. The lvl2 scene loads. The old player is destroyed; a fresh Player prefab instance spawns in lvl2.
5. `OnSceneLoaded` fires:
   a. Player references are re-cached.
   b. `SaveSystem.ApplyToStats(ActiveSave, ...)` writes the captured stats onto the new player. HP, strength, weapon, everything matches what the player had crossing the trigger.
   c. `SaveCheckpoint("lvl2")` writes the new save to disk.
6. Player is now in lvl2 at the scene's Player-prefab placement, with their old stats intact, and the save file says "checkpoint = lvl2".

If they die or quit and Continue, they reload at the START of lvl2 with these same stats.

## Where to place the Player in the new scene

The Player prefab placement IS the spawn point for the level. When the new scene loads, the Player instance appears wherever you placed it in the scene file. So:

- In **lvl2.unity**, drag the Player prefab into the scene at the **start** of the level (right where you want the player to enter).
- Place a SpawnPoint-tagged empty at the same spot (used for in-place death respawn fallback).

The transition doesn't need to know about positioning — that's the new scene's job.

## Test mode behavior

If `GameManager.IsTestMode == true` (player came via the Test Arena button), `TransitionToLevel` skips the save capture and just loads the scene. The TestingArena's normal "reset to full + equip chosen weapon" flow handles the new scene as usual.

If you put a LevelTransition trigger in the TestingArena that points to another arena, it'll just hand off without writing any save file. Useful for testing room-to-room.

## Common gotchas

- **Trigger doesn't fire** → check the Player GameObject is tagged `Player` AND the collider has `Is Trigger` on. The Player must also have a Rigidbody or be moved by a CharacterController for trigger events to fire (you already have a CharacterController, so this is fine).
- **Destination scene doesn't load** → it's not in Build Settings. Add it.
- **Player loads with wrong stats** → make sure your Player prefab has both `EntityStats` and `XPSystem` components and they're getting properly initialized in `Awake/Start`.
- **Player loads at world origin (0,0,0) in the new scene** → the Player prefab's position in the new scene is (0,0,0). Open the new scene, move the Player to where you want them, save.
- **Floor never goes past 3** → that's by design (`Mathf.Min(currentFloor + 1, 3)` in GameManager, matching the `floorThreeCap` system in PlayerStatBlock).

## Visual feedback (optional)

Right now the trigger is invisible at runtime (the Cube has whatever material you set). Some options:

- **Glowing portal**: use an emissive material with a particle effect.
- **Stairs / doorway prop**: place the trigger volume on a stair model or doorway prop, hide the cube itself (renderer off), keep the collider on.
- **Footstep indicator on the floor**: small decal showing "step here to advance".

The script itself doesn't render anything — purely a logic volume. You get to art-direct it however you want.

## Build Settings reminder

`File → Build Profiles → Scene List`. Every scene the player can reach during the game (MainMenu, PlayerCustom, lvl1, lvl2, lvl3, TestingArena, etc.) must be in this list. Drag scenes from the Project window into the list, or use "Add Open Scenes" while each one is open.
