# Boss Cutscene Setup

One new script (`BossCutsceneController.cs`) plus Unity-side setup for video playback and UI. End result: walk into lvl5, the right cutscene for your weapon plays, then the boss bar grows in and combat starts.

## 1. Import the three MP4 files into Unity

1. Download the three cutscenes from Google Drive to your computer.
2. In Unity, navigate the Project window to `Assets/`. Create a folder if you like — e.g. `Assets/Cutscenes/`.
3. Drag the three `.mp4` files into that folder.
4. Unity auto-imports each as a **VideoClip** asset — they'll show up with a small filmstrip icon.
5. Rename them clearly: `BossCutscene_Blade.mp4`, `BossCutscene_Hammer.mp4`, `BossCutscene_Bow.mp4`.

### Import settings worth checking

Click one of the imported clips. In the Inspector:

- **Transcode**: ON if you want consistent playback across platforms. OFF if you trust the source file format.
- **Dimensions**: leave at "Original" unless you need to resize.
- **Codec**: leave default ("Auto") — Unity picks based on platform.
- **Aspect Ratio**: "Stretch" or "Keep Existing" depending on whether the source matches 16:9.

## 2. Create the RenderTexture for video output

The VideoPlayer needs a target texture to render the video onto. The RawImage on the canvas then displays that texture.

1. In the Project window, right-click → **Create → Render Texture**. Name it `CutsceneRT`.
2. Click it. In the Inspector:
   - **Size**: `1920 × 1080` (or whatever matches your cutscene resolution)
   - **Anti-aliasing**: None
   - **Color Format**: leave default (R8G8B8A8_SRGB)
   - **Depth Buffer**: No depth buffer

Save. This texture is the bridge between the VideoPlayer and the visible canvas.

## 3. Build the cutscene canvas (showing the video to the player)

This goes on your MainUI canvas — or create a dedicated `CutsceneCanvas` if you'd rather keep it separate.

### Option A — on the MainUI canvas (simpler)

1. Open `UIs.prefab` (the one with the MainUI canvas).
2. Right-click the MainUI canvas → **UI → Raw Image**. Name it `CutsceneScreen`.
3. In the Inspector:
   - **RectTransform**: stretch to fill screen — Anchor preset → Stretch both axes (Shift+Alt to also set pivot + position). Left/Top/Right/Bottom = 0.
   - **Raw Image → Texture**: drag your `CutsceneRT` Render Texture in.
   - **Raycast Target**: OFF (no input)
4. Disable `CutsceneScreen` for now (uncheck the GameObject) — script will enable it when cutscene plays.

### Option B — a dedicated CutsceneCanvas (cleaner separation)

1. Right-click in Hierarchy → **UI → Canvas**. Name it `CutsceneCanvas`.
2. Set its **Sort Order** to a high number (e.g. 1000) so it draws on top of all other UI.
3. Add a RawImage child as in Option A.

Either way, the script's `Video Canvas Root` field will point at the RawImage's GameObject.

## 4. Create the cutscene controller GameObject in lvl5

In the lvl5 scene Hierarchy:

1. Right-click → **Create Empty**. Name it `BossCutsceneRig`.
2. Position doesn't matter — it's a logic GameObject.
3. **Add Component → Video Player**. Configure:
   - **Source**: `Video Clip` (default)
   - **Render Mode**: `Render Texture`
   - **Target Texture**: drag your `CutsceneRT` in
   - **Audio Output Mode**: `Audio Source` (if you want sound) — also add an AudioSource component and route it
   - **Play On Awake**: **OFF** (the script controls when to play)
4. **Add Component → Boss Cutscene Controller** (the script you just got).
5. Wire the Inspector slots:
   - **Blade Cutscene** → drag `BossCutscene_Blade` VideoClip
   - **Hammer Cutscene** → drag `BossCutscene_Hammer` VideoClip
   - **Bow Cutscene** → drag `BossCutscene_Bow` VideoClip
   - **Fallback Cutscene** → optional, drag any of them
   - **Video Canvas Root** → drag the `CutsceneScreen` RawImage GameObject
   - **Boss Health Bar** → drag the `BossHealthBarRoot` (or whichever GameObject has BossHealthBarUI)
6. Leave the rest at defaults:
   - `Play On Start` = ON (auto-plays when scene loads)
   - `Freeze Player During Cutscene` = ON
   - `Allow Skip` = ON (player can press Escape to skip)
   - `Boss Bar Intro Delay` = 0.3s
   - `Verbose` = ON during testing

