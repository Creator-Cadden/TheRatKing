# Animator Architecture — How Triggers, States, and Per-Weapon Animations Connect

## The architecture in one paragraph

The Player has TWO animators per equipped weapon: the **rat body animator** (a single shared animator on the rat skeleton) and the **active weapon's mesh animator** (one per weapon — blade, hammer, bow). The rat body animates the rat itself (running, jumping, swing pose). The weapon animator animates the weapon mesh (sword glow, hammer spin, bow draw/release). They run in parallel and stay in sync via shared parameter names.

`WeaponModelSwapper` is the **single source of truth** for animator references. Every script that fires animator parameters reads from it.

## The script ownership

| Script | What it does to animators |
|---|---|
| **WeaponModelSwapper** | Holds Inspector refs: rat body animator (`weaponAnimator`), `bladeAnimator`, `hammerAnimator`, `bowAnimator`. Toggles model visibility + writes the `Weapon` int parameter on the rat body animator. Exposes `ActiveWeaponAnimator` property — returns the right weapon's animator. |
| **PlayerCombat** | Holds Inspector ref to rat body animator (`_primaryAnimator`). On attack, fires `Attk` / `AirAttk` triggers on BOTH `_primaryAnimator` AND `_swapper.ActiveWeaponAnimator`. |
| **PlayerMovement** | Same pattern — holds `_primaryAnimator`, fires every SetBool/SetFloat on both `_primaryAnimator` AND `_swapper.ActiveWeaponAnimator`. (NEW after the refactor — used to have a hard-coded `_secondaryAnimator`.) |
| **BladeCombat / HammerCombat / BowController** | Don't hold any animator refs. PlayerCombat fires their triggers. |

So you set up animator refs in TWO places only:
1. `PlayerCombat._primaryAnimator` → drag the rat body animator
2. `WeaponModelSwapper` → drag the rat body animator into `weaponAnimator`, plus drag each weapon's mesh animator into the matching slot

Then both PlayerCombat and PlayerMovement read the active weapon animator dynamically.

## The shared parameter names

All four animators (rat body + blade + hammer + bow) should accept the same parameter names so PlayerCombat / PlayerMovement can fire triggers blindly without knowing which weapon is active:

| Parameter | Type | Set by | What it means |
|---|---|---|---|
| `Running` | Float | PlayerMovement | 0 = idle, 1 = moving. Drives the Blend Tree. |
| `Jump` | Bool | PlayerMovement | True briefly when jump starts. |
| `Falling` | Bool | PlayerMovement | True while in the air falling. |
| `Grounded` | Bool | PlayerMovement | True while on the ground. |
| `Contact` | Bool | PlayerMovement | True for one frame on landing. |
| `Attk` | Trigger | PlayerCombat | Player pressed LMB while grounded. |
| `AirAttk` | Trigger | PlayerCombat | Player pressed LMB mid-air. |
| `Weapon` | Int | WeaponModelSwapper | 0=Blade, 1=Hammer, 2=Bow. Set on weapon swap. |
| `Death` | Bool | GameManager / EntityStats | True when the player dies (existing). |

Every animator (rat body, blade, hammer, bow) should have these parameters defined — even if a parameter doesn't do anything for that weapon. Unity silently no-ops when you set a parameter that exists but isn't wired into a transition.

## The rat body animator — branching attacks by weapon

This is where your bow delay and hammer-using-blade-animation bugs almost certainly live. The rat body animator needs separate ATTACK STATES for each weapon and TRANSITIONS that route based on the `Weapon` int.

```
                    [Blend Tree]  ← idle/run blend, default state
                          │
                          │  trigger: Attk
                          │
            ┌─────────────┼─────────────┐
            ↓             ↓             ↓
       Weapon==0     Weapon==1      Weapon==2
       (Blade)       (Hammer)       (Bow)
            ↓             ↓             ↓
       AttackRun     HammerWindup    Bow Draw
         (blade        → Hammer        (bow's
         swing)        → recover       attack
                                       pose)
```

### Three transitions out of Blend Tree (basic attacks):

| From | To | Condition | Has Exit Time |
|---|---|---|---|
| Blend Tree | AttackRun | `Attk` trigger + `Weapon == 0` | **NO** |
| Blend Tree | HammerWindup | `Attk` trigger + `Weapon == 1` | **NO** |
| Blend Tree | Bow Draw | `Attk` trigger + `Weapon == 2` | **NO** |

**Critical: "Has Exit Time" must be UNCHECKED on every weapon attack transition.** If it's checked with a value like 0.9, the transition won't fire until the current state has played for 90% of its duration. That's where your **bow delay** is coming from. Triggers are supposed to fire immediately; Exit Time forces a wait first.

### Transition Duration

Set the transition **Duration** field to a small value (0.05s–0.1s) for snappy attacks. Larger durations blend smoother but feel delayed.

