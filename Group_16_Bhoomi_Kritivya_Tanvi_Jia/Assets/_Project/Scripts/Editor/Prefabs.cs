using RescueSim.Audio;
using RescueSim.Hazards;
using RescueSim.Interaction;
using RescueSim.Survivors;
using RescueSim.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using static RescueSim.EditorTools.BuilderUtil;

namespace RescueSim.EditorTools
{
    /// <summary>Builds the reusable prefabs (fire, gas leak, survivor, debris block) from primitives, generated materials and the runtime scripts.</summary>
    public static class Prefabs
    {
        public static GameObject Fire()
        {
            var root = new GameObject("Fire");
            var fire = root.AddComponent<FireHazard>();
            var exposure = root.AddComponent<ExposureSource>();
            Ref(exposure, "hazard", fire);
            Val(exposure, "radius", 4f);
            Val(exposure, "ratePerSecond", 0.16f);

            var flameMat = Particle("Fx_Flame", new Color(1f, 1f, 1f, 1f), true);
            var smokeMat = Particle("Fx_Smoke", new Color(0.12f, 0.12f, 0.12f, 0.7f), false);

            var flames = MakeParticles(root.transform, "Flames", new PS
            {
                material = flameMat, lifetime = 0.9f, speed = 1.3f, size = 0.45f, startSizeMax = 0.9f, rate = 28f, gravity = -0.25f,
                coneAngle = 14f, coneRadius = 0.35f, max = 60, color = Flame(), loop = true, playOnAwake = true
            }, new Vector3(0, 0.1f, 0), new Vector3(-90, 0, 0));
            var smoke = MakeParticles(root.transform, "Smoke", new PS
            {
                material = smokeMat, lifetime = 4.5f, speed = 1.1f, size = 0.9f, startSizeMax = 1.6f, rate = 7f, gravity = -0.05f,
                coneAngle = 22f, coneRadius = 0.4f, max = 45, color = Fade(new Color(0.15f, 0.15f, 0.15f), 0.55f), loop = true, playOnAwake = true
            }, new Vector3(0, 0.9f, 0), new Vector3(-90, 0, 0));

            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 0.9f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.52f, 0.18f); l.range = 9f; l.intensity = 5f; l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;

            var crackleGo = new GameObject("Crackle");
            crackleGo.transform.SetParent(root.transform, false);
            var crackle = Sound(crackleGo, SpatialSound.Cue.None, 0.6f, 2f, 22f);

            Ref(fire, "flames", flames); Ref(fire, "smoke", smoke); Ref(fire, "fireLight", l);
            Ref(fire, "exposure", exposure); Ref(fire, "crackle", crackle);
            return SavePrefab(root, "Fire");
        }

        public static GameObject GasLeak()
        {
            var root = new GameObject("GasLeak");
            var gas = root.AddComponent<GasLeakHazard>();
            var exposure = root.AddComponent<ExposureSource>();
            Ref(exposure, "hazard", gas);
            Val(exposure, "radius", 5f);
            Val(exposure, "ratePerSecond", 0.14f);

            var gasMat = Particle("Fx_Gas", new Color(0.7f, 0.85f, 0.3f, 0.55f), false);
            var cloud = MakeParticles(root.transform, "GasCloud", new PS
            {
                material = gasMat, lifetime = 5f, speed = 0.7f, size = 1.4f, startSizeMax = 2.6f, rate = 14f, gravity = -0.02f,
                coneAngle = 38f, coneRadius = 0.15f, max = 70, color = Fade(new Color(0.75f, 0.9f, 0.35f), 0.4f), loop = true, playOnAwake = true
            }, Vector3.zero, new Vector3(-90, 0, 0));

            var hissGo = new GameObject("Hiss");
            hissGo.transform.SetParent(root.transform, false);
            var hiss = Sound(hissGo, SpatialSound.Cue.None, 0.6f, 2f, 24f);

            Ref(gas, "cloud", cloud); Ref(gas, "exposure", exposure); Ref(gas, "hiss", hiss);
            return SavePrefab(root, "GasLeak");
        }

