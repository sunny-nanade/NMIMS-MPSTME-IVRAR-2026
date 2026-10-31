using System.Linq;
using System.Collections.Generic;
using RescueSim.Audio;
using RescueSim.Core;
using RescueSim.Interaction;
using RescueSim.Triage;
using RescueSim.UI;
using RescueSim.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using static RescueSim.EditorTools.BuilderUtil;

namespace RescueSim.EditorTools
{
    /// <summary>Builds TriageSimulation.unity: the industrial mass-casualty START triage exercise (not part of the campaign).</summary>
    public static partial class SceneBuilder
    {
        public const string TriageScenePath = ScenesDir + "/TriageSimulation.unity";

        // Yard layout (floor at Y=0). Player stands in the middle so every casualty is within the 8 m selection range.
        static readonly Vector3 TriageStart = new Vector3(0f, 0f, 7f);

        // casualty id -> position and yaw. Order matches TriageScenario.Create().
        static readonly Dictionary<string, (Vector3 pos, float yaw)> TriageSpots = new Dictionary<string, (Vector3, float)>
        {
            { "C01", (new Vector3(-7f, 0f, 4f), 20f) },
            { "C02", (new Vector3(7f, 0f, 4f), -30f) },
            { "C03", (new Vector3(0f, 0f, 2f), 180f) },
            { "C04", (new Vector3(-3.5f, 0f, 8.5f), 70f) },
            { "C05", (new Vector3(3.5f, 0f, 8.5f), -60f) },
            { "C06", (new Vector3(-6f, 0f, 11f), 110f) },
            { "C07", (new Vector3(0f, 0f, 12f), 90f) },
            { "C08", (new Vector3(6f, 0f, 11f), 75f) },
            { "C09", (new Vector3(-3f, 0f, 5f), 120f) },
            { "C10", (new Vector3(3f, 0f, 5f), 40f) },
        };

        [MenuItem("Rescue Sim/Build/Triage Scene")]
        public static void BuildTriageOnly()
        {
            ResetCache();
            EnsureDir(ScenesDir); EnsureDir(MaterialsDir);
            BuildTriage();
            RegisterScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] Rebuilt TriageSimulation and registered it in Build Settings.");
        }

