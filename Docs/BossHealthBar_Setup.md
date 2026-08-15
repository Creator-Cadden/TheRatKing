# Boss Health Bar Setup

Two pieces:
1. **`BossHealthBarUI.cs`** on the MainUI canvas
2. **A specific UI hierarchy** with concrete values you can punch into Unity

Plus the cutscene flow: bar stays hidden during cutscene, then `PlayIntro()` is called at the moment the player gains control and the bar fades in WHILE the fill animates from 0 to current HP.

## Part 1 — Remove the world-space HealthBarPivot from the boss

Open `FatRatBoss.prefab` in Prefab Mode:

1. Find the `HealthBarPivot` child.
2. **Disable** it (uncheck the GameObject) or **delete** it entirely.
3. Save the prefab (Ctrl+S), exit Prefab Mode.

The boss has no world bar now. The screen bar takes over.

## Part 2 — Build the bar UI on MainUI

Open `UIs.prefab`. We'll add a `BossHealthBarRoot` under the MainUI canvas. The structure:

```
BossHealthBarRoot         (empty, top-center anchored)
  ├── Background          (Image — dark backdrop)
  ├── FillBackground      (Image — dark red "empty" track)
  ├── Fill                (Image, Filled — the actual bar)
  ├── NameLabel           (TMP_Text)
  └── HpLabel             (TMP_Text)
```

### Step-by-step build, with values to punch in

#### 2a. BossHealthBarRoot (the container)

1. Right-click the MainUI canvas → **Create Empty Child**. Name it `BossHealthBarRoot`.
2. In the Inspector, the RectTransform values:
   - **Anchor Preset**: top-center (click the anchor swatch top-left of the RectTransform → top row, middle column)
   - **Pivot**: `(0.5, 1)` (top-center)
   - **Pos X / Y / Z**: `(0, -50, 0)` — sits 50 pixels below the top edge
   - **Width / Height**: `(700, 50)`
