using System.Collections.Generic;
using RescueSim.Audio;
using RescueSim.Core;
using RescueSim.Interaction;
using RescueSim.Player;
using RescueSim.Survivors;
using RescueSim.UI;
using RescueSim.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using static RescueSim.EditorTools.BuilderUtil;

namespace RescueSim.EditorTools
{
    /// <summary>Campaign-level pieces of the builder: stage mission data, the main menu, and the UI shared by all three stages.</summary>
    public static partial class SceneBuilder
    {
        // ---- Mission data for the new stages ---------------------------------------------------

        static MissionDefinition LoadOrCreate(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);
            if (m == null) { m = ScriptableObject.CreateInstance<MissionDefinition>(); AssetDatabase.CreateAsset(m, path); }
            return m;
        }

        static MissionDefinition CreateQuakeMission()
        {
            var m = LoadOrCreate(QuakeAssetPath);
            m.missionId = "earthquake-search-rescue";
            m.stageIndex = 1; m.sceneName = "EarthquakeRescue"; m.usesExposure = false;
            m.accent = new Color(1f, 0.78f, 0.15f);
            m.title = "Earthquake Search and Rescue";
            m.tagline = "An earthquake has cracked a small building. Two people are still inside.";
            m.cardBlurb = "Follow the safe route, clear the fallen debris and guide two survivors to the rescue zone.";
            m.briefing =
                "An earthquake has damaged a small office building. A corridor is blocked by rubble and two people are waiting for help.\n\n" +
                "1. Follow the yellow arrows into the building.\n" +
                "2. Walk down the corridor to the blocked section.\n" +
                "3. Grab the fallen blocks with GRIP and move them out of the corridor.\n" +
                "4. Find both survivors. Hold GRIP on each one to check them, keep holding to lift them.\n" +
                "5. Carry each one to the green RESCUE ZONE and release GRIP there.\n\n" +
                "There is no timer pressure: take your time and follow the glowing markers.";
            m.timeLimitSeconds = 900f;
            m.exposureRecoveryPerSecond = 0.1f;
            m.objectives = new List<MissionDefinition.ObjectiveDefinition>
            {
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Enter, title = "Enter the damaged building", hint = "Follow the yellow arrows to the door." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.SafeRoute, title = "Follow the safe route", hint = "Stay on the marked corridor." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Route, title = "Clear the blocked corridor", hint = "Grab the blocks and move them aside." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Locate, title = "Find both survivors", hint = "Look in the side rooms." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Evacuate, title = "Carry them to the rescue zone", hint = "Keep GRIP held to carry. Release it inside the green zone." },
            };
            m.scoring = ScoreWeights.Default;
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        static MissionDefinition CreateFloodMission()
        {
            var m = LoadOrCreate(FloodAssetPath);
            m.missionId = "flash-flood-evacuation";
            m.stageIndex = 2; m.sceneName = "FloodEvacuation"; m.usesExposure = false;
            m.accent = new Color(0.25f, 0.7f, 1f);
            m.title = "Flash Flood Evacuation";
            m.tagline = "Floodwater has filled a residential street. Two neighbours need help.";
            m.cardBlurb = "Wade along the marked route, grab the rescue buoy and escort two civilians to higher ground.";
            m.briefing =
                "Floodwater has entered a residential street. The water is knee-deep and rising: you only need to walk, never swim.\n\n" +
                "1. Wade into the street and grab the orange RESCUE BUOY with GRIP.\n" +
                "2. Follow the blue arrows along the evacuation route.\n" +
                "3. Find both civilians on the porches.\n" +
                "4. Hold GRIP on each person to check them, keep holding to lift them.\n" +
                "5. Carry each one up the ramp to the raised SAFE ZONE and release GRIP there.\n\n" +
                "There is no timer pressure: follow the glowing markers.";
            m.timeLimitSeconds = 900f;
            m.exposureRecoveryPerSecond = 0.1f;
            m.objectives = new List<MissionDefinition.ObjectiveDefinition>
            {
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Enter, title = "Enter the flooded street", hint = "Wade forward from the staging area." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Equip, title = "Grab the rescue buoy", hint = "It is on the post by the start." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.SafeRoute, title = "Follow the evacuation route", hint = "Follow the blue arrows." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Locate, title = "Find both civilians", hint = "Check the porches." },
                new MissionDefinition.ObjectiveDefinition { id = ObjectiveIds.Evacuate, title = "Carry them to the safe zone", hint = "Keep GRIP held. Release it inside the raised zone." },
            };
            m.scoring = ScoreWeights.Default;
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        // ---- Shared stage setup ----------------------------------------------------------------

