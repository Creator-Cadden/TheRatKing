# Testing Arena — Setup with Prefabs

Goal: a `TestingArena.unity` scene that mirrors lvl1's player/camera/UI setup, where every shared GameObject is a **prefab**. Edit the prefab once → every scene that uses it updates. No more re-copying after a tweak.

The scripts are already wired. This doc is the Unity-side work, done in order.

---

## Why prefabs (vs. copy-paste or wrapper grouping)

- **Copy-paste**: two scenes drift apart the moment you tweak one. Every fix has to be re-applied manually.
- **Wrapper GameObject** (your "Baseline" attempt): works in principle, but any non-identity transform on the wrapper stretches everything inside. Easy to introduce by accident.
- **Prefabs**: each shared GameObject lives in `Assets/Prefabs/`. Scenes reference them. Open the prefab, edit, save — every scene using it picks up the change next time you open it. This is the Unity-native way to share setups.

You'll do a bit of upfront work converting the lvl1 hierarchy to prefab instances. Then every future scene is `drag prefabs in → done`.

---

## 1. Create the Prefabs folder

`Assets/` → right-click → **Create → Folder** → name it `Prefabs`. (Skip if already exists.)

## 2. Identify what to prefab from lvl1

Open `lvl1.unity`. In the Hierarchy, find the *top-level* GameObjects that are shared with other gameplay scenes. Likely candidates:

