# Architecture Index — Operation Dry Wire

> **Living document.** If you change a file's role, add or remove a script, change a layer or tag, change a cross-system call, or move level coordinates, update this file in the same change. Keep entries short; this is an index, not a manual.

Unity 6000.3 URP, a single-level FPS prototype. Design spec: [plan.md](plan.md) (original brief: [tristan fps game.md](tristan%20fps%20game.md)). The code is "spaghetti OK" on purpose: a few large MonoBehaviours, static singletons, no asmdefs (everything compiles into `Assembly-CSharp` / `Assembly-CSharp-Editor`).

## Start here

| You want to… | Read first |
|---|---|
| Change the map, add or move guards/hostages, or change prefab construction | [LevelBuilder.cs](../Assets/_Game/Editor/LevelBuilder.cs) |
| Change guard behaviour | [Guard.cs](../Assets/_Game/Scripts/AI/Guard.cs) |
| Change the stealth or noise rules | [NoiseSystem.cs](../Assets/_Game/Scripts/Noise/NoiseSystem.cs), [PlayerNoise.cs](../Assets/_Game/Scripts/Player/PlayerNoise.cs) |
| Change win/fail rules or the objective text | [GameManager.cs](../Assets/_Game/Scripts/Core/GameManager.cs) |
| Change the HUD | [MissionUI.cs](../Assets/_Game/Scripts/Core/MissionUI.cs), [DecibelMeter.cs](../Assets/_Game/Scripts/Noise/DecibelMeter.cs) |
| Tune the weapons | `Assets/_Game/Data/*.asset` (WeaponData). Initial values come from `LevelBuilder.Build()` |

## Key fact: the scene is generated

`Assets/_Game/Scenes/Mission_PowerPlant.unity`, every prefab in `Prefabs/`, every material in `Materials/`, and the NavMesh asset are **outputs** of the menu item **Hostage → Build Level** (`LevelBuilder.Build`). Hand edits to those files are overwritten on the next build. Make the change in `LevelBuilder.cs` instead. The one exception is `Data/*.asset` (WeaponData): the builder creates these only if they are missing, so tweaks to them survive a rebuild.

Build order inside `Build()`: ensure layers (TagManager) → folders → materials → WeaponData → new empty scene → build and save the Player/Guard/Hostage/Heli prefabs → lighting/fog → ground, fence, outposts, decor, plant, roof → instantiate the player and cameras → guards and routes → hostages → heli → the `GameSystems` object (GameManager, AlarmSystem with rally points) → MissionUI → PostFX → NavMeshSurface bake (saved as `Scenes/Mission_PowerPlant_NavMesh.asset`) → save the scene → add it to Build Settings at index 0.

## File map

```
Assets/_Game/
  Editor/LevelBuilder.cs      Generates everything (see above). Geometry helpers: Box, Wall(openings), SlabWithHole, Ramp, MeshProp.
  Scripts/
    Core/
      GameManager.cs          Singleton (exec order -500). Mission state, hostage list, kills, alarm/radio flags, objective text, Fail/Complete/Restart, cursor lock.
      MissionUI.cs            Singleton. Builds the whole uGUI canvas + EventSystem in Awake. Flash(), SetPrompt(), ShowHitMarker(), ShowEndScreen().
      PostFX.cs               Creates a global Volume at runtime (tonemap, bloom, color, vignette). Damage vignette is driven by the player's Health.
      Health.cs               HP, optional regen (player only), events Damaged(amount, dir) and Died.
      Hitbox.cs               On colliders. Hit() → Health.Damage × multiplier (guard head ×4, hostage head ×2).
      IInteractable.cs        GetPrompt(out usable), HoldTime, Interact(). Implemented by Hostage and Radio.
      GameInput.cs            Static wrapper over the project-wide InputSystem_Actions "Player" map.
      Layers.cs               Layer indices and shared raycast masks (PlayerShotMask, GuardShotMask, OccluderMask, InteractMask).
      NavMeshBootstrap.cs     Exec order -1000. Bakes the NavMesh at runtime only if the saved data is missing.
    Player/
      PlayerController.cs     CharacterController FPS controller: walk/sprint/crouch/jump, mouse look (yaw on body, pitch on Head), recoil, stacks the viewmodel overlay camera onto Camera.main.
      PlayerNoise.cs          Distance-based footsteps → NoiseSystem.Emit. +5 dB per following hostage.
      PlayerInteract.cs       Raycast from Head; hold E to interact; drives the HUD prompt.
    Weapons/
      WeaponData.cs           ScriptableObject: damage, rpm, mag, reserve, reload, spread, recoil, impulse, noise dB.
      Weapon.cs               Hitscan from Head, Hitbox damage, tracer/impact, muzzle light/flash, Cinemachine impulse, procedural viewmodel kick/bob/reload.
      WeaponSwitcher.cs       Keys 1/2 or scroll; toggles weapon GameObjects.
      Tracer.cs               Static Spawn(line) and Impact(marker) helpers.
    Noise/
      NoiseSystem.cs          Static Emit(pos, dB, source, fromPlayer). Falloff 20·log10(d), −15 dB per occluder hit. Limit 65 dB within 25 m forces Investigate.
      DecibelMeter.cs         HUD bar. Decays NoiseSystem.PlayerPeakDb at 40 dB/s.
    AI/
      Guard.cs                The whole state machine (see below). Static Guard.All registry.
      GuardVision.cs          Cone + range + linecast. CanSee / CanSeePlayer (head or chest).
      GuardHearing.cs         Threshold 30 dB, "loud" 55 dB. Forwards to Guard.HearNoise.
      PatrolRoute.cs          Child transforms = looping waypoints.
      AlarmSystem.cs          Singleton. Any guard with CombatTime ≥ 5 s → Raise() (irreversible). Interior guards go to rally points. PushTo() after the radio call. Also holds the VectorExt.WithY extension.
    Hostage/Hostage.cs        Tied → Following ⇄ Holding → Dead/Extracted. NavMeshAgent follow with a per-index offset; crouches with the player.
    Extraction/
      ExtractionZone.cs       Bounds check via Contains(pos) (NavMeshAgents don't fire triggers).
      Radio.cs                Usable only when every living hostage is freed and inside the zone → Helicopter.Call().
      Helicopter.cs           45 s countdown; flies in over the last 25 s; lands; completes when the player and all living hostages are in the zone.
  Data/        USP-S.asset, M4A1-S.asset   (generated once, then hand-tuned)
  Prefabs/     Player, Guard, Hostage, Heli (generated)
  Materials/   (generated, URP Lit/Unlit)
  Scenes/      Mission_PowerPlant.unity + NavMesh asset (generated)
Assets/InputSystem_Actions.inputactions   Project-wide actions. Added actions: Reload, Weapon1, Weapon2, plus a Ctrl crouch binding.
```