### Same for the air attack transitions:

| From | To | Condition | Has Exit Time |
|---|---|---|---|
| Blend Tree | AirAttk (blade) | `AirAttk` trigger + `Weapon == 0` | NO |
| Blend Tree | (hammer air slam state) | `AirAttk` trigger + `Weapon == 1` | NO |
| Blend Tree | Bow Release / BowAir | `AirAttk` trigger + `Weapon == 2` | NO |

If your rat body animator only has an "AirAttk" state for blade and nothing for the other two, the trigger fires but the animator just plays the blade air attack. That's why **hammer is using blade's animation** — there's no transition from Blend Tree to a hammer-specific state on the rat body.

### How to fix the hammer using blade animation

Open the **rat body animator** (not the hammer's). Add a hammer-specific attack state (e.g. `HammerSwingPose`). Add a transition from BlendTree to that state with conditions:
- `Attk` trigger
- `Weapon` int == 1

Now when hammer is equipped and you press LMB, the rat body animates with the hammer-specific pose instead of falling through to the blade state.

### How to fix the bow delay

Open the rat body animator. Find the transition Blend Tree → `Bow Draw` (or whatever your bow state is). Verify:
- **Has Exit Time = UNCHECKED**
- **Transition Duration = 0.05–0.1s** (not 0.25 default)
- **Settings → Interruption Source = Current State** (lets a newer trigger interrupt an ongoing transition)
- The transition has conditions `Attk` AND `Weapon == 2`

If Has Exit Time is checked, that's your delay — uncheck it.

## The per-weapon animators — what each one needs

Each weapon's mesh animator (the one on the Blade/Hammer/Bow GameObject under the rat's hand) needs the same parameter names so PlayerCombat / PlayerMovement can drive them blindly.

### Blade animator (the simplest — your reference)

States you have:
- Blend Tree (idle / run blend)
- AttackRun — the swing pose
- AirAttk — the spin
- Jump / Fall / Land — these aren't strictly needed if the rat body animator handles them, but having them on the weapon animator keeps the weapon visually tracking

Triggers wired:
- `Attk` from Blend Tree → AttackRun
- `AirAttk` from Blend Tree → AirAttk

Has Exit Time: **NO** on attack transitions. **YES** on AttackRun → Blend Tree (use a small Exit Time like 0.9 so the swing plays out before returning).

### Hammer animator

Same structure as blade plus the windup → swing → recover sequence:
- Blend Tree
- HammerWindup — anticipation pose
- Hammer (1) — actual swing
- Hammer recover — settle back

Transitions:
- Blend Tree → HammerWindup on `Attk` trigger (Has Exit Time: NO)
- HammerWindup → Hammer (1) automatically after its animation (Has Exit Time: YES, ~1.0)
- Hammer (1) → Hammer recover automatically (Has Exit Time: YES, ~1.0)
- Hammer recover → Blend Tree automatically (Has Exit Time: YES, ~0.9)

This gives the hammer a deliberate "wind up, swing, settle" feel that matches HammerCombat's `swingWindup` field.

### Bow animator

You showed me Bow Draw → Bow Release in the rat body animator. The bow's own animator (the bow mesh) needs:
- Bow Still (idle)
- Bow Draw (drawing back the string)
- Bow Release (snap forward — the actual shot animation)
- BowAir (mid-air attack)
- BowMove (drawn while walking, optional)

Transitions:
- Blend Tree → Bow Draw on `Attk` trigger + condition `IsAiming == true` (if you want)
- Bow Draw → Bow Release automatically after draw anim
- Bow Release → Blend Tree automatically

OR simpler if no aim/free-look distinction at the animator level:
- Blend Tree → Bow Release on `Attk` trigger (Has Exit Time: NO, Duration: 0.05)
- Bow Release → Blend Tree automatically (Has Exit Time: YES, ~0.9)

For mid-air shots:
- Blend Tree → BowAir on `AirAttk` trigger (Has Exit Time: NO)
- BowAir → Blend Tree (Has Exit Time: YES)

## Why your bow rat-body animation is delayed

Two likely causes, either one or both:

**1. `Has Exit Time` is checked on the Blend Tree → Bow Release transition in the rat body animator.**
The transition fires on Attk trigger, but Unity waits for the current state (the Blend Tree) to reach exit time before transitioning. If exit time is 0.9, you wait up to 90% of the blend tree's animation length before the bow draw plays.
**Fix**: uncheck Has Exit Time on that transition.

**2. The bow's mesh animator transitions are immediate but the rat body's transitions aren't.**
Bow mesh animator plays its draw immediately on the trigger. Rat body animator waits for exit time. Result: bow string moves while the rat is still in run pose.
**Fix**: same — uncheck Has Exit Time on the rat body's bow transitions.

## Inspector wiring — who needs which animator slot

After the recent refactor, here's where every animator reference lives:

