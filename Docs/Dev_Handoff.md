# The Rat King — Dev Handoff (for continuing in a new chat)

Snapshot of work through 2026-08-20. All code lives in this project folder — a new chat can read the scripts directly. Point a fresh chat here with: **"continue on The Rat King, read Docs/Dev_Handoff.md."**

## Project facts
- **New Input System only** (`activeInputHandler: 1`). Read keys via `UnityEngine.InputSystem` (`Keyboard.current`), never old `Input.GetKey`.
- Reusable prefabs in `Assets/Prefabs/MainLevelPrefabs`: Player, CamRig, SpawnPoint, UIs, Audio, DeathPlane, Cursormanager.
- Player prefab root is tagged `Player`; spawn objects tagged `SpawnPoint`.
- `GameManager` + `Cursormanager` persist (DontDestroyOnLoad) from the Main Menu.

## Tutorial system (new)
Flow: name → weapon select (`PlayerCustom`) → **`Tutorial` scene** → level 1.
- `GameManager`: added `tutorialScene`, `useTutorialOnNewGame`, `IsInTutorial`, `ChosenWeapon`, `FinishTutorial()`. `StartNewGame` routes to the tutorial only if `TutorialSettings.AnyEnabled`. Tutorial scene = practice mode (equips chosen weapon at full stats, writes **no** save/checkpoint).
- `Assets/Scripts/Level`: `TutorialManager` (one continuous flow; Movement part shown only if a PlayerMovement exists, Combat part is weapon-specific; **tap Enter** to continue, **hold Tab ~0.9s** to skip the current part with a fill bar; movement drills auto-advance off `PlayerMovement.OnJumped/OnRollStarted/HorizontalSpeed`; combat/info via continue key or `NotifyObjective`). `TutorialSettings` (PlayerPrefs `ShowBasics`/`ShowCombat`). `TutorialSettingsToggle` (binds a UI Toggle). `TutorialTrigger` (objective zone). Skip works even in an empty scene.
- **TODO in editor:** build the `Tutorial` scene — drag MainLevelPrefabs in; build the prompt/skip UI (promptPanel CanvasGroup, promptText, sectionLabel, continueHint, skipFill as Image Type=Filled, skipHint) and wire `TutorialManager`. Optional: a `TutorialDummy` for interactive combat drills; wire the two settings toggles into a settings panel (`MainMenuUI.OnSettings` is still a stub).

## Menu juice (`Assets/Scripts/ScreenUI`)
- `MenuFX` — auto-created top overlay: `FadeIn`, `Flash`, `Wipe` (black-band transition), `PlayIntro` ("presents" → title reveal), `FadeOutThen`.
- `UIButtonJuice` — hover scale / press / click-flash. Keep `tintOnHover` OFF; turn `flashOnClick` OFF (UISelectionFX owns the click burst now).
- `UIMenuEntrance` — staggered slide-in from fully off-screen (based on canvas size).
- `UISelectionFX` — bronze→gold border reusing the button's own sprite (Image Type=Sliced, fillCenter=false); shimmer diamonds; hover turns fill gold + text dark (`hoverTextColor`); click burst; own sorting canvas raised by `activeSortBoost` while active so it beats neighbours' frames.
- `UIMenuMagnifier` — dock fisheye: scale by list-distance, dim others by distance, front-lift focused via `SetAsLastSibling`. **Buttons must be manually positioned, NOT in a Layout Group.** Do NOT reintroduce per-button Canvas/GraphicRaycaster — it broke hover.
- `MainMenuUI` wired for intro, wipe panel transitions, fade-out scene loads.
- Button palette: fill `2B2621`, bronze border `6E5A34`, gold hover `FFD37A`, parchment text `E9DCBE`, menu bg `14120E`.

## Combat / enemy juice
- Damage numbers: pop + arc + size-by-damage (`DamageNumberSpawner`). Reaction text (SHRUG/FLINCH/STAGGER/DELAYED) raised + pop via `ReactionPopText` in `EnemyAI`.
- Hit-reaction sounds (HitShrug/Flinch/Stagger/Delayed) + BladeAir / HammerSlam wired in AudioManager + combat scripts.
- Custom cursor: `Assets/Art/UI/MouseCursor.png` + `CursorManager.cursorTexture` / `cursorHotspot`.

## PlayerRegistry pattern (new, important)
- `Assets/Scripts/Core/PlayerRegistry` (static: `Player`, `Root`, `Stats`, `XP`, `HasPlayer`, `OnPlayerReady`/`OnPlayerGone`) + `Assets/Scripts/Player/PlayerRegistrar` (put ONCE on the Player prefab; registers on enable).
- This is the fix for **un-appliable prefab overrides** caused by cross-scene serialized references (UI/scripts asking for the player or cameras). Cameras are already resolved at runtime by `PlayerMovement.ResolveCamerasIfNeeded`.
- **TODO (optional):** convert UI scripts (health bar, XP bar, `StatMenuUI`, `BossHealthBarUI`, `CanvasCameraBinder`) to read `PlayerRegistry` and delete their dragged-in refs; add camera slots to the registry if a UI needs the camera object.

## Direction / advice on record
Architecture is fine and standard for a 2-person indie. Main risk = over-polishing menus/systems before Floor 1 is a complete playable slice. Next priority: lock the tutorial + one full floor and playtest, not more architecture. (Scope/pricing details are in the root `Game_Design_Doc.md` / `Pricing_and_Rewards.md`.)
