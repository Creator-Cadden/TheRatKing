# Tutorial_Setup.md — the "1_T Tutorial" scene

**Phases → Steps → Objectives.** A step is one beat. It either puts a line on the
centre prompt panel and waits for a key (story beats), or it puts one or more
**objective cards** on the right of the screen and waits for them all to be done.

Every gate is an explicit signal out of the real movement/combat code — no
"they've probably got it by now" timers — so the tutorial can't teach something
the game stopped doing.

## Files

| File | What it is |
|---|---|
| `Scripts/Level/TutorialManager.cs` | The phase/step/objective state machine. |
| `Scripts/ScreenUI/ObjectiveListUI.cs` | The task list on the right. Not tutorial-specific — reuse it for quests, room goals, interact hints. |
| `Scripts/ScreenUI/ObjectiveCardUI.cs` | One row: text + key icons that go white → green. |
| `Scripts/Level/TutorialEnemyDrop.cs` | Drops a spawned enemy in from above. |
| `Scripts/Level/TutorialDummyGuard.cs` | Keeps a teaching dummy alive until released. |
| `Scripts/ScreenUI/ScreenFader.cs` | Reusable black-curtain fade. |
| `Scripts/Level/TutorialTrigger.cs` | Unchanged — still works (`Objective` gate). |
| `Scripts/Level/TutorialSettings.cs` | Unchanged — gates whole phases from the settings menu. |

Hooks added to existing files:

- `PlayerMovement` — `MoveInput` and `IsSprinting` are public.
- `PlayerCombat` — `OnBasicAttack` / `OnJumpAttack`, fired only when an attack
  actually starts (cooldown passed, stamina paid), never on the raw click.
- `GameManager.tutorialScene` → `"1_T Tutorial"`.

---

## The objective list

Cards slide in from the right, stack downward, and slide back out when finished —
the cards below glide up into the vacated slot. That falls out of the layout
rather than being animated by hand: the list re-numbers the visible rows and every
card eases toward whatever slot it was last handed.

Multiple cards can be live at once, completed in any order. The movement lesson
uses that — move / jump / sprint / dash all offered together.

Icons come from the **Icon Library** on `ObjectiveListUI` — a flat list of
`id → sprite` rows. Run **Tools ▸ Rat King ▸ Fill Objective Icon Library** and it
fills itself from `Assets/Art/UI/KeyIcons`, one row per PNG, id = the filename:

```
Icon Library     Size 11
  Element 0   Id "A"      Sprite A      Completed Sprite (none)
  Element 1   Id "CTRL"   Sprite CTRL   Completed Sprite (none)
  …
```

That's the whole setup. **Nothing else references sprites.** A step names its
icons by id — `iconIds = { "SPACE", "LMB" }` — and the card looks them up at
runtime. Change the art by swapping the sprite on the matching row; add a key by
dropping a PNG in the folder and re-running the fill.

**Completed Sprite** is optional: leave it empty and the card tints the white
sprite with **Complete Color** when that icon is done. Fill it only if you want
genuinely different art for the green state.

If an id has no row, the card still appears — it just logs a warning naming the
missing icon and shows text only. So the flow is testable before any art exists.

### Card prefab

RectTransform pivot **and** anchor = top-right.

```
ObjectiveCard                RectTransform · CanvasGroup · ObjectiveCardUI
  ├ Background               Image
  ├ Label                    TMP_Text
  ├ Counter                  TMP_Text   ("1 / 3"; auto-hidden when not needed)
  └ Icons                    HorizontalLayoutGroup
      └ Icon_0 … Icon_5      Image  ← drag all six into Icon Slots
```

Six slots is plenty; unused ones hide themselves. Save it as a prefab and leave it
out of the scene.

---

## Scene setup

### The fast way

Open `1_T Tutorial` and run **Tools ▸ Rat King ▸ Setup Tutorial Scene**
(`Assets/Editor/TutorialSetupTool.cs`).

It creates everything below that's missing, creates the card prefab at
`Assets/Prefabs/UI/ObjectiveCard.prefab`, and drags every reference into place.
It's **safe to re-run** — it looks things up by name first, only creates what
isn't there, and won't stomp sizes, colours or positions you've changed.

Three things it can't do, left for you afterwards:

1. **Enemy prefabs** — drop Grunt / Balloon Rat / ToughRat into the four waves.
2. **Spawn point positions** — they're created at the manager's position; drag
   them where you want them in the room.
3. **Icon sprites** — draw them, then fill the Objective List's Icon Library.

Two more menu items alongside it:

- **Fill Objective Icon Library** — imports `Assets/Art/UI/KeyIcons` as Sprites and
  fills the library.
