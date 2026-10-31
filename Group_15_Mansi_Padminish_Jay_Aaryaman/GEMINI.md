# Unity VR Project Context

- Engine: Unity 6 LTS
- Render Pipeline: Universal Render Pipeline (URP). NEVER use Standard Shaders or Shader.Find(). Always use URP-compatible materials (Universal Render Pipeline/Lit).
- SDK: Meta XR SDK (OVRCameraRig / OpenXR).
- Hardware: Simulating locally via Meta XR Simulator. Do not require physical headset connections for testing.
- Code Style: C# modern conventions, event-driven decoupling, clean separation of concerns.