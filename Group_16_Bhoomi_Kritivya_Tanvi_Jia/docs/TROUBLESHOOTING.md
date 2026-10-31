# Troubleshooting

## Unity editor
| Symptom | Cause / fix |
|---|---|
| Edits made outside Unity (another editor/script) aren't compiled | Unity only recompiles when it regains focus or on **Assets ▸ Refresh (Cmd+R)**. Pressing Play also forces a compile (it can take 30–60 s on first run). Don't edit scripts while in Play mode — the domain reload is deferred until you stop. |
| `No MissionDefinition assigned` in the console | The scene was built before the mission asset existed. Run **Rescue Sim ▸ Build ▸ Everything**. |
| Several `Materials N` / `Prefabs N` folders appear under `Assets/_Project` | Fixed in the builder (`BuilderUtil.EnsureDir`). Delete any empty numbered duplicates. |
| Pink materials | Pack materials must be URP Lit. They were upgraded in place; if you re-import the pack, run **Window ▸ Rendering ▸ Render Pipeline Converter** (Built-in → URP, Material Upgrade). |
| `BoxCollider does not support negative scale` warnings (16) | Pre-existing content inside the Tirgames pack scene objects; harmless. |
| `Account API did not become accessible within 30 seconds` | Unity Hub/Cloud sign-in network warning; unrelated to the project. |
| Large binary files show as text pointers or are missing | Install Git LFS (`git lfs install && git lfs pull`). |

## OpenXR / Meta XR Simulator (macOS, Apple Silicon)
- Enable the runtime: in the Meta XR Simulator app switch on **XR Simulator runtime enabled**. Then press Play in Unity — the simulator opens automatically and shows "Meta Quest 3".
- Verify XR is alive: the console prints `[XRProbe] HMD present=True tracked=True … left=True right=True` every 10 s and `[XRProbe] RightHand triggerPressed PRESSED/released` on input (component `XRInputProbe` on the rig; can be disabled).
- **Keyboard does nothing**: click once inside the headset view of the simulator (an empty area, *not* on a controller model — clicking a controller selects it for manual posing and only that controller will then respond).
- Unity "No frames received from the OpenXR application": the Game view must be open and visible, and Unity must not be paused or mid-compile. Stop Play, let compilation finish, press Play again.
- Both controllers move together because **Use same input for both sides** is on in the simulator; the *right* controller ray is the visible one. The left controller points up/left by default and is used for the wrist HUD.
- UI buttons need the ray on the *button*, not the panel text. `[XRProbe] Right Controller/Near-Far Interactor UI hit: Button_Begin Mission` appears when aimed correctly. Step back with **S** or pitch with the arrow keys to adjust.
- When the simulator is focused Unity's frames can appear to stall in screenshots; switching back to Unity resumes normal updates.
- Running PlayMode tests also starts the simulator (OpenXR loader is active in the editor); results are unaffected.

## Quest / Android
1. Install Unity **Android Build Support** with **OpenJDK** and **Android SDK & NDK Tools** via Unity Hub (modules on the 6000.3.21f1 editor).
2. **Rescue Sim ▸ Configure XR and Android** (idempotent): OpenXR loader + Meta Quest feature group on Android, IL2CPP, ARM64, min SDK 29, Vulkan.
3. Build Profiles ▸ **Meta Quest** ▸ Switch Platform (first switch re-imports textures as ASTC; allow tens of minutes). Return to **macOS** to use the simulator again.
4. **Rescue Sim ▸ Build ▸ Android APK (Quest)** → `Builds/Android/FactoryRescueVR.apk` and `Builds/android-build-report.txt`.
5. Install with `adb install -r Builds/Android/FactoryRescueVR.apk` (Quest in developer mode). **This has not been done in this project** — no physical headset testing has occurred.
6. Project Validation (Edit ▸ Project Settings ▸ XR Plug-in Management ▸ Project Validation ▸ Quest tab) lists remaining optional items. The SSAO note refers to the desktop *PC* quality tier; the Android default quality (Mobile) has no SSAO.

## Gameplay
- Fire won't go out: aim the spray cone at the *base* of the flames and keep the trigger down; it regrows slowly when you stop. For the electrical fire, cut the breaker first.
- Passage not clearing: all debris blocks must leave the yellow gizmo volume in the partition wall.
- Survivor won't follow: hold grip on them until the ring fills; keep them out of smoke.