- **Player** — character + character controller + EntityStats + XPSystem + animator + any visual children
- **CameraRig** — the parent containing Main Camera (with CinemachineBrain), `freeLookCamera`, `aimCamera`, `cameraPitch`, `shoulderPos`. If they're not already under a single parent, that's the only step worth doing manually now: create an empty named `CameraRig` with `(0,0,0) (0,0,0) (1,1,1)` transform, then drag those cameras into it.
- **GameManager** — the persistent singleton
- **PixelDisplay** — the Screen Space Overlay Canvas with the RawImage that shows your render texture (this is what fixes "Display 1 no camera rendering")
- **HUD** — the Canvas with health bar, stamina, XP, attack cooldown, etc.
- **DeathScreen** — the death overlay UI
- **PauseMenu** — the pause UI
- **EventSystem** — UI input
- **SpawnPoint** — the tagged spawn point (you'll probably want a *different* one per arena, so this might not need to be a prefab)

**Important**: before prefabbing each one, verify its Transform is `(0, 0, 0)` position, `(0, 0, 0)` rotation, `(1, 1, 1)` scale — or whatever its intended pose is. Anything weird gets baked into the prefab.

For UI canvases this is automatic — they're sized by CanvasScaler. For the Player and CameraRig, you'll set their position in each scene after dropping the prefab in.

## 3. Make each prefab (one at a time)

For each GameObject in the list above:

1. In the **Hierarchy**, click and drag the GameObject onto `Assets/Prefabs/` in the Project window.
2. Unity creates `<Name>.prefab`. The Hierarchy entry turns **blue** — that means it's now a prefab instance.
3. Done. Move on to the next one.

Do this carefully one at a time and **save the scene after each** (`Ctrl+S`) so you don't lose track of state.

## 4. Verify lvl1 still plays

Hit Play in lvl1. Everything should work exactly as before — prefabs are still just instances in your scene with the same data. If something breaks, undo (`Ctrl+Z`) and investigate before continuing.

## 5. Create TestingArena.unity

1. `File → New Scene` → choose **Basic (Built-in)** or whatever your URP template is.
2. `File → Save As` → `Assets/Scenes/TestingArena.unity`.
3. In the new scene, delete the default Main Camera (you'll use the prefab one instead). Keep or delete the default Directional Light — your call.

## 6. Populate TestingArena from prefabs

In the Project window, open `Assets/Prefabs/`. Drag each prefab into the Hierarchy of TestingArena (or into the Scene view to drop at a position):

- GameManager
- Player
- CameraRig
- PixelDisplay
- HUD
- DeathScreen
- PauseMenu
- EventSystem
- A Directional Light (if you deleted the default)

Now add an arena floor (Plane), walls, and a `SpawnPoint` empty (tagged `SpawnPoint`) where you want the player to land.

## 7. Add TestingArena to Build Settings

`File → Build Profiles` → Scene List → drag `TestingArena.unity` in. Make sure its checkbox is enabled.

## 8. Set the scene name on GameManager

Open `MainMenu.unity` (or wherever the GameManager prefab instance lives that's the entry point):

1. Select the GameManager in the Hierarchy.
2. In the Inspector → **Scene Names** section → set **Test World Scene** = `TestingArena`.
3. If it's a prefab instance and you want every scene to inherit this default, click **Overrides** at the top of the Inspector → **Apply All** to push it back into the prefab.

## 9. Re-do the Test Arena button in MainMenu

`MainMenu.unity`:

1. In `Canvas → MainMenuRoot → SlotPanel`, duplicate one of the slot buttons.
2. Rename it `TestWorldButton`.
3. Strip out the children that don't apply (the SlotSubLabel, the DeleteX_X icon). Leave just one TMP label.
4. Position it as the 4th item in the slot panel.
5. Select `MainMenuRoot` in the Hierarchy → in the Inspector find the **Test Arena** section on MainMenuUI → drag `TestWorldButton` into **Test World Button**. Optional: drag its TMP label into **Test World Label**.

## 10. Test the flow

Play MainMenu. Click `PLAY` (or `LOAD GAME`) → Slot panel opens → `ENTER TEST ARENA` → WeaponSelect → pick weapon → lands in TestingArena with full stats and the chosen weapon. Death reloads the arena. Return-to-menu clears test mode.

---

## Daily workflow once it's set up

- **Editing the Player** — open the **Player prefab** (double-click in Project, or click Open Prefab on the instance). Make changes. Save. Every scene that uses Player gets the change.
- **Per-scene tweaks** — e.g. positioning the Player at a different spawn in TestingArena: just move the instance in the Hierarchy. The position becomes an "override" on that instance, marked in **blue** in the Inspector. Doesn't affect other scenes.
- **Propagating a tweak you made on an instance** — top of Inspector → **Overrides → Apply All**.
- **Reverting unwanted changes on an instance** — Overrides → **Revert All**.

## Pitfalls to avoid

- **Don't parent a prefab instance under a non-identity transform.** That's the trap that broke things before. Either keep instances at the root of the Hierarchy, or only group them under empties with `(0,0,0)(0,0,0)(1,1,1)` transforms.
- **Don't move references between prefabs.** If your Player prefab has a serialized reference to the CameraRig prefab's `freeLookCamera`, that link is brittle across scenes. Better: in each scene, after dropping both prefabs in, re-link the Player's serialized camera fields to the CameraRig instances in that scene. Or write a small bootstrapping script that finds them by tag at runtime.
- **Don't prefab the SpawnPoint.** It's scene-specific — every scene has its own spawn position.

## Order I'd actually do this in (if you want a tight checklist)

1. Make `Assets/Prefabs/` folder.
2. In lvl1, drag `GameManager` → Prefabs. Save.
3. Drag `Player` → Prefabs. Save.
4. Group cameras under `CameraRig` empty with identity transform, drag → Prefabs. Save.
5. Drag the UI canvases (PixelDisplay, HUD, DeathScreen, PauseMenu, EventSystem) → Prefabs. Save.
6. Press Play in lvl1, verify nothing broke.
7. Create `TestingArena.unity`. Add to Build Settings.
8. Drag prefabs into TestingArena. Add a floor + SpawnPoint.
9. Re-do the Test Arena button in MainMenu.
10. Test the full flow.

Once this is in, future scenes (lvl2, lvl3, boss arenas) cost ~30 seconds each — `New Scene → drag prefabs in → done`.