## 5. Wire the boss bar

`BossHealthBarUI` should have `Show Mode = Manual` (it waits for our cue):

1. Open the boss bar prefab (or the instance in lvl5).
2. **Show Mode** → `Manual`.
3. `Intro Grow Duration` → tune to taste (default 1.5s).

When the cutscene ends, the controller calls `bossHealthBar.PlayIntro()` → the bar fades in and the fill grows from 0 to the boss's current HP.

## 6. Skip prompt (optional)

If you want to show "Press Esc to skip" during the cutscene:

1. On the cutscene canvas, add a small TMP_Text child anchored to a corner.
2. Set its text to `Press ESC to skip`.
3. Disable the GameObject by default.
4. Drag it into the `Skip Prompt Root` slot in the controller.

The script enables it when the cutscene plays and disables it when it ends or is skipped.

## 7. Verify the flow

Test from MainMenu:

1. Start a new game → equip **Blade** in WeaponSelect → land in lvl1.
2. Walk through lvl1 transition → lvl2 → lvl3.
3. Walk through the lvl3 → lvl5 transition (since you have no lvl4 yet, your LevelTransition trigger should already point at lvl5).
4. lvl5 loads.
5. The Blade cutscene plays automatically — `BossCutsceneRig` detected the equipped weapon and chose the right clip.
6. During the cutscene, the player can't move or attack.
7. When the video ends (or you press Esc) → cutscene canvas hides → 0.3s pause → boss bar fades in and the fill grows from 0 to full → combat begins.

Repeat with Hammer and Bow loadouts to verify all three cutscenes route correctly.

## 8. How the weapon detection works

The script picks the cutscene based on this priority:

1. **`GameManager.Instance.ActiveSave.equippedWeapon`** — the weapon stored in the save file. This is the most reliable source because it's what `LevelTransition` writes when you cross from lvl3 → lvl5. The save knows you have Hammer because you equipped Hammer at WeaponSelect.
2. **Player's `EntityStats.EquippedWeapon`** — fallback if there's no active save (e.g. you dropped straight into lvl5 from the editor for testing).
3. **Blade** — final fallback if both above somehow fail.

So during normal play flow: save → transition → new scene → controller reads save → matching clip plays.

## 9. Common gotchas

| Symptom | Likely cause | Fix |
|---|---|---|
| Black screen, no video plays | RawImage's Texture field isn't set | Drag CutsceneRT into the Raw Image → Texture slot |
| Video plays but no sound | VideoPlayer's Audio Output Mode is None | Set Audio Output Mode to Audio Source + add an AudioSource component |
| Wrong cutscene plays | Weapon detection picking wrong source | Turn on Verbose → console shows what weapon was detected and which clip was picked |
| Cutscene plays but boss bar never appears | BossHealthBar reference not wired, or BossHealthBarUI.showMode is set wrong | Wire the field + set showMode = Manual |
| Cutscene canvas covers gameplay forever after the video ends | OnClipFinished not firing | Check that the VideoPlayer has the clip assigned AND its Loop checkbox is OFF |
| Player can move during the cutscene | `Freeze Player During Cutscene` is off | Turn it on |
| Skip key doesn't work | The Keyboard.current isn't available (rare) | Try using a different key or restart Unity |

## 10. If you want the bar to NOT show during the cutscene at all

That's already how it works:

- `BossHealthBarUI.showMode = Manual` keeps the bar invisible at scene start (alpha = 0).
- It only shows when `PlayIntro()` is called by the cutscene controller after the video ends.

So during the entire cutscene, the bar is fully hidden. Combat HUD elements (health, stamina, XP, attack cooldown) are still visible — if you also want those hidden during the cutscene, add their CanvasGroup to the cutscene controller via a separate field and fade them out when the cutscene starts.

## 11. The script does NOT need to be on the player

Drop the BossCutsceneRig anywhere in lvl5. It auto-finds the player via tag when it needs to lock player input or read the equipped weapon stats.

## 12. To preview a specific cutscene without going through the full flow

1. Open lvl5 in the Editor.
2. Find the `BossCutsceneRig`.
3. Temporarily wire the same clip into all three weapon slots.
4. Hit Play. Always plays that clip.
5. Remember to revert when you're done testing.

OR you can write a quick debug script that calls `BossCutsceneController.PlayCutscene()` on a key press for testing.
