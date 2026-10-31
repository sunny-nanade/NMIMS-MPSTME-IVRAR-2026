using System.Collections.Generic;
using RescueSim.Audio;
using RescueSim.Core;
using RescueSim.Interaction;
using RescueSim.Survivors;
using RescueSim.UI;
using RescueSim.World;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using static RescueSim.EditorTools.BuilderUtil;

namespace RescueSim.EditorTools
{
    /// <summary>Stage 3: a compact flooded residential street with a rescue buoy, two stranded civilians and a raised safe zone.</summary>
    public static partial class SceneBuilder
    {
        const float WaterLevel = 0.45f;

        static void BuildFlood()
        {
            var def = AssetDatabase.LoadAssetAtPath<MissionDefinition>(FloodAssetPath);
            var c = BeginStage(def, new Vector3(0, 0, -8f), 0f,
                sky: new Color(0.3f, 0.38f, 0.46f), fog: new Color(0.3f, 0.37f, 0.44f), fogDensity: 0.024f, ambient: new Color(0.26f, 0.31f, 0.38f));
            AddSun(new Color(0.55f, 0.65f, 0.85f), 0.7f, new Vector3(50f, -20f, 0f));

            var env = new GameObject("Environment").transform;
            var ground = Lit("Mat_FloodGround", new Color(0.2f, 0.22f, 0.2f), 0.05f);
            var asphalt = Lit("Mat_Asphalt", new Color(0.13f, 0.14f, 0.15f), 0.35f);
            var curb = Lit("Mat_Sidewalk", new Color(0.4f, 0.4f, 0.4f), 0.1f);
            var mud = Lit("Mat_Mud", new Color(0.26f, 0.2f, 0.14f), 0.05f);
            var roofMat = Lit("Mat_Roof", new Color(0.28f, 0.14f, 0.12f), 0.1f);
            var window = Unlit("Mat_Window", new Color(1f, 0.82f, 0.45f));
            var dark = Lit("Mat_DoorDark", new Color(0.08f, 0.07f, 0.06f), 0.1f);
            var metal = Lit("Mat_FloodMetal", new Color(0.35f, 0.37f, 0.4f), 0.5f, 0.6f);
            var wood = Lit("Mat_FloodWood", new Color(0.4f, 0.3f, 0.2f), 0.2f);
            var railMat = Lit("Mat_Rail", new Color(0.95f, 0.75f, 0.1f), 0.3f);
            var greenMat = Unlit("Mat_FloodGreen", new Color(0.2f, 0.95f, 0.45f));
            var tentMat = Lit("Mat_Tent", new Color(0.9f, 0.9f, 0.88f), 0.1f);
            var redMat = Unlit("Mat_RedCross", new Color(0.9f, 0.1f, 0.1f));
            var water = LitTransparent("Mat_Flood", new Color(0.12f, 0.34f, 0.42f, 0.66f), 0.95f, RippleTexture(), new Vector2(14f, 16f));
            var arrowMat = UnlitTransparent("Mat_ArrowBlue", new Color(0.55f, 0.92f, 1f, 0.95f), ArrowTexture());
            Color[] houseColors = { new Color(0.62f, 0.55f, 0.45f), new Color(0.5f, 0.56f, 0.6f), new Color(0.6f, 0.5f, 0.48f), new Color(0.52f, 0.58f, 0.5f) };

            // ---- Ground, street, sidewalks ----
            Box(env, "Ground", new Vector3(0, -0.1f, 22f), new Vector3(200f, 0.2f, 260f), ground);
            Box(env, "Street", new Vector3(0, 0.02f, 22f), new Vector3(10f, 0.05f, 80f), asphalt);
            Box(env, "Sidewalk_W", new Vector3(-6.5f, 0.075f, 22f), new Vector3(3f, 0.15f, 80f), curb);
            Box(env, "Sidewalk_E", new Vector3(6.5f, 0.075f, 22f), new Vector3(3f, 0.15f, 80f), curb);
            var laneMat = Unlit("Mat_LaneLine", new Color(0.8f, 0.7f, 0.2f));
            for (int i = 0; i < 18; i++) Box(env, "Lane", new Vector3(0, 0.06f, -10f + i * 4.5f), new Vector3(0.15f, 0.01f, 2f), laneMat, false);

            // ---- Houses (4 per side) ----
            float[] hz = { 6f, 19f, 32f, 45f };
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                for (int i = 0; i < hz.Length; i++)
                {
                    var root = new GameObject($"House_{(side == 0 ? "W" : "E")}{i}").transform;
                    root.SetParent(env, false);
                    root.position = new Vector3(sx * 12.5f, 0, hz[i]);
                    var wallMat = Lit("Mat_House" + ((i + side) % 4), houseColors[(i + side) % 4], 0.08f);
                    Box(root, "Body", new Vector3(0, 2.5f, 0), new Vector3(7f, 5f, 9f), wallMat);
                    Box(root, "MudLine", new Vector3(-sx * 3.51f, 0.3f, 0), new Vector3(0.04f, 0.7f, 9f), mud, false);
                    foreach (var rz in new[] { -2.6f, 2.6f })
                    {
                        Box(root, "Window", new Vector3(-sx * 3.52f, 1.9f, rz), new Vector3(0.03f, 1.1f, 1.0f), window, false);
                        Box(root, "WindowUp", new Vector3(-sx * 3.52f, 3.9f, rz), new Vector3(0.03f, 1.1f, 1.0f), window, false);
                    }
                    Box(root, "Door", new Vector3(-sx * 3.52f, 1.1f, 0), new Vector3(0.04f, 2.2f, 1.1f), dark, false);
                    var slabW = Box(root, "RoofA", new Vector3(-1.75f, 5.93f, 0), new Vector3(3.98f, 0.2f, 9.6f), roofMat, false);
                    slabW.transform.rotation = Quaternion.Euler(0, 0, 28f);
                    var slabE = Box(root, "RoofB", new Vector3(1.75f, 5.93f, 0), new Vector3(3.98f, 0.2f, 9.6f), roofMat, false);
                    slabE.transform.rotation = Quaternion.Euler(0, 0, -28f);
                }
            }