        sealed class StageCore
        {
            public UnityEngine.SceneManagement.Scene scene;
            public Rig rig; public Transform head, gameplay;
            public PlayerVitals vitals; public MissionController mission; public MissionDefinition def;
        }

        /// <summary>Creates an empty scene with the XR rig, vitals, mission controller, toast and wrist HUD.</summary>
        static StageCore BeginStage(MissionDefinition def, Vector3 start, float yaw, Color sky, Color fog, float fogDensity, Color ambient)
        {
            if (def == null) throw new System.InvalidOperationException("[SceneBuilder] Stage mission asset missing - cannot build stage.");
            // NewScene(Single) unloads unused assets, which destroys an asset object loaded beforehand - reload it afterwards.
            var defPath = AssetDatabase.GetAssetPath(def);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            def = AssetDatabase.LoadAssetAtPath<MissionDefinition>(defPath);
            if (def == null) throw new System.InvalidOperationException("[SceneBuilder] Could not reload mission asset " + defPath);
            var core = new StageCore { def = def, scene = scene };
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog; RenderSettings.fogDensity = fogDensity;

            core.rig = AddRig(start, yaw);
            core.head = core.rig.cam.transform;
            core.rig.cam.clearFlags = CameraClearFlags.SolidColor;
            core.rig.cam.backgroundColor = sky;
            core.gameplay = new GameObject("Gameplay").transform;

            var vitalsGo = new GameObject("PlayerVitals");
            vitalsGo.transform.SetParent(core.rig.root.transform, false);
            core.vitals = vitalsGo.AddComponent<PlayerVitals>();
            Ref(core.vitals, "head", core.head);
            var feedback = vitalsGo.AddComponent<ExposureFeedback>();
            var coughGo = new GameObject("Cough"); coughGo.transform.SetParent(core.head, false);
            var cough = Sound(coughGo, SpatialSound.Cue.None, 0.7f, 1f, 6f);
            Val(cough, "spatialBlend", 0f);
            Ref(feedback, "vitals", core.vitals); Ref(feedback, "cough", cough);
            Ref(core.rig.overlay, "vitals", core.vitals);

            var missionGo = new GameObject("MissionController");
            core.mission = missionGo.AddComponent<MissionController>();
            Ref(core.mission, "mission", def); Ref(core.mission, "vitals", core.vitals);
            return core;
        }

        /// <summary>UI, toast, wrist HUD (after survivors are registered). Call last.</summary>
        static void FinishStage(StageCore c, List<Survivor> survivors, string scenePath)
        {
            Refs(c.mission, "hazards", new List<RescueSim.Hazards.Hazard>());
            Refs(c.mission, "survivors", survivors);
            BuildMissionUI(c.rig, c.mission, c.head);
            BuildWristHud(c.rig, c.mission, c.vitals, c.head, c.def.usesExposure);
            BuildToast(c.mission, c.head);
            EditorSceneManager.MarkSceneDirty(c.scene);
            EditorSceneManager.SaveScene(c.scene, scenePath);
        }

        /// <summary>Invisible walkable floor that also lets the player teleport anywhere on it.</summary>
        static void AddTeleportFloor(Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject("TeleportFloor");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            go.AddComponent<TeleportationArea>();
        }

        static void AddInvisibleWalls(Transform parent, Vector3 center, Vector2 size, float height = 8f)
        {
            void Box(string name, Vector3 c, Vector3 s)
            {
                var g = new GameObject(name); g.transform.SetParent(parent, false);
                g.transform.position = c; g.AddComponent<BoxCollider>().size = s;
            }
            Box("WallW", center + new Vector3(-size.x / 2f, height / 2f, 0), new Vector3(0.5f, height, size.y));
            Box("WallE", center + new Vector3(size.x / 2f, height / 2f, 0), new Vector3(0.5f, height, size.y));
            Box("WallS", center + new Vector3(0, height / 2f, -size.y / 2f), new Vector3(size.x, height, 0.5f));
            Box("WallN", center + new Vector3(0, height / 2f, size.y / 2f), new Vector3(size.x, height, 0.5f));
        }

