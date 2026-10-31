# Change list (branch `feature/factory-rescue-vr`, base `7edc4cb`)

Nothing has been pushed. No existing asset was deleted or replaced. All work is local commits.

## Created
- **Gameplay runtime** `Assets/_Project/Scripts/Runtime/` (assembly `RescueSim.Runtime`): Core (MissionController, MissionDefinition, ObjectiveTracker, ScoreCalculator), Hazards (Fire, GasLeak, Electrical, ExposureSource), Interaction (MainBreaker, GasValve, Extinguisher, DebrisItem, PassageMonitor, HoverHighlight), Survivors (Survivor, ExitZone), Player (PlayerVitals, GameSettings, Comfort*, ExposureFeedback, XRInputProbe), UI (world-space menus, wrist HUD, toast, markers, scene transitions), Audio (procedural cues + spatial loops).
- **Editor tooling** `Assets/_Project/Scripts/Editor/`: SceneBuilder (menu *Rescue Sim ▸ Build ▸ Everything*), Prefabs, BuilderUtil, XRProjectSetup (*Rescue Sim ▸ Configure XR and Android*), SceneReport, AndroidBuild (*Rescue Sim ▸ Build ▸ Android APK (Quest)*).
- **Data/scenes/prefabs/materials/shaders** under `Assets/_Project/` (Mission asset, `MainMenu` and `FactoryRescue` scenes with baked NavMesh, Fire/GasLeak/Survivor/DebrisBlock prefabs, URP particle + comfort-vignette shaders).
- **Tests** `Assets/_Project/Tests/EditMode` (39) and `PlayMode` (13).
- **XR configuration**: `Assets/XR/*`, `Assets/XRI/*`, XRI Starter Assets sample (imported text assets/prefabs), TextMesh Pro essentials, `Packages/manifest.json` additions (OpenXR 1.18, Unity OpenXR: Meta 2.6.1, XRI 3.6.1, Input System, AI Navigation).
- **Docs**: `README.md`, `Docs/TROUBLESHOOTING.md`, `Docs/CHANGES.md`, `Docs/AUDIT.md`.

## Modified
- `Assets/TirgamesAssets/Factory/Models/Materials/*.mat`: shader converted Built-in Standard → URP Lit (required to render in URP). Textures/meshes untouched.
- `Assets/Settings/Mobile_RPAsset.asset`: 4× MSAA on, HDR off, per-object additional lights 3 (Quest tier).
- `ProjectSettings/*`: Android/Quest + OpenXR settings (IL2CPP, ARM64, min SDK 29, Vulkan), build scene list, input handling, Project Validation fix (prioritize input polling).
- `FactoryRescue` scene is a *copy* of the pack's `FactoryNight` scene with post-processing/missing scripts stripped; the original pack scenes are untouched.

## Removed
- Nothing from the original project. (Empty duplicate folders `Materials N` / `Prefabs N` produced by an earlier builder bug are removed — see git log.)

## Not committed on purpose
- LFS-type binaries from the XRI sample (png/fbx/wav) and TextMesh Pro `EmojiOne.png`: the authoring VM has no git-lfs. Add them from a machine with Git LFS (`git add` then commit) before pushing.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/` (git-ignored).

## Milestone 2: three-stage campaign
- Added: `EarthquakeRescue` and `FloodEvacuation` scenes + mission assets, main-menu campaign (stage cards, locks, progress, settings, controls, demo unlock-all), shared briefing/HUD/results UI, `CampaignProgress`/`StageCatalog`/`StageScoring`, `WaterSurface`, `StarRating`, zone/pickup objectives, `RunTests` editor menu, new EditMode (57 total) and PlayMode (23 total) tests.
- Changed: `SceneBuilder` split into partials; `VRPanel` placement; `ScoreCalculator` hazard-free scoring; `AndroidBuild` output now `DisasterResponseVR.apk`.
- Nothing deleted. Nothing pushed.