        [MenuItem("Rescue Sim/Build/Main Menu Only")]
        public static void BuildMenuOnly()
        {
            ResetCache();
            EnsureDir(ScenesDir); EnsureDir(MaterialsDir);
            BuildMenu();
            RegisterScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] Rebuilt MainMenu (adds the Triage Study button).");
        }

        static void BuildTriage()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetSkybox();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.25f, 0.3f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.12f, 0.12f, 0.14f);
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional; moon.color = new Color(0.6f, 0.68f, 0.9f); moon.intensity = 0.7f;
            moon.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var rig = AddRig(TriageStart, 0f);
            var head = rig.cam.transform;

            // ---- Yard -------------------------------------------------------------------------
            var yard = new GameObject("Yard").transform;
            var groundMat = Lit("Mat_TriageGround", new Color(0.2f, 0.2f, 0.21f), 0.2f);
            var wallMat = Lit("Mat_TriageWall", new Color(0.3f, 0.3f, 0.32f), 0.1f);
            var lineMat = Lit("Mat_TriageLine", new Color(0.95f, 0.8f, 0.15f), 0.2f);
            Prim(PrimitiveType.Cube, "Ground", yard, new Vector3(0, -0.05f, 7f), new Vector3(30f, 0.1f, 26f), groundMat, true);
            AddTeleportFloor(yard, new Vector3(0, 0.02f, 7f), new Vector3(28f, 0.1f, 24f));
            AddInvisibleWalls(yard, new Vector3(0, 0, 7f), new Vector2(28f, 24f));
            Prim(PrimitiveType.Cube, "BackWall", yard, new Vector3(0, 3f, 19.5f), new Vector3(30f, 6f, 0.4f), wallMat);
            Prim(PrimitiveType.Cube, "LeftWall", yard, new Vector3(-14.8f, 3f, 7f), new Vector3(0.4f, 6f, 26f), wallMat);
            Prim(PrimitiveType.Cube, "RightWall", yard, new Vector3(14.8f, 3f, 7f), new Vector3(0.4f, 6f, 26f), wallMat);
            // Painted triage zone edge.
            Prim(PrimitiveType.Cube, "ZoneLineN", yard, new Vector3(0, 0.01f, 14.2f), new Vector3(16f, 0.02f, 0.12f), lineMat);
            Prim(PrimitiveType.Cube, "ZoneLineS", yard, new Vector3(0, 0.01f, 0.8f), new Vector3(16f, 0.02f, 0.12f), lineMat);
            Prim(PrimitiveType.Cube, "ZoneLineW", yard, new Vector3(-8f, 0.01f, 7.5f), new Vector3(0.12f, 0.02f, 13.4f), lineMat);
            Prim(PrimitiveType.Cube, "ZoneLineE", yard, new Vector3(8f, 0.01f, 7.5f), new Vector3(0.12f, 0.02f, 13.4f), lineMat);

            PlaceProp(yard, "Barrel01a", new Vector3(-11f, 0f, 16f), 20f);
            PlaceProp(yard, "Barrel01b", new Vector3(-10.2f, 0f, 16.6f), 70f);
            PlaceProp(yard, "Barrel01c", new Vector3(11f, 0f, 16.5f), 120f);
            PlaceProp(yard, "MetalCabinet01_1", new Vector3(12.5f, 0f, 3f), 270f);
            PlaceProp(yard, "PowerBox01_1", new Vector3(-12.8f, 0f, 2f), 90f);
            PlaceProp(yard, "Debris01_1", new Vector3(-9f, 0f, 14.5f), 40f);
            PlaceProp(yard, "Debris01_2", new Vector3(9.5f, 0f, 14.8f), 200f);

            var sign = new GameObject("Signs").transform;
            SignPost(sign, "TRIAGE YARD", "START - tag every casualty", new Vector3(0, 3.2f, 18.8f), UIFactory.Accent, 0.004f, false);
            SignPost(sign, "ASSEMBLY", "casualty collection point", new Vector3(0, 2.4f, 0.4f), UIFactory.Good, 0.0035f, false);

            // ---- Hazards (cosmetic: no effect on the answer, only on atmosphere and time pressure) ----
            var hazRoot = new GameObject("Hazards").transform;
            var flameMat = Particle("Fx_Flame", new Color(1f, 1f, 1f, 1f), true);
            var smokeMat = Particle("Fx_Smoke", new Color(0.12f, 0.12f, 0.12f, 0.7f), false);
            var smokes = new List<ParticleSystem>(); var lights = new List<Light>();
            Vector3[] fires = { new Vector3(-12f, 0f, 12f), new Vector3(12f, 0f, 9f), new Vector3(-2f, 0f, 17f) };
            for (int i = 0; i < fires.Length; i++)
            {
                var fp = new GameObject("FireSpot_" + (i + 1)).transform;
                fp.SetParent(hazRoot, false); fp.position = fires[i];
                Prim(PrimitiveType.Cylinder, "Drum", fp, new Vector3(0, 0.45f, 0), new Vector3(0.7f, 0.45f, 0.7f), Lit("Mat_TriageDrum", new Color(0.25f, 0.1f, 0.08f), 0.4f, 0.6f));
                MakeParticles(fp, "Flames", new PS
                {
                    material = flameMat, lifetime = 0.9f, speed = 1.3f, size = 0.5f, startSizeMax = 0.8f, rate = 30f, gravity = -0.1f,
                    coneAngle = 14f, coneRadius = 0.25f, max = 60, color = Flame(), loop = true, playOnAwake = true
                }, new Vector3(0, 0.95f, 0), new Vector3(-90, 0, 0));
                smokes.Add(MakeParticles(fp, "Smoke", new PS
                {
                    material = smokeMat, lifetime = 5f, speed = 1.0f, size = 1.0f, startSizeMax = 2.0f, rate = 8f, gravity = -0.05f,
                    coneAngle = 22f, coneRadius = 0.3f, max = 120, color = Fade(new Color(0.15f, 0.15f, 0.15f), 0.6f), loop = true, playOnAwake = true
                }, new Vector3(0, 1.3f, 0), new Vector3(-90, 0, 0)));
                var l = AddPoint(fp, "FireLight", fires[i] + new Vector3(0, 1.4f, 0), new Color(1f, 0.55f, 0.2f), 2.5f, 9f);
                Pulse(l, LightPulse.Mode.Flicker, 1f, 3.5f, 4f, i * 0.9f);
                lights.Add(l);
            }
            var alarm1 = AddPoint(hazRoot, "AlarmLight_A", new Vector3(-12f, 4f, 3f), new Color(1f, 0.1f, 0.08f), 2.5f, 12f);
            Pulse(alarm1, LightPulse.Mode.Pulse, 0.3f, 3f, 0.7f);
            var alarm2 = AddPoint(hazRoot, "AlarmLight_B", new Vector3(12f, 4f, 15f), new Color(1f, 0.1f, 0.08f), 2.5f, 12f);
            Pulse(alarm2, LightPulse.Mode.Pulse, 0.3f, 3f, 0.7f, 1.5f);
            AddPoint(yard, "YardLight_A", new Vector3(0, 4.5f, 5f), new Color(1f, 0.85f, 0.6f), 3f, 18f);
            AddPoint(yard, "YardLight_B", new Vector3(0, 4.5f, 12f), new Color(1f, 0.85f, 0.6f), 3f, 18f);

            // ---- Systems (manager, logger, hazard, desktop input) ----------------------------------
            var sys = new GameObject("TriageSystems");
            var manager = sys.AddComponent<STARTTriageSimulationManager>();
            var logger = sys.AddComponent<TriageTelemetryLogger>();
            var hazard = sys.AddComponent<TriageHazardEscalation>();
            var desktop = sys.AddComponent<TriageDesktopInput>();
            Refs(hazard, "smoke", smokes); Refs(hazard, "fireLights", lights);

            // ---- Casualties -------------------------------------------------------------------------
            var cases = TriageScenario.Create();
            var casualtyRoot = new GameObject("Casualties").transform;
            var profiles = new List<CasualtyProfile>();
            foreach (var c in cases) profiles.Add(BuildCasualty(casualtyRoot, c, manager));

            Refs(manager, "casualties", profiles);
            Ref(manager, "head", head); Ref(manager, "logger", logger); Ref(manager, "hazard", hazard);
            Val(manager, "perfusion", (int)PerfusionCriterion.CapillaryRefill);
            Ref(desktop, "manager", manager);

            // ---- UI ----------------------------------------------------------------------------------
            var ui = BuildTriageUI(rig, head, manager);

            Ambient("AmbientHum", SpatialSound.Cue.AmbientHum, 0.22f, Vector3.zero, false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TriageScenePath);
        }

        static CasualtyProfile BuildCasualty(Transform parent, CasualtyCase c, STARTTriageSimulationManager manager)
        {
            var spot = TriageSpots[c.id];
            bool standing = c.vitals.canWalk;
            var root = new GameObject(c.id);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(spot.pos, Quaternion.Euler(0, spot.yaw, 0));

            var skin = Lit("Mat_CasualtySkin", new Color(0.85f, 0.65f, 0.5f), 0.2f);
            var clothes = Lit("Mat_CasualtyClothes", new Color(0.22f, 0.3f, 0.5f), 0.1f);
            var vest = Lit("Mat_CasualtyVest", new Color(0.9f, 0.45f, 0.1f), 0.3f);
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            Vector3 center, size;
            bool human = CasualtyModels.Available;
            if (human)
            {
                // Rigged human body (visual only). Gameplay uses the root box collider below, never the body mesh.
                int idx = int.TryParse(new string(c.id.Where(char.IsDigit).ToArray()), out var n) ? n - 1 : 0;
                string avatar = CasualtyModels.AvatarFor(null, idx);
                var model = CasualtyModels.AddHuman(body, avatar);
                CasualtyModels.AddAppearance(root, model, avatar);
                if (standing)
                {
                    center = new Vector3(0, 0.9f, 0); size = new Vector3(0.7f, 1.9f, 0.6f);
                }
                else
                {
                    // Lying on their back with the head toward +X (same layout as the former placeholder).
                    body.localRotation = Quaternion.Euler(0, -90, 0) * Quaternion.Euler(-90, 0, 0);
                    body.localPosition = new Vector3(-0.85f, 0.12f, 0f);
                    center = new Vector3(0, 0.2f, 0); size = new Vector3(2.0f, 0.5f, 0.7f);
                }
            }
            else
            {
                if (standing)
                {
                    Prim(PrimitiveType.Capsule, "Torso", body, new Vector3(0, 0.9f, 0), new Vector3(0.45f, 0.9f, 0.3f), clothes);
                    Prim(PrimitiveType.Sphere, "Head", body, new Vector3(0, 1.72f, 0), Vector3.one * 0.22f, skin);
                    Prim(PrimitiveType.Cube, "Vest", body, new Vector3(0, 1.1f, 0), new Vector3(0.48f, 0.4f, 0.34f), vest);
                    center = new Vector3(0, 0.9f, 0); size = new Vector3(0.7f, 1.9f, 0.6f);
                }
                else
                {
                    // Lying on their back: body axis along local X.
                    Prim(PrimitiveType.Capsule, "Torso", body, new Vector3(0, 0.17f, 0), new Vector3(0.34f, 0.8f, 0.3f), clothes).transform.localRotation = Quaternion.Euler(0, 0, 90);
                    Prim(PrimitiveType.Sphere, "Head", body, new Vector3(0.9f, 0.16f, 0), Vector3.one * 0.22f, skin);
                    Prim(PrimitiveType.Cube, "Vest", body, new Vector3(0.3f, 0.2f, 0), new Vector3(0.4f, 0.14f, 0.4f), vest);
                    center = new Vector3(0, 0.2f, 0); size = new Vector3(2.0f, 0.5f, 0.7f);
                }


            }

            var col = root.AddComponent<BoxCollider>(); col.center = center; col.size = size;
            var interactable = root.AddComponent<XRSimpleInteractable>();
            var selectable = root.AddComponent<CasualtySelectable>();
            var profile = root.AddComponent<CasualtyProfile>();
            profile.Configure(c.id, c.vitals.Clone(), c.description);
            selectable.Configure(manager, profile);

            // Triage tag floating above the casualty (colour set at runtime from the assigned category).
            float tagY = standing ? 2.15f : 1.2f;
            var tag = Prim(PrimitiveType.Cube, "TriageTag", root.transform, new Vector3(0, tagY, 0), new Vector3(0.3f, 0.2f, 0.04f), Lit("Mat_TriageTagNeutral", new Color(0.55f, 0.58f, 0.62f), 0.3f));
            var tagRenderer = tag.GetComponent<Renderer>();
            var canvas = UIFactory.CreateWorldCanvas("Label", new Vector2(360, 110), root.transform, 0.004f, false);
            canvas.transform.localPosition = new Vector3(0, tagY + 0.3f, 0);
            canvas.gameObject.AddComponent<FaceCamera>();
            var label = UIFactory.AddText(canvas.transform, c.id, 80f, Vector2.zero, new Vector2(360, 110), TextAlignmentOptions.Center, UIFactory.TextMain, FontStyles.Bold);
            profile.SetVisualRefs(tagRenderer, label);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static TriageUI BuildTriageUI(Rig rig, Transform head, STARTTriageSimulationManager manager)
        {
            var uiRoot = new GameObject("UI").transform;
            var criterion = PerfusionCriterion.CapillaryRefill;

            // ---- Start panel ----
            var sc = UIFactory.CreateWorldCanvas("StartPanel", new Vector2(1200, 880), uiRoot);
            var startPanel = sc.gameObject.AddComponent<VRPanel>();
            Val(startPanel, "distance", 2.2f);
            UIFactory.AddBackground(sc.transform, UIFactory.Panel);
            UIFactory.AddText(sc.transform, "INDUSTRIAL MASS-CASUALTY TRIAGE", 58f, new Vector2(0, -24), new Vector2(1120, 80), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            UIFactory.AddText(sc.transform,
                "Ten casualties, one factory yard on fire. Walk to each casualty, point and press TRIGGER to select, run the START checks (walk, breathing, airway, respiratory rate, perfusion, commands), then assign RED, YELLOW, GREEN or BLACK.\n\n" +
                "Tags are final. Your time per casualty and every decision is logged for the study.",
                30f, new Vector2(0, -120), new Vector2(1080, 270), TextAlignmentOptions.TopLeft, UIFactory.TextMain);
            var protocolText = UIFactory.AddText(sc.transform, "", 24f, new Vector2(0, -395), new Vector2(1080, 44), TextAlignmentOptions.Center, UIFactory.TextDim);
            var participantBtn = UIFactory.AddButton(sc.transform, "Participant", new Vector2(-300, -450), new Vector2(520, 80), null, UIFactory.Button, 30f);
            var participantLabel = UIFactory.AddText(sc.transform, "Participant: TEST", 28f, new Vector2(300, -470), new Vector2(520, 44), TextAlignmentOptions.Center, UIFactory.TextDim, FontStyles.Bold);
            var provenanceBtn = UIFactory.AddButton(sc.transform, "Data type", new Vector2(-300, -540), new Vector2(520, 80), null, UIFactory.Button, 30f);
            var provenanceLabel = UIFactory.AddText(sc.transform, "Data: SoftwareTest", 28f, new Vector2(300, -560), new Vector2(520, 44), TextAlignmentOptions.Center, UIFactory.TextDim, FontStyles.Bold);
            var feedbackToggle = UIFactory.AddToggle(sc.transform, "Show correct / incorrect after each tag", new Vector2(0, -630), new Vector2(760, 70), true, null);
            var beginBtn = UIFactory.AddButton(sc.transform, "Begin Exercise", new Vector2(0, -750), new Vector2(560, 90), null, UIFactory.AccentDim, 42f);
            var startMessage = UIFactory.AddText(sc.transform, "", 26f, new Vector2(0, -706), new Vector2(1120, 36), TextAlignmentOptions.Center, new Color(1f, 0.8f, 0.3f), FontStyles.Bold);

            // ---- Assess panel ----
            var ac = UIFactory.CreateWorldCanvas("AssessPanel", new Vector2(1400, 940), uiRoot);
            var assessPanel = ac.gameObject.AddComponent<VRPanel>();
            Val(assessPanel, "distance", 2.0f);
            UIFactory.AddBackground(ac.transform, UIFactory.Panel);
            var header = UIFactory.AddText(ac.transform, "Select a casualty", 52f, new Vector2(0, -16), new Vector2(1320, 70), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            var description = UIFactory.AddText(ac.transform, "", 30f, new Vector2(0, -88), new Vector2(1320, 70), TextAlignmentOptions.Center, UIFactory.TextMain);
            var examButtons = new List<Button>(); var examResults = new List<TMP_Text>();
            var kinds = (ExamKind[])System.Enum.GetValues(typeof(ExamKind));
            for (int i = 0; i < kinds.Length; i++)
            {
                float y = -170f - i * 76f;
                var b = UIFactory.AddButton(ac.transform, STARTEvaluator.Label(kinds[i], criterion), new Vector2(-440, y), new Vector2(480, 66), null, UIFactory.Button, 28f);
                var r = UIFactory.AddText(ac.transform, "", 28f, new Vector2(280, y - 6), new Vector2(800, 56), TextAlignmentOptions.Left, UIFactory.TextMain);
                examButtons.Add(b); examResults.Add(r);
            }
            var tagButtons = new List<Button>();
            string[] names = { "GREEN", "YELLOW", "RED", "BLACK" };
            Color[] cols = { new Color(0.1f, 0.55f, 0.2f), new Color(0.75f, 0.62f, 0.05f), new Color(0.75f, 0.1f, 0.08f), new Color(0.04f, 0.04f, 0.04f) };
            for (int i = 0; i < 4; i++)
                tagButtons.Add(UIFactory.AddButton(ac.transform, names[i], new Vector2(-495 + i * 330, -640), new Vector2(300, 96), null, cols[i], 38f));
            var feedback = UIFactory.AddText(ac.transform, "", 30f, new Vector2(0, -752), new Vector2(1320, 100), TextAlignmentOptions.Center, UIFactory.TextMain, FontStyles.Bold);
            var progress = UIFactory.AddText(ac.transform, "", 26f, new Vector2(0, -870), new Vector2(1320, 44), TextAlignmentOptions.Center, UIFactory.TextDim);

            // ---- Results panel ----
            var rc = UIFactory.CreateWorldCanvas("ResultsPanel", new Vector2(1300, 900), uiRoot);
            var resultsPanel = rc.gameObject.AddComponent<VRPanel>();
            Val(resultsPanel, "distance", 2.1f);
            UIFactory.AddBackground(rc.transform, UIFactory.Panel);
            UIFactory.AddText(rc.transform, "EXERCISE COMPLETE", 60f, new Vector2(0, -24), new Vector2(1220, 80), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            var summary = UIFactory.AddText(rc.transform, "", 30f, new Vector2(0, -120), new Vector2(1200, 520), TextAlignmentOptions.TopLeft, UIFactory.TextMain);
            var pathText = UIFactory.AddText(rc.transform, "", 22f, new Vector2(0, -660), new Vector2(1200, 90), TextAlignmentOptions.Center, UIFactory.TextDim);
            var restart = UIFactory.AddButton(rc.transform, "Restart", new Vector2(-300, -780), new Vector2(480, 90), null, UIFactory.AccentDim, 38f);
            var menu = UIFactory.AddButton(rc.transform, "Main Menu", new Vector2(300, -780), new Vector2(480, 90), null, UIFactory.Button, 38f);

            var ui = rig.transitions.gameObject.AddComponent<TriageUI>();
            Ref(ui, "manager", manager); Ref(ui, "head", head); Ref(ui, "transitions", rig.transitions);
            Ref(ui, "startPanel", startPanel); Ref(ui, "assessPanel", assessPanel); Ref(ui, "resultsPanel", resultsPanel);
            Ref(ui, "beginButton", beginBtn); Ref(ui, "participantButton", participantBtn); Ref(ui, "provenanceButton", provenanceBtn);
            Ref(ui, "participantLabel", participantLabel); Ref(ui, "provenanceLabel", provenanceLabel); Ref(ui, "startMessage", startMessage);
            Ref(ui, "feedbackToggle", feedbackToggle); Ref(ui, "protocolText", protocolText);
            Ref(ui, "headerText", header); Ref(ui, "descriptionText", description); Ref(ui, "feedbackText", feedback); Ref(ui, "progressText", progress);
            Refs(ui, "examButtons", examButtons); Refs(ui, "examResults", examResults); Refs(ui, "tagButtons", tagButtons);
            Ref(ui, "summaryText", summary); Ref(ui, "pathText", pathText);
            Ref(ui, "restartButton", restart); Ref(ui, "menuButton", menu);
            return ui;
        }
    }
}