```
Player.prefab
├── PlayerCombat
│   └── _primaryAnimator → drag the rat body animator
│
├── PlayerMovement
│   └── _primaryAnimator → drag the rat body animator (same one)
│
└── WeaponModelSwapper
    ├── Weapon Animator → drag the rat body animator (same one)
    ├── Blade Animator  → drag the blade mesh animator
    ├── Hammer Animator → drag the hammer mesh animator
    └── Bow Animator    → drag the bow mesh animator
```

The rat body animator is referenced three times (PlayerCombat, PlayerMovement, WeaponModelSwapper). That's intentional — each script has its own reason to know about it, and you don't want any of them to be coupled through "ask another script for it."

The per-weapon mesh animators are ONLY on WeaponModelSwapper. PlayerCombat and PlayerMovement get them via `_swapper.ActiveWeaponAnimator`.

## What "ActiveWeaponAnimator" returns at runtime

```csharp
// In WeaponModelSwapper:
public Animator ActiveWeaponAnimator => _stats.EquippedWeapon switch {
    WeaponType.Blade  => bladeAnimator,
    WeaponType.Hammer => hammerAnimator,
    WeaponType.Bow    => bowAnimator,
    _                 => null,
};
```

So at any given moment:
- Blade equipped → returns the bladeAnimator
- Hammer equipped → returns the hammerAnimator
- Bow equipped → returns the bowAnimator

PlayerCombat fires `Attk` on this. The right animator gets it. The wrong ones don't see it. Clean.

## Per-weapon controller does NOT need its own animator slot

Because of the above, you don't need to add Inspector slots to BladeCombat / HammerCombat / BowController. They focus on combat logic. PlayerCombat handles the animator triggers via the swapper.

If you ever WANT a weapon controller to have weapon-specific animator logic (e.g. BowController triggers a specific "ChargeStart" on the bow when the player begins holding LMB), it can do so via:

```csharp
// In BowController:
private WeaponModelSwapper _swapper;
void Awake() { _swapper = GetComponent<WeaponModelSwapper>(); }

public void BeginAimedShot() {
    _swapper?.ActiveWeaponAnimator?.SetTrigger("ChargeStart");
    _isCharging = true;
    // ...
}
```

So weapon controllers CAN fire their own animator triggers when they need to. But for the basic Attk / AirAttk shared by all weapons, PlayerCombat handles it centrally.

## Quick diagnostic flow

When an animation isn't playing right, work through this:

1. **Set Verbose ON in WeaponModelSwapper.** Press P/1/2/3 to swap weapons. Console should show "Active weapon → X" each time.
2. **In the rat body animator, watch the Animator window during Play mode.** When you press LMB, does the state flash to the right attack state? If no → transition issue (Has Exit Time, missing condition, missing transition).
3. **In the weapon's animator, watch the same way.** When you press LMB, does the weapon's swing state play? If no → either the WeaponModelSwapper slot is empty for that weapon, or the weapon's animator doesn't have the right transition wired.
4. **Check parameter values in the Animator window's Parameters panel during Play.** When you swap weapons, does the `Weapon` int change? When you attack, does the `Attk` trigger flash? These tell you the script side is working.

## Quick fixes for your three current bugs

### Hammer uses blade's animation
1. Open the rat body animator.
2. Add a hammer-specific attack state if it doesn't exist, OR check the existing transition from Blend Tree → AttackRun. If it has no `Weapon` condition, blade's transition will fire for hammer too because of fall-through.
3. Add `Weapon == 1` as a transition condition to a hammer-specific state.
4. Verify the WeaponModelSwapper's `Hammer Animator` slot is set.

### Bow rat body animation delays
1. Open the rat body animator.
2. Find the transition from Blend Tree → bow attack state (Bow Draw or similar).
3. **Uncheck Has Exit Time** on that transition.
4. Set Transition Duration to 0.05–0.1.

### Hammer using blade after the above fix
1. Verify WeaponModelSwapper.hammerAnimator is set in the Inspector to the actual hammer mesh's Animator component.
2. With Verbose ON, swap to hammer — console says "Active weapon → Hammer".
3. With Animator window open on the hammer animator, swap to hammer — confirm the animator is selected (not the blade's).
4. If the hammer animator opens to blade-looking states, your hammer mesh probably has the wrong Animator Controller asset assigned. Each weapon mesh needs its OWN Animator Controller.

## File reference — what I changed

- `PlayerMovement.cs` — removed `_secondaryAnimator` serialized field. Added `_swapper` cache. SetBool/SetFloat now fire on `_primaryAnimator` AND `_swapper.ActiveWeaponAnimator` (dynamically resolves to whichever weapon is equipped).
- `PlayerCombat.cs` — already does the same pattern with `FireAttackAnims()` helper.

No changes to BladeCombat / HammerCombat / BowController — they don't touch animators directly. PlayerCombat handles attack triggers for all weapons.