- **Re-layout Prompt Panel** — forces the centre panel back to a layout that fits.
  Separate from Setup on purpose: Setup only positions things the first time it
  creates them, so it can't fix a panel that's already too short. This one
  overwrites those numbers, moving the section title *above* the panel so
  downward-growing prompt text can never reach it.

Everything under here is what the tool builds, for when you want to change it or
build it by hand.

### The hierarchy

The root is **`TutorialSystem`**, deliberately not `Tutorial` — that name is taken
by the room model (`Assets/Art/Modles/Enviro/Tutorial/Tutorial.fbx`). Parenting UI
under an imported model instance makes every child a prefab override and the scene
references don't stick.

```
TutorialSystem
├ TutorialManager            ← TutorialManager.cs
├ StartPoint                 ← empty; where fade-resets put the player
├ SpawnPoints
│  ├ Spawn_Dummy             wave 0
│  ├ Spawn_Grunt             wave 1
│  ├ Spawn_Balloon           wave 2
│  ├ Spawn_Low               wave 3
│  └ Spawn_High              wave 3
├ FadeCanvas                 Overlay, Sort Order 999
│  └ FadeQuad                black Image, stretch-fill, Raycast Target OFF
│                            + CanvasGroup (alpha 0) + ScreenFader
└ TutorialCanvas             Overlay, Sort Order 100
   ├ ObjectiveList           RectTransform, anchor + pivot TOP-RIGHT,
   │                         anchoredPos ≈ (-24, -120)
   │                         + ObjectiveListUI (container = itself)
   ├ PromptPanel             + CanvasGroup
   │  ├ SectionLabel  (TMP)
   │  ├ PromptText    (TMP)
   │  ├ ContinueHint  (TMP)
   │  ├ SkipFill      (Image, Image Type = Filled)
   │  └ SkipHint      (TMP)
   └ EndPromptPanel          set INACTIVE in the inspector
      ├ Btn_Restart  → TutorialManager.RestartTutorial()
      └ Btn_Continue → TutorialManager.ContinueToGame()
```

Spawn points go **on the floor** — the drop height is added on top, so the enemy
falls to exactly that spot.

On TutorialManager: drag in the prompt widgets, the `ObjectiveList`, the
`EndPromptPanel`, SkipFill / SkipHint, and `StartPoint`. Leave **Stat Menu** empty
— it finds the one in your UIs prefab on its own.

---

## Waves

A wave is a list of **spawn entries**, each its own prefab + point, so one wave can
hold two different enemies.

| Field | Notes |
|---|---|
| Spawns | prefab + point (+ a note field for your own sanity). |
| Drop Height / Speed | 8 / 16 is a good thud. |
| Initial Delay | Beat before the first one drops, so a wave doesn't appear the instant the step begins. Default 1.2s. |
| Between Delay | Beat between each enemy in the wave — they arrive one at a time. Default 0.9s. |
| Land Fx | Optional prefab spawned on impact. |
| **Passive** | Never attacks — harmless. |
| **Stationary** | Never moves once it lands. Still turns to face you, still flinches and staggers. |
| **Invulnerable While Teaching** | Pins its health floor at 1 so a drill can't end early. Released by a step's Release Wave Index or Kill Wave Index. |

The default flow expects:

| # | Purpose | Spawns | Passive | Stationary | Invuln |
|---|---|---|---|---|---|
| 0 | Teaching dummy — all attacks + leveling | 1 × `Grunt` | ✅ | ✅ | ✅ |
| 1 | Basic attacker, intro enemy | 1 × `Grunt` | — | — | — |
| 2 | Decal attacker | 1 × **`AirGrunt`** | — | — | — |
| 3 | Armour-class lesson | `Grunt` (light) + `ToughRat` (heavy) | ✅ | ✅ | ✅ |

Wave 0 also carries **XP Override 10** and **Coin Override 5** — runtime values
that beat the stat block, so the first kill pays exactly one level and the
stat-spend lesson has a point to spend without editing the shared Grunt asset.

Decals are taught as a *family* in prose — circles, cones, rectangles a rat
charges down, drops from above — with the balloon rat as the single worked
example, rather than one enemy per shape.

### Armour chevrons

`ToughnessChevrons` (`Scripts/Enemy`) stacks one chevron over an enemy's head per
point of Toughness — none at all for toughness 0, which is itself the information.
It needs no wiring: the sprite loads from `Assets/Resources/UI/Chevron.png` and the
count comes from EntityStats. The tutorial adds it automatically to spawned waves
(**Show Toughness Chevrons** on the wave); drop it on your enemy prefabs to have
it in the main game too.

### The no-damage period

