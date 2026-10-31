using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSER;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CSER.EditorTools
{
    /// Builds the whole VR escape room from code: CSER > Build Escape Room Scene.
    /// Re-running it regenerates the scene from scratch, so every change is reproducible.
    public static partial class EscapeRoomBuilder
    {
        const string Root = "Assets/_Project";
        const string MatDir = Root + "/Materials";

        // room extents (metres)
        const float HalfX = 2.5f, HalfZ = 2.25f, Height = 2.8f;
        const float DoorZ0 = -1.45f, DoorZ1 = -0.55f, DoorH = 2.1f;
        const float WinZ0 = 0.0f, WinZ1 = 1.6f, WinY0 = 0.95f, WinY1 = 2.35f;

        static Transform env, props, ui;
        static readonly List<string> log = new List<string>();

        /// Chapter 2: Fred's office (the escape room).
        public static void BuildOffice(bool bake)
        {
            var scene = NewScene();
            Lighting();
            Room();
            var rig = Rig(new Vector3(0, 0, -0.95f), 0);
            var head = FindDeep(rig.transform, "CenterEyeAnchor");
            var game = Managers(2);
            game.AddComponent<RoomPuzzles>();
            Furniture(out float deskTop);
            FredDrawers();
            Puzzles(game, deskTop);
            Interfaces(game, head, deskTop);
            var kn = AddKnock(GameObject.Find("DoorHinge").GetComponent<Hinge>(), DoorKnock.Mode.Keypad, new Vector3(HalfX, 1.0f, (DoorZ0 + DoorZ1) / 2),
                "PRESS  P  AT THE DOOR TO ENTER THE OVERRIDE CODE", new Vector3(HalfX - 0.02f, DoorH + 0.42f, (DoorZ0 + DoorZ1) / 2), 90);
            kn.keypad = Object.FindAnyObjectByType<DoorKeypad>();
            CommonUI(game, head, new Vector3(0, 1.5f, 0.15f), 0);
            Teleports();
            var d = game.GetComponent<ChapterDirector>();
            d.steps = new[]
            {
                Step(TaskId.LockWorkstation, "Fred's monitor", null, "FredMonitor"),
                Step(TaskId.EvidencePrintout, "Printout", "EvidenceBox", "Printout"),
                Step(TaskId.EvidenceNote, "Sticky note", "EvidenceBox", "StickyNote"),
                Step(TaskId.ShredDocs, "Confidential", "Shredder", "Doc_Salaries", "Doc_Clients"),
                Step(TaskId.QuarantineUSB, "Briefcase", "Quarantine", "USB_Stick"),
                Step(TaskId.FredLogin, "Sign in here", null, "FredMonitor"),
                Step(TaskId.NewPassword, "New password", null, "FredMonitor"),
                Step(TaskId.Enable2FA, "Hardware token", "FredMonitor", "HardwareToken"),
                Step(TaskId.PhishingTriage, "SecureMail", null, "FredMonitor"),
                Step(TaskId.SocialEngineering, "TeamChat", null, "FredMonitor"),
                Step(TaskId.EncryptData, "SOC Console", null, "FredMonitor"),
                Step(TaskId.Escape, "Enter the code", null, "DoorKeypad"),
            };
            d.steps[7].dropLabel = "Type the code here";
            FinishScene(scene, "Ch2_FredsOffice", head, bake);
        }

        static LightingSettings LightingAsset()
        {
            var settingsPath = Root + "/Scenes/Story_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
            if (!ls)
            {
                ls = new LightingSettings();
                AssetDatabase.CreateAsset(ls, settingsPath);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 20;
            ls.lightmapMaxSize = 2048;
            ls.ao = true;
            ls.aoMaxDistance = 0.5f;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.maxBounces = 3;
            ls.indirectScale = 1.8f;   // brighter bounced light: rooms read as lit, not murky
            ls.albedoBoost = 1.6f;
            EditorUtility.SetDirty(ls);
            return ls;
        }

        /// Synchronous bake of the open scene (lightmaps, light probes, reflection probe), then save.
        static void BakeNow()
        {
            Lightmapping.lightingSettings = LightingAsset();
            Lightmapping.Bake();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[CSER] Lighting baked: " + EditorSceneManager.GetActiveScene().name);
        }

        [MenuItem("CSER/Bake Lighting (open scene)", priority = 20)]
        public static void Bake() => BakeNow();

        static void MarkStatic(Transform root)
        {
            const StaticEditorFlags flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponentInParent<Canvas>(true) || t.GetComponentInParent<Hinge>(true) || t.GetComponentInParent<SlideDrawer>(true)) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                // imported meshes without clean lightmap UVs are lit from light probes instead of lightmaps
                if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh && AssetDatabase.GetAssetPath(mf.sharedMesh).Contains("/Models/")
                    && t.TryGetComponent<MeshRenderer>(out var mr))
                    mr.receiveGI = ReceiveGI.LightProbes;
            }
        }

        static void Probes()
        {
            var lp = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
            lp.transform.SetParent(env);
            var pts = new List<Vector3>();
            for (float x = -HalfX + 0.3f; x <= HalfX - 0.3f; x += 0.8f)
            for (float z = -HalfZ + 0.3f; z <= HalfZ - 0.3f; z += 0.8f)
            foreach (var y in new[] { 0.3f, 1.0f, 1.7f, 2.5f })
                pts.Add(new Vector3(x, y, z));
            lp.probePositions = pts.ToArray();

            var rp = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            rp.transform.SetParent(env);
            rp.transform.position = new Vector3(0, 1.4f, 0);
            rp.mode = ReflectionProbeMode.Baked;
            rp.boxProjection = true;
            rp.size = new Vector3(HalfX * 2, Height, HalfZ * 2);
            rp.resolution = 256;
            rp.intensity = 1f;
        }

        static void PostProcessing(Transform head)
        {
            var path = Root + "/Settings/EscapeRoom_Post.asset";
            Directory.CreateDirectory(Root + "/Settings");
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            bool fresh = !profile;
            if (fresh)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            if (fresh)
            {
            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>();
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.6f);
            var ca = Add<ColorAdjustments>();
            ca.postExposure.Override(0.35f);
            ca.contrast.Override(14f);
            ca.saturation.Override(6f);
            var wb = Add<WhiteBalance>();
            wb.temperature.Override(6f);
            var vg = Add<Vignette>();
            vg.intensity.Override(0.16f);
            vg.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            }
            // tuned every build (the profile asset is reused): a higher bloom threshold keeps white UI cards crisp
            if (profile.TryGet(out Bloom bl)) { bl.threshold.Override(1.6f); bl.intensity.Override(0.3f); }
            if (profile.TryGet(out ColorAdjustments cadj)) cadj.postExposure.Override(0.55f);
            if (profile.TryGet(out Vignette vg2)) vg2.intensity.Override(0.12f);
            EditorUtility.SetDirty(profile);
            var vol = new GameObject("PostProcessing").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            foreach (var cam in head.root.GetComponentsInChildren<Camera>(true))
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
            }
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.supportsHDR = true;
                urp.msaaSampleCount = 4;
                urp.shadowDistance = 8f;
                EditorUtility.SetDirty(urp);
            }
        }

        // ------------------------------------------------------------------ lighting & room shell

        static void Lighting()
        {
            // dim neutral environment; the baked ceiling panels do the real work
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.74f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.6f, 0.57f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.29f, 0.26f);
            RenderSettings.fog = false;
            var hdr = AssetDatabase.LoadAssetAtPath<Texture>(Root + "/Textures/HDRI/modern_buildings_night_2k.hdr");
            if (hdr)
            {
                var sky = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/NightCitySky.mat");
                if (!sky)
                {
                    sky = new Material(Shader.Find("Skybox/Panoramic"));
                    AssetDatabase.CreateAsset(sky, MatDir + "/NightCitySky.mat");
                }
                sky.SetTexture("_MainTex", hdr);
                sky.SetFloat("_Exposure", 0.9f);
                sky.SetFloat("_Rotation", 200f);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            // recessed LED ceiling panels: emissive tile + baked rectangle area light each
            var panelMat = Mat("CeilingPanel", Color.white, 0.2f, 0, new Color(2.2f, 2.15f, 2.0f));
            foreach (var x in new[] { -1.2f, 1.2f })
            foreach (var z in new[] { -1.1f, 0.9f })
            {
                Box("CeilingPanel", env, new Vector3(x, Height - 0.004f, z), new Vector3(0.6f, 0.01f, 0.6f), panelMat, false);
                var l = new GameObject("PanelLight").AddComponent<Light>();
                l.transform.SetParent(env);
                l.type = LightType.Rectangle;
                l.lightmapBakeType = LightmapBakeType.Baked;
                l.areaSize = new Vector2(0.6f, 0.6f);
                l.color = new Color(1f, 0.95f, 0.88f);
                l.intensity = 22f;
                l.range = 10;
                l.transform.SetPositionAndRotation(new Vector3(x, Height - 0.02f, z), Quaternion.Euler(90, 0, 0));
            }
            // one mixed light for realtime shadows of objects in your hands
            var key = new GameObject("Key Light (mixed)").AddComponent<Light>();
            key.transform.SetParent(env);
            key.type = LightType.Spot;
            key.lightmapBakeType = LightmapBakeType.Mixed;
            key.spotAngle = 120;
            key.innerSpotAngle = 60;
            key.range = 6;
            key.intensity = 4.5f;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.6f;
            key.transform.SetPositionAndRotation(new Vector3(0.1f, Height - 0.1f, 0.2f), Quaternion.Euler(90, 0, 0));
            // cool spill from the city through the window
            var spill = new GameObject("Window Spill").AddComponent<Light>();
            spill.transform.SetParent(env);
            spill.type = LightType.Rectangle;
            spill.lightmapBakeType = LightmapBakeType.Baked;
            spill.areaSize = new Vector2(1.6f, 1.3f);
            spill.color = new Color(0.55f, 0.65f, 1f);
            spill.intensity = 1.2f;
            spill.transform.SetPositionAndRotation(new Vector3(-HalfX + 0.02f, 1.65f, (WinZ0 + WinZ1) / 2), Quaternion.Euler(0, 90, 0));
        }

        static void Room()
        {
            var wall = AcgMat("Wall", "PaintedPlaster017", Palette.Hex("F0E4CC"), new Vector2(2.2f, 1.3f), 0.15f, bump: 0.3f);
            var carpet = AcgMat("Carpet", "Carpet012", Palette.Hex("9DB0E8"), new Vector2(5, 4.5f), 0.05f, bump: 0.6f);
            var ceiling = AcgMat("Ceiling", "OfficeCeiling001", Color.white, new Vector2(2.5f, 2.25f), 0.1f);
            var skirting = Mat("Skirting", new Color(0.18f, 0.18f, 0.19f), 0.35f);
            var frame = AcgMat("Aluminium", "Metal009", new Color(0.85f, 0.86f, 0.88f), Vector2.one, 0.7f, 0.9f);
            var glow = Mat("CorridorGlow", Color.white, 0.2f, 0, new Color(1.6f, 1.6f, 1.5f));

            Box("Floor", env, new Vector3(0, -0.05f, 0), new Vector3(HalfX * 2, 0.1f, HalfZ * 2), carpet);
            Box("Ceiling", env, new Vector3(0, Height + 0.05f, 0), new Vector3(HalfX * 2, 0.1f, HalfZ * 2), ceiling);
            Box("Wall_Back", env, new Vector3(0, Height / 2, HalfZ + 0.05f), new Vector3(HalfX * 2 + 0.2f, Height, 0.1f), wall);
            Box("Wall_Front", env, new Vector3(0, Height / 2, -HalfZ - 0.05f), new Vector3(HalfX * 2 + 0.2f, Height, 0.1f), wall);
            // left wall with a window onto the city
            float lx = -HalfX - 0.05f;
            Box("Wall_Left_A", env, new Vector3(lx, Height / 2, (WinZ0 - HalfZ) / 2), new Vector3(0.1f, Height, WinZ0 + HalfZ), wall);
            Box("Wall_Left_B", env, new Vector3(lx, Height / 2, (WinZ1 + HalfZ) / 2), new Vector3(0.1f, Height, HalfZ - WinZ1), wall);
            Box("Wall_Left_Sill", env, new Vector3(lx, WinY0 / 2, (WinZ0 + WinZ1) / 2), new Vector3(0.1f, WinY0, WinZ1 - WinZ0), wall);
            Box("Wall_Left_Top", env, new Vector3(lx, (WinY1 + Height) / 2, (WinZ0 + WinZ1) / 2), new Vector3(0.1f, Height - WinY1, WinZ1 - WinZ0), wall);
            float wz = (WinZ0 + WinZ1) / 2, ww = WinZ1 - WinZ0, wh = WinY1 - WinY0;
            Box("Window_Bottom", env, new Vector3(-HalfX + 0.02f, WinY0 + 0.02f, wz), new Vector3(0.16f, 0.04f, ww), frame);
            Box("Window_Top", env, new Vector3(-HalfX + 0.02f, WinY1 - 0.02f, wz), new Vector3(0.12f, 0.04f, ww), frame);
            Box("Window_L", env, new Vector3(-HalfX + 0.02f, (WinY0 + WinY1) / 2, WinZ0 + 0.02f), new Vector3(0.12f, wh, 0.04f), frame);
            Box("Window_R", env, new Vector3(-HalfX + 0.02f, (WinY0 + WinY1) / 2, WinZ1 - 0.02f), new Vector3(0.12f, wh, 0.04f), frame);
            Box("Window_Mullion", env, new Vector3(-HalfX + 0.02f, (WinY0 + WinY1) / 2, wz), new Vector3(0.1f, wh, 0.03f), frame);
            var glass = Mat("Glass", new Color(0.6f, 0.7f, 0.8f, 0.08f), 0.95f);
            MakeTransparent(glass);
            Box("Window_Glass", env, new Vector3(-HalfX - 0.01f, (WinY0 + WinY1) / 2, wz), new Vector3(0.01f, wh, ww), glass, false);
            // right wall with the exit doorway
            float rx = HalfX + 0.05f;
            Box("Wall_Right_A", env, new Vector3(rx, Height / 2, (DoorZ1 + HalfZ) / 2), new Vector3(0.1f, Height, HalfZ - DoorZ1), wall);
            Box("Wall_Right_B", env, new Vector3(rx, Height / 2, (DoorZ0 - HalfZ) / 2), new Vector3(0.1f, Height, DoorZ0 + HalfZ), wall);
            Box("Wall_Right_Lintel", env, new Vector3(rx, (DoorH + Height) / 2, (DoorZ0 + DoorZ1) / 2), new Vector3(0.1f, Height - DoorH, DoorZ1 - DoorZ0), wall);
            var doorFrame = Mat("DoorFrame", new Color(0.88f, 0.87f, 0.85f), 0.45f);
            Box("DoorFrame_Top", env, new Vector3(HalfX - 0.01f, DoorH + 0.03f, (DoorZ0 + DoorZ1) / 2), new Vector3(0.14f, 0.06f, DoorZ1 - DoorZ0 + 0.12f), doorFrame);
            Box("DoorFrame_L", env, new Vector3(HalfX - 0.01f, DoorH / 2, DoorZ0 - 0.03f), new Vector3(0.14f, DoorH, 0.06f), doorFrame);
            Box("DoorFrame_R", env, new Vector3(HalfX - 0.01f, DoorH / 2, DoorZ1 + 0.03f), new Vector3(0.14f, DoorH, 0.06f), doorFrame);
            // bright corridor seen when the door opens
            Box("Corridor_Floor", env, new Vector3(HalfX + 1.1f, -0.05f, (DoorZ0 + DoorZ1) / 2), new Vector3(2.1f, 0.1f, 1.6f), glow);
            Box("Corridor_End", env, new Vector3(HalfX + 2.2f, 1.4f, (DoorZ0 + DoorZ1) / 2), new Vector3(0.1f, 2.8f, 1.6f), glow);
            Box("Corridor_SideA", env, new Vector3(HalfX + 1.1f, 1.4f, DoorZ0 - 0.3f), new Vector3(2.1f, 2.8f, 0.1f), glow);
            Box("Corridor_SideB", env, new Vector3(HalfX + 1.1f, 1.4f, DoorZ1 + 0.3f), new Vector3(2.1f, 2.8f, 0.1f), glow);
            Box("Corridor_Top", env, new Vector3(HalfX + 1.1f, 2.85f, (DoorZ0 + DoorZ1) / 2), new Vector3(2.1f, 0.1f, 1.6f), glow);

            Box("Skirting_Back", env, new Vector3(0, 0.05f, HalfZ - 0.008f), new Vector3(HalfX * 2, 0.1f, 0.016f), skirting, false);
            Box("Skirting_Front", env, new Vector3(0, 0.05f, -HalfZ + 0.008f), new Vector3(HalfX * 2, 0.1f, 0.016f), skirting, false);
            Box("Skirting_Left", env, new Vector3(-HalfX + 0.008f, 0.05f, 0), new Vector3(0.016f, 0.1f, HalfZ * 2), skirting, false);
            Box("Skirting_RightA", env, new Vector3(HalfX - 0.008f, 0.05f, (DoorZ1 + HalfZ) / 2), new Vector3(0.016f, 0.1f, HalfZ - DoorZ1), skirting, false);
            Box("Skirting_RightB", env, new Vector3(HalfX - 0.008f, 0.05f, (DoorZ0 - HalfZ) / 2), new Vector3(0.016f, 0.1f, DoorZ0 + HalfZ), skirting, false);
            Box("Cornice_Back", env, new Vector3(0, Height - 0.03f, HalfZ - 0.015f), new Vector3(HalfX * 2, 0.06f, 0.03f), doorFrame, false);
            Box("Cornice_Front", env, new Vector3(0, Height - 0.03f, -HalfZ + 0.015f), new Vector3(HalfX * 2, 0.06f, 0.03f), doorFrame, false);
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // ------------------------------------------------------------------ XR rig (Meta Interaction SDK quick action)

        static GameObject Rig(Vector3 pos, float yaw)
        {
            var created = OVRQuickActionsAPI.AddOVRInteractionRig(true);
            log.Add("Interaction rig objects: " + string.Join(", ", created.Select(c => c.name)));
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            rig.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var sp = rig.gameObject.AddComponent<StartPose>();
            sp.position = pos;
            sp.yaw = yaw;
            rig.gameObject.AddComponent<PlayerBlocker>();
            var mgr = Object.FindAnyObjectByType<OVRManager>();
            if (mgr) mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            foreach (var cam in rig.GetComponentsInChildren<Camera>())
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                // a tight near/far range keeps depth-buffer precision high: no flickering (z-fighting) surfaces
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 60f;
                cam.useOcclusionCulling = false;
            }
            return rig.gameObject;
        }

        static GameObject Managers(int chapter)
        {
            var go = new GameObject("_Game");
            go.AddComponent<GameManager>().chapter = chapter;
            var sfx = go.AddComponent<Sfx>();
            string a = Root + "/Audio/UI/";
            AudioClip C(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>(a + n + ".ogg");
            sfx.click = C("click_002"); sfx.confirm = C("confirmation_004"); sfx.success = C("confirmation_002");
            sfx.error = C("error_004"); sfx.pickup = C("select_003"); sfx.drop = C("drop_002");
            sfx.open = C("open_002"); sfx.close = C("close_002"); sfx.glitch = C("glitch_003");
            sfx.bong = C("bong_001"); sfx.toast = C("question_001"); sfx.key = C("tick_001"); sfx.win = C("maximize_006");
            go.AddComponent<Narrator>();
            if (chapter > 0)
            {
                go.AddComponent<GameUI>();
                go.AddComponent<KeyboardGrab>();
                go.AddComponent<ChapterDirector>();
                go.AddComponent<ChapterTitle>();
            }
            return go;
        }

        // ------------------------------------------------------------------ furniture & decoration

        static void Furniture(out float deskTop)
        {
            var ph = Root + "/Models/PolyHaven/";
            var deskA = Model(ph + "metal_office_desk/metal_office_desk_1k.fbx", env, new Vector3(0.82f, 0, 0.62f), DeskYaw, width: 1.6f, height: 0.75f);
            Model(ph + "metal_office_desk/metal_office_desk_1k.fbx", env, new Vector3(-0.82f, 0, 0.62f), DeskYaw, width: 1.6f, height: 0.75f);
            deskTop = BoundsOf(deskA).max.y;
            log.Add($"Desk top height {deskTop:0.000}");

            OfficeChair(new Vector3(1.2f, 0, 0.05f), 15);
            OfficeChair(new Vector3(-1.2f, 0, 0.05f), -15);
            Model(ph + "modern_arm_chair_01/modern_arm_chair_01_1k.fbx", env, new Vector3(1.95f, 0, 1.75f), 215, height: 0.85f);
            var plant = Model(ph + "potted_plant_02/potted_plant_02_1k.fbx", env, new Vector3(-2.05f, 0, 1.85f), 30, height: 1.45f);
            // the leaves spread wide: only the pot should stop the player
            if (plant && plant.TryGetComponent<BoxCollider>(out var plantCol)) plantCol.size = Vector3.Scale(plantCol.size, new Vector3(0.3f, 1, 0.3f));
            var sideboard = Model(ph + "modern_wooden_cabinet/modern_wooden_cabinet_1k.fbx", env, new Vector3(-2.22f, 0, 0.8f), 90, width: 1.45f);
            sideboardTop = BoundsOf(sideboard).max.y;
            var cabinet = Model(ph + "drawer_cabinet/drawer_cabinet_1k.fbx", env, new Vector3(2.2f, 0, 0.2f), 270, height: 0.7f);
            cabinetTop = BoundsOf(cabinet).max.y;
            Model(ph + "security_camera_01/security_camera_01_1k.fbx", env, new Vector3(2.3f, 2.45f, 2.05f), 225, height: 0.3f, collider: false);
            Model(ph + "wall_clock/wall_clock_1k.fbx", env, new Vector3(-2.47f, 2.0f, -1.0f), 90, height: 0.32f, collider: false);
            Model(ph + "fire_alarm/fire_alarm_1k.fbx", env, new Vector3(2.48f, 1.4f, 0.55f), 270, height: 0.13f, collider: false);

            // colleague's desk
            var bezel = Mat("Bezel", new Color(0.05f, 0.05f, 0.055f), 0.75f);
            var lockScreen = Mat("LockScreen", new Color(0.04f, 0.1f, 0.2f), 0.9f, 0, new Color(0.08f, 0.18f, 0.35f));
            var mon = new GameObject("ColleagueMonitor").transform;
            mon.SetParent(env);
            mon.position = new Vector3(-0.55f, deskTop, 0.85f);
            LocalBox("Foot", mon, new Vector3(0, 0.008f, 0), new Vector3(0.24f, 0.016f, 0.18f), bezel, true);
            LocalBox("Neck", mon, new Vector3(0, 0.15f, 0.03f), new Vector3(0.05f, 0.28f, 0.03f), bezel, false);
            LocalBox("Bezel", mon, new Vector3(0, 0.36f, 0.015f), new Vector3(0.6f, 0.37f, 0.025f), bezel, true);
            LocalBox("Screen", mon, new Vector3(0, 0.36f, 0.0015f), new Vector3(0.575f, 0.345f, 0.002f), lockScreen, false);
            Label(mon, "Locked\n<size=50%>Anna Visser</size>", new Vector3(0, 0.36f, -0.003f), Quaternion.identity, new Vector2(0.3f, 0.12f), new Color(0.85f, 0.9f, 1f));
            Keyboard(new Vector3(-0.55f, deskTop, 0.5f));
            Keyboard(new Vector3(0.72f, deskTop, 0.42f));
            LocalBox("Mouse", env, Vector3.zero, new Vector3(0.06f, 0.025f, 0.1f), bezel, false).transform.position = new Vector3(1.0f, deskTop + 0.0125f, 0.42f);
            Model(ph + "classic_laptop/classic_laptop_1k.fbx", props, new Vector3(-1.08f, deskTop, 0.35f), 200, width: 0.34f);
            Model(ph + "stationery_supplies/stationery_supplies_1k.fbx", props, new Vector3(1.25f, deskTop, 0.85f), 160, width: 0.22f, collider: false);
        }

        const float DeskYaw = 0f;
        static float sideboardTop = 0.75f, cabinetTop = 0.7f;

        static void Keyboard(Vector3 pos)
        {
            var k = new GameObject("Keyboard").transform;
            k.SetParent(env);
            k.position = pos;
            k.rotation = Quaternion.Euler(4, 0, 0);
            LocalBox("Base", k, new Vector3(0, 0.008f, 0), new Vector3(0.44f, 0.016f, 0.14f), Mat("KeyboardBase", new Color(0.08f, 0.08f, 0.09f), 0.5f), false);
            LocalBox("Keys", k, new Vector3(0, 0.018f, 0.005f), new Vector3(0.42f, 0.006f, 0.11f), Mat("Keys", new Color(0.16f, 0.16f, 0.17f), 0.35f), false);
        }

        /// Five-star office chair from primitives with fabric + brushed metal PBR materials.
        static void OfficeChair(Vector3 pos, float yaw)
        {
            var fabric = AcgMat("ChairFabric", "Fabric030", new Color(0.32f, 0.33f, 0.36f), new Vector2(2, 2), 0.1f);
            var metal = AcgMat("ChairMetal", "Metal009", new Color(0.75f, 0.76f, 0.78f), Vector2.one, 0.7f, 0.9f);
            var plastic = Mat("ChairPlastic", new Color(0.07f, 0.07f, 0.08f), 0.4f);
            var c = new GameObject("OfficeChair").transform;
            c.SetParent(env);
            c.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            for (int i = 0; i < 5; i++)
            {
                var leg = LocalBox("Leg", c, Vector3.zero, new Vector3(0.04f, 0.03f, 0.3f), metal, false).transform;
                leg.localRotation = Quaternion.Euler(0, i * 72, 0);
                leg.localPosition = leg.localRotation * new Vector3(0, 0.07f, 0.15f);
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(wheel.GetComponent<Collider>());
                wheel.transform.SetParent(c, false);
                wheel.transform.localPosition = leg.localRotation * new Vector3(0, 0.03f, 0.29f);
                wheel.transform.localScale = Vector3.one * 0.055f;
                wheel.GetComponent<Renderer>().sharedMaterial = plastic;
            }
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.transform.SetParent(c, false);
            post.transform.localPosition = new Vector3(0, 0.27f, 0);
            post.transform.localScale = new Vector3(0.05f, 0.18f, 0.05f);
            post.GetComponent<Renderer>().sharedMaterial = metal;
            LocalBox("Seat", c, new Vector3(0, 0.48f, 0), new Vector3(0.5f, 0.09f, 0.48f), fabric, true);
            LocalBox("SeatBase", c, new Vector3(0, 0.42f, 0), new Vector3(0.42f, 0.04f, 0.4f), plastic, false);
            var back = LocalBox("Back", c, new Vector3(0, 0.82f, -0.25f), new Vector3(0.46f, 0.52f, 0.07f), fabric, true).transform;
            back.localRotation = Quaternion.Euler(-8, 0, 0);
            LocalBox("Spine", c, new Vector3(0, 0.55f, -0.26f), new Vector3(0.06f, 0.2f, 0.03f), plastic, false);
            foreach (var s in new[] { -1f, 1f })
            {
                LocalBox("ArmPost", c, new Vector3(s * 0.25f, 0.58f, -0.02f), new Vector3(0.03f, 0.16f, 0.04f), plastic, false);
                LocalBox("ArmPad", c, new Vector3(s * 0.25f, 0.67f, 0.0f), new Vector3(0.06f, 0.025f, 0.26f), plastic, false);
            }
        }

        // ------------------------------------------------------------------ puzzle objects

        static void Puzzles(GameObject game, float top)
        {
            var puzzles = game.GetComponent<RoomPuzzles>();
            var paper = Mat("Paper", new Color(0.97f, 0.97f, 0.95f), 0.1f);
            var sticky = Mat("StickyNote", new Color(1f, 0.9f, 0.3f), 0.1f);
            var plastic = Mat("PlasticDark", new Color(0.13f, 0.14f, 0.16f), 0.45f);
            var plasticLight = AcgMat("PlasticLight", "Plastic010", new Color(0.86f, 0.87f, 0.88f), Vector2.one, 0.5f);
            var metal = AcgMat("Metal", "Metal009", new Color(0.8f, 0.82f, 0.85f), Vector2.one, 0.75f, 0.9f);
            var leather = AcgMat("Leather", "Leather030", new Color(0.75f, 0.55f, 0.4f), new Vector2(2, 2), 0.35f);
            var red = Mat("QuarantineRed", new Color(0.75f, 0.1f, 0.1f), 0.45f);
            var cardboard = AcgMat("Cardboard", "Cardboard001", new Color(0.95f, 0.85f, 0.7f), Vector2.one, 0.1f);
            var tokenBlue = Mat("TokenBlue", new Color(0.12f, 0.3f, 0.6f), 0.5f);

            // --- printer on the colleague's desk with the forgotten printout
            var printer = new GameObject("Printer").transform;
            printer.SetParent(props);
            printer.position = new Vector3(-1.2f, top, 0.78f);
            printer.rotation = Quaternion.Euler(0, 180, 0);
            LocalBox("Body", printer, new Vector3(0, 0.11f, 0), new Vector3(0.42f, 0.22f, 0.36f), plasticLight, true);
            LocalBox("Top", printer, new Vector3(0, 0.225f, 0.03f), new Vector3(0.4f, 0.015f, 0.28f), plastic, false);
            LocalBox("Tray", printer, new Vector3(0, 0.03f, -0.24f), new Vector3(0.3f, 0.01f, 0.14f), plastic, false);
            LocalBox("Panel", printer, new Vector3(0.14f, 0.2f, -0.17f), new Vector3(0.1f, 0.03f, 0.03f), plastic, false);
            var printout = Paper("Printout", printer.TransformPoint(new Vector3(0, 0.06f, -0.22f)), 180 + 180, paper,
                "<b>PRINT JOB #4471</b>\nFRED-PC-07  01:58\n\n<b>vpn_access_export.csv</b>\nfdekker;VPN-FULL;*****\nadmin_ops;VPN-FULL;*****\nsvc_backup;VPN-FULL;*****");
            Grab(printout, ItemKind.EvidencePrintout, "Printout",
                "A printout of VPN access data, printed at 01:58 from Fred's PC. That's evidence of the attack.",
                "This printout is evidence of the attack. Bag it in the EVIDENCE box.");

            // --- waste bin with the sticky note
            var bin = new GameObject("WasteBin");
            bin.transform.SetParent(props);
            bin.transform.position = new Vector3(-0.3f, 0, -1.9f);   // against the front wall, out of the walkway
            var binMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(binMesh.GetComponent<Collider>());
            binMesh.name = "Can";
            binMesh.transform.SetParent(bin.transform, false);
            binMesh.transform.localPosition = new Vector3(0, 0.19f, 0);
            binMesh.transform.localScale = new Vector3(0.3f, 0.19f, 0.3f);
            binMesh.GetComponent<Renderer>().sharedMaterial = AcgMat("BinMetal", "Metal009", new Color(0.25f, 0.26f, 0.28f), new Vector2(3, 1), 0.6f, 0.8f);
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.transform.SetParent(bin.transform, false);
            rim.transform.localPosition = new Vector3(0, 0.381f, 0);
            rim.transform.localScale = new Vector3(0.27f, 0.002f, 0.27f);
            rim.GetComponent<Renderer>().sharedMaterial = Mat("BinInside", new Color(0.03f, 0.03f, 0.03f), 0.2f);
            var binB = new Bounds(bin.transform.position + Vector3.up * 0.19f, new Vector3(0.3f, 0.38f, 0.3f));
            var binCol = bin.AddComponent<BoxCollider>();
            binCol.center = new Vector3(0, 0.19f, 0);
            binCol.size = new Vector3(0.3f, 0.38f, 0.3f);
            var trashPaper = Mat("CrumpledPaper", new Color(0.92f, 0.92f, 0.9f), 0.05f);
            for (int i = 0; i < 4; i++)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Crumpled";
                Object.DestroyImmediate(ball.GetComponent<Collider>());
                ball.transform.SetParent(bin.transform);
                ball.transform.position = new Vector3(binB.center.x + Mathf.Cos(i * 1.7f) * 0.07f, binB.max.y - 0.02f + (i % 2) * 0.015f, binB.center.z + Mathf.Sin(i * 1.7f) * 0.07f);
                ball.transform.localScale = Vector3.one * 0.07f / bin.transform.lossyScale.x;
                ball.GetComponent<Renderer>().sharedMaterial = trashPaper;
            }
            var note = Paper("StickyNote", new Vector3(binB.center.x, binB.max.y + 0.03f, binB.center.z), 10, sticky,
                "new pw:\n<b>cat's name\n+ year we\ngot her + !</b>", new Vector2(0.076f, 0.076f));
            Grab(note, ItemKind.EvidenceNote, "Sticky note",
                "A sticky note from the bin: 'new pw: cat's name + year we got her + !'. That's Fred's password pattern.",
                "Keep this note as evidence of how the password leaked. Bag it in the EVIDENCE box.");
            var binZone = Zone("WasteBinZone", bin.transform, new Vector3(binB.center.x, binB.max.y + 0.06f, binB.center.z), new Vector3(0.34f, 0.16f, 0.34f), "waste bin", ItemKind.Recycle);
            puzzles.wasteBin = binZone;

            // --- documents on Fred's desk
            var docA = Paper("Doc_Salaries", new Vector3(1.12f, top + 0.004f, 0.32f), -15, paper, "<color=#C62828><b>CONFIDENTIAL</b></color>\n\n<b>Q3 Salary overview</b>\nNorthwind B.V.\n\nM. Jansen   EUR ******\nA. Visser   EUR ******\nF. Dekker   EUR ******");
            Grab(docA, ItemKind.Confidential, "Confidential document", "CONFIDENTIAL: Q3 salary overview, left on the desk.", "Confidential paper has to be destroyed. Use the shredder.");
            var docB = Paper("Doc_Clients", new Vector3(1.3f, top + 0.008f, 0.55f), 20, paper, "<color=#C62828><b>CONFIDENTIAL</b></color>\n\n<b>Client list 2026</b>\n\nHarbor Logistics\nVanguard Health\nPolder Energy\n...");
            Grab(docB, ItemKind.Confidential, "Confidential document", "CONFIDENTIAL: the 2026 client list. This shouldn't be lying around.", "Confidential paper has to be destroyed. Use the shredder.");
            var menu = Paper("LunchMenu", new Vector3(0.38f, top + 0.004f, 0.28f), -5, paper, "<b>CANTEEN</b>\n\nThursday:\n<b>Pasta day!</b>\n\nFriday:\nPizza & salad");
            Grab(menu, ItemKind.Recycle, "Lunch menu", "The canteen lunch menu. Not sensitive.", "It's just a lunch menu. Throw it in the waste bin.");

            // --- shredder
            var shredder = new GameObject("Shredder").transform;
            shredder.SetParent(props);
            shredder.position = new Vector3(2.0f, 0, 0.95f);
            shredder.rotation = Quaternion.Euler(0, 200, 0);
            var shBody = new GameObject("Body").transform;
            shBody.SetParent(shredder, false);
            LocalBox("Bin", shBody, new Vector3(0, 0.22f, 0), new Vector3(0.38f, 0.44f, 0.28f), plastic, true);
            LocalBox("Head", shBody, new Vector3(0, 0.5f, 0), new Vector3(0.4f, 0.12f, 0.3f), plasticLight, true);
            LocalBox("Slot", shBody, new Vector3(0, 0.562f, 0), new Vector3(0.3f, 0.006f, 0.02f), Mat("Slot", Color.black, 0.1f), false);
            Label(shBody, "SHREDDER", new Vector3(0, 0.5f, -0.155f), Quaternion.identity, new Vector2(0.3f, 0.06f), Color.white);
            var shZone = Zone("ShredderSlot", shredder, shredder.TransformPoint(new Vector3(0, 0.62f, 0)), new Vector3(0.42f, 0.14f, 0.32f), "shredder", ItemKind.Confidential);
            shZone.intake = new GameObject("Intake").transform;
            shZone.intake.SetParent(shredder, false);
            shZone.intake.localPosition = new Vector3(0, 0.57f, 0);
            shZone.intake.localRotation = Quaternion.Euler(90, 0, 0);
            puzzles.shredder = shZone;
            puzzles.shredderBody = shBody;

            // --- evidence box on the floor
            var box = new GameObject("EvidenceBox");
            box.transform.SetParent(props);
            box.transform.SetPositionAndRotation(new Vector3(-1.2f, 0, -1.85f), Quaternion.Euler(0, 185, 0));   // label faces into the room
            OpenBox(box.transform, new Vector3(0.5f, 0.3f, 0.38f), cardboard);
            var boxB = BoundsOf(box);
            Label(box.transform, "EVIDENCE", new Vector3(0, 0.17f, -0.203f), Quaternion.identity, new Vector2(0.4f, 0.09f), new Color(0.7f, 0.08f, 0.08f));
            var evZone = Zone("EvidenceZone", box.transform, boxB.center + Vector3.up * 0.04f, boxB.size * 0.85f, "evidence box", ItemKind.EvidencePrintout, ItemKind.EvidenceNote);
            puzzles.evidenceBox = evZone;

            // --- quarantine container on the side table by the door
            var q = new GameObject("Quarantine").transform;
            q.SetParent(props);
            q.position = new Vector3(2.2f, cabinetTop, 0.2f);
            q.rotation = Quaternion.Euler(0, 270, 0);
            OpenBox(q, new Vector3(0.32f, 0.16f, 0.24f), red);
            Label(q, "QUARANTINE", new Vector3(0, 0.08f, -0.132f), Quaternion.identity, new Vector2(0.3f, 0.05f), Color.white);
            Label(q, "UNKNOWN DEVICES ONLY", new Vector3(0, 0.035f, -0.132f), Quaternion.identity, new Vector2(0.3f, 0.025f), Color.white);
            var qZone = Zone("QuarantineZone", q, q.TransformPoint(new Vector3(0, 0.1f, 0)), new Vector3(0.32f, 0.18f, 0.24f), "quarantine container", ItemKind.USB);
            puzzles.quarantine = qZone;

            // --- briefcase on the drawer cabinet: hardware token + suspicious USB
            var bc = new GameObject("Briefcase").transform;
            bc.SetParent(props);
            bc.position = new Vector3(-2.2f, sideboardTop, 0.45f);
            bc.rotation = Quaternion.Euler(0, 90, 0);
            OpenBox(bc, new Vector3(0.46f, 0.07f, 0.33f), leather);
            var lidPivot = new GameObject("LidHinge").transform;
            lidPivot.SetParent(bc, false);
            lidPivot.localPosition = new Vector3(0, 0.07f, 0.165f);
            LocalBox("Lid", lidPivot, new Vector3(0, 0.02f, -0.165f), new Vector3(0.46f, 0.04f, 0.33f), leather, true);
            LocalBox("Handle", bc, new Vector3(0, 0.05f, -0.19f), new Vector3(0.12f, 0.025f, 0.03f), plastic, false);
            LocalBox("Clasp", lidPivot, new Vector3(0.15f, 0.02f, -0.335f), new Vector3(0.04f, 0.03f, 0.01f), metal, false);
            var hinge = lidPivot.gameObject.AddComponent<Hinge>();
            hinge.openEuler = new Vector3(-105, 0, 0);
            puzzles.briefcaseLid = hinge;
            var click = MakeClickable(lidPivot.Find("Lid").gameObject, "Fred's briefcase. It isn't locked.");
            UnityEventTools.AddPersistentListener(click.onClick, puzzles.OpenBriefcase);

            var usb = new GameObject("USB_Stick");
            usb.transform.SetParent(props);
            usb.transform.position = bc.TransformPoint(new Vector3(-0.1f, 0.025f, 0));
            usb.transform.rotation = bc.rotation * Quaternion.Euler(0, 20, 0);
            LocalBox("Body", usb.transform, Vector3.zero, new Vector3(0.065f, 0.014f, 0.022f), plastic, false);
            LocalBox("Tip", usb.transform, new Vector3(0.042f, 0, 0), new Vector3(0.02f, 0.008f, 0.014f), metal, false);
            Label(usb.transform, "SALARIES 2026", new Vector3(-0.003f, 0.009f, 0), Quaternion.Euler(90, 0, 0), new Vector2(0.06f, 0.016f), Color.white);
            usb.AddComponent<BoxCollider>().size = new Vector3(0.085f, 0.02f, 0.03f);
            Grab(usb, ItemKind.USB, "USB stick", "A USB stick labelled 'SALARIES 2026'. Fred didn't bring this. Classic USB bait: never plug it in!",
                "Unknown USB sticks go in the red QUARANTINE container by the door.");

            var token = new GameObject("HardwareToken");
            token.transform.SetParent(props);
            token.transform.position = bc.TransformPoint(new Vector3(0.1f, 0.03f, 0));
            token.transform.rotation = bc.rotation * Quaternion.Euler(0, 180, 0);
            LocalBox("Body", token.transform, Vector3.zero, new Vector3(0.075f, 0.016f, 0.04f), tokenBlue, false);
            LocalBox("Screen", token.transform, new Vector3(0, 0.0085f, 0.002f), new Vector3(0.055f, 0.002f, 0.022f), Mat("LCD", new Color(0.62f, 0.72f, 0.6f), 0.6f), false);
            var code = Label(token.transform, "000 000", new Vector3(0, 0.0112f, 0.004f), Quaternion.Euler(90, 0, 0), new Vector2(0.05f, 0.014f), new Color(0.05f, 0.08f, 0.05f));
            var bars = Label(token.transform, "||||||||||", new Vector3(0, 0.0112f, -0.0055f), Quaternion.Euler(90, 0, 0), new Vector2(0.05f, 0.005f), new Color(0.05f, 0.08f, 0.05f));
            Label(token.transform, "SecureKey", new Vector3(0, 0.0092f, -0.016f), Quaternion.Euler(90, 0, 0), new Vector2(0.05f, 0.007f), Color.white);
            token.AddComponent<BoxCollider>().size = new Vector3(0.075f, 0.02f, 0.04f);
            var auth = token.AddComponent<AuthToken>();
            auth.display = code;
            auth.countdown = bars;
            Grab(token, ItemKind.Token, "Hardware token", "Fred's hardware 2FA token. It shows a new 6-digit code every 30 seconds.", "Keep the token. You need its code for 2FA on Fred's account.");

            // --- tutorial / decor props
            var stapler = Model(Root + "/Models/PolyHaven/vintage_stapler/vintage_stapler_1k.fbx", props, new Vector3(-0.3f, top, 0.3f), 160, width: 0.17f);
            Grab(stapler, ItemKind.Prop, "Stapler", "A stapler. Nothing suspicious here.", null);
            var pads = Model(Root + "/Models/PolyHaven/office_notepads/office_notepads_1k.fbx", props, new Vector3(-0.85f, top, 0.3f), 200, width: 0.2f);
            Grab(pads, ItemKind.Prop, "Notepads", "Blank notepads.", null);
            var clip = Model(Root + "/Models/PolyHaven/clipboard/clipboard_1k.fbx", props, new Vector3(1.5f, top, 0.35f), 150, width: 0.24f);
            Grab(clip, ItemKind.Prop, "Clipboard", "An empty clipboard.", null);
        }

        // ------------------------------------------------------------------ canvases: workstation, frame, posters, board, keypad, HUD

        static void Interfaces(GameObject game, Transform head, float top)
        {
            var bezelMat = Mat("Bezel", new Color(0.06f, 0.06f, 0.07f), 0.6f);

            // --- Fred's monitor (the in-game browser)
            var mon = new GameObject("FredMonitor").transform;
            mon.SetParent(props);
            mon.position = new Vector3(0.72f, top, 0.82f);
            LocalBox("Foot", mon, new Vector3(0, 0.008f, 0), new Vector3(0.26f, 0.016f, 0.2f), bezelMat, true);
            LocalBox("Neck", mon, new Vector3(0, 0.16f, 0.03f), new Vector3(0.05f, 0.3f, 0.03f), bezelMat, false);
            var screenPivot = new GameObject("Screen").transform;
            screenPivot.SetParent(mon, false);
            screenPivot.localPosition = new Vector3(0, 0.42f, 0);
            screenPivot.localRotation = Quaternion.Euler(10, 0, 0);
            FramedScreen(screenPivot, Vector2.zero, 1280 * 0.00058f, 800 * 0.00058f, bezelMat, 0f, 0.02f);
            var ws = MakeCanvas("WorkstationCanvas", screenPivot, Vector3.zero, Quaternion.identity, new Vector2(1280, 800), 0.00058f);   // canvas plane = frame front face (see FramedScreen)
            var wsComp = mon.gameObject.AddComponent<Workstation>();
            wsComp.screen = ws;
            AddCanvasInteraction(ws.gameObject, mon, poke: true);

            // floating keyboard: 6 x 6 cm keys (100 units at 0.6 mm), tilted toward the player like a lectern above
            // the desk keyboard and below the line of sight to the screen. Its ray / poke surfaces live under the
            // pivot, so the workstation hides them together with the panel when no text field is active.
            var kbPivot = new GameObject("FloatingKeyboard").transform;
            kbPivot.SetParent(mon, true);
            kbPivot.SetPositionAndRotation(new Vector3(0.72f, top + 0.19f, 0.38f), Quaternion.Euler(35, 0, 0));
            var kb = MakeCanvas("KeyboardCanvas", kbPivot, Vector3.zero, Quaternion.identity, new Vector2(1120, 560), 0.0006f);
            AddCanvasInteraction(kb.gameObject, kbPivot, poke: true);
            wsComp.keyboardCanvas = kb;

            // --- Fred's digital photo frame
            var frame = new GameObject("PhotoFrame").transform;
            frame.SetParent(props);
            frame.position = new Vector3(0.22f, top, 0.62f);
            frame.rotation = Quaternion.Euler(0, -25, 0);
            var framePivot = new GameObject("Tilt").transform;
            framePivot.SetParent(frame, false);
            framePivot.localPosition = new Vector3(0, 0.115f, 0);
            framePivot.localRotation = Quaternion.Euler(12, 0, 0);
            LocalBox("Bezel", framePivot, new Vector3(0, 0, 0.008f), new Vector3(0.3f, 0.22f, 0.016f), bezelMat, true);
            LocalBox("Stand", frame, new Vector3(0, 0.06f, 0.06f), new Vector3(0.04f, 0.12f, 0.01f), bezelMat, false).transform.localRotation = Quaternion.Euler(-25, 0, 0);
            var pf = MakeCanvas("FrameCanvas", framePivot, new Vector3(0, 0, -0.0005f), Quaternion.identity, new Vector2(560, 400), 0.0005f);
            Object.DestroyImmediate(pf.GetComponent<GraphicRaycaster>());
            var photo = UIKit_Image(pf, "Photo");
            photo.rectTransform.anchorMin = Vector2.zero; photo.rectTransform.anchorMax = Vector2.one;
            photo.rectTransform.offsetMin = new Vector2(0, 70); photo.rectTransform.offsetMax = Vector2.zero;
            var capBg = UIKit_Image(pf, "CaptionBg");
            capBg.color = new Color(0, 0, 0, 0.85f);
            capBg.rectTransform.anchorMin = Vector2.zero; capBg.rectTransform.anchorMax = new Vector2(1, 0);
            capBg.rectTransform.pivot = new Vector2(0.5f, 0); capBg.rectTransform.sizeDelta = new Vector2(0, 70);
            var cap = UGUIText(capBg.rectTransform, "", 30);
            var pfc = frame.gameObject.AddComponent<PhotoFrame>();
            pfc.photo = photo;
            pfc.captionText = cap;
            pfc.slides = new[]
            {
                SpriteAt(Root + "/Textures/photo_cat_1.jpg"),
                SpriteAt(Root + "/Textures/photo_cat_2.jpg"),
            };
            pfc.captions = new[]
            {
                "Fluffy - our princess",
                "Fluffy's first day home - 2019",
                "Happy birthday Fluffy!",
                "Since 2019 with us - best cat ever",
            };
            var frameClick = MakeClickable(framePivot.Find("Bezel").gameObject, "A digital photo frame with a slideshow. Fred really loves his cat.");
            UnityEventTools.AddPersistentListener(frameClick.onClick, pfc.Next);

            // --- posters & wall board (back wall)
            PosterAt(Poster.Kind.Passwords, new Vector3(-1.55f, 1.55f, HalfZ - 0.005f), Quaternion.identity, 0.00085f);
            PosterAt(Poster.Kind.CleanDesk, new Vector3(-0.72f, 1.62f, HalfZ - 0.005f), Quaternion.identity, 0.0006f);
            PosterAt(Poster.Kind.Shred, new Vector3(1.7f, 1.62f, HalfZ - 0.005f), Quaternion.identity, 0.0006f);
            PosterAt(Poster.Kind.Phishing, new Vector3(-HalfX + 0.005f, 1.55f, -0.75f), Quaternion.Euler(0, -90, 0), 0.0007f);
            var board = MakeCanvas("WallBoardCanvas", env, new Vector3(0.45f, 1.68f, HalfZ - 0.01f), Quaternion.identity, new Vector2(900, 560), 0.0013f);
            Object.DestroyImmediate(board.GetComponent<GraphicRaycaster>());
            board.gameObject.AddComponent<WallBoard>().canvas = board;

            // --- exit door + keypad
            var hingeT = new GameObject("DoorHinge").transform;
            hingeT.SetParent(env);
            hingeT.position = new Vector3(HalfX + 0.02f, 0, DoorZ0);
            var doorMat = TexMat("DoorWood", "cherry_veneer", new Color(0.8f, 0.65f, 0.5f), new Vector2(1, 2), 0.35f);
            LocalBox("Door", hingeT, new Vector3(0, DoorH / 2, (DoorZ1 - DoorZ0) / 2), new Vector3(0.045f, DoorH - 0.01f, DoorZ1 - DoorZ0 - 0.01f), doorMat, true);
            LocalBox("Handle", hingeT, new Vector3(-0.04f, 1.0f, DoorZ1 - DoorZ0 - 0.1f), new Vector3(0.03f, 0.03f, 0.14f), MetalMat(), false);
            var doorHinge = hingeT.gameObject.AddComponent<Hinge>();
            doorHinge.openEuler = new Vector3(0, -100, 0);
            doorHinge.duration = 1.6f;
            Label(env, "EXIT", new Vector3(HalfX - 0.02f, DoorH + 0.2f, (DoorZ0 + DoorZ1) / 2), Quaternion.Euler(0, 90, 0), new Vector2(0.35f, 0.12f), new Color(0.2f, 1f, 0.45f), true);
            var kp = MakeCanvas("KeypadCanvas", env, new Vector3(HalfX - 0.01f, 1.32f, DoorZ1 + 0.3f), Quaternion.Euler(0, 90, 0), new Vector2(360, 520), 0.0006f);
            var kpRoot = new GameObject("DoorKeypad").transform;
            kpRoot.SetParent(env);
            kp.SetParent(kpRoot, true);
            var keypad = kpRoot.gameObject.AddComponent<DoorKeypad>();
            keypad.canvas = kp;
            keypad.door = doorHinge;
            AddCanvasInteraction(kp.gameObject, kpRoot, poke: true);

        }

        static Material MetalMat() => AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Metal.mat");

        static void PosterAt(Poster.Kind kind, Vector3 pos, Quaternion rot, float scale)
        {
            var c = MakeCanvas("Poster_" + kind, env, pos, rot, new Vector2(600, 840), scale);
            foreach (var g in c.GetComponentsInChildren<GraphicRaycaster>()) Object.DestroyImmediate(g);
            var p = c.gameObject.AddComponent<Poster>();
            p.kind = kind;
            p.canvas = c;
        }

        // ------------------------------------------------------------------ teleport hotspots

        static void Teleports()
        {
            var spots = new (string name, Vector3 pos)[]
            {
                ("Spot_Start", new Vector3(0, 0, -0.95f)),
                ("Spot_FredDesk", new Vector3(0.75f, 0, -0.15f)),
                ("Spot_PrinterDesk", new Vector3(-0.8f, 0, -0.75f)),
                ("Spot_Briefcase", new Vector3(-1.35f, 0, -0.9f)),
                ("Spot_Door", new Vector3(1.75f, 0, -0.85f)),
            };
            var parent = new GameObject("TeleportHotspots").transform;
            foreach (var (name, pos) in spots)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent);
                go.transform.position = pos;
                try
                {
                    QuickActionsAPI.AddTeleportInteraction(go, TeleportSurfaceType.Hotspot, TeleportHotspotSnap.SnapPosition);
                }
                catch (Exception e)
                {
                    log.Add($"Teleport hotspot {name} failed: {e.Message}");
                }
            }
            TeleportFloor(parent, new Vector3(HalfX, Height, HalfZ));
        }

        // ------------------------------------------------------------------ helpers: interactions

        static void AddCanvasInteraction(GameObject canvas, Transform keepUnder, bool poke)
        {
            try
            {
                foreach (var go in QuickActionsAPI.AddRayCanvasInteraction(canvas))
                    if (go && !go.transform.IsChildOf(keepUnder)) go.transform.SetParent(keepUnder, true);
                if (poke)
                    foreach (var go in QuickActionsAPI.AddPokeCanvasInteraction(canvas))
                        if (go && !go.transform.IsChildOf(keepUnder)) go.transform.SetParent(keepUnder, true);
            }
            catch (Exception e)
            {
                log.Add($"Canvas interaction on {canvas.name} failed: {e}");
            }
        }

        static GrabItem Grab(GameObject go, ItemKind kind, string name, string caption, string wrongZone)
        {
            if (!go.GetComponent<Collider>() && !go.GetComponentInChildren<Collider>())
            {
                var b = BoundsOf(go);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                var s = go.transform.lossyScale;
                bc.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z);
            }
            if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
            rb.mass = kind == ItemKind.Prop ? 0.4f : 0.08f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            try
            {
                QuickActionsAPI.AddGrabInteraction(go);
                QuickActionsAPI.AddDistanceGrabInteraction(go, DistanceGrabMode.PullToHand);
            }
            catch (Exception e)
            {
                log.Add($"Grab setup on {go.name} failed: {e}");
            }
            // The distance-grab wizard adds a "time-out snap zone" that flies released items back to their spawn.
            // Puzzle items must stay where the player drops them, so remove it (as the wizard does when no zone is set).
            foreach (var si in go.GetComponentsInChildren<SnapInteractor>(true)) Object.DestroyImmediate(si);
            var snapZone = go.transform.parent ? go.transform.parent.Find($"ISDK_SnapZone ({go.name})") : null;
            if (snapZone) Object.DestroyImmediate(snapZone.gameObject);
            // The distance-grab reticle draws a white "ghost" copy of the item's mesh in the same place on hover; on thin
            // items (papers, sticky note) it z-fights with the real surface and flickers. Keep only the small icon reticle.
            foreach (var rm in go.GetComponentsInChildren<Oculus.Interaction.DistanceReticles.ReticleDataMesh>(true)) Object.DestroyImmediate(rm);
            // the access badge stays where the player lets go of it instead of falling to the floor
            bool floaty = kind == ItemKind.Badge;
            rb.isKinematic = floaty;
            rb.useGravity = !floaty;
            var item = go.AddComponent<GrabItem>();
            item.kind = kind;
            // papers turn upright to the eyes for reading; USB / token show their labelled face
            item.holdPose = kind == ItemKind.EvidencePrintout || kind == ItemKind.EvidenceNote || kind == ItemKind.Confidential || kind == ItemKind.Recycle
                ? GrabItem.HoldPose.Read
                : kind == ItemKind.USB || kind == ItemKind.Token ? GrabItem.HoldPose.Inspect : GrabItem.HoldPose.Free;
            item.displayName = name;
            item.caption = caption;
            item.wrongZoneHint = wrongZone;
            return item;
        }

        static Clickable MakeClickable(GameObject target, string caption)
        {
            var col = target.GetComponent<Collider>();
            var rayGo = new GameObject("RayTarget");
            rayGo.transform.SetParent(target.transform, false);
            var surface = rayGo.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(col);
            var ray = rayGo.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);
            var wrapper = rayGo.AddComponent<InteractableUnityEventWrapper>();
            wrapper.InjectAllInteractableUnityEventWrapper(ray);
            var c = target.AddComponent<Clickable>();
            c.caption = caption;
            return c;
        }

        static DropZone Zone(string name, Transform parent, Vector3 worldCenter, Vector3 size, string label, params ItemKind[] accepts)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = worldCenter;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            var s = go.transform.lossyScale;
            bc.size = new Vector3(size.x / s.x, size.y / s.y, size.z / s.z);
            var z = go.AddComponent<DropZone>();
            z.zoneName = label;
            z.accepts = accepts;
            z.intake = go.transform;
            return z;
        }

        // ------------------------------------------------------------------ helpers: geometry

        static GameObject Box(string name, Transform parent, Vector3 worldCenter, Vector3 size, Material m, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = parent == env;
            return go;
        }

        /// Monitor / laptop screen surround without any surface coplanar with the canvas: four frame bars whose front
        /// face is exactly the canvas plane (frontZ), plus a backing box recessed behind the canvas. The canvas
        /// therefore sits flush in the frame (no visible gap) and never z-fights, even with a 16-bit depth buffer.
        static void FramedScreen(Transform parent, Vector2 center, float w, float h, Material m, float frontZ,
            float frame = 0.015f, float depth = 0.025f, float backGap = -1f)
        {
            // thin back plate at the very back of the frame: the canvas has the whole cavity (>= 9 mm) of clearance,
            // far more than any depth-buffer precision, and the frame bars close the box on every side
            const float plate = 0.005f;
            LocalBox("Bezel", parent, new Vector3(center.x, center.y, frontZ + depth - plate / 2), new Vector3(w + 2 * frame, h + 2 * frame, plate), m, true);
            float fz = frontZ + depth / 2;
            LocalBox("FrameTop", parent, new Vector3(center.x, center.y + h / 2 + frame / 2, fz), new Vector3(w + 2 * frame, frame, depth), m, false);
            LocalBox("FrameBottom", parent, new Vector3(center.x, center.y - h / 2 - frame / 2, fz), new Vector3(w + 2 * frame, frame, depth), m, false);
            LocalBox("FrameLeft", parent, new Vector3(center.x - w / 2 - frame / 2, center.y, fz), new Vector3(frame, h, depth), m, false);
            LocalBox("FrameRight", parent, new Vector3(center.x + w / 2 + frame / 2, center.y, fz), new Vector3(frame, h, depth), m, false);
        }

        static GameObject LocalBox(string name, Transform parent, Vector3 localCenter, Vector3 size, Material m, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// Open-topped box (5 slabs) so items can be dropped inside.
        static void OpenBox(Transform parent, Vector3 size, Material m)
        {
            float t = 0.012f;
            LocalBox("Bottom", parent, new Vector3(0, t / 2, 0), new Vector3(size.x, t, size.z), m, true);
            LocalBox("Front", parent, new Vector3(0, size.y / 2, -size.z / 2), new Vector3(size.x, size.y, t), m, true);
            LocalBox("Back", parent, new Vector3(0, size.y / 2, size.z / 2), new Vector3(size.x, size.y, t), m, true);
            LocalBox("Left", parent, new Vector3(-size.x / 2, size.y / 2, 0), new Vector3(t, size.y, size.z), m, true);
            LocalBox("Right", parent, new Vector3(size.x / 2, size.y / 2, 0), new Vector3(t, size.y, size.z), m, true);
        }

        /// Invisible colliders (floor + 4 walls) matching a model's bounds, for bins and boxes.
        static void Container(Transform model, Bounds b, float t)
        {
            var root = new GameObject("ContainerColliders").transform;
            root.SetParent(model, true);
            root.position = b.center;
            root.rotation = Quaternion.identity;
            void C(Vector3 c, Vector3 s)
            {
                var go = new GameObject("Col");
                go.transform.SetParent(root, true);
                go.transform.position = c;
                go.transform.rotation = Quaternion.identity;
                var bc = go.AddComponent<BoxCollider>();
                var ls = go.transform.lossyScale;
                bc.size = new Vector3(s.x / ls.x, s.y / ls.y, s.z / ls.z);
            }
            Vector3 e = b.extents;
            C(new Vector3(b.center.x, b.min.y + t / 2, b.center.z), new Vector3(b.size.x, t, b.size.z));
            C(new Vector3(b.min.x + t / 2, b.center.y, b.center.z), new Vector3(t, b.size.y, b.size.z));
            C(new Vector3(b.max.x - t / 2, b.center.y, b.center.z), new Vector3(t, b.size.y, b.size.z));
            C(new Vector3(b.center.x, b.center.y, b.min.z + t / 2), new Vector3(b.size.x, b.size.y, t));
            C(new Vector3(b.center.x, b.center.y, b.max.z - t / 2), new Vector3(b.size.x, b.size.y, t));
        }

        static GameObject Paper(string name, Vector3 pos, float yaw, Material m, string text, Vector2? size = null)
        {
            var s = size ?? new Vector2(0.21f, 0.297f);
            var go = new GameObject(name);
            go.transform.SetParent(props);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            LocalBox("Sheet", go.transform, new Vector3(0, 0.0015f, 0), new Vector3(s.x, 0.003f, s.y), m, false);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, 0.004f, 0);
            bc.size = new Vector3(s.x, 0.008f, s.y);
            var t = Label(go.transform, text, new Vector3(0, 0.0045f, 0), Quaternion.Euler(90, 0, 0), new Vector2(s.x * 0.86f, s.y * 0.88f), new Color(0.1f, 0.1f, 0.12f));
            t.alignment = TextAlignmentOptions.TopLeft;
            t.fontSizeMax = s.y < 0.1f ? 0.12f : 0.16f;
            // backside so the sheet reads as paper from below
            return go;
        }

        static TextMeshPro Label(Transform parent, string text, Vector3 localPos, Quaternion localRot, Vector2 size, Color color, bool world = false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            if (world) go.transform.SetPositionAndRotation(localPos, localRot);
            else
            {
                go.transform.localPosition = localPos;
                go.transform.localRotation = localRot;
            }
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = size;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.01f;
            t.fontSizeMax = size.y * 7f;
            t.fontStyle = FontStyles.Bold;
            t.textWrappingMode = TextWrappingModes.Normal;
            var ls = go.transform.lossyScale;
            if (Mathf.Abs(ls.x - 1) > 0.01f) go.transform.localScale = new Vector3(1 / ls.x, 1 / ls.y, 1 / ls.z);
            return t;
        }

        static RectTransform MakeCanvas(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (parent == env || parent == ui) rt.SetPositionAndRotation(pos, rot);
            else
            {
                rt.localPosition = pos;
                rt.localRotation = rot;
            }
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * scale;
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            return rt;
        }

        static Image UIKit_Image(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI UGUIText(RectTransform parent, string text, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }

        static GameObject Model(string path, Transform parent, Vector3 floorPos, float yaw, float width = 0, float height = 0, bool collider = true)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset)
            {
                log.Add("MISSING model " + path);
                var ph = new GameObject(Path.GetFileNameWithoutExtension(path) + "_MISSING");
                ph.transform.SetParent(parent);
                ph.transform.position = floorPos;
                return ph;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one;
            ApplyPolyHavenMaterials(go, path);
            var b = BoundsOf(go);
            float k = 1;
            if (width > 0) k = width / Mathf.Max(b.size.x, b.size.z);
            else if (height > 0) k = height / b.size.y;
            // both given: fit the footprint to width, then stretch vertically to the exact height
            go.transform.localScale = width > 0 && height > 0 ? new Vector3(k, height / b.size.y, k) : Vector3.one * k;
            b = BoundsOf(go);
            go.transform.position += floorPos - new Vector3(b.center.x, b.min.y, b.center.z);
            if (collider)
            {
                b = BoundsOf(go);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                // world-aligned bounds of a yawed model: undo rotation for the local box size
                var local = Quaternion.Inverse(go.transform.rotation) * b.size;
                var s = go.transform.lossyScale;
                bc.size = new Vector3(Mathf.Abs(local.x) / s.x, Mathf.Abs(local.y) / s.y, Mathf.Abs(local.z) / s.z);
            }
            if (parent == env) foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// Swaps a Kenney model's flat colours for project materials: light parts -> light, dark parts -> dark.
        static void Recolor(GameObject go, Material light, Material dark)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    var c = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor") : mats[i].color;
                    float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    mats[i] = lum > 0.35f ? light : dark;
                }
                r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------ helpers: materials & textures

        static Material Mat(string name, Color color, float smoothness, float metallic = 0, Color? emission = null)
        {
            Directory.CreateDirectory(MatDir);
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material AcgMat(string name, string id, Color tint, Vector2 tiling, float smoothness, float metallic = 0, float bump = 1f)
        {
            var m = Mat(name, tint, smoothness, metallic);
            var dir = Root + "/Textures/ACG/";
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}{id}_diff.jpg");
            var nor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}{id}_nor_gl.jpg");
            m.SetTexture("_BaseMap", diff);
            m.SetTextureScale("_BaseMap", tiling);
            if (nor) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", bump); }
            return m;
        }

        static Material TexMat(string name, string tex, Color tint, Vector2 tiling, float smoothness, bool useDiffuse = true)
        {
            var m = Mat(name, tint, smoothness);
            var dir = Root + "/Textures/";
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}{tex}_diff_1k.jpg");
            var nor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}{tex}_nor_gl_1k.jpg");
            m.SetTexture("_BaseMap", useDiffuse ? diff : null);
            m.SetTextureScale("_BaseMap", tiling);
            if (nor) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 0.6f); }
            return m;
        }

        static void ApplyPolyHavenMaterials(GameObject go, string modelPath)
        {
            if (!modelPath.Contains("/PolyHaven/")) return;
            var dir = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            var id = Path.GetFileName(dir);
            var diffs = Directory.GetFiles(dir, "*_diff_*").Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')).ToArray();
            var nors = Directory.GetFiles(dir, "*_nor_gl_*").Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')).ToArray();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string mname = mats[i] ? mats[i].name : id;
                    string token = mname.Replace(id, "").Trim('_', ' ').ToLowerInvariant();
                    var d = Pick(diffs, token);
                    var n = Pick(nors, token);
                    string matName = "PH_" + id + (token.Length > 0 ? "_" + token : "");
                    var m = Mat(matName, Color.white, 0.35f);
                    if (d != null) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(d));
                    if (n != null) { m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(n)); m.EnableKeyword("_NORMALMAP"); }
                    if (d != null && d.Contains("leaves"))
                    {
                        m.SetFloat("_AlphaClip", 1);
                        m.SetFloat("_Cutoff", 0.5f);
                        m.EnableKeyword("_ALPHATEST_ON");
                        m.SetFloat("_Cull", 0);
                        m.renderQueue = (int)RenderQueue.AlphaTest;
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        static string Pick(string[] files, string token)
        {
            if (files.Length == 0) return null;
            if (token.Length > 0)
            {
                var f = files.FirstOrDefault(x => Path.GetFileName(x).ToLowerInvariant().Contains("_" + token + "_"));
                if (f != null) return f;
            }
            return files[0];
        }

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
            {
                var mp = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(mp) is ModelImporter mi && !mi.generateSecondaryUV)
                {
                    mi.generateSecondaryUV = true;
                    mi.SaveAndReimport();
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Models", Root + "/Textures" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(p) is not TextureImporter ti) continue;
                bool normal = p.Contains("_nor_gl_");
                bool photo = p.Contains("photo_");
                bool dirty = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (photo && ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
                int max = photo ? 512 : p.Contains("/HDRI/") ? 2048 : 1024;
                if (ti.maxTextureSize != max) { ti.maxTextureSize = max; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
        }

        static Sprite SpriteAt(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        static Transform FindDeep(Transform t, string name)
        {
            if (!t) return null;
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r) return r;
            }
            return null;
        }
    }
}