            // ---- Porches with steps for the stranded civilians ----
            void Porch(float sx, float z)
            {
                var root = new GameObject("Porch").transform;
                root.SetParent(env, false);
                Box(root, "Deck", new Vector3(sx * 8.0f, 0.375f, z), new Vector3(2.0f, 0.75f, 3.2f), wood);
                Box(root, "Step2", new Vector3(sx * 6.8f, 0.25f, z), new Vector3(0.4f, 0.5f, 3.2f), wood);
                Box(root, "Step1", new Vector3(sx * 6.4f, 0.125f, z), new Vector3(0.4f, 0.25f, 3.2f), wood);
                Box(root, "RoofPost1", new Vector3(sx * 7.2f, 1.9f, z - 1.4f), new Vector3(0.1f, 2.3f, 0.1f), wood, false);
                Box(root, "RoofPost2", new Vector3(sx * 7.2f, 1.9f, z + 1.4f), new Vector3(0.1f, 2.3f, 0.1f), wood, false);
                Box(root, "PorchRoof", new Vector3(sx * 8.0f, 3.1f, z), new Vector3(2.2f, 0.12f, 3.4f), roofMat, false);
            }
            Porch(1f, 19f);
            Porch(-1f, 32f);

            // ---- Cars (half-submerged), cones, lamps, gauge ----
            void Car(Vector3 p, float yaw, Color col)
            {
                var root = new GameObject("Car").transform;
                root.SetParent(env, false); root.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
                var body = Lit("Mat_Car_" + ColorUtility.ToHtmlStringRGB(col), col, 0.6f, 0.3f);
                Box(root, "Body", new Vector3(0, 0.62f, 0), new Vector3(1.8f, 0.75f, 4.3f), body);
                Box(root, "Cabin", new Vector3(0, 1.22f, -0.2f), new Vector3(1.6f, 0.55f, 2.3f), Lit("Mat_CarGlass", new Color(0.1f, 0.14f, 0.18f), 0.9f), false);
                foreach (var o in new[] { new Vector2(-0.95f, 1.3f), new Vector2(0.95f, 1.3f), new Vector2(-0.95f, -1.3f), new Vector2(0.95f, -1.3f) })
                {
                    var w = Prim(PrimitiveType.Cylinder, "Wheel", root, new Vector3(o.x, 0.32f, o.y), new Vector3(0.64f, 0.12f, 0.64f), dark);
                    w.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
            }
            Car(new Vector3(3.2f, 0, 11f), 14f, new Color(0.6f, 0.1f, 0.1f));
            Car(new Vector3(-3.0f, 0, 26.5f), -18f, new Color(0.15f, 0.3f, 0.55f));
            Car(new Vector3(3.3f, 0, 38f), 168f, new Color(0.75f, 0.75f, 0.7f));
            var coneMat = Lit("Mat_Cone", new Color(1f, 0.4f, 0.05f), 0.3f, 0f, true);
            for (int i = 0; i < 10; i++)
            {
                var cone = Prim(PrimitiveType.Cylinder, "Cone", env, new Vector3((i % 2 == 0 ? -4.2f : 4.2f), 0.3f, 2f + i * 4.2f), new Vector3(0.28f, 0.3f, 0.28f), coneMat);
            }
            foreach (var lz in new[] { 6f, 22f, 38f })
                foreach (var lx in new[] { -5.6f, 5.6f })
                {
                    Box(env, "LampPole", new Vector3(lx, 2.6f, lz), new Vector3(0.12f, 5.2f, 0.12f), metal, false);
                    Box(env, "LampHead", new Vector3(lx * 0.8f, 5.15f, lz), new Vector3(0.9f, 0.14f, 0.3f), window, false);
                }
            // Depth gauge post with stripes.
            for (int i = 0; i < 8; i++)
                Box(env, "Gauge", new Vector3(5.2f, 0.15f + i * 0.3f, 4f), new Vector3(0.14f, 0.28f, 0.1f), i % 2 == 0 ? railMat : dark, false);

            // Rescue van at the staging area.
            var van = new GameObject("RescueVan").transform;
            van.SetParent(env, false); van.position = new Vector3(-3.4f, 0, -9.5f); van.rotation = Quaternion.Euler(0, 6f, 0);
            Box(van, "Body", new Vector3(0, 1.2f, 0), new Vector3(2.2f, 2.0f, 5.2f), Lit("Mat_Van", new Color(0.9f, 0.9f, 0.88f), 0.4f, 0.2f));
            Box(van, "Stripe", new Vector3(0, 1.2f, 0), new Vector3(2.22f, 0.4f, 5.22f), Lit("Mat_VanStripe", new Color(1f, 0.45f, 0.1f), 0.3f), false);
            Box(van, "Windshield", new Vector3(0, 1.7f, 2.62f), new Vector3(1.9f, 0.7f, 0.04f), Lit("Mat_CarGlass", new Color(0.1f, 0.14f, 0.18f), 0.9f), false);
            foreach (var o in new[] { new Vector2(-1.1f, 1.7f), new Vector2(1.1f, 1.7f), new Vector2(-1.1f, -1.7f), new Vector2(1.1f, -1.7f) })
            {
                var w = Prim(PrimitiveType.Cylinder, "Wheel", van, new Vector3(o.x, 0.4f, o.y), new Vector3(0.8f, 0.14f, 0.8f), dark);
                w.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }

            // ---- Safe zone: ramp + raised platform ----
            const float rampAngle = 14.04f;
            var ramp = Box(env, "Ramp", new Vector3(0, 0.86f, 47.97f), new Vector3(6f, 0.3f, 8.25f), curb);
            ramp.transform.rotation = Quaternion.Euler(-rampAngle, 0, 0);
            var platform = Box(env, "Platform", new Vector3(0, 1.0f, 57f), new Vector3(12f, 2.0f, 10f), curb);
            var platformEdge = Box(env, "PlatformEdgeStripe", new Vector3(0, 2.005f, 57f), new Vector3(11.2f, 0.01f, 9.2f), Lit("Mat_SafePad", new Color(0.18f, 0.45f, 0.28f), 0.1f), false);
            void Rail(string name, Vector3 centre, Vector3 size, float pitch = 0f)
            {
                var r = Box(env, name, centre, size, railMat);
                r.transform.rotation = Quaternion.Euler(pitch, 0, 0);
            }
            Rail("Rail_PW", new Vector3(-6f, 2.55f, 57f), new Vector3(0.12f, 1.1f, 10f));
            Rail("Rail_PE", new Vector3(6f, 2.55f, 57f), new Vector3(0.12f, 1.1f, 10f));
            Rail("Rail_PN", new Vector3(0f, 2.55f, 62f), new Vector3(12f, 1.1f, 0.12f));
            Rail("Rail_PSW", new Vector3(-4.5f, 2.55f, 52f), new Vector3(3f, 1.1f, 0.12f));
            Rail("Rail_PSE", new Vector3(4.5f, 2.55f, 52f), new Vector3(3f, 1.1f, 0.12f));
            Rail("Rail_RW", new Vector3(-3.05f, 1.42f, 47.97f), new Vector3(0.1f, 1.0f, 8.25f), -rampAngle);
            Rail("Rail_RE", new Vector3(3.05f, 1.42f, 47.97f), new Vector3(0.1f, 1.0f, 8.25f), -rampAngle);
            foreach (var (px, pz, sx, sz) in new[] { (0f, 53.1f, 11f, 0.14f), (0f, 61f, 11f, 0.14f), (-5f, 57f, 0.14f, 8f), (5f, 57f, 0.14f, 8f) })
                Box(env, "SafeEdge", new Vector3(px, 2.03f, pz), new Vector3(sx, 0.02f, sz), greenMat, false);
            Box(env, "TentCanopy", new Vector3(-2.8f, 4.4f, 58.6f), new Vector3(3.6f, 0.1f, 3f), tentMat, false);
            foreach (var o in new[] { new Vector2(-4.4f, 57.2f), new Vector2(-1.2f, 57.2f), new Vector2(-4.4f, 60f), new Vector2(-1.2f, 60f) })
                Box(env, "TentPole", new Vector3(o.x, 3.2f, o.y), new Vector3(0.07f, 2.4f, 0.07f), metal, false);
            Box(env, "CrossV", new Vector3(-2.8f, 3.6f, 61.3f), new Vector3(0.25f, 0.8f, 0.02f), redMat, false);
            Box(env, "CrossH", new Vector3(-2.8f, 3.6f, 61.3f), new Vector3(0.8f, 0.25f, 0.02f), redMat, false);
            Box(env, "TentBack", new Vector3(-2.8f, 3.3f, 61.4f), new Vector3(3.6f, 2.2f, 0.05f), tentMat, false);
            Box(env, "Stretcher", new Vector3(-2.8f, 2.45f, 58.8f), new Vector3(0.8f, 0.08f, 1.9f), metal, false);

            // ---- Water: two scrolling layers, kept apart to avoid z-fighting ----
            var wp = Prim(PrimitiveType.Quad, "Floodwater", env, new Vector3(0, WaterLevel, 22f), new Vector3(220f, 280f, 1f), water);
            wp.transform.rotation = Quaternion.Euler(90, 0, 0);
            wp.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ws = wp.AddComponent<WaterSurface>();
            Ref(ws, "target", wp.GetComponent<Renderer>());
            Val(ws, "bobHeight", 0.02f);

            // ---- Route arrows above the water ----
            Vector3 f(float x, float z) => new Vector3(x, WaterLevel + 0.08f, z);
            ArrowTrail(env, arrowMat, f(0, -6.5f), f(0, 43f), 2.4f, 1.0f);
            ArrowTrail(env, arrowMat, f(0.9f, 19f), f(5.2f, 19f), 1.6f, 0.8f);
            ArrowTrail(env, arrowMat, f(-0.9f, 32f), f(-5.2f, 32f), 1.6f, 0.8f);

            // ---- Navigation ----
            var surface = env.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            if (surface.navMeshData != null)
            {
                string navPath = ScenesDir + "/FloodEvacuation_NavMesh.asset";
                AssetDatabase.DeleteAsset(navPath);
                AssetDatabase.CreateAsset(surface.navMeshData, navPath);
                surface.navMeshData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath);
            }
            else Debug.LogError("[SceneBuilder] Flood NavMesh bake produced no data.");
            foreach (var t in env.GetComponentsInChildren<Transform>(true))
                if (t != wp.transform && t.name != "Floodwater") GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            foreach (var go in new[] { ramp, platform }) go.AddComponent<TeleportationArea>();