`EntityStats.SuppressAllDamage` is a global switch: while it's up, nothing takes
damage — player or enemy. The tutorial raises it whenever an explanation is on
screen waiting for the continue key (**Freeze Damage During Prompts**), so the
player can read without being chewed on and their idle clicking can't kill the
thing being explained. It thaws at the *start* of every step, because Kill Wave
Index goes through `TakeDamage` and a leftover freeze would silently eat the kill.
Reuse it for cutscenes and menus.

**There is no prefab called Balloon.** `Enemys/AirGrunt` is the Balloon Rat — it
carries the "Balloon Rat" stat block: decal attack only, no basic attack,
toughness 0. Grunt is toughness 0 with a basic attack; ToughRat is toughness 3
with both, which is what makes the pair in wave 3 read so differently.

**Player safety.** TutorialManager's **Player Cannot Die** (on by default) pins the
player's health floor at 1 for the whole tutorial. Hits still hurt, still knock you
about, still flash the vignette — a lesson just never ends in a death screen. It's
cleared the moment they leave for the first real level, and it's a runtime property
rather than a serialized field, so it can't leak onto a prefab.

---

## Gates

| Gate | Completes when | Uses |
|---|---|---|
| `MoveDirections` | all four directions pushed | icons in order **W, S, A, D** (fwd, back, left, right) |
| `MoveHold` | moving for `holdTime` | Hold Time |
| `Jump` | jumped `requiredCount` times | Required Count |
| `Sprint` | actually sprinting for `holdTime` | Hold Time |
| `Dash` | rolled `requiredCount` times | Required Count |
| `BasicAttack` | `requiredCount` grounded attacks | Required Count |
| `JumpAttack` | `requiredCount` air attacks | Required Count |
| `Aim` | aim held for `holdTime` | Hold Time |
| `StatMenu` | the stat menu opens | — |
| `WaveCleared` | every enemy in `waveIndex` is dead | Wave Index |
| `HitEachTarget` | one ordinary hit landed on **every** enemy in the wave | Wave Index |
| `JumpHitEachTarget` | one air-attack hit landed on every enemy in the wave | Wave Index |
| `Objective` | something calls `NotifyObjective(id)` | Objective Id |

**Icon behaviour.** `MoveDirections` maps icons one-to-one with directions. For
everything else: if the number of icons happens to equal the number of things to
do, they light one per unit (three `LMB` icons for a three-hit combo). Otherwise
the icons just say *which key* and the "2 / 3" counter carries the progress.

**The card progress bar.** A translucent track spans the bottom of the card with a
green fill that eases left-to-right. Two toggles: `TutorialManager ▸ Show Progress
Bars` is the master switch, and each objective has its own **Show Progress Bar**.
Hold gates (`MoveHold`, `Sprint`, `Aim`) fill continuously; `MoveDirections` fills
in quarters as W/A/S/D are pressed; counting gates fill per unit. A `WaveCleared`
objective with **Progress From Health** on (the default) drains the bar as the
enemies lose HP instead — "kill the rat" is a gate with two states, so a kill
counter leaves the bar empty for the whole fight and then throws it away, while
health makes the card a damage meter. An
objective with the bar off, or a read-only step, shows no track at all.

> **Do not drive this with `Image.fillAmount`.** `Image.OnPopulateMesh` bails out to
> a plain full-rect quad the moment `activeSprite` is null, so a Filled image with
> an empty Sprite field draws 100% wide forever and ignores `fillAmount` — a static
> green bar however correct Type / Fill Method / Origin look in the inspector.
> `ObjectiveCardUI` therefore animates the fill's `RectTransform.anchorMax.x`
> instead, which needs no sprite, and `EnsureBar()` re-normalises that rect on every
> card so a hand-wired prefab can't reintroduce the bug.

**How jump hits are told apart.** Damage events don't say which attack dealt them,
so the manager times it: a hit landing within `jumpHitWindow` (1.0s) of an air
attack counts as a jump hit, anything else is ordinary. No hooks needed inside the
weapon scripts. Raise the window if bow arrows are in flight too long.

### Prompt flow, freezing and input

**The prompt panel pages itself.** `TutorialPromptUI` takes a step's whole prompt
and splits it: a blank line starts a new page, and any paragraph over **Words Per
Page** (34) is cut again at sentence ends. Each page gets a read time worked out
from its own word count (`baseSeconds + words × secondsPerWord`, clamped to
2.2–10s) and turns itself over. The continue key skips ahead early — it is never
required to make progress. So a player who reads presses nothing, and a player who
doesn't presses E as fast as they like. `Auto Advance After` still applies on top
as a hard ceiling on the whole step.

Everything in the panel runs on **unscaled** time, because the freeze beats below
set `Time.timeScale` to 0.