## Runtime wiring

Singletons: `GameManager.I`, `MissionUI.I`, `AlarmSystem.I`. Static registries: `Guard.All`, `GameManager.I.hostages` (found in Awake). `NoiseSystem` and `Tracer` are static.

```
Weapon.Fire ─► Hitbox.Hit ─► Health.Damage ─► Died ─► Guard.Die  ─► GameManager.GuardKilled
          │                                     └──► Hostage.Die ─► GameManager.HostageKilled (3 → Fail)
          └► NoiseSystem.Emit(fromPlayer) ─► GuardHearing.OnNoise ─► Guard.HearNoise
PlayerNoise / reload / landing / guard body fall ─► NoiseSystem.Emit
Guard.ShootAtPlayer ─► Hitbox (hostages: crossfire) or the player's Health ─► Player Died ─► GameManager.PlayerDied ─► Fail
AlarmSystem.Update (CombatTime ≥ 5 s) ─► Raise ─► Guard.OnAlarm(rally)
PlayerInteract ─► IInteractable (Hostage.Free / Radio.Interact ─► Helicopter.Call ─► … ─► GameManager.Complete)
MissionUI.Update polls GameManager, WeaponSwitcher.Current, NoiseSystem.PlayerPeakDb (via DecibelMeter)
```

Scene object references are set by LevelBuilder, with GameManager.Awake falling back to `Find*`. GameManager holds `player`, `extractionZone`, `helicopter`, and the tracer/impact/flash materials.

### Guard state machine (Guard.cs)
`Idle` (post; towers sweep by `scanAngle`) / `Patrol` → `Suspicious` (3 s, turns to the stimulus) → `Investigate` (walks there, looks around for 6 s) → `Combat` (chase to `preferredRange` 12 m, burst fire, tracks `CombatTime`) · `Dead`.
- **Vision**: the detection meter (0–1) fills based on distance, stance (crouch ×0.55), light (under a roof ×0.75) and sprint (×1.3), and fills twice as fast when the guard is already alert. At 0.3 → Suspicious, 0.6 → Investigate, 1 → Combat. Within 3 m it is instant.
- **Bodies**: a guard that sees a dead guard goes to Investigate, then Combat 2 s later.
- **Damage**: when hit → Combat immediately.
- **Flags**: `stationary` (towers: no agent), `interior` (moves to a rally point on alarm). Agents start disabled in the prefab; `Start` enables them and calls `Warp`.

## Layers, tags, cameras

| Layer | Index* | Used for |
|---|---|---|
| Player | 6 | CharacterController root. Tag `Player`. |
| Enemy | 7 | All guard parts |
| Hostage | 8 | All hostage parts |
| Environment | 9 | All level geometry. **The only layer the NavMesh bakes from.** Occludes vision and sound. |
| Interactable | 10 | Radio |
| Viewmodel | 11 | First-person guns. Rendered only by the overlay camera. |

\*Indices are assigned by `LevelBuilder.EnsureLayers` (the first free slot from 6). Code always uses `LayerMask.NameToLayer`, never the raw numbers.

