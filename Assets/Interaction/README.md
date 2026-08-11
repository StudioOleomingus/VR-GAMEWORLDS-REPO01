# Look → Pick Up → Inspect

An interaction layer for the Easy Peasy First Person Controller. Nothing in
`Assets/EasyPeasyFirstPersonController/` was modified, so the asset stays upgradeable.

## Setup

1. **Player** — add `PlayerInteractor` to the same GameObject as your `FirstPersonController`
   (the root of the `FirstPersonController` prefab, the one with the CharacterController).
   Everything auto-wires: it finds the child camera, finds the controller to freeze, builds
   the popup canvas, and creates the depth-of-field volume at runtime.

2. **Objects** — add `Interactable` to anything you want to pick up. It needs a **Collider**.
   Set `Display Name` and `Action Hint`; leave the rest at defaults to start.
   If the object has a Rigidbody, put the `Interactable` on the *same* GameObject as the Rigidbody.

3. **TextMeshPro** — if you have never used TMP in this project, run
   `Window ▸ TextMeshPro ▸ Import TMP Essential Resources` once, or the popup renders blank.
   (A console warning will tell you if this is needed.)

That's it. Play, look at the object, walk up, press **E**.

## Controls

| Input | Action |
|---|---|
| Look at object within `Focus Range` (6 m) | Popup fades in above it |
| Walk within `Pickup Range` (2.5 m) | Popup gains an `[E]` hint |
| **E** | Object lifts on a gentle arc into an inspect pose; background defocuses; movement and camera look freeze |
| Mouse | Rotates the held object, trackball-style |
| **E** | Object settles back exactly where it was; blur fades; control returns |

## Tuning

**PlayerInteractor**

- `Focus Range` / `Pickup Range` — the two distance thresholds.
- `Aim Assist Radius` — thickens the look ray so you don't need pixel-perfect aim. 0 disables it.
- `Pickup Duration` / `Place Duration` / `Travel Arc Height` — the "gentleness" of the lift. Longer + higher arc reads slower and more deliberate.
- `Auto Fit Padding` — how much of the screen the object fills. Higher = further away = smaller.
- `Rotate Sensitivity` / `Rotate Damping` — damping is an exponential smoothing rate; drop it to ~5 for a heavy, floaty object, raise to ~25 for something crisp.
- `Aperture` / `Focal Length` — blur strength. Low aperture and high focal length = more blur. Focus distance is driven automatically to wherever the object is being held.
- `Disable While Inspecting` — components switched off during inspect. Add your own scripts here (footstep audio, weapon sway) if they should pause too.

**Interactable**

- `Inspect Rotation Offset` — the euler pose, relative to the camera, the object settles into. Use this to present a specific face.
- `Inspect Distance Override` — overrides the auto-fit entirely for one object.
- `Inspect Fit Multiplier` — nudges the auto-fit for one object without touching the global setting.
- `Label Height Padding` / `Label Offset` — where the popup floats.
- `Can Pick Up` — false gives you a look-at-only label (signage, scenery).
- Events: `On Focused`, `On Unfocused`, `On Picked Up`, `On Placed Down`.

**InteractionPromptUI** — added automatically to the player at runtime. Select the player
in play mode to tweak `Panel Color`, font sizes, `Scale Per Metre` and `Fade Speed`;
copy the values you like back onto the component in the inspector... or just edit the
defaults in `InteractionPromptUI.cs`.

## Notes and gotchas

- **Post-processing** is force-enabled on the player camera at runtime so the blur works.
  If you'd rather manage that yourself, untick `Use Depth Of Field`.
- The blur is a runtime `Volume` at priority 100. Assign your own to `Inspect Volume`
  if you want to author it as an asset — the interactor will add a `DepthOfField`
  override to it and drive the weight, leaving your other overrides alone.
- **Colliders are disabled** while an object is held, so it can't shove the player or block
  your own raycasts. Original enabled states are restored on place.
- Held objects can intersect walls if you pick something up while pressed against geometry.
  Since movement is frozen you generally can't get into that state, but shrink
  `Max Hold Distance` if you see it.
- **Scripting hooks:** `PlayerInteractor.IsInspecting`, `.HeldObject`, `.FocusedObject`,
  and `.CancelInspect()` to force-drop before a scene load or cutscene.

## Files

```
Assets/Interaction/Scripts/
  Interactable.cs          marker + per-object settings
  PlayerInteractor.cs      raycast, pickup/place state machine, blur, player freeze
  InteractionPromptUI.cs   world-space billboard popup, built at runtime
```