**Freeze and show.** A step with `Focus = Spotlight` stops the game, darkens the
screen and leaves a lit window around the first living enemy of `Focus Wave Index`
— so the word STAGGER is on screen at the same time as the STAGGER label floating
over the rat that just took the hit. `Focus Delay` (0.35s realtime) lets the
label finish popping before time stops, or it would freeze mid-pop. A focus step
always waits for the key: that's the one place E is load-bearing.

> The shade is **four quads** around the lit rectangle, not one full-screen quad
> with a hole. The thing being shown is world-space 3D text (`EnemyAI` spawns a
> `TextMeshPro` object above the enemy), and any screen-space overlay would cover
> it whatever the draw order. An actual gap is the only way the 3D scene shows
> through, and it needs no shader, so it behaves the same under any pipeline.

A focus step must have **no objective cards** — freezing would stop the player
completing them. The manager logs a warning and skips the focus if you add both.

**Input locks.** `PlayerInputLock` is a static gate the input callbacks consult;
nothing is disabled, so the camera, animator and controller keep their state.
A phase can set `Lock Attacks` (the Movement phase does — there is nothing to
swing at yet), and a step can override with `Input Lock`. Left on `Inherit`, any
pure-text step becomes no-attacking: you can still walk around while reading, you
just can't kill the dummy the next three steps are about. A freeze beat locks
everything, because input still fires at `timeScale` 0.

### Resetting

`RestartTutorial()` destroys every spawned enemy (the old version cleared the live
list without destroying what was in it, which left the previous run's rats standing
in the room), resets every objective's runtime counters, and drops the focus,
the input locks and the damage freeze.

`ContinueToGame()` does all of that **and** clears the globals before the first
real level loads: `EntityStats.SuppressAllDamage`, the player's `HealthFloor`,
`GameFreeze` and `PlayerInputLock`. Each of those is a static or a global, so one
left set would follow the player out of the tutorial — an unkillable rat king is a
worse bug than a rough tutorial.

### Step fields

- **Prompt** — centre-panel line. With no objectives, this *is* the step and it
  waits for the continue key (or **Auto Advance After**).
- **Objectives** — the cards. Each can carry a **Weapon Only** filter, so one step
  holds the blade/hammer/bow versions of the same beat and only the right one
  appears.
- **Fade Reset Before** — fade to black, put the player back on the start point,
  fade in, then run the step.
- **Spawn / Release / Kill Wave Index** — fire when the step begins. Kill uses
  `TakeDamage`, not `Destroy`, so XP and gold pay out through the normal death path.
- **On Begin / On Complete** — UnityEvents for anything scene-specific.

---

## Key bindings — two conflicts worth knowing

Checked against `InputSystem_Actions.inputactions`:

| Key | Already bound to |
|---|---|
| Space | Jump |
| LShift | Sprint |
| Ctrl / C | Roll (dash) |
| Tab | **Stat Menu** |
| Enter | **Attack** (second binding alongside LMB) |
| F | Interact |
| LMB / RMB | Attack / Aim |

Defaults are **E** to continue and **Backspace** to hold-skip. Change them if you
like, just not to anything in that table.

---

## Default flow

1. **Movement** *(skippable)* — intro line → one step with four cards: WASD ·
   jump · sprint · dash → outro line
2. **Combat** *(skippable)* — fade-reset, drop the dummy → 3 basic attacks →
   1 jump attack → hold aim *(all three swap per weapon)* → kill the dummy →
   "XP and Rat Coin" → open the stat menu
3. **Enemies** — grunt (basic attacker) → balloon rat (decal) → the low/high
   toughness pair: hit each once, read the difference, jump-attack each, finish them
4. End prompt: **Restart tutorial** / **Continue**

Restart rebuilds the whole run in-scene behind a fade — no scene reload.

The flow is built in code and used only while **Phases** is empty in the inspector.
Touch anything there and your list wins.

---

## Still to do

- Build the card prefab and the two Canvas hierarchies; unpack `Tutorial`.
- Draw the key icon sprites and fill the Icon Library.
- Place the five spawn points and fill in the four waves.
- Delete the pre-placed `Enemys/BaseGrunts` — the manager spawns its own — and
  either remove `RoomEncounter` or point it only at the final exit.
- Add `1_T Tutorial` to **Build Settings**. `StartNewGame` checks
  `CanStreamedLevelBeLoaded` and silently goes straight to `1_1 Engagement` if the
  scene isn't listed.
- Decide whether the Enemies phase should be skippable (it isn't by default).
- `CursorManager` may fight the end prompt's cursor unlock — check when you wire
  the buttons.

Testing note: pressing Play directly on `1_T Tutorial` means no GameManager, so the
flow runs and reads the weapon off the player, but **Continue** logs a warning
instead of loading `1_1 Engagement`. Enter from MainMenu → New Game for the real path.