3. Add Component → **CanvasGroup** (script auto-adds one if missing, but it's cleaner to add it manually so you can see it).
4. Add Component → **Boss Health Bar UI** (the script we wrote).

#### 2b. Background (dark backdrop)

1. Right-click `BossHealthBarRoot` → **UI → Image**. Name it `Background`.
2. RectTransform: **Anchor Preset → Alt-Shift + stretch both axes** (fills the parent rect).
3. Image:
   - **Source Image**: leave None (solid color), or use Unity's default `UISprite` for soft corners
   - **Color**: `(0.1, 0.1, 0.1, 0.85)` — near-black, slightly translucent
   - **Raycast Target**: OFF (no input)

#### 2c. FillBackground (the "empty" track)

This is the dark slot the fill bar sits in, so when HP drops you see dark red instead of transparent.

1. Right-click `BossHealthBarRoot` → **UI → Image**. Name it `FillBackground`.
2. RectTransform: **Anchor Preset → Stretch both**. Then set Left=4, Right=4, Top=4, Bottom=4 — inset 4 pixels for a border feel.
3. Image:
   - **Source Image**: None (or `UISprite`)
   - **Color**: `(0.18, 0.05, 0.05, 1)` — dark red
   - **Raycast Target**: OFF

#### 2d. Fill (the actual moving bar)

1. Right-click `BossHealthBarRoot` → **UI → Image**. Name it `Fill`.
2. RectTransform: identical to FillBackground — Stretch both, inset 4 on all sides.
3. Image:
   - **Source Image**: None (or `UISprite`)
   - **Color**: leave white — the script tints it red→orange based on HP
   - **Image Type**: **Filled** ← critical
   - **Fill Method**: **Horizontal**
   - **Fill Origin**: **Left**
   - **Fill Amount**: 1 (script drives this)
   - **Raycast Target**: OFF

#### 2e. NameLabel (boss name)

1. Right-click `BossHealthBarRoot` → **UI → Text - TextMeshPro**. (Use the import-essentials prompt if Unity asks.) Name it `NameLabel`.
2. RectTransform: Anchor Preset → top-center. Pos: `(0, 22)` — sits just above the bar. Size: `(700, 28)`.
3. TextMeshPro:
   - **Text**: `Fat Rat Boss`
   - **Font Size**: `24`
   - **Alignment**: Center, Middle
   - **Font Style**: Bold
   - **Color**: White
   - **Outline / Underlay**: optional dark outline for readability
4. **Raycast Target**: OFF

#### 2f. HpLabel (optional "current / max")

1. Right-click `BossHealthBarRoot` → **UI → Text - TextMeshPro**. Name it `HpLabel`.
2. RectTransform: Anchor Preset → middle-center (fills bar). Pos: `(0, 0)`. Size: same as the bar `(700, 50)`.
3. TextMeshPro:
   - **Text**: `500 / 500` (script overwrites this)
   - **Font Size**: `18`
   - **Alignment**: Center, Middle
   - **Font Style**: Bold
   - **Color**: White
4. **Raycast Target**: OFF

### Wire the script

Click `BossHealthBarRoot` in the hierarchy. In the Inspector, the BossHealthBarUI component:

- **Canvas Group** → the CanvasGroup component on this same GameObject (Unity should auto-fill).
- **Fill Image** → drag the `Fill` child Image
- **Name Label** → drag the `NameLabel` child TMP
- **Hp Label** → drag the `HpLabel` child TMP

- **Boss Display Name**: `Fat Rat Boss` (or whatever)
- **Hp Label Format**: `{0} / {1}` (default)
- **Full Color**: `(0.85, 0.15, 0.15)` (dark red, the bar at full HP)
- **Low Color**: `(0.95, 0.6, 0.1)` (orange, near empty)
- **Lerp Speed**: `6`

- **Show Mode**: `Wait for cutscene PlayIntro() / Manual Show()` ← default now
- **Fade In Duration**: `0.5`
- **Fade Out Duration**: `0.8`
- **Hide After Death Delay**: `1.5`
- **Intro Grow Duration**: `1.5`

- **Target Boss**: leave null (auto-find), OR drag the boss's EntityStats if you want to pin it explicitly
- **Verbose**: ON while testing; OFF when shipping

Save the prefab. Done with the build.

## Part 3 — Cutscene flow

The default `Show Mode` is **Manual** — meaning the bar STAYS HIDDEN until something calls `PlayIntro()` on it. This is exactly what you want for a cutscene intro.

The `PlayIntro()` method does two animations in parallel:

1. **CanvasGroup fades in** from alpha 0 → 1 over `fadeInDuration`.
2. **Fill grows** from 0 → current HP fraction over `introGrowDuration`, smooth-stepped for a satisfying "ramp up" feel.

After the intro, the bar STAYS visible and tracks the boss's HP as you damage him. It fades out only when the boss dies.

### Three ways to trigger PlayIntro

**Option A — From an existing cutscene Timeline (recommended if you use Timeline)**

1. Add a **Signal Track** to your cutscene Timeline.
2. At the end of the timeline, add a Signal Emitter.
3. On the BossHealthBarRoot GameObject, add a **Signal Receiver** component.
4. Wire the signal to call `BossHealthBarUI.PlayIntro` (no parameters version).

When the cutscene reaches that signal, the bar starts its intro.

**Option B — From an Animator's Animation Event**

1. In your cutscene Animator, scroll the timeline to the moment the bar should appear.
2. Right-click the timeline → **Add Animation Event**.
3. In the event, pick `BossHealthBarUI` → `PlayIntro()` from the function dropdown.

**Option C — Simple: call it from any script when the cutscene ends**

```csharp
// In your cutscene controller, after the cutscene plays:
void OnCutsceneEnd()
{
    FindFirstObjectByType<BossHealthBarUI>()?.PlayIntro();
}
```

Or wire it through a `UnityEvent` on a CutsceneEndTrigger:

```csharp
public UnityEvent onCutsceneEnd;
// In Inspector, drag the BossHealthBarRoot, pick PlayIntro
```

**Option D — Quick test without a cutscene**

Just put a temporary script that calls `PlayIntro` in Start with a 2-second delay:

```csharp
void Start() {
    Invoke(nameof(StartIntro), 2f);
}
void StartIntro() {
    FindFirstObjectByType<BossHealthBarUI>().PlayIntro();
}
```

Watch the bar fade in and fill up. Once it works, replace this with your real cutscene trigger.

## Part 4 — What happens at runtime

Timeline of events in a boss arena scene:

1. Scene loads. Boss is in place. Health bar is hidden (alpha 0, fill 0).
2. Cutscene plays. Bar stays hidden.
3. Cutscene ends → `PlayIntro()` fires.
4. CanvasGroup fades in over 0.5s. AT THE SAME TIME, the fill smoothly animates from 0 → full (or whatever the boss's current HP fraction is).
5. After 1.5s the bar is fully visible at full HP.
6. Player engages boss. Each hit drops the fill smoothly (lerpSpeed = 6).
7. Color shifts from dark red → orange as HP nears 0.
8. Boss dies. Bar shows 0 / max for 1.5s, then fades out.
9. Player retries → scene reloads → bar resets to hidden → next cutscene → `PlayIntro` again.

## Part 5 — Tuning levers

| Field | Effect |
|---|---|
| `Intro Grow Duration` (1.5s) | How long the intro fill takes to grow to full. 0.5 = punchy, 3+ = dramatic. |
| `Fade In Duration` (0.5s) | How quickly the bar appears. Tied to feel of cutscene transition. |
| `Lerp Speed` (6) | How fast the bar reacts to damage during combat. Higher = snappier. |
| `Hide After Death Delay` (1.5s) | How long "0 HP" stays visible after death. Lets the player register the kill. |
| `Full Color / Low Color` | The color ramp. Swap to your art palette. |

## Part 6 — Verification

1. Open the boss scene, hit Play.
2. Bar is invisible during the cutscene.
3. At cutscene end → bar fades in + fill grows from 0 → 100%.
4. Bar stays visible. Hit boss → fill drops smoothly.
5. Boss dies → bar shows 0, holds 1.5s, fades out.

If anything's wrong:
- Bar never appears → `Verbose` ON, check console. "Auto-bound to FatRatBoss_X" should appear at start. "PlayIntro(...)" should appear at cutscene end.
- Bar appears immediately at scene start → `Show Mode` is set to `OnStart` or `OnFirstDamage` instead of `Manual`. Switch to Manual.
- Fill doesn't grow during intro → `Fill Image` ref is missing, or its **Image Type** isn't set to **Filled** with **Horizontal** fill method.
- Bar tracks the wrong character → manually drag the correct boss EntityStats into `Target Boss`.

## Optional polish (later)

If you want a more dramatic boss bar:
- **Two-layer fill** — a yellow "ghost" bar that lags behind the red bar by ~0.5s, showing damage taken as a yellow chunk that drains into the empty space.
- **Boss portrait** — left of the bar, small icon of the boss.
- **Phase markers** — vertical tick marks at HP thresholds where the boss changes patterns.
- **Shake on hit** — small ShakeUI script that bumps the bar when the boss takes damage.

All of these are additive — none change the core script. Tell me when you want to add any.