            // ---- Lights ----
            var lights = new GameObject("Lights").transform;
            foreach (var lz in new[] { 6f, 22f, 38f })
                AddPoint(lights, "LampLight", new Vector3(-4.6f, 4.9f, lz), new Color(1f, 0.82f, 0.5f), 4f, 10f);
            var b1 = AddPoint(lights, "VanRed", new Vector3(-3.9f, 2.5f, -9.5f), new Color(1f, 0.1f, 0.1f), 5f, 8f); Pulse(b1, LightPulse.Mode.Strobe, 0f, 6f, 1.6f);
            var b2 = AddPoint(lights, "VanBlue", new Vector3(-2.9f, 2.5f, -9.5f), new Color(0.2f, 0.4f, 1f), 5f, 8f); Pulse(b2, LightPulse.Mode.Strobe, 0f, 6f, 1.6f, 0.5f);
            AddPoint(lights, "SafeLight", new Vector3(0f, 3.6f, 57f), new Color(0.3f, 1f, 0.5f), 3f, 10f);
            Box(van, "BeaconL", new Vector3(-0.5f, 2.3f, 1.5f), new Vector3(0.3f, 0.16f, 0.2f), redMat, false);
            Box(van, "BeaconR", new Vector3(0.5f, 2.3f, 1.5f), new Vector3(0.3f, 0.16f, 0.2f), Unlit("Mat_BeaconBlue", new Color(0.2f, 0.4f, 1f)), false);

