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
using static RescueSim.EditorTools.BuilderUtil;

namespace RescueSim.EditorTools
{
    /// <summary>Stage 2: a compact earthquake-damaged office building. Procedural geometry plus a few pack props.</summary>
    public static partial class SceneBuilder
    {
        const float QWallH = 3.4f;

        static void BuildEarthquake()
        {
            var def = AssetDatabase.LoadAssetAtPath<MissionDefinition>(QuakeAssetPath);
            var c = BeginStage(def, new Vector3(0, 0, -12f), 0f,
                sky: new Color(0.42f, 0.38f, 0.34f), fog: new Color(0.42f, 0.38f, 0.34f), fogDensity: 0.02f, ambient: new Color(0.3f, 0.25f, 0.21f));
            AddSun(new Color(1f, 0.75f, 0.5f), 0.8f, new Vector3(38f, -25f, 0f));

            var env = new GameObject("Environment").transform;
            var concrete = LitTextured("Mat_QuakeConcrete", new Color(0.68f, 0.67f, 0.64f), ConcreteTexture(), new Vector2(3f, 1.5f), 0.06f);
            var interior = LitTextured("Mat_QuakeInterior", new Color(0.6f, 0.6f, 0.58f), ConcreteTexture(), new Vector2(3f, 1.5f), 0.05f);
            var floorMat = LitTextured("Mat_QuakeFloor", new Color(0.32f, 0.3f, 0.28f), ConcreteTexture(), new Vector2(6f, 6f), 0.12f);
            var ground = LitTextured("Mat_QuakeGround", new Color(0.4f, 0.4f, 0.38f), ConcreteTexture(), new Vector2(10f, 10f), 0.02f);
            var crackMat = Unlit("Mat_Crack", new Color(0.05f, 0.04f, 0.035f));
            var wood = Lit("Mat_QuakeWood", new Color(0.35f, 0.24f, 0.15f), 0.2f);
            var metal = Lit("Mat_QuakeMetal", new Color(0.28f, 0.3f, 0.32f), 0.4f, 0.6f);
            var paper = Lit("Mat_Paper", new Color(0.85f, 0.84f, 0.8f), 0.1f);
            var rebar = Lit("Mat_Rebar", new Color(0.35f, 0.18f, 0.1f), 0.3f, 0.5f);
            var greenMat = Unlit("Mat_QuakeGreen", new Color(0.2f, 0.95f, 0.45f));
            var tentMat = Lit("Mat_Tent", new Color(0.9f, 0.9f, 0.88f), 0.1f);
            var redMat = Unlit("Mat_RedCross", new Color(0.9f, 0.1f, 0.1f));
            var orangeMark = Unlit("Mat_SearchMark", new Color(1f, 0.45f, 0.05f));
            var arrowMat = UnlitTransparent("Mat_ArrowYellow", new Color(1f, 0.85f, 0.15f, 0.95f), ArrowTexture());

            // ---- Ground and floors ----
            Box(env, "Ground", new Vector3(0, -0.1f, 6f), new Vector3(160f, 0.2f, 200f), ground);
            Box(env, "BuildingFloor", new Vector3(0, -0.09f, 15f), new Vector3(18.4f, 0.22f, 18.4f), floorMat);

            // ---- Walls ----
            void Wall(string name, float x0, float z0, float x1, float z1, Material m, float y0 = 0f, float y1 = QWallH)
            {
                bool alongX = Mathf.Abs(x1 - x0) >= Mathf.Abs(z1 - z0);
                float len = alongX ? Mathf.Abs(x1 - x0) : Mathf.Abs(z1 - z0);
                var size = alongX ? new Vector3(len, y1 - y0, 0.3f) : new Vector3(0.3f, y1 - y0, len);
                Box(env, name, new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f), size, m);
            }
            Wall("Front_W", -9f, 6f, -1f, 6f, concrete);
            Wall("Front_E", 1f, 6f, 9f, 6f, concrete);
            Wall("Front_Lintel", -1f, 6f, 1f, 6f, concrete, 2.55f);
            Wall("Wall_West", -9f, 6f, -9f, 24f, concrete);
            Wall("Wall_East", 9f, 6f, 9f, 24f, concrete);
            Wall("Wall_Back", -9f, 24f, 9f, 24f, concrete);
            Wall("Corr_W_1", -2f, 6f, -2f, 11f, interior);
            Wall("Corr_W_2", -2f, 13.4f, -2f, 24f, interior);
            Wall("Corr_W_Lintel", -2f, 11f, -2f, 13.4f, interior, 2.55f);
            Wall("Corr_E_1", 2f, 6f, 2f, 18.5f, interior);
            Wall("Corr_E_2", 2f, 21f, 2f, 24f, interior);
            Wall("Corr_E_Lintel", 2f, 18.5f, 2f, 21f, interior, 2.55f);

