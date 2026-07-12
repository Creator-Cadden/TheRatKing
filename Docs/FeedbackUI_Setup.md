# Feedback UI Setup

Three new pieces:

- **DamageNumberSpawner** — floating "30" / "5" on enemies when they get hit
- **XPGainIndicator** — "+10 XP from Grunt_Rat" rising notification
- **LevelUpIndicator** — "LEVEL UP! Press TAB to spend points" panel

Plus a small change: `XPSystem.AddXP` now takes an optional source name, and `EnemyXPDrop` passes the enemy's name through.

---

## 1. DamageNumberSpawner — per-entity

### Where it goes
On every entity that should show damage numbers:
- `Player.prefab` (root)
- Every enemy prefab (`GruntCone`, `ToughGruntTest`, `BossTest`, future Captain, future FatRat_Boss)

### Setup
Open each prefab → **Add Component → Damage Number Spawner**. Defaults are reasonable.

### Inspector knobs worth knowing
| Field | What it does |
|---|---|
| `Spawn Offset` | Y is height above the entity's pivot. Default 1.6 — works for ~human-sized rats. For the giant boss, bump to 3-4. |
| `Font Size` | World units. 5 default. For the boss prefab, you might want 8-10 so numbers read at distance. |
| `Low Color` / `High Color` | Color ramp from `Low Damage` to `High Damage`. Big hits (crits) tint orange-red. |
| `Scatter Radius` | Random XZ jitter so 10 hits don't stack on the same pixel. |

### How it works
Listens to `EntityStats.onDamageTaken`. Creates a new `TextMeshPro` 3D object at the entity's position with the damage amount. The number floats up, fades out, and self-destroys after ~1.2s. No prefab to author — it's all runtime.

Numbers are world-space, so they're rendered through the Main Camera → into the RenderTexture → pixelized along with the rest of the world. Free pixel-art consistency.

### Put on the Player too?
Yes — when the player takes damage, a number floats off them. Tells you exactly how much that hit was. Skip it if you find it visually noisy on the player and want the red flash to be the only feedback there.

---

## 2. XPGainIndicator — one per scene

### Where it goes
On the **MainUI canvas**, an empty container where the feed lines should rise from.

### Setup
1. Open MainUI canvas. Create empty GameObject → name `XPFeed`.
2. RectTransform: anchor it where the feed should be — bottom-right is a common pick. Give it some width (e.g. 240) and height (e.g. 28). The lines spawn at the bottom of this rect and rise.
3. **Add Component → XP Gain Indicator**.
4. (Optional) If you have a TMP_Text prefab with your project font, drag it into **Text Prefab**. If empty, the script creates plain bold gold text at runtime.
5. Tweak `Lifetime` (2.2s), `Float Distance` (70px — how far the line rises), `Format With Source` / `Format No Source` strings.

### How it works
Auto-finds the player, hooks `XPSystem.onXPGainedFromSource`. On each gain, spawns one line: `+10 XP from Grunt_Rat`. The line rises and fades over `Lifetime`, then self-destroys.

### Enemy display names
`EnemyXPDrop` has a new field **Display Name**. Leave it blank to use the GameObject's name minus `(Clone)`. Fill it for nicer text:
- ConeGrunt prefab → Display Name: `Grunt Rat`
- ToughGruntTest prefab → Display Name: `Tough Rat`
- BossTest prefab → Display Name: `Captain`
- (future) FatRat_Boss prefab → Display Name: `Fat King`

---

## 3. LevelUpIndicator — one per scene

### Where it goes
On the **MainUI canvas**, a panel on the right side of the screen.

### Setup
1. On MainUI canvas, create the hierarchy:
   ```
   LevelUpPanel       (empty + RectTransform — anchored to right-middle)
     ├── BigText      (TMP_Text — "LEVEL UP!" large font)
     └── HintText     (TMP_Text — "Press TAB to spend stat points" smaller font)
   ```
2. Anchor `LevelUpPanel` to the right-middle of the screen. RectTransform: Anchor preset → right-middle, then offset whatever pixels to inset from edge.
3. Position BigText at top of the panel, HintText below it (use a Vertical Layout Group on the panel if you want auto-spacing).
4. Add Component on **LevelUpPanel** → **Level Up Indicator**.
5. In Inspector:
   - Drag `BigText` into **Main Text**.
   - Drag `HintText` into **Sub Text**.
   - Tweak `Big Message Hold` (2.5s — how long "LEVEL UP!" stays before downgrading to the reminder).

### How it works
Auto-finds the player, hooks `XPSystem.onLevelUp` and `XPSystem.onStatPointSpent`.

Flow:
- Level up → fade in → "LEVEL UP! / Press TAB to spend stat points" → hold → downgrade to "Unspent points: N / Press TAB to spend" → stays until all spent → fade out
- If player loads a save with unspent points already → fades straight to the reminder

The reminder updates with the actual remaining count. As they spend points, the number ticks down. At zero, the panel fades out.

---

## Quick wiring summary

| Where | What | Why |
|---|---|---|
| Player prefab | DamageNumberSpawner | Show numbers when player takes damage |
| Each enemy prefab | DamageNumberSpawner | Show numbers when each enemy takes damage |
| Each enemy prefab → `EnemyXPDrop.Display Name` | Set per type | Nicer "+5 XP from Grunt Rat" |
| MainUI canvas → XPFeed empty | XPGainIndicator | Floating "+X XP" lines |
| MainUI canvas → LevelUpPanel + children | LevelUpIndicator | Level-up notification + reminder |

---

## Things to verify after setup

1. **Hit a Grunt** with the blade — number appears above its head, floats up, fades. Color is white-ish (small hit).
2. **Crit a Grunt** — number is larger, tinted toward orange.
3. **Kill a Grunt** — `+10 XP from Grunt Rat` rises and fades in the corner.
4. **Hit enough enemies to level up** — `LEVEL UP!` panel fades in, holds, then downgrades to `Unspent points: 1`.
5. **Open Stat Menu (Tab), spend points** — counter ticks down. At zero, the panel fades out.

## Common gotchas

- **No damage numbers** → check the entity has `EntityStats` (the spawner relies on `onDamageTaken`).
- **Numbers too small / too big** → tune `Font Size` on `DamageNumberSpawner`. Bosses need bigger; small grunts can use smaller.
- **No XP text on kill** → the enemy needs an `EnemyXPDrop` and the `XPSystem` on the player needs to be receiving `onXPGainedFromSource`. (Existing scripts already wire this.)
- **Level up panel never appears** → it's the same root-binding issue as MainUI. The script auto-binds on Start and OnEnable, but if you reload scenes a lot it can lose the player reference. If it ever stops working mid-game, save the scene and reload from MainMenu.