Cameras: `Main Camera` (Base, CinemachineBrain, post-processing on, culls out Viewmodel) ← `FPS CinemachineCamera` (HardLockToTarget + RotateWithFollowTarget on `Player/Head`, ImpulseListener). `Player/Head/ViewmodelCamera` is a URP Overlay camera, added to the main camera's stack at runtime in `PlayerController.Start`.

## Level layout (LevelBuilder coordinates)

- Floor surfaces are at `FloorY = {0.1, 4.1, 8.1, 12.1}` (F1, F2, F3, Roof). Slab thickness 0.3. +z is north. The ground plane is at y = 0.
- **Building** footprint: x[-20, 20], z[-15, 15]. Outer walls are 0.3 thick and centred on those lines.
- **Interior stairs** (east side): lane A x[16.7, 19.85] holds F1→F2 and F3→Roof; lane B x[13.5, 16.5] holds F2→F3. Both run along z[-12, -4]. The divider is at x = 16.6. There are slab holes above each ramp.
- **Fire escape** (west side, outside the wall): lanes at x ≈ -21.4 and x ≈ -23.8. Landings at z[11, 14] (F2, Roof) and z[0, 3] (F3), with doors in the west wall.
- **Breach points**:
  - Front door: south wall, x[-2, 2].
  - Back service door: east wall, z[3, 5].
  - Vent: west wall F1, z[-6, -5], 1.35 m high. Crouch only, too low for agents.
- **Fence**: x[-60, 60], z[-55, 55]. Gaps: south gate x[-5, 5], east z[0, 4], west z[-10, -6]. Player spawn is at (0, 0, -80).
- **Outposts**:
  - Tower A (-40, 25) and Tower B (30, -45): guards at y = 6, vision range 40.
  - Sandbag post (-28, -35).
  - Gate booth (9, -50).
- **Roof**: helipad and ExtractionZone at (0, 12.1, 3), 14×14. The radio sits at the zone's NE corner.
- **Placements**: all guard/route/hostage positions are in `BuildGuards()` and the `H(...)` calls in `Build()`. There are 7 exterior guards, 10 interior guards and 5 hostages.

## Common changes (recipes)

- **Add a guard**: add a `G("Name", pos, yaw, Route(...)?, interior:, stationary:, scan:, range:)` line in `BuildGuards`. Rebuild.
- **Add level geometry**: use `Box`/`Wall`/`Ramp` so the piece gets the Environment layer and the NavMesh includes it. Ramps must stay ≤ 45° (agent slope limit) and keep the low end sunk into the floor (the `Ramp` helper does this).
- **New interactable**: implement `IInteractable` on a component. Put a collider on a layer in `Layers.InteractMask` (Default, Environment, Hostage or Interactable).
- **New noise source**: call `NoiseSystem.Emit(pos, dB, gameObject, fromPlayer)`. Pass `fromPlayer: true` only if it should show on the HUD meter and count toward the 65 dB limit.
- **New HUD element**: create it in `MissionUI.Awake` with `MakeText`/`MakeImage` and update it in `MissionUI.Update`.

## Verifying changes without the editor UI

The editor usually has the project open, which blocks `-batchmode` on this folder.
1. **Compile check**: copy the `Library/Bee/artifacts/*/Assembly-CSharp(.Editor).rsp`, swap in the current `.cs` list, and run `Unity.app/Contents/Resources/Scripting/NetCoreRuntime/dotnet …/DotNetSdkRoslyn/csc.dll @file.rsp`. Alternatively, read `~/Library/Logs/Unity/Editor.log` for `error CS` after the editor recompiles.
2. **Build + play smoke test**: APFS-clone the project to a scratch dir (`cp -cR Assets Packages ProjectSettings Library`), add a throwaway editor script there, and run `Unity -batchmode -projectPath <copy> -executeMethod <Class.Method>`. That script can call `LevelBuilder.Build()`, check `NavMesh.CalculatePath` between key points, enter play mode (`[InitializeOnLoad]` + `SessionState` flag + `EditorApplication.update`), and render PNGs with `Camera.Render()` (omit `-nographics` for screenshots). Never add these test scripts to the real project.

## Gotchas

- NavMeshAgents don't trigger `OnTriggerEnter`. Use `ExtractionZone.Contains` and bounds checks instead.
- Guard/Hostage `NavMeshAgent`s are **disabled in the prefab**. Enable them, then `Warp`, only once the NavMesh exists.
- Primitive cylinders and spheres in the environment use `MeshProp`, which swaps in a MeshCollider. Scaled sphere/capsule colliders become huge invisible blobs.
- The guard `?`/`!` TextMesh uses the built-in GUI text shader, so it draws through walls. That is intended as stealth feedback.
- `WeaponData` assets aren't overwritten on rebuild. Delete one to regenerate its defaults.
- The NavMesh bakes **only** the Environment layer, inside a volume at center (0, 9, 0), size (140, 26, 130). Geometry outside that volume has no NavMesh.