        static Survivor AddCivilian(StageCore c, string objName, string display, Vector3 pos, float yaw, string requires, string blockedMessage, ExitZone exit)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/Survivor.prefab");
            var s = PlaceSurvivor(prefab, c.gameplay, objName, display, pos, yaw, requires, c.mission, c.vitals, c.head, exit);
            if (!string.IsNullOrEmpty(blockedMessage)) Val(s, "blockedMessage", blockedMessage);
            return s;
        }

        static ExitZone AddExitZone(Transform parent, string name, Vector3 center, Vector3 size, string signText, float signHeight, Color signColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var exit = go.AddComponent<ExitZone>();
            exit.Size = size;
            // Visible boundary: translucent floor pad plus edge strips (child renderers also drive the "release to deliver" glow).
            var padMat = Unlit("Mat_SafeZonePad", new Color(0.2f, 0.9f, 0.4f, 1f));
            float fy = -size.y * 0.5f + 0.03f;
            Prim(PrimitiveType.Cube, "ZoneEdgeN", go.transform, new Vector3(0, fy, size.z * 0.5f), new Vector3(size.x, 0.04f, 0.12f), padMat);
            Prim(PrimitiveType.Cube, "ZoneEdgeS", go.transform, new Vector3(0, fy, -size.z * 0.5f), new Vector3(size.x, 0.04f, 0.12f), padMat);
            Prim(PrimitiveType.Cube, "ZoneEdgeE", go.transform, new Vector3(size.x * 0.5f, fy, 0), new Vector3(0.12f, 0.04f, size.z), padMat);
            Prim(PrimitiveType.Cube, "ZoneEdgeW", go.transform, new Vector3(-size.x * 0.5f, fy, 0), new Vector3(0.12f, 0.04f, size.z), padMat);
            var zl = new GameObject("SafeLight").AddComponent<Light>();
            zl.transform.SetParent(go.transform, false); zl.transform.localPosition = new Vector3(0, fy + 0.8f, 0);
            zl.type = LightType.Point; zl.color = new Color(0.3f, 1f, 0.5f); zl.intensity = 1.5f; zl.range = 7f; zl.shadows = LightShadows.None;
            var c = UIFactory.CreateWorldCanvas("Sign", new Vector2(1000, 200), go.transform, 0.0028f, false);
            c.transform.localPosition = new Vector3(0, signHeight, 0);
            c.gameObject.AddComponent<FaceCamera>();
            UIFactory.AddText(c.transform, signText, 100f, new Vector2(0, 0), new Vector2(980, 130), TextAlignmentOptions.Center, signColor, FontStyles.Bold);
            return exit;
        }

        static void SignPost(Transform parent, string text, string sub, Vector3 pos, Color color, float scale = 0.0016f, bool face = true)
        {
            var c = UIFactory.CreateWorldCanvas("Sign_" + text, new Vector2(560, 150), parent, scale, false);
            c.transform.position = pos;
            if (face) c.gameObject.AddComponent<FaceCamera>();
            UIFactory.AddBackground(c.transform, new Color(0.04f, 0.05f, 0.07f, 0.78f));
            UIFactory.AddText(c.transform, text, 50f, new Vector2(0, -12), new Vector2(540, 70), TextAlignmentOptions.Center, color, FontStyles.Bold);
            if (!string.IsNullOrEmpty(sub))
                UIFactory.AddText(c.transform, sub, 28f, new Vector2(0, -84), new Vector2(540, 56), TextAlignmentOptions.Center, UIFactory.TextDim);
        }

        /// <summary>Row of floor arrows (yaw in degrees, 0 = +Z) from a to b, every <paramref name="spacing"/> metres.</summary>
        static void ArrowTrail(Transform parent, Material mat, Vector3 a, Vector3 b, float spacing, float size = 0.9f)
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 0.01f) return;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int n = Mathf.Max(1, Mathf.FloorToInt(len / spacing));
            for (int i = 0; i <= n; i++)
            {
                var q = Prim(PrimitiveType.Quad, "Arrow", parent, Vector3.zero, new Vector3(size, size, 1f), mat);
                q.transform.position = a + d * (i / (float)n);
                q.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
                q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static Light AddPoint(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            return l;
        }

        static void Pulse(Light l, LightPulse.Mode mode, float min, float max, float speed, float phase = 0f)
        {
            var p = l.gameObject.AddComponent<LightPulse>();
            Val(p, "mode", (int)mode); Val(p, "minIntensity", min); Val(p, "maxIntensity", max); Val(p, "speed", speed); Val(p, "phase", phase);
        }

        static Light AddSun(Color color, float intensity, Vector3 euler)
        {
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional; l.color = color; l.intensity = intensity; l.shadows = LightShadows.None;
            RenderSettings.sun = l;
            return l;
        }

        /// <summary>Particle volume that stays centred on the player (dust, rain).</summary>
        static ParticleSystem FollowingParticles(StageCore c, string name, PS d, Vector3 offset, Vector3 boxSize, Vector3 euler)
        {
            var root = new GameObject(name);
            var ps = MakeParticles(root.transform, "Particles", d, Vector3.zero, euler);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box; shape.scale = boxSize;
            var f = root.AddComponent<FollowHead>();
            f.Configure(c.head, offset);
            return ps;
        }

        static GameObject MakeStatic(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            return root.gameObject;
        }

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider = true)
            => Prim(PrimitiveType.Cube, name, parent, center, size, mat, collider);

        static void Crack(Transform parent, Material mat, Vector3 start, Vector3 along, Vector3 normal, int seed, int segments = 6)
        {
            var rng = new System.Random(seed);
            Vector3 p = start + normal * 0.012f;
            Vector3 up = Vector3.up;
            Vector3 dir = (along * 0.4f - up).normalized;
            for (int i = 0; i < segments; i++)
            {
                float turn = ((float)rng.NextDouble() - 0.5f) * 80f;
                dir = Quaternion.AngleAxis(turn, normal) * dir;
                if (Vector3.Dot(dir, -up) < 0.2f) dir = Quaternion.AngleAxis(turn > 0 ? -50f : 50f, normal) * dir;
                float len = 0.35f + (float)rng.NextDouble() * 0.45f;
                var seg = Prim(PrimitiveType.Cube, "Crack", parent, Vector3.zero, new Vector3(0.035f * (1f - i / (float)segments * 0.6f) + 0.01f, len, 0.012f), mat);
                seg.transform.position = p + dir * (len * 0.5f);
                seg.transform.rotation = Quaternion.LookRotation(normal, dir);
                p += dir * len;
            }
        }

        // ---- Main menu -------------------------------------------------------------------------

        static void BuildMenu()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var missions = new[]
            {
                AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionAssetPath),
                AssetDatabase.LoadAssetAtPath<MissionDefinition>(QuakeAssetPath),
                AssetDatabase.LoadAssetAtPath<MissionDefinition>(FloodAssetPath)
            };
            SetSkybox();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.16f);

            var rig = AddRig(Vector3.zero, 0f);

            var floorMat = Lit("Mat_MenuFloor", new Color(0.13f, 0.13f, 0.14f), 0.35f);
            var wallMat = Lit("Mat_MenuWall", new Color(0.2f, 0.2f, 0.22f), 0.1f);
            var stage = new GameObject("Stage").transform;
            Prim(PrimitiveType.Cube, "Floor", stage, new Vector3(0, -0.05f, 4f), new Vector3(24f, 0.1f, 24f), floorMat, true);
            Prim(PrimitiveType.Cube, "BackWall", stage, new Vector3(0, 3f, 9f), new Vector3(24f, 6f, 0.3f), wallMat);
            Prim(PrimitiveType.Cube, "LeftWall", stage, new Vector3(-8f, 3f, 4f), new Vector3(0.3f, 6f, 24f), wallMat);
            Prim(PrimitiveType.Cube, "RightWall", stage, new Vector3(8f, 3f, 4f), new Vector3(0.3f, 6f, 24f), wallMat);
            PlaceProp(stage, "Barrel01a", new Vector3(-5.2f, 0f, 7.5f), 20f);
            PlaceProp(stage, "Barrel01b", new Vector3(-4.4f, 0f, 8.0f), 70f);
            PlaceProp(stage, "Barrel01c", new Vector3(5.4f, 0f, 7.8f), 120f);
            PlaceProp(stage, "MetalCabinet01_1", new Vector3(6.6f, 0f, 8.0f), 190f);
            PlaceProp(stage, "PowerBox01_1", new Vector3(-6.4f, 0f, 8.2f), 180f);

            var warm = AddPoint(stage, "KeyLight", new Vector3(0, 3.6f, 3.5f), new Color(1f, 0.72f, 0.45f), 4f, 14f);
            var red = AddPoint(stage, "AlarmLight", new Vector3(0, 2.6f, 8f), new Color(1f, 0.15f, 0.1f), 2.5f, 9f);
            Pulse(red, LightPulse.Mode.Pulse, 0.5f, 3f, 0.6f);

            var titleCanvas = UIFactory.CreateWorldCanvas("Title", new Vector2(1800, 260), stage, 0.004f, false);
            titleCanvas.transform.position = new Vector3(0, 6.2f, 8.8f);
            UIFactory.AddText(titleCanvas.transform, "DISASTER RESPONSE VR", 150f, new Vector2(0, -6), new Vector2(1760, 190), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            UIFactory.AddText(titleCanvas.transform, "Fire  -  Earthquake  -  Flood", 60f, new Vector2(0, -190), new Vector2(1760, 70), TextAlignmentOptions.Center, UIFactory.TextDim);

            var uiRoot = new GameObject("UI").transform;

            // ---- Main panel (1500 x 900) ----
            var main = UIFactory.CreateWorldCanvas("MainPanel", new Vector2(1500, 900), uiRoot);
            var mainPanel = main.gameObject.AddComponent<VRPanel>();
            Val(mainPanel, "distance", 2.3f);
            Val(mainPanel, "heightOffset", 0.05f);
            UIFactory.AddBackground(main.transform, UIFactory.Panel);
            UIFactory.AddText(main.transform, "DISASTER RESPONSE VR", 78f, new Vector2(0, -18), new Vector2(1400, 100), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            UIFactory.AddText(main.transform, "Join the response team. Choose a disaster zone.", 30f, new Vector2(0, -118), new Vector2(1400, 40), TextAlignmentOptions.Center, UIFactory.TextDim);
            var progressText = UIFactory.AddText(main.transform, "", 28f, new Vector2(0, -164), new Vector2(1400, 38), TextAlignmentOptions.Center, UIFactory.TextMain, FontStyles.Bold);
            var progressFill = UIFactory.AddBar(main.transform, new Vector2(0, -206), new Vector2(900, 16), UIFactory.Accent, "Progress");

            var cards = new List<StageCardView>();
            float[] xs = { -480f, 0f, 480f };
            for (int i = 0; i < 3; i++)
            {
                var m = missions[i];
                var card = UIFactory.AddButton(main.transform, "", new Vector2(xs[i], -240), new Vector2(440, 440), null, new Color(0.14f, 0.17f, 0.23f, 1f), 30f);
                card.name = "Card_" + (i + 1);
                var lbl = card.transform.Find("Label"); if (lbl != null) Object.DestroyImmediate(lbl.gameObject);
                var bg = card.GetComponent<Image>();
                var stripe = UIFactory.NewUI<Image>("Stripe", card.transform);
                stripe.color = m != null ? m.accent : UIFactory.Accent; stripe.raycastTarget = false;
                var sr = stripe.rectTransform; sr.anchorMin = new Vector2(0, 1); sr.anchorMax = new Vector2(1, 1); sr.pivot = new Vector2(0.5f, 1); sr.sizeDelta = new Vector2(0, 14); sr.anchoredPosition = Vector2.zero;
                UIFactory.AddText(card.transform, $"STAGE {i + 1}", 28f, new Vector2(0, -28), new Vector2(400, 36), TextAlignmentOptions.Center, UIFactory.TextDim, FontStyles.Bold);
                UIFactory.AddText(card.transform, m != null ? m.title.ToUpperInvariant() : "STAGE", 40f, new Vector2(0, -66), new Vector2(400, 110), TextAlignmentOptions.Center, m != null ? m.accent : UIFactory.Accent, FontStyles.Bold);
                UIFactory.AddText(card.transform, m != null ? m.cardBlurb : "", 25f, new Vector2(0, -184), new Vector2(390, 140), TextAlignmentOptions.Top, UIFactory.TextMain);
                var stars = UIFactory.AddStars(card.transform, new Vector2(0, -330), 58f);
                var status = UIFactory.AddText(card.transform, "", 24f, new Vector2(0, -400), new Vector2(420, 34), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
                var lockGo = UIFactory.NewUI<Image>("Lock", card.transform);
                lockGo.color = new Color(0.02f, 0.02f, 0.03f, 0.7f); lockGo.raycastTarget = false;
                UIFactory.Stretch(lockGo.rectTransform);
                UIFactory.AddText(lockGo.transform, "LOCKED", 64f, new Vector2(0, -250), new Vector2(420, 90), TextAlignmentOptions.Center, UIFactory.TextDim, FontStyles.Bold);
                lockGo.transform.SetAsLastSibling();
                status.transform.SetAsLastSibling();
                lockGo.transform.SetSiblingIndex(card.transform.childCount - 2);
                cards.Add(new StageCardView { mission = m, button = card, status = status, stars = stars, lockOverlay = lockGo.gameObject, background = bg });
            }

            var message = UIFactory.AddText(main.transform, "", 28f, new Vector2(0, -706), new Vector2(1400, 40), TextAlignmentOptions.Center, new Color(1f, 0.8f, 0.3f), FontStyles.Bold);
            var help = UIFactory.AddButton(main.transform, "Controls", new Vector2(-590, -762), new Vector2(240, 84), null, UIFactory.Button, 32f);
            var settingsBtn = UIFactory.AddButton(main.transform, "Settings", new Vector2(-330, -762), new Vector2(240, 84), null, UIFactory.Button, 32f);
            var demo = UIFactory.AddToggle(main.transform, "Demo: unlock all stages", new Vector2(20, -762), new Vector2(420, 84), CampaignProgress.UnlockAll, null);
            var reset = UIFactory.AddButton(main.transform, "Reset progress", new Vector2(370, -762), new Vector2(240, 84), null, UIFactory.Button, 28f);
            var triage = UIFactory.AddButton(main.transform, "Triage Study", new Vector2(585, -22), new Vector2(300, 64), null, UIFactory.AccentDim, 28f);
            var quit = UIFactory.AddButton(main.transform, "Quit", new Vector2(610, -762), new Vector2(200, 84), null, UIFactory.Button, 32f);

            // ---- Help panel ----
            var helpCanvas = UIFactory.CreateWorldCanvas("HelpPanel", new Vector2(1000, 780), uiRoot);
            var helpPanel = helpCanvas.gameObject.AddComponent<VRPanel>();
            UIFactory.AddBackground(helpCanvas.transform, UIFactory.Panel);
            UIFactory.AddText(helpCanvas.transform, "CONTROLS", 56f, new Vector2(0, -26), new Vector2(900, 70), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            UIFactory.AddText(helpCanvas.transform,
                "<b>POINT + TRIGGER</b>  presses menu buttons.\n" +
                "<b>GRIP</b>  grabs objects (extinguisher, debris, buoy) and works the breaker. Hold GRIP on a person to lift and carry them (release GRIP in the green zone to deliver); hold GRIP on a valve to turn it.\n" +
                "<b>TRIGGER</b>  while holding the extinguisher sprays it.\n" +
                "<b>LEFT STICK</b> walk.   <b>RIGHT STICK</b> turn.\n" +
                "<b>PUSH STICK FORWARD AND RELEASE</b> to teleport.\n" +
                "<b>MENU BUTTON</b> (left controller) pauses.\n" +
                "<b>RAISE YOUR LEFT WRIST</b> to read your checklist.\n\n" +
                "Follow the glowing markers and floor arrows. Nothing here needs fast reflexes or swimming.",
                30f, new Vector2(0, -110), new Vector2(920, 520), TextAlignmentOptions.TopLeft, UIFactory.TextMain);
            var helpBack = UIFactory.AddButton(helpCanvas.transform, "Back", new Vector2(0, -660), new Vector2(360, 84), null, UIFactory.AccentDim, 36f);

            var sc = UIFactory.CreateWorldCanvas("SettingsPanel", new Vector2(1000, 760), uiRoot);
            var settings = SettingsPanel.Build(sc);

            var menu = rig.transitions.gameObject.AddComponent<MainMenuController>();
            Ref(menu, "head", rig.cam.transform); Ref(menu, "transitions", rig.transitions);
            Ref(menu, "mainPanel", mainPanel); Ref(menu, "settingsPanel", settings); Ref(menu, "helpPanel", helpPanel);
            Ref(menu, "helpButton", help); Ref(menu, "helpBackButton", helpBack); Ref(menu, "settingsButton", settingsBtn);
            Ref(menu, "quitButton", quit); Ref(menu, "triageButton", triage); Ref(menu, "resetButton", reset); Ref(menu, "demoToggle", demo);
            Ref(menu, "progressText", progressText); Ref(menu, "progressFill", progressFill); Ref(menu, "messageText", message);
            var so = new SerializedObject(menu);
            var list = so.FindProperty("cards");
            list.arraySize = cards.Count;
            for (int i = 0; i < cards.Count; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("mission").objectReferenceValue = cards[i].mission;
                e.FindPropertyRelative("button").objectReferenceValue = cards[i].button;
                e.FindPropertyRelative("status").objectReferenceValue = cards[i].status;
                e.FindPropertyRelative("stars").objectReferenceValue = cards[i].stars;
                e.FindPropertyRelative("lockOverlay").objectReferenceValue = cards[i].lockOverlay;
                e.FindPropertyRelative("background").objectReferenceValue = cards[i].background;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            Ambient("AmbientHum", SpatialSound.Cue.AmbientHum, 0.22f, Vector3.zero, false);
            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        // ---- Mission UI shared by all stages ---------------------------------------------------

        static MissionUI BuildMissionUI(Rig rig, MissionController mission, Transform head)
        {
            var uiRoot = new GameObject("UI").transform;

            var briefing = UIFactory.CreateWorldCanvas("BriefingPanel", new Vector2(1100, 900), uiRoot);
            var briefingPanel = briefing.gameObject.AddComponent<VRPanel>();
            UIFactory.AddBackground(briefing.transform, UIFactory.Panel);
            var bStage = UIFactory.AddText(briefing.transform, "", 30f, new Vector2(0, -26), new Vector2(1000, 40), TextAlignmentOptions.Center, UIFactory.TextDim, FontStyles.Bold);
            var bTitle = UIFactory.AddText(briefing.transform, "", 54f, new Vector2(0, -66), new Vector2(1020, 80), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            bTitle.enableAutoSizing = true; bTitle.fontSizeMin = 30f; bTitle.fontSizeMax = 54f; bTitle.textWrappingMode = TextWrappingModes.NoWrap;
            var bBody = UIFactory.AddText(briefing.transform, "", 30f, new Vector2(0, -160), new Vector2(980, 540), TextAlignmentOptions.TopLeft, UIFactory.TextMain);
            UIFactory.AddText(briefing.transform, "TRIGGER presses buttons   -   GRIP grabs, carries, delivers   -   Left wrist shows the checklist", 24f, new Vector2(0, -700), new Vector2(1000, 40), TextAlignmentOptions.Center, UIFactory.TextDim);
            var begin = UIFactory.AddButton(briefing.transform, "Begin Mission", new Vector2(0, -770), new Vector2(560, 100), null, UIFactory.AccentDim, 42f);

            var pause = UIFactory.CreateWorldCanvas("PausePanel", new Vector2(800, 640), uiRoot);
            var pausePanel = pause.gameObject.AddComponent<VRPanel>();
            UIFactory.AddBackground(pause.transform, UIFactory.Panel);
            UIFactory.AddText(pause.transform, "PAUSED", 64f, new Vector2(0, -40), new Vector2(700, 90), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            var resume = UIFactory.AddButton(pause.transform, "Resume", new Vector2(0, -160), new Vector2(520, 90), null, UIFactory.AccentDim, 40f);
            var pSettings = UIFactory.AddButton(pause.transform, "Settings", new Vector2(0, -270), new Vector2(520, 90), null, UIFactory.Button, 38f);
            var pRestart = UIFactory.AddButton(pause.transform, "Restart Stage", new Vector2(0, -380), new Vector2(520, 90), null, UIFactory.Button, 38f);
            var pMenu = UIFactory.AddButton(pause.transform, "Main Menu", new Vector2(0, -490), new Vector2(520, 90), null, UIFactory.Button, 38f);

            var settingsCanvas = UIFactory.CreateWorldCanvas("SettingsPanel", new Vector2(1000, 760), uiRoot);
            var settings = SettingsPanel.Build(settingsCanvas);

            var results = UIFactory.CreateWorldCanvas("ResultsPanel", new Vector2(1100, 960), uiRoot);
            var resultsPanel = results.gameObject.AddComponent<VRPanel>();
            UIFactory.AddBackground(results.transform, UIFactory.Panel);
            var rTitle = UIFactory.AddText(results.transform, "", 64f, new Vector2(0, -28), new Vector2(1000, 90), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            var rSub = UIFactory.AddText(results.transform, "", 32f, new Vector2(0, -112), new Vector2(1000, 44), TextAlignmentOptions.Center, UIFactory.TextDim);
            var rStars = UIFactory.AddStars(results.transform, new Vector2(0, -170), 110f, 20f);
            var rScore = UIFactory.AddText(results.transform, "", 68f, new Vector2(0, -296), new Vector2(1000, 90), TextAlignmentOptions.Center, UIFactory.TextMain, FontStyles.Normal);
            var rBody = UIFactory.AddText(results.transform, "", 32f, new Vector2(0, -400), new Vector2(900, 330), TextAlignmentOptions.TopLeft, UIFactory.TextMain);
            var retry = UIFactory.AddButton(results.transform, "Retry", new Vector2(-370, -790), new Vector2(320, 100), null, UIFactory.Button, 38f);
            var next = UIFactory.AddButton(results.transform, "Next Stage", new Vector2(0, -790), new Vector2(380, 100), null, UIFactory.AccentDim, 38f);
            var rMenu = UIFactory.AddButton(results.transform, "Main Menu", new Vector2(370, -790), new Vector2(320, 100), null, UIFactory.Button, 38f);
            var nextLabel = next.transform.Find("Label").GetComponent<TMP_Text>();

            var camp = UIFactory.CreateWorldCanvas("CampaignPanel", new Vector2(1100, 900), uiRoot);
            var campPanel = camp.gameObject.AddComponent<VRPanel>();
            UIFactory.AddBackground(camp.transform, UIFactory.Panel);
            UIFactory.AddText(camp.transform, "CAMPAIGN COMPLETE", 66f, new Vector2(0, -30), new Vector2(1000, 90), TextAlignmentOptions.Center, UIFactory.Good, FontStyles.Bold);
            UIFactory.AddText(camp.transform, "Operation successful. Fire, earthquake and flood: every zone responded to.", 30f, new Vector2(0, -120), new Vector2(940, 80), TextAlignmentOptions.Center, UIFactory.TextMain);
            var cStars = UIFactory.AddStars(camp.transform, new Vector2(0, -210), 110f, 20f);
            var cBody = UIFactory.AddText(camp.transform, "", 34f, new Vector2(0, -350), new Vector2(940, 330), TextAlignmentOptions.Top, UIFactory.TextMain);
            var cMenu = UIFactory.AddButton(camp.transform, "Main Menu", new Vector2(-280, -740), new Vector2(480, 100), null, UIFactory.AccentDim, 40f);
            var cReplay = UIFactory.AddButton(camp.transform, "Replay Stage", new Vector2(280, -740), new Vector2(480, 100), null, UIFactory.Button, 40f);

            var sys = rig.transitions.gameObject;
            var mui = sys.AddComponent<MissionUI>();
            Ref(mui, "mission", mission); Ref(mui, "head", head); Ref(mui, "transitions", rig.transitions);
            Ref(mui, "briefingPanel", briefingPanel); Ref(mui, "pausePanel", pausePanel); Ref(mui, "settingsPanel", settings);
            Ref(mui, "resultsPanel", resultsPanel); Ref(mui, "campaignPanel", campPanel);
            Ref(mui, "briefingStage", bStage); Ref(mui, "briefingTitle", bTitle); Ref(mui, "briefingBody", bBody); Ref(mui, "beginButton", begin);
            Ref(mui, "resumeButton", resume); Ref(mui, "pauseSettingsButton", pSettings); Ref(mui, "pauseRestartButton", pRestart); Ref(mui, "pauseMenuButton", pMenu);
            Ref(mui, "resultsTitle", rTitle); Ref(mui, "resultsSubtitle", rSub); Ref(mui, "resultsScore", rScore); Ref(mui, "resultsBody", rBody);
            Ref(mui, "resultsStars", rStars); Ref(mui, "replayButton", retry); Ref(mui, "nextButton", next); Ref(mui, "nextButtonLabel", nextLabel);
            Ref(mui, "resultsMenuButton", rMenu);
            Ref(mui, "campaignBody", cBody); Ref(mui, "campaignStars", cStars); Ref(mui, "campaignMenuButton", cMenu); Ref(mui, "campaignReplayButton", cReplay);

            var pi = sys.AddComponent<PauseInput>();
            pi.Configure(mui);
            return mui;
        }
    }
}