            // ---- Rain and ambience ----
            var rain = FollowingParticles(c, "RainFollower", new PS
            {
                material = Particle("Fx_Rain", new Color(0.8f, 0.9f, 1f, 0.55f), false), lifetime = 0.8f, speed = 13f, size = 0.015f, startSizeMax = 0.035f, rate = 650f, gravity = 0f,
                coneAngle = 1f, coneRadius = 1f, max = 560, color = Fade(new Color(0.85f, 0.93f, 1f), 0.6f), loop = true, playOnAwake = true
            }, new Vector3(0, 9f, 0), new Vector3(18f, 0.2f, 18f), new Vector3(90f, 0f, 0f));
            var rr = rain.GetComponent<ParticleSystemRenderer>();
            rr.renderMode = ParticleSystemRenderMode.Stretch; rr.velocityScale = 0.04f; rr.lengthScale = 3f;
            Ambient("Rain", SpatialSound.Cue.Rain, 0.45f, Vector3.zero, false);
            Ambient("WaterLap", SpatialSound.Cue.WaterLap, 0.35f, Vector3.zero, false);
            Ambient("Wind", SpatialSound.Cue.Wind, 0.14f, Vector3.zero, false);
            Ambient("DistantSiren", SpatialSound.Cue.Alarm, 0.07f, new Vector3(0, 8, 70), true, 10f, 90f);