            // ---- Ceilings (no colliders; the roof is a visual slab) ----
            void Ceil(string name, float x0, float x1, float z0, float z1) =>
                Box(env, name, new Vector3((x0 + x1) / 2f, QWallH + 0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), interior, false);
            Ceil("Ceil_Corr_A", -2f, 2f, 6f, 8.5f);
            Ceil("Ceil_Corr_B", -2f, 2f, 11.5f, 24f);
            Ceil("Ceil_West_A", -9f, -2f, 6f, 14f);
            Ceil("Ceil_West_B", -9f, -2f, 18f, 24f);
            Ceil("Ceil_East", 2f, 9f, 6f, 24f);

            // ---- Collapsed slab leaning in the west room, rebar and rubble ----
            var slab = Box(env, "FallenSlab", new Vector3(-5.5f, 1.15f, 15.8f), new Vector3(5.4f, 0.28f, 3.4f), interior);
            slab.transform.rotation = Quaternion.Euler(-32f, 4f, 3f);
            for (int i = 0; i < 5; i++)
            {
                var r = Prim(PrimitiveType.Cylinder, "Rebar", env, new Vector3(-7.4f + i * 0.9f, 2.15f, 14.1f), new Vector3(0.03f, 0.55f, 0.03f), rebar);
                r.transform.rotation = Quaternion.Euler(18f + i * 4f, 0f, (i - 2) * 9f);
            }
            var beam = Box(env, "FallenBeam", new Vector3(5.8f, 0.55f, 10.5f), new Vector3(0.45f, 0.4f, 5f), concrete);
            beam.transform.rotation = Quaternion.Euler(0f, 18f, 8f);
            var pillar = Prim(PrimitiveType.Cylinder, "BrokenPillar", env, new Vector3(-3.2f, 0.4f, 9f), new Vector3(0.7f, 0.9f, 0.7f), concrete, true);
            pillar.transform.rotation = Quaternion.Euler(78f, 0f, 20f);

            // Pack rubble props (real meshes) around the building.
            string[] chunks = { "Debris01_1", "Debris01_3", "Debris01_5", "Debris01_7", "Debris01_10", "Debris01_11", "Debris01_2", "Debris01_4" };
            Vector3[] rubble =
            {
                new Vector3(3.2f, 0f, 3.8f), new Vector3(4.4f, 0f, 4.6f), new Vector3(-6.2f, 0f, 4.4f), new Vector3(-7.4f, 0f, 3.2f),
                new Vector3(3.6f, 0f, 8.4f), new Vector3(-4.6f, 0f, 20.5f), new Vector3(6.8f, 0f, 14.2f), new Vector3(0.8f, 0f, 22.8f)
            };
            for (int i = 0; i < rubble.Length; i++) PlaceProp(env, chunks[i % chunks.Length], rubble[i], i * 47f);
            PlaceProp(env, "Barrel01a", new Vector3(-10.5f, 0f, 1.5f), 10f);
            PlaceProp(env, "Barrel01c", new Vector3(11f, 0f, 0.5f), 80f);
            PlaceProp(env, "MetalCabinet01_1", new Vector3(-6.5f, 0.45f, 22.5f), 90f);

