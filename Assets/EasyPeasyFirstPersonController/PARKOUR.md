# Parkour Movement Layer

Sprint momentum, wall running and landing impact, layered onto the Easy Peasy controller.
Unlike the interaction system, **this does modify the vendor scripts** — see *Modified files*
at the bottom before you update the asset from the Package Manager.

Everything is gated behind **`Enable Parkour`** on `FirstPersonController`. Turn it off and
the controller behaves exactly as it did before.

## What it does

**Momentum.** Holding Shift no longer snaps you to one sprint speed. Momentum builds from
0 to 1 over `Momentum Build Time`, and speed ramps `Sprint Speed → Parkour Speed` along with
it. FOV widens `Sprint Fov → Parkour Fov`, and motion blur climbs. Release Shift and momentum
bleeds away over `Momentum Decay Time`. Momentum carries into the air, so a fast run keeps
its speed and its wide FOV through a jump.

**Wall running.** Once momentum passes `Min Momentum To Wall Run`, walls become sticky. Get
alongside one and you latch on: the camera leans toward it, gravity drops to `Wall Run Gravity`
so you slide down slowly, and you travel along the surface at `Wall Run Speed`. **Press Space**
to launch off toward the opposite wall. Chain those launches and you can ping-pong down a
corridor indefinitely with no floor underneath.

**Landing impact.** Land hard enough and the camera pitches down toward the ground, then hauls
itself back up. Mouse look is ignored for the first part of the recoil, so the landing takes the
view away from you for a beat before handing it back.

## The tuning knob you asked for

**`Wall Jump Intensity`** (0.1–3, default 1) on `FirstPersonController` scales the whole
wall-to-wall jump at once. Raise it for a wilder, longer-range ping-pong; lower it for
something tight and controlled. The three forces underneath it are also individually exposed:

| Field | Default | What it does |
|---|---|---|
| `Wall Jump Side Force` | 7 | Push straight out from the wall — this is what carries you across |
| `Wall Jump Up Force` | 5 | Upward kick, so each bounce gains a little height |
| `Wall Jump Forward Force` | 3 | Push along the wall, preserving run direction through the jump |

All three are multiplied by `Wall Jump Intensity`.

## Setup

Nothing, if you already have the controller in your scene. On play it adds
`ParkourSpeedEffects` to itself, which builds the motion blur volume and enables
post-processing on the camera.

One thing worth setting: **`Wall Run Mask`**. Left as `Nothing` it falls back to `Ground Mask`,
which usually just works. Give walls their own layer if you want to control precisely what's
climbable.

## Tuning

**Momentum**

- `Parkour Speed` (9) — top speed at full momentum. `Sprint Speed` is the floor.
- `Momentum Build Time` (2.5s) — how long to reach top speed. Longer feels heavier.
- `Momentum Decay Time` (1s) — how fast it bleeds off. Short interruptions won't reset you.
- `Parkour Fov` (88) — FOV at full momentum. The single biggest speed cue; push it to 95+ for something aggressive.
- `Wall Run Sustains Momentum` (on) — wall running holds momentum steady instead of letting it decay. Turn off and long chains gradually slow you down.

**Wall run**

- `Min Momentum To Wall Run` (0.35) — how fast you must be going before walls grab. Raise it if you're sticking to things you didn't mean to.
- `Wall Run Gravity` (2.2) — vs. normal gravity 9.81. Lower drifts down more slowly. 0 holds height exactly.
- `Wall Run Max Duration` (2s) — per-wall time limit.
- `Wall Run Camera Tilt` (14°) — the lean. Needs `Use Camera Tilt` on.
- `Wall Reattach Cooldown` (0.25s) — how long the wall you just left stays un-grabbable. A *different* wall is always immediately available, which is what makes ping-pong work.
- `Allow Wall Run From Ground` (on) — off means you must jump at a wall to start.
- `Wall Run Entry Hop` (2.5) / `Wall Run Ground Grace` (0.25s) — these stop a ground-started run from cancelling itself on the first frame. Leave them alone unless a wall run refuses to start from a sprint.

**Landing impact**

- `Landing Impact Threshold` (6 m/s) — landings slower than this are ignored. Raise it if normal jumps are triggering the effect.
- `Landing Impact Max Speed` (18 m/s) — the speed that produces full strength.
- `Landing Pitch Amount` (28°) / `Landing Dip Amount` (0.45 m) — how far the view dives.
- `Landing Dive Duration` (0.12s) / `Landing Recover Duration` (0.45s) — keep the dive short and sharp; the recovery carries the weight.
- `Landing Look Lock Fraction` (0.55) — how much of the recoil you spend without control. Set to 0 to keep the visual but never take the mouse away.

**Speed blur** — on `ParkourSpeedEffects`, added automatically at runtime.

- `Max Motion Blur` (0.55) — intensity at full momentum. URP caps at 1.
- `Blur Threshold` (0.15) — momentum below which nothing is applied, so a jog stays clean.
- `Blur Response` (5) — how quickly blur chases momentum. Lower is laggier and more cinematic.
- `Blur Mode` — `CameraOnly` is cheap and safe. `CameraAndObjects` needs motion vectors.

## Notes and gotchas

- **Wall detection** casts left and right at 60% of the controller's height, and rejects
  anything more than ~17° off vertical, so floors and ramps won't grab you.
- **You can't stick by running face-first at a wall** — the entry check requires you to be
  travelling roughly along the surface, not into it.
- **Space is edge-triggered for wall jumps.** Holding it from a previous jump won't fire one;
  you have to release and press again. That's deliberate — it's what makes the ping-pong a
  rhythm rather than something that happens to you.
- **Two runtime Volumes** now exist if you're also using the interaction system: speed blur at
  priority 90 and inspect DoF at 100. They compose fine.
- The momentum FOV ramp rides on the existing `Use Fov Kick` toggle. Turn that off and you
  lose the FOV cue but keep the speed.

## Modified files

```
Scripts/FirstPersonController.cs        parkour fields, momentum, wall detection, launch queue,
                                        landing impact, jump edge detection, camera application
Scripts/PlayerStateFactory.cs           + WallRun()
Scripts/States/PlayerWallRunState.cs    NEW — the wall run state
Scripts/States/PlayerGroundedState.cs   momentum-scaled speed/FOV, wall run entry, landing report
Scripts/States/PlayerJumpingState.cs    launch consumption, wall run entry, momentum air speed
Scripts/States/PlayerFallState.cs       wall run entry, momentum air speed and FOV
Scripts/ParkourSpeedEffects.cs          NEW — momentum-driven motion blur
```

Each state also gained an early-return after `CheckSwitchStates()`. The original code kept
running movement for the rest of the frame after a transition had already fired, which would
have had the old state fighting the new one.
