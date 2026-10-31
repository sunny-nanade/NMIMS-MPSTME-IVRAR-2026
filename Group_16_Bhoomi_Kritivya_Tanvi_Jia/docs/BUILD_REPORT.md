# Verified build report (rebuilt after the findings-reveal fix)

Built 2026-10-26 00:06 IST (18:37 UTC on 2026-10-25) on the author's Mac from the project working tree at commit `5f7ac5e81815caeb4859e96f8099f79aeeee5cad` (contains code commit `f05441073ed9c3fc794deb61c0e17f1da2e487dc`, the fix). The working tree also held uncommitted, Unity-regenerated serialization changes (fileID reordering in `OpenXR Package Settings.asset`, URP settings, `UnityConnectSettings.asset`, texture `.meta` files, and an untracked `ProjectSettings/BuildProfileUtilityOpenXR.asset`). They were reviewed as noise and deliberately **not committed**; they were present during the build.

| Item | Value |
|---|---|
| Path (author's Mac) | `/Users/jiajadhav/VR-Disaster-Simulator/Builds/Android/DisasterResponseVR.apk` (ignored by Git, never committed) |
| Size | **242,635,956 bytes** (231.4 MiB) |
| SHA-256 | `a533c6d08427da2d5268c421d9d6cb5ea64f807248abdce518c7476798ecd6f4` |
| Unity | 6000.3.21f1 |
| Target | Android, IL2CPP, **arm64-v8a only** (`lib/arm64-v8a/libil2cpp.so`), debug-signed |
| Scenes (5) | MainMenu, FactoryRescue, EarthquakeRescue, FloodEvacuation, TriageSimulation (APK holds `level0`-`level4`) |
| Manifest | contains `com.oculus.supportedDevices`, `android.hardware.vr.headtracking`, `org.khronos.openxr`, `com.oculus.vr.focusaware` |
| Build result | Succeeded in 1 min 08 s (incremental). 354 warnings. 1 error line: `NullReferenceException` inside `UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature.CreateValidationRules`, a validation-rule callback in the OpenXR package; the build still reports Succeeded and the APK was produced. Not proven harmless on a device |
| Fix included? | Yes: the APK metadata contains the neutral prompt "Findings appear only after you check them." and the protocol line "Training exercise, not clinically validated." The scene description text no longer shows casualty findings (PlayMode test covers this) |
| Previous APK (superseded) | 242,633,136 bytes, SHA-256 `eaa125ab…94107c8c`, built before the fix. Do not publish it |

## Tests on the same working tree (macOS editor target, 2026-10-26)
| Suite | Result |
|---|---|
| EditMode | 109 passed, 0 failed, 0 skipped |
| PlayMode | 31 passed, 0 failed, 0 skipped |
Source: `Builds/TestSummary.txt` written by `Rescue Sim > Tests`.

## Device status
**Not installed or run on any physical Quest.** No headset or `adb` was available to the author of this report. Frame rate, comfort, controller ergonomics and panel placement are unverified. `MANUAL_TEST_CHECKLIST.md` lists the headset checks, all pending.


# Upgrade build (humans + hand tracking), 2026-10-29
Built on the author's Mac from the uncommitted working tree on top of `5f7ac5e` (carry feature commit `2d79fe1` is local, not pushed).

| Item | Value |
|---|---|
| Path | `/Users/jiajadhav/VR-Disaster-Simulator/Builds/Android/DisasterResponseVR_HumansHands.apk` (ignored by Git) |
| Size | **249,530,021 bytes** |
| SHA-256 | `819fcc430dcdff846acafd00ee5d9b49e0e312ab5df97213533b7afa2c103ca5` |
| Build result | Succeeded in 8 min 55 s (full build after platform switch), 359 warnings, 1 error line: same `NullReferenceException` in the OpenXR Meta Quest validation callback as the previous build |
| Manifest / libs | `oculus.software.handtracking`, `com.oculus.permission.HAND_TRACKING`, `libUnityOpenXRHands.so`, ARM64 only |
| Tests (editor) | EditMode 121/121, PlayMode 57/57 |
| Device status | **Not installed or run on a physical Quest.** |
| Previous APK | `DisasterResponseVR.apk` 242,635,956 bytes, SHA `a533c6d0...`, untouched |
