# Changes

## 0.4.48 beta1 — hand-steered movement

Release of the 0.4.45–0.4.48 work below as one beta: pointed thrust, direct ratchet rotation, and the grapple on the right controller in the hand-steered layout, with the two-stick layout unchanged on F10.

## 0.4.48 — right-hand grapple controls and smoother direct rotation

- Hand-steered layout: grapple retract and tether preview/place move from the left trigger to A, interact and grapple push move from left X to B, and the right stick click recalls tethers. The left hand now only steers. All three are rebindable under `[HandMovement]`; the stick layout is unchanged.
- Direct rotation also sets the body transform each frame when the rigidbody has no interpolation, so the rendered body no longer lags the hand by a physics step at low frame rates. Logs the rigidbody interpolation mode once for diagnosis.
- The two-stick layout's grip + left stick D-pad shift no longer fires in the hand-steered layout, where that combination is steering. Holding the grip and thrusting forward no longer opens the scanner.

## 0.4.47 — direct (ratchet) rotation (local test build)

- Holding the left grip now locks the suit orientation to the controller one to one: it turns exactly as the controller turns from the orientation at the squeeze. Release, reposition the hand, hold again to continue. Applied after the game's own axis rotation each frame; turning velocities are zeroed while held so release leaves no coasting. The 20 degree twist deadzone still removes accidental roll.
- `RotationMode = Rate` restores the 0.4.46 speed-control behaviour; `DirectRotationGain` scales the applied angle.

## 0.4.46 — hand-steered movement (local test build)

- Add a hand-steered movement layout, on by default. The left stick thrusts in the direction the left controller points, giving analog thrust on all three axes from one stick. Holding the left grip and turning, tilting or twisting that controller rotates the body; the orientation at the squeeze is neutral. Roll has its own larger deadzone so it needs a deliberate twist. A short haptic pulse confirms each new rotation anchor.
- In this layout the right stick is the D-pad in gameplay without a modifier; A/B, the right stick click and the old roll buttons are unused. Left stick click brakes. Right grip still changes tool mode.
- F10 switches between hand-steered and two-stick movement and saves the choice. Thrust frame, deadzones, range, curve, buttons and inversions are configurable under `[HandMovement]`.
- Replaces the 0.4.45 space-mouse experiment, which moved thrust with hand position and proved hard to control. Desktop tests cover the rotation anchor, deadzones, roll threshold, axis signs and the controller-relative thrust mapping. Headset feel is untested until a Quest 3 / Virtual Desktop run.

## 0.4.44 beta1 — responsive tool switching and heat-bar visibility

- Apply the settling delay only when the same selected tool returns from being stowed for a grab or interaction. Ordinary tool selection no longer adds a minimum visibility delay. Retain synchronized heat-bar hiding and the guard against tools rising from the feet after interactions.
- Hide cutter heat-bar and tool-child UI graphics with the tool mesh; restore native opacity when visible. Includes the couch/gamepad support and shared tool presentation listed under 0.4.42 below. The final 0.4.44 gameplay test was accepted; the release preserves its binary.

## 0.4.43 — cutter heat-bar visibility (test build)

- Hide the cutter's heat-bar UI and attached canvas graphics together with its mesh during grabs, console interactions and the return animation, in both input modes. Restore native UI opacity when the tool is visible; heat/cooldown calculations and interaction input remain unchanged.

## 0.4.42 beta1 — couch/gamepad play and consistent tool presentation

- Add automatic switching between motion controls and native gamepad controls on deliberate button/stick input, plus fixed Gamepad and MotionControllers preferences. Idle tracking does not take over; release controls after switching.
- Support Xbox-style gamepads and Steam Controller gamepad layouts through Steam Input. Couch aiming uses the right stick with independent headset looking, native bindings, menus and gamepad vibration. Individual Steam Input layouts still require testing.
- Let native gamepad arbitration continue after handoff instead of repeatedly overriding the active device, addressing the input loss reported during early couch tests.
- Add readable world-space cutter/grapple aiming rings in couch mode. Preserve native tool range and game rules.
- Use one 65%-of-native default scale for cutter, grapple, held charge and detonator in both modes. Preserve original meshes/textures, native couch placement and calibrated motion aiming. Ignore the retired separate GrappleVisualScale setting.
- Hide stowed tools during grabs and console/door interactions. Wait for cutter/grapple return movement to settle before showing them again; hide separate native shadow copies.
- Retain the established HUD, menus, loading, structural scanner, helmet-damage and XR haptic fixes. Remove temporary interaction tracing; detailed input diagnostics remain off by default.

Versions 0.4.33–0.4.41 were local test builds. The user confirmed working controls, reticles, sizing, doors/consoles and tool return behavior in the final iterations. This release is packaging/diagnostic cleanup of that baseline; see docs/RELEASE-VALIDATION.md for validation limits.

## 0.4.32 beta1 — helmet damage and input recovery prevention

- Attach visor damage to the tracked head and read eye coverage from the actual HDRP/OpenXR render passes.
- Preserve native apparent sizing for both small white sprites and the larger white crack; keep repair/removal under game control.
- Fit the separate critical shattered-glass model across the headset view, retaining its native material and warning lights.
- Retry temporarily unavailable XR eye data instead of permanently reverting to the original close-up effects.
- Guard unsupported legacy joystick slots/indices that can interrupt input updates and leave subsequent updates stuck.
- Keep existing bindings, calibrated tools, HUD settings, scanner rendering and installer behavior unchanged.

Versions 0.4.28–0.4.31 were local test candidates, not public releases.

## 0.4.27 beta — portable release preparation

- Package the accepted 0.4.26 controls, tool calibration, HUD and haptics.
- Disable automatic performance recording on fresh installs; retain F9 and the explicit one-shot option.
- Remove retired performance-comparison switches and the unused early debug-ray setting.
- Add portable install/update/uninstall, Steam library detection, manual folder selection, verified backups, conflict detection and interrupted-operation recovery.
- Preserve user settings while removing known obsolete keys; include clean fresh BepInEx defaults with the console hidden.
- Replace historical setup instructions with current Pico/Frame controls, build instructions, known limitations and a test checklist.

## Accepted gameplay baseline

0.4.25 added native Steam Frame split-gamepad input. 0.4.26 added right full grip as Y and left full grip as D-pad Up while retaining the physical buttons. Earlier work added tracked tools/movement/menus, calibrated visuals, curved HUD, loading/menu fixes, structural scanner room/pressure labels, haptics and cached UI geometry. Detailed engineering history is retained in the source checkout.