        public static GameObject Survivor()
        {
            var suit = Lit("Mat_SurvivorSuit", new Color(0.85f, 0.38f, 0.12f), 0.15f, 0f, true);
            var skin = Lit("Mat_Skin", new Color(0.78f, 0.58f, 0.45f), 0.2f, 0f, true);
            var helmet = Lit("Mat_Helmet", new Color(0.95f, 0.8f, 0.1f), 0.4f, 0f, true);
            var vest = Lit("Mat_Vest", new Color(0.75f, 0.9f, 0.2f), 0.3f, 0f, true);

            var root = new GameObject("Survivor");
            var vis = new GameObject("Visual");
            vis.transform.SetParent(root.transform, false);
            if (CasualtyModels.Available)
            {
                // The human body (Microsoft Rocketbox avatar) is added per casualty by the scene builders via
                // CasualtyModels.Dress, so every casualty can look different. Colliders stay on the root: one generous grab volume.
                root.AddComponent<CasualtyAppearance>();
            }
            else
            {
                Prim(PrimitiveType.Capsule, "Legs", vis.transform, new Vector3(0, 0.45f, 0), new Vector3(0.28f, 0.45f, 0.24f), suit);
                Prim(PrimitiveType.Capsule, "Torso", vis.transform, new Vector3(0, 1.1f, 0), new Vector3(0.38f, 0.35f, 0.24f), suit);
                Prim(PrimitiveType.Cube, "Vest", vis.transform, new Vector3(0, 1.12f, 0), new Vector3(0.4f, 0.4f, 0.27f), vest);
                Prim(PrimitiveType.Capsule, "ArmL", vis.transform, new Vector3(-0.27f, 1.05f, 0), new Vector3(0.12f, 0.32f, 0.12f), suit);
                Prim(PrimitiveType.Capsule, "ArmR", vis.transform, new Vector3(0.27f, 1.05f, 0), new Vector3(0.12f, 0.32f, 0.12f), suit);
                Prim(PrimitiveType.Sphere, "Head", vis.transform, new Vector3(0, 1.58f, 0), Vector3.one * 0.24f, skin);
                Prim(PrimitiveType.Sphere, "Helmet", vis.transform, new Vector3(0, 1.64f, 0), new Vector3(0.28f, 0.2f, 0.28f), helmet);
            }

            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(0.9f, 1.1f, 2.0f); box.center = new Vector3(0, 0.55f, -0.7f);

            var beaconMat = Unlit("Mat_Beacon", Color.white);
            var beacon = Prim(PrimitiveType.Sphere, "Beacon", root.transform, new Vector3(0, 2.3f, 0), Vector3.one * 0.2f, beaconMat);
            beacon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var canvas = UIFactory.CreateWorldCanvas("Status", new Vector2(420, 170), root.transform, 0.0035f, false);
            canvas.transform.localPosition = new Vector3(0, 2.75f, 0);
            canvas.gameObject.AddComponent<FaceCamera>();
            var label = UIFactory.AddText(canvas.transform, "", 40f, new Vector2(0, -8), new Vector2(400, 100), TextAlignmentOptions.Center, UIFactory.TextMain, FontStyles.Bold);
            var bar = UIFactory.AddBar(canvas.transform, new Vector2(0, -120), new Vector2(300, 22), UIFactory.Accent, "Assess");

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f; agent.height = 1.8f; agent.acceleration = 8f; agent.angularSpeed = 360f;

            var s = root.AddComponent<Survivor>();
            var distress = new GameObject("Distress"); distress.transform.SetParent(root.transform, false);
            var status = new GameObject("StatusSound"); status.transform.SetParent(root.transform, false);
            var dSound = Sound(distress, SpatialSound.Cue.None, 0.8f, 2f, 30f);
            var sSound = Sound(status, SpatialSound.Cue.None, 0.7f, 2f, 20f);

            Ref(s, "visual", vis.transform); Ref(s, "beaconRenderer", beacon.GetComponent<Renderer>());
            Ref(s, "progressRing", bar); Ref(s, "statusLabel", label);
            Ref(s, "distressSound", dSound); Ref(s, "statusSound", sSound);

            root.AddComponent<HoverHighlight>();
            return SavePrefab(root, "Survivor");
        }

        public static GameObject DebrisBlock()
        {
            var mat = Lit("Mat_DebrisConcrete", new Color(0.42f, 0.41f, 0.4f), 0.1f, 0f, true);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DebrisBlock";
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<Rigidbody>();
            go.AddComponent<DebrisItem>();
            go.AddComponent<HoverHighlight>();
            return SavePrefab(go, "DebrisBlock");
        }
    }
}
