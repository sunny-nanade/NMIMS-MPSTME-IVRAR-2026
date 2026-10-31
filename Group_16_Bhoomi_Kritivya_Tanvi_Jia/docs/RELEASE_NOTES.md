# Disaster Response VR — group-16-v1.0 (Quest APK) — DRAFT, NOT PUBLISHED (awaiting approval)

**Asset:** `DisasterResponseVR.apk`, 242,635,956 bytes (231.4 MiB), SHA-256 `a533c6d08427da2d5268c421d9d6cb5ea64f807248abdce518c7476798ecd6f4`. Debug-signed, IL2CPP ARM64, Vulkan, min SDK 29, Meta Quest OpenXR feature enabled, 5 scenes. Built 2026-10-26 with Unity 6000.3.21f1 (1 min incremental build). The APK is not committed to Git; it is attached to the release only.

**APK status:** rebuilt on 2026-10-26 from the working tree at commit `5f7ac5e` (includes fix `f054410`); see `BUILD_REPORT.md`. Replaces the earlier APK `eaa125ab…`, which predated the fix.

**What it is:** a three-stage VR disaster-rescue game (Factory, Earthquake, Flood) with main menu, progression, scoring and stars, **plus** a stand-alone START mass-casualty triage exercise (**Triage Study** button on the main menu): ten casualties in a burning factory yard, six START checks per casualty, Green/Yellow/Red/Black tags, results panel, CSV telemetry (records, summary, confusion matrix). START is implemented from published descriptions and is a training aid, **not clinically validated**.

**Install (Quest 3/3S, developer mode):** `adb install -r DisasterResponseVR.apk`; launch from Unknown Sources.

**Verification (macOS editor, 2026-10-25):** EditMode 109/109 and PlayMode 31/31 pass (57 + 23 campaign tests unchanged, plus 52 EditMode cases and 8 PlayMode tests for the triage exercise). Playthroughs are driven by XR Interaction Toolkit events and the real UI buttons, not by a headset.

**APK check:** Android build reported Succeeded (1 error line from the OpenXR Meta Quest validation callback, 354 warnings). The package contains the 5 scenes (`level0`-`level4`) and the Quest manifest entries (`com.oculus.supportedDevices`, `android.hardware.vr.headtracking`).

**Not verified:** the APK has **never been installed or run on a physical headset** (no device, no `adb` here), so comfort, frame rate, controller interaction and panel placement are untested. No human-subject data exists. The one build error is a `NullReferenceException` inside the Unity OpenXR package's Meta Quest validation rules (`MetaQuestFeature.CreateValidationRules`); the build still completes and the cause is unresolved.

**Publish only after the team agrees** (exact commands are in the final report; no force-push):

```bash
git push -u bhoomi feature/factory-rescue-vr
gh release create group-16-v1.0 Builds/Android/DisasterResponseVR.apk \
  --repo bhoomibhandari88/VR-Disaster-Simulator --target feature/factory-rescue-vr \
  --title "Disaster Response VR v1.0 (Quest APK)" --notes-file Docs/RELEASE_NOTES.md
```

Afterwards confirm the uploaded asset's SHA-256 matches the value above.


## Upgrade build (not yet released): human casualties, hand tracking, reliable carry
`DisasterResponseVR_HumansHands.apk`, 249,530,021 bytes, SHA-256 `819fcc430dcdff846acafd00ee5d9b49e0e312ab5df97213533b7afa2c103ca5`, built 2026-10-29. The APK listed above (`a533c6d0...`) is the previous build.
- Casualties are Microsoft Rocketbox human avatars (MIT licence), varied by person; casualty IDs and gameplay unchanged.
- Controller-free play with Quest hand tracking (pinch, carry latch, safe hand-over to controllers).
- Fix: casualties can now actually be hovered and grabbed (their trigger grab volume was invisible to XRI's interactors) - applies to controllers too.
- EditMode 121/121 and PlayMode 57/57 in the editor. **Never run on a physical headset.** See `Docs/HUMANS_AND_HANDS_UPGRADE.md`.