            // ---- Gameplay ----
            var exit = AddExitZone(c.gameplay, "SafeZone", new Vector3(0f, 3.5f, 57f), new Vector3(11f, 3f, 9f), "SAFE ZONE", 2.3f, UIFactory.Good);

            var enter = new GameObject("Zone_Enter").AddComponent<ZoneObjective>();
            enter.transform.SetParent(c.gameplay, false); enter.transform.position = new Vector3(0, 1.5f, 2.5f);
            enter.Configure(c.mission, c.head, ObjectiveIds.Enter, new Vector3(12f, 3f, 3f), "You are in the flooded street. Grab the rescue buoy.");
            var check = new GameObject("Zone_Route").AddComponent<ZoneObjective>();
            check.transform.SetParent(c.gameplay, false); check.transform.position = new Vector3(0, 1.5f, 25.5f);
            check.Configure(c.mission, c.head, ObjectiveIds.SafeRoute, new Vector3(12f, 3f, 2f), "Route checkpoint reached. Keep following the arrows.", ObjectiveIds.Enter);

            // Rescue buoy on a post.
            Box(c.gameplay, "BuoyPost", new Vector3(1.8f, 0.52f, -2.0f), new Vector3(0.12f, 1.05f, 0.12f), metal);
            Box(c.gameplay, "BuoyPlate", new Vector3(1.8f, 1.075f, -2.0f), new Vector3(0.5f, 0.05f, 0.5f), metal);
            var buoy = new GameObject("RescueBuoy");
            buoy.transform.SetParent(c.gameplay, false);
            buoy.transform.position = new Vector3(1.8f, 1.17f, -2.0f);
            var mf = buoy.AddComponent<MeshFilter>(); mf.sharedMesh = SaveMesh(TorusMesh(0.26f, 0.075f), "Torus_Buoy");
            var mr = buoy.AddComponent<MeshRenderer>(); mr.sharedMaterial = Lit("Mat_Buoy", new Color(1f, 0.38f, 0.05f), 0.35f, 0f, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                var st = Prim(PrimitiveType.Cube, "Stripe", buoy.transform, new Vector3(Mathf.Cos(a) * 0.26f, 0, Mathf.Sin(a) * 0.26f), new Vector3(0.1f, 0.17f, 0.17f), Lit("Mat_BuoyWhite", new Color(0.95f, 0.95f, 0.95f), 0.3f));
                st.transform.rotation = Quaternion.Euler(0, -i * 90f, 0);
            }
            var bc = buoy.AddComponent<BoxCollider>(); bc.size = new Vector3(0.66f, 0.17f, 0.66f);
            var rb = buoy.AddComponent<Rigidbody>(); rb.mass = 1f;
            buoy.AddComponent<XRGrabInteractable>();
            buoy.AddComponent<HoverHighlight>();
            var pickup = buoy.AddComponent<PickupObjective>();
            pickup.Configure(c.mission, ObjectiveIds.Equip, "Rescue buoy equipped. Now find the civilians.");
            Ref(pickup, "sound", Sound(buoy, SpatialSound.Cue.None, 0.8f, 1.5f, 15f));
            SignPost(c.gameplay, "RESCUE BUOY", "Grab it with GRIP", new Vector3(1.8f, 1.95f, -2.0f), new Color(1f, 0.55f, 0.15f));
            SignPost(c.gameplay, "FLOODWATER", "Knee-deep. Walk, never swim.", new Vector3(0f, 2.3f, -3.5f), new Color(0.5f, 0.9f, 1f));
            SignPost(c.gameplay, "EVACUATION ROUTE", "Follow the blue arrows", new Vector3(0f, 2.3f, 8f), new Color(0.55f, 0.92f, 1f));
            SignPost(c.gameplay, "RAISED SAFE ZONE", "Lead people up the ramp", new Vector3(0f, 3.0f, 41f), new Color(0.3f, 1f, 0.5f));