            // ---- Office furniture, some overturned ----
            void Table(Vector3 p, float yaw, float roll)
            {
                var root = new GameObject("Table").transform;
                root.SetParent(env, false); root.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, roll));
                Box(root, "Top", new Vector3(0, 0.75f, 0), new Vector3(1.6f, 0.06f, 0.8f), wood);
                for (int i = 0; i < 4; i++) Box(root, "Leg", new Vector3((i % 2 - 0.5f) * 1.4f, 0.37f, (i / 2 - 0.5f) * 0.6f), new Vector3(0.06f, 0.74f, 0.06f), wood, false);
            }
            void Chair(Vector3 p, float yaw, float tiltX)
            {
                var root = new GameObject("Chair").transform;
                root.SetParent(env, false); root.SetPositionAndRotation(p, Quaternion.Euler(tiltX, yaw, 0));
                Box(root, "Seat", new Vector3(0, 0.45f, 0), new Vector3(0.45f, 0.06f, 0.45f), metal, false);
                Box(root, "Back", new Vector3(0, 0.75f, -0.2f), new Vector3(0.45f, 0.55f, 0.05f), metal, false);
                Box(root, "Post", new Vector3(0, 0.22f, 0), new Vector3(0.06f, 0.44f, 0.06f), metal, false);
            }
            Table(new Vector3(-6.5f, 0f, 8.2f), 20f, 0f);
            Table(new Vector3(-4.5f, 0.55f, 9.6f), 75f, 88f); // overturned
            Table(new Vector3(-7.2f, 0f, 19.5f), 100f, 0f);
            Table(new Vector3(5.2f, 0f, 20.5f), 15f, 0f);
            Table(new Vector3(4.0f, 0.5f, 13.4f), 160f, 86f); // overturned
            Chair(new Vector3(-5.6f, 0f, 8.9f), 40f, 0f); Chair(new Vector3(-5.0f, 0.3f, 10.9f), 200f, 85f);
            Chair(new Vector3(-6.4f, 0f, 20.6f), 280f, 0f); Chair(new Vector3(6.6f, 0.3f, 19.4f), 70f, -80f);
            Chair(new Vector3(7.0f, 0f, 15.0f), 130f, 0f);
            var shelf = Box(env, "Shelf_Fallen", new Vector3(7.7f, 0.9f, 22.8f), new Vector3(0.45f, 1.9f, 1.4f), wood);
            shelf.transform.rotation = Quaternion.Euler(0f, 0f, -24f);
            var rng = new System.Random(3);
            for (int i = 0; i < 34; i++)
            {
                var pp = Prim(PrimitiveType.Cube, "Paper", env, Vector3.zero, new Vector3(0.28f, 0.006f, 0.2f), paper);
                bool west = i % 2 == 0;
                pp.transform.position = new Vector3(west ? Mathf.Lerp(-8.5f, -2.6f, (float)rng.NextDouble()) : Mathf.Lerp(2.6f, 8.5f, (float)rng.NextDouble()), 0.04f, Mathf.Lerp(7f, 23f, (float)rng.NextDouble()));
                pp.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            }

            // ---- Cracks and search marks ----
            Crack(env, crackMat, new Vector3(5.2f, 3.3f, 5.84f), Vector3.right, Vector3.back, 1, 7);
            Crack(env, crackMat, new Vector3(-5.5f, 3.3f, 5.84f), Vector3.left, Vector3.back, 2, 6);
            Crack(env, crackMat, new Vector3(-1.84f, 3.3f, 8.0f), Vector3.forward, Vector3.right, 3, 6);
            Crack(env, crackMat, new Vector3(1.84f, 3.3f, 10.0f), Vector3.forward, Vector3.left, 4, 6);
            Crack(env, crackMat, new Vector3(1.84f, 3.3f, 16.5f), Vector3.forward, Vector3.left, 5, 5);
            Crack(env, crackMat, new Vector3(-1.84f, 3.3f, 17.5f), Vector3.forward, Vector3.right, 6, 6);
            Crack(env, crackMat, new Vector3(-8.84f, 3.3f, 12f), Vector3.forward, Vector3.right, 7, 7);
            Crack(env, crackMat, new Vector3(8.84f, 3.3f, 19f), Vector3.back, Vector3.left, 8, 7);
            Crack(env, crackMat, new Vector3(0.0f, 3.3f, 23.84f), Vector3.right, Vector3.back, 9, 7);
            for (int i = 0; i < 2; i++)
            {
                var x = Prim(PrimitiveType.Cube, "SearchMark", env, new Vector3(-3.4f, 1.9f, 5.83f), new Vector3(1.1f, 0.09f, 0.01f), orangeMark);
                x.transform.rotation = Quaternion.Euler(0, 0, i == 0 ? 45f : -45f);
            }

            // ---- Rescue zone (outside) ----
            var zoneCentre = new Vector3(0f, 1.5f, -2.5f);
            var zoneSize = new Vector3(10f, 3f, 6f);
            foreach (var (px, pz, sx, sz) in new[] { (0f, -5.5f, 10f, 0.12f), (0f, 0.5f, 10f, 0.12f), (-5f, -2.5f, 0.12f, 6f), (5f, -2.5f, 0.12f, 6f) })
                Box(env, "ZoneEdge", new Vector3(px, 0.03f, pz), new Vector3(sx, 0.02f, sz), greenMat, false);
            Box(env, "TentCanopy", new Vector3(-2.8f, 2.4f, -2.8f), new Vector3(3.6f, 0.1f, 3f), tentMat, false);
            foreach (var o in new[] { new Vector2(-4.4f, -4.2f), new Vector2(-1.2f, -4.2f), new Vector2(-4.4f, -1.4f), new Vector2(-1.2f, -1.4f) })
                Box(env, "TentPole", new Vector3(o.x, 1.2f, o.y), new Vector3(0.07f, 2.4f, 0.07f), metal, false);
            Box(env, "TentBack", new Vector3(-2.8f, 1.3f, -4.2f), new Vector3(3.6f, 2.2f, 0.05f), tentMat, false);
            Box(env, "CrossV", new Vector3(-2.8f, 1.6f, -4.15f), new Vector3(0.25f, 0.8f, 0.02f), redMat, false);
            Box(env, "CrossH", new Vector3(-2.8f, 1.6f, -4.15f), new Vector3(0.8f, 0.25f, 0.02f), redMat, false);
            Box(env, "Stretcher", new Vector3(-2.8f, 0.45f, -2.5f), new Vector3(0.8f, 0.08f, 1.9f), metal);
            Box(env, "Stretcher2", new Vector3(3f, 0.45f, -3f), new Vector3(0.8f, 0.08f, 1.9f), metal);
            var pole = Prim(PrimitiveType.Cylinder, "FlagPole", env, new Vector3(4.2f, 2f, -4.8f), new Vector3(0.07f, 2f, 0.07f), metal);
            Box(env, "Flag", new Vector3(4.65f, 3.4f, -4.8f), new Vector3(0.9f, 0.55f, 0.02f), greenMat, false);

            // ---- Route arrows ----
            Vector3 f(float x, float z) => new Vector3(x, 0.035f, z);
            ArrowTrail(env, arrowMat, f(0, -8f), f(0, 4.2f), 1.8f);
            ArrowTrail(env, arrowMat, f(0, 7.8f), f(0, 12.8f), 1.6f);
            ArrowTrail(env, arrowMat, f(0, 17.9f), f(0, 19.0f), 1.1f);
            ArrowTrail(env, arrowMat, f(0.9f, 19.75f), f(3.3f, 19.75f), 1.2f);
            ArrowTrail(env, arrowMat, f(-0.9f, 12.2f), f(-3.5f, 12.2f), 1.3f);

            // ---- Navigation (baked before movable debris, from the Environment only) ----
            var surface = env.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            if (surface.navMeshData != null)
            {
                string navPath = ScenesDir + "/EarthquakeRescue_NavMesh.asset";
                AssetDatabase.DeleteAsset(navPath);
                AssetDatabase.CreateAsset(surface.navMeshData, navPath);
                surface.navMeshData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath);
            }
            else Debug.LogError("[SceneBuilder] Earthquake NavMesh bake produced no data.");
            MakeStatic(env);

            // ---- Lighting ----
            var lights = new GameObject("Lights").transform;
            var shaft = new GameObject("CeilingHoleLight").AddComponent<Light>();
            shaft.transform.SetParent(lights, false);
            shaft.transform.SetPositionAndRotation(new Vector3(0f, 3.5f, 10f), Quaternion.Euler(90f, 0f, 0f));
            shaft.type = LightType.Spot; shaft.spotAngle = 50f; shaft.range = 7f; shaft.intensity = 14f; shaft.color = new Color(1f, 0.86f, 0.62f); shaft.shadows = LightShadows.None;
            var patch = Prim(PrimitiveType.Quad, "LightPatch", lights, new Vector3(0, 0.04f, 10f), new Vector3(4.5f, 4.5f, 1f), Particle("Fx_LightPatch", new Color(1f, 0.85f, 0.55f, 0.4f), true));
            patch.transform.rotation = Quaternion.Euler(90, 0, 0);
            var r1 = AddPoint(lights, "EmergencyWest", new Vector3(-5f, 3.0f, 12f), new Color(1f, 0.18f, 0.1f), 4f, 7f); Pulse(r1, LightPulse.Mode.Pulse, 0.6f, 4f, 0.7f);
            var r2 = AddPoint(lights, "EmergencyCorridor", new Vector3(0f, 3.0f, 19.5f), new Color(1f, 0.2f, 0.12f), 4f, 7f); Pulse(r2, LightPulse.Mode.Flicker, 0.3f, 4f, 1.2f, 2f);
            AddPoint(lights, "AmberEast", new Vector3(6f, 3.0f, 16f), new Color(1f, 0.6f, 0.2f), 3f, 7f);
            AddPoint(lights, "Floodlight", new Vector3(-5f, 5f, -3f), new Color(0.95f, 0.95f, 1f), 6f, 15f);
            AddPoint(lights, "SafeLight", new Vector3(0f, 2.2f, -2.5f), new Color(0.3f, 1f, 0.5f), 2.5f, 8f);

            // ---- Atmosphere ----
            var dustMat = Particle("Fx_Dust", new Color(0.8f, 0.7f, 0.55f, 0.35f), false);
            FollowingParticles(c, "DustFollower", new PS
            {
                material = dustMat, lifetime = 9f, speed = 0.08f, size = 0.03f, startSizeMax = 0.09f, rate = 30f, gravity = 0f,
                coneAngle = 25f, coneRadius = 1f, max = 250, color = Fade(new Color(0.85f, 0.75f, 0.6f), 0.5f), loop = true, playOnAwake = true
            }, new Vector3(0, 1.6f, 0), new Vector3(14f, 3.2f, 14f), Vector3.zero);
            MakeParticles(lights, "DebrisDust", new PS
            {
                material = Particle("Fx_DustCloud", new Color(0.75f, 0.65f, 0.5f, 0.22f), false), lifetime = 6f, speed = 0.25f, size = 0.8f, startSizeMax = 1.5f, rate = 3f, gravity = -0.01f,
                coneAngle = 30f, coneRadius = 1.3f, max = 20, color = Fade(new Color(0.75f, 0.65f, 0.5f), 0.3f), loop = true, playOnAwake = true
            }, new Vector3(0, 0.3f, 15.5f), new Vector3(-90, 0, 0));
            Ambient("Rumble", SpatialSound.Cue.Rumble, 0.28f, Vector3.zero, false);
            Ambient("Wind", SpatialSound.Cue.Wind, 0.16f, Vector3.zero, false);
            Ambient("DistantAlarm", SpatialSound.Cue.Alarm, 0.1f, new Vector3(0, 6, 40), true, 10f, 70f);

            // ---- Gameplay ----
            var exit = AddExitZone(c.gameplay, "RescueZone", zoneCentre, zoneSize, "RESCUE ZONE", 1.7f, UIFactory.Good);

            var enter = new GameObject("Zone_Enter").AddComponent<ZoneObjective>();
            enter.transform.SetParent(c.gameplay, false); enter.transform.position = new Vector3(0, 1.5f, 7.8f);
            enter.Configure(c.mission, c.head, ObjectiveIds.Enter, new Vector3(3.6f, 3f, 2.4f), "You are inside. Follow the corridor.");
            var route = new GameObject("Zone_SafeRoute").AddComponent<ZoneObjective>();
            route.transform.SetParent(c.gameplay, false); route.transform.position = new Vector3(0, 1.5f, 12.4f);
            route.Configure(c.mission, c.head, ObjectiveIds.SafeRoute, new Vector3(3.8f, 3f, 1.4f), "Corridor reached. Clear the blocks ahead.", ObjectiveIds.Enter);

            // Lightweight blocks across the corridor.
            var tape = Box(c.gameplay, "HazardTape", new Vector3(0, 1.1f, 14.2f), new Vector3(3.8f, 0.08f, 0.02f), Unlit("Mat_Tape", new Color(1f, 0.8f, 0.05f)), false);
            var blockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/DebrisBlock.prefab");
            var items = new List<DebrisItem>();
            var layout = new[] { (-1.5f, 0.36f), (-0.5f, 0.36f), (0.5f, 0.36f), (1.5f, 0.36f), (-1.0f, 1.08f), (0f, 1.08f), (1.0f, 1.08f), (-0.4f, 1.8f) };
            int n = 0;
            foreach (var (bx, by) in layout)
            {
                var b = (GameObject)PrefabUtility.InstantiatePrefab(blockPrefab, c.gameplay);
                b.name = "Block_" + n++;
                b.transform.localScale = new Vector3(0.7f, 0.68f, 0.7f);
                b.transform.position = new Vector3(bx, by, 15.5f + ((float)rng.NextDouble() - 0.5f) * 0.4f);
                b.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 30f - 15f, 0);
                var di = b.GetComponent<DebrisItem>();
                Val(di, "massKg", 5f);
                items.Add(di);
            }
            var monitorGo = new GameObject("PassageMonitor");
            monitorGo.transform.SetParent(c.gameplay, false);
            monitorGo.transform.position = new Vector3(0f, 1.5f, 15.5f);
            var pm = monitorGo.AddComponent<PassageMonitor>();
            Ref(pm, "mission", c.mission);
            Val(pm, "size", new Vector3(4.2f, 3f, 2.8f));
            Refs(pm, "debris", items);
            Refs(pm, "hideWhenCleared", new List<GameObject> { tape });
            Ref(pm, "sound", Sound(monitorGo, SpatialSound.Cue.None, 0.8f, 2f, 25f));
            SignPost(c.gameplay, "BLOCKED", "Grab the blocks with GRIP and move them aside", new Vector3(0f, 1.75f, 13.4f), new Color(1f, 0.8f, 0.2f));
            SignPost(c.gameplay, "SAFE ROUTE", "Follow the yellow arrows", new Vector3(0f, 2.3f, 4.8f), new Color(1f, 0.85f, 0.2f));

            var survivors = new List<Survivor>
            {
                AddCivilian(c, "Survivor_Amira", "Amira", new Vector3(-6.2f, 0f, 10.4f), 100f, "", null, exit),
                AddCivilian(c, "Survivor_Ben", "Ben", new Vector3(6.4f, 0f, 22.2f), 215f, ObjectiveIds.Route, "Path blocked. Clear the corridor first.", exit),
            };

            // ---- Markers ----
            var markers = new GameObject("ObjectiveMarkers").transform;
            Marker(markers, c.mission, c.head, ObjectiveIds.Enter, "Enter here", new Vector3(0, 3.1f, 3.6f));
            Marker(markers, c.mission, c.head, ObjectiveIds.SafeRoute, "Follow corridor", new Vector3(0, 2.5f, 9.6f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Route, "Clear blocks", new Vector3(0, 2.6f, 14.6f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Locate, "Search room", new Vector3(-4.2f, 2.5f, 12.2f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Locate, "Search room", new Vector3(5.2f, 2.5f, 19.8f));
            Marker(markers, c.mission, c.head, ObjectiveIds.Evacuate, "Rescue zone", new Vector3(0, 3.2f, -1.2f));

            // ---- Colliders for walking / teleporting, and the outer limit ----
            var limits = new GameObject("Boundary (invisible colliders)").transform;
            AddTeleportFloor(limits, new Vector3(0, -0.25f, 6f), new Vector3(44f, 0.5f, 70f));
            AddInvisibleWalls(limits, new Vector3(0, 0, 6f), new Vector2(40f, 66f));

            FinishStage(c, survivors, QuakeScenePath);
        }
    }
}
