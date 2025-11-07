# XREE-LAB

This repository hosts a small Unity experiment (Meta Quest target) that spawns four draggable, world-space waveform panels (CH1..CH4) and wires them to the project's interaction building-blocks so the Meta/Interaction SDK hand/pinch grabbing works.

This README summarizes what we've built so far, what we tried, what works/doesn't, where key assets and scripts live, and how to test on the headset (including adb log capture commands and the diagnostics the project prints).

## High-level goal
- Spawn four waveform panels in front of the user (CH1..CH4).
- Panels should be grabbable by Meta hand/pinch interactions (the project's building-block wiring: parent Rigidbody/Collider/Grabbable + child HandGrabInteractable + GrabInteractable + MovementProvider).
- Allow creating a prefab in Editor and reliably instantiating it at runtime so inspector-configured HandGrab / MovementProvider / rules are preserved.

## What is implemented
- Waveform visuals: a small `WaveformPanel` component (in `Assets/Scripts`) drives a LineRenderer and simple fake waveform parameters (frequency/amplitude/noise). Panels are lightweight, designed for prototype/dummy data.
- `WaveformManager` (`Assets/Scripts/WaveformManager.cs`) is responsible for spawning the four panels. It prefers to instantiate a configured prefab (`panelPrefab`) assigned in the inspector (recommended). If `panelPrefab` is not set it creates a procedural fallback panel at runtime.
- Runtime injection and compatibility code: to make runtime-spawned panels work with the Meta Interaction SDK, we've implemented reflection-based fallbacks that try available injector methods (InjectColliders, InjectRigidbody, InjectPinchGrabRules, InjectOptionalMovementProvider, etc.) and, if needed, set private backing fields directly where safe.
- Diagnostics: `WaveformManager` now logs a set of diagnostics right after each instantiation (camera list, renderers, material shader names, renderer bounds). There's also `TestGreenCubeSpawner` and `PanelGrabVerifier` scripts used during debugging to verify grabbing and backing-field injection.
- Editor helper: `Assets/Editor/SaveSelectedAsWaveformPrefab.cs` adds a menu item to save a selected GameObject to `Assets/Prefabs/Panel_CH.prefab` so you can build and serialize a prefab in Editor with the correct Interaction SDK values.

## Key files and game objects
- `Assets/Scripts/WaveformManager.cs` — spawns panels, logs diagnostics, does defensive injection for the Interaction SDK. Public field `panelPrefab` points to the prefab asset to instantiate.
- `Assets/Prefabs/Panel_CH.prefab` — the prefab we use for panels (example). Place a reference to this asset into `WaveformManager.panelPrefab` in Edit mode (Project view) so the reference persists into builds.
- `Assets/Scripts/WaveformPanel.cs` — waveform drawing & parameters.
- `Assets/Scripts/TestGreenCubeSpawner.cs` — test spawner used while developing the injection logic.
- `Assets/Scripts/PanelGrabVerifier.cs` — verifier that runs checks and prints results after spawn.
- `Assets/Editor/SaveSelectedAsWaveformPrefab.cs` — Editor utility to create prefabs from selected scene objects into `Assets/Prefabs/`.

## Prefab contents and materials
- `Panel_CH.prefab` (in `Assets/Prefabs/`) contains:
	- Root GameObject `Panel_CH` with a Rigidbody and BoxCollider sized to the panel.
	- `WaveformPanel` component with default dummy waveform parameters (resolution, frequency, amplitude, color).
	- `LineRenderer` used for waveform visualization.
	- Child GameObjects: `Background` (Quad, MeshRenderer, material `Panel_Background`), frame edges (`Edge_Top`, `Edge_Left`, etc.), and a `Grab_Handle` child used as the grab handle.
	- Interaction SDK components wired on the prefab: `Grabbable` (parent), `GrabInteractable`, `HandGrabInteractable` (installation child) and `MoveTowardsTargetProvider`.
- Materials used:
	- `Assets/Panel_Background.mat` — URP-compatible material (Unlit/URP) to avoid magenta artifacts on URP builds.
	- `Assets/Panel_Frame.mat`, `Assets/Panel_Handle.mat` — small materials for frame/handle.

The prefab was intentionally assembled in Editor so the HandGrabInteractable serialized properties (Pinch/Palm grabbing rules, MovementProvider references) are preserved and don't need fragile runtime reflection to set.

## What has been tried (summary of approaches)
- Runtime-only wiring by creating panels procedurally and using reflection to set private backing fields and call injector methods. This worked in the Editor in many cases but was fragile due to Start() ordering races and un-serialized runtime changes not persisting into build-time assets.
- Prefab-first workflow: create a fully-configured prefab in Editor (with Inspector-set HandGrab rules, MovementProvider, and materials) and then instantiate that prefab at runtime. This is more robust and recommended.
- Added a one-frame re-injection coroutine (`ReinjectHandGrabRulesNextFrame`) to try to address Start() ordering races when reflection is unavoidable.

## What works
- Prefab when created in Editor and assigned as an asset in `WaveformManager.panelPrefab` shows correct visuals and grabbing behavior in Editor Play mode.
- Diagnostic logging in Editor and in-device build (if you build with Script Debugging enabled) prints instantiation and renderer/material details.
- Reflection/injection utilities successfully find common injection methods for many Interaction SDK versions and fall back to setting private backing fields where available.

## Known issues / What didn't work reliably
- If you assign a scene object instance (a Hierarchy object) to `WaveformManager.panelPrefab` during Play instead of the prefab asset from the Project view, that scene reference does not persist to builds — panels will not appear in the built player. Always assign the prefab asset (Project → Assets/Prefabs/Panel_CH).
- Some earlier edits introduced duplicate/fragmented code in `WaveformManager.cs` which caused compile errors; those were cleaned up. If you see compile errors, fix them first — scripts won't run until compilation succeeds.
- Runtime reflection is brittle across SDK versions and can miss private fields with different names or signature changes; prefer the prefab workflow.
- Shader/material pitfalls: if a material uses a shader that isn't available on the Android/Quest build (or not included in the build), objects may appear invisible or magenta on device. Use URP-compatible Unlit shaders for stable results and verify shaders are included.