            var survivors = new List<Survivor>
            {
                AddCivilian(c, "Survivor_Lena", "Lena", new Vector3(8.0f, 0.75f, 19.3f), 90f, ObjectiveIds.Equip, "Grab the rescue buoy first.", exit),
                AddCivilian(c, "Survivor_Omar", "Omar", new Vector3(-8.0f, 0.75f, 32.3f), 270f, ObjectiveIds.Equip, "Grab the rescue buoy first.", exit),
            };

            var markers = new GameObject("ObjectiveMarkers").transform;
            Marker(markers, c.mission, c.head, ObjectiveIds.Enter, "Enter here", new Vector3(0, 3.0f, 3f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Equip, "Rescue buoy", new Vector3(1.8f, 2.55f, -2.0f));
            Marker(markers, c.mission, c.head, ObjectiveIds.SafeRoute, "Follow route", new Vector3(0, 3.0f, 14f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Locate, "Civilian", new Vector3(8f, 4.4f, 19.3f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Locate, "Civilian", new Vector3(-8f, 4.4f, 32.3f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Evacuate, "Safe zone", new Vector3(0, 5.6f, 54f));

            var limits = new GameObject("Boundary (invisible colliders)").transform;
            AddTeleportFloor(limits, new Vector3(0, -0.25f, 22f), new Vector3(60f, 0.5f, 96f));
            AddInvisibleWalls(limits, new Vector3(0, 0, 24f), new Vector2(34f, 84f), 12f);

            FinishStage(c, survivors, FloodScenePath);
        }
    }
}
