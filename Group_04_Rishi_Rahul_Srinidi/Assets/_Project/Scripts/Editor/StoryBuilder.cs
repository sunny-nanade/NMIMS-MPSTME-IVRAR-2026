using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSER;
using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CSER.EditorTools
{
    /// Story-mode scenes: main menu rooftop, Chapter 1 (SOC), Chapter 3 (server room), plus the shared
    /// per-scene setup (rig, UI, narrator, guide, fader, post, probes, bake). Chapter 2 lives in EscapeRoomBuilder.cs.
    public static partial class EscapeRoomBuilder
    {
        static readonly string[] SceneOrder = { "MainMenu", "Ch1_NightShift", "Ch2_FredsOffice", "Ch3_ServerRoom" };
        static Vector3 roomExtents = new Vector3(HalfX, Height, HalfZ);

        [MenuItem("CSER/Build All Scenes (story mode)", priority = 0)]
        public static void BuildAll()
        {
            ConfigureTextures();
            BuildMenuScene(true);
            BuildNightShift(true);
            BuildOffice(true);
            BuildServerRoom(true);
            var old = Root + "/Scenes/EscapeRoom.unity";
            if (File.Exists(old)) AssetDatabase.DeleteAsset(old);
            UpdateBuildSettings();
            EditorSceneManager.OpenScene(ScenePathOf("MainMenu"));
            Debug.Log("[CSER] All story scenes built.");
        }

        [MenuItem("CSER/Build Scene/Main Menu", priority = 10)] static void M0() { BuildMenuScene(true); UpdateBuildSettings(); }
        [MenuItem("CSER/Build Scene/Chapter 1 - Night Shift", priority = 11)] static void M1() { BuildNightShift(true); UpdateBuildSettings(); }
        [MenuItem("CSER/Build Scene/Chapter 2 - Fred's Office", priority = 12)] static void M2() { BuildOffice(true); UpdateBuildSettings(); }
        [MenuItem("CSER/Build Scene/Chapter 3 - Server Room", priority = 13)] static void M3() { BuildServerRoom(true); UpdateBuildSettings(); }

        static string ScenePathOf(string name) => $"{Root}/Scenes/{name}.unity";

        static void UpdateBuildSettings()
        {
            EditorBuildSettings.scenes = SceneOrder.Where(n => File.Exists(ScenePathOf(n)))
                .Select(n => new EditorBuildSettingsScene(ScenePathOf(n), true)).ToArray();
        }

        static Scene NewScene()
        {
            log.Clear();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            env = new GameObject("Environment").transform;
            props = new GameObject("Props").transform;
            ui = new GameObject("UI").transform;
            roomExtents = new Vector3(HalfX, Height, HalfZ);
            return scene;
        }

        static void FinishScene(Scene scene, string name, Transform head, bool bake)
        {
            PostProcessing(head);
            ProbesFor(roomExtents);
            MarkStatic(env);
            Directory.CreateDirectory(Root + "/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePathOf(name));
            AssetDatabase.SaveAssets();
            Debug.Log($"[CSER] Built {name}.\n" + string.Join("\n", log));
            if (bake) BakeNow();
        }

        static void ProbesFor(Vector3 ext)
        {
            var lp = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
            lp.transform.SetParent(env);
            var pts = new List<Vector3>();
            for (float x = -ext.x + 0.3f; x <= ext.x - 0.3f; x += 0.9f)
            for (float z = -ext.z + 0.3f; z <= ext.z - 0.3f; z += 0.9f)
            foreach (var y in new[] { 0.3f, 1.0f, 1.7f, ext.y - 0.3f })
                pts.Add(new Vector3(x, y, z));
            lp.probePositions = pts.ToArray();
            var rp = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            rp.transform.SetParent(env);
            rp.transform.position = new Vector3(0, ext.y * 0.5f, 0);
            rp.mode = ReflectionProbeMode.Baked;
            rp.boxProjection = true;
            rp.size = new Vector3(ext.x * 2, ext.y, ext.z * 2);
            rp.resolution = 256;
        }

        static ChapterDirector.Step Step(TaskId task, string label, string drop, params string[] targets)
        {
            return new ChapterDirector.Step
            {
                task = task,
                label = label,
                targets = targets.Select(n => GameObject.Find(n)).Where(g => g).Select(g => g.transform).ToArray(),
                dropTarget = drop != null && GameObject.Find(drop) ? GameObject.Find(drop).transform : null,
            };
        }

        /// Card, menu, HUD, wrist panel, guide beacon and fader: identical in every chapter.
        static void CommonUI(GameObject game, Transform head, Vector3 cardPos, float cardYaw)
        {
            var gameUI = game.GetComponent<GameUI>();
            gameUI.head = head;

            var cardRoot = new GameObject("CardRoot").transform;
            cardRoot.SetParent(ui);
            cardRoot.SetPositionAndRotation(cardPos, Quaternion.Euler(0, cardYaw, 0));
            var card = MakeCanvas("CardCanvas", cardRoot, Vector3.zero, Quaternion.identity, new Vector2(1100, 760), 0.0012f);
            AddCanvasInteraction(card.gameObject, cardRoot, poke: false);
            gameUI.cardRoot = cardRoot;
            gameUI.cardCanvas = card;

            var menuRoot = new GameObject("MenuRoot").transform;
            menuRoot.SetParent(ui);
            menuRoot.position = cardPos + Vector3.down * 0.2f;
            var menu = MakeCanvas("MenuCanvas", menuRoot, Vector3.zero, Quaternion.identity, new Vector2(1000, 700), 0.00092f);
            AddCanvasInteraction(menu.gameObject, menuRoot, poke: true);
            gameUI.menuRoot = menuRoot;
            gameUI.menuCanvas = menu;

            var invRoot = new GameObject("InventoryRoot").transform;
            invRoot.SetParent(ui);
            invRoot.position = cardPos + Vector3.down * 0.2f;
            var invCanvas = MakeCanvas("InventoryCanvas", invRoot, Vector3.zero, Quaternion.identity, new Vector2(1000, 640), 0.00092f);
            AddCanvasInteraction(invCanvas.gameObject, invRoot, poke: true);
            var inv = game.AddComponent<Inventory>();
            inv.head = head;
            inv.root = invRoot;
            inv.canvas = invCanvas;

            var hubRoot = new GameObject("HubRoot").transform;
            hubRoot.SetParent(ui);
            hubRoot.position = cardPos + Vector3.down * 0.2f;
            var hubCanvas = MakeCanvas("HubCanvas", hubRoot, Vector3.zero, Quaternion.identity, new Vector2(1000, 700), 0.00092f);
            AddCanvasInteraction(hubCanvas.gameObject, hubRoot, poke: true);
            var hub = game.AddComponent<Hub>();
            hub.head = head;
            hub.root = hubRoot;
            hub.canvas = hubCanvas;

            var hudRoot = new GameObject("HudRoot").transform;
            hudRoot.SetParent(ui);
            hudRoot.position = cardPos;
            var follow = hudRoot.gameObject.AddComponent<LazyFollow>();
            follow.offset = new Vector3(0, -0.2f, 1.15f);
            follow.target = head;
            // always-on-top objective / notification popup: large text, queued, ray + poke dismiss button
            var popupRoot = new GameObject("PopupRoot").transform;
            popupRoot.SetParent(ui);
            popupRoot.position = cardPos;
            var popupCanvas = MakeCanvas("PopupCanvas", popupRoot, Vector3.zero, Quaternion.identity, new Vector2(1000, 720), 0.0014f);
            AddCanvasInteraction(popupCanvas.gameObject, popupRoot, poke: true);
            var popup = popupRoot.gameObject.AddComponent<PriorityPopup>();
            popup.head = head;
            popup.canvas = popupCanvas;

            var hud = MakeCanvas("HudCanvas", hudRoot, Vector3.zero, Quaternion.identity, new Vector2(1000, 640), 0.0014f);
            foreach (var g in hud.GetComponentsInChildren<GraphicRaycaster>()) Object.DestroyImmediate(g);
            hud.GetComponent<Canvas>().sortingOrder = 10;
            gameUI.hudCanvas = hud;

            // wrist panel on the left hand anchor (tracks the controller or the tracked hand)
            var leftAnchor = FindDeep(head.root, "LeftHandAnchor");
            if (leftAnchor)
            {
                var wristRoot = new GameObject("WristPanel").transform;
                wristRoot.SetParent(leftAnchor, false);
                wristRoot.localPosition = new Vector3(0.0f, 0.05f, -0.04f);
                wristRoot.localRotation = Quaternion.Euler(55, 0, 0);
                var wrist = MakeCanvas("WristCanvas", wristRoot, Vector3.zero, Quaternion.identity, new Vector2(540, 300), 0.00065f);
                AddCanvasInteraction(wrist.gameObject, wristRoot, poke: true);
                gameUI.wristCanvas = wrist;
            }

            var beacon = new GameObject("GuideBeacon").AddComponent<GuideBeacon>();
            beacon.transform.SetParent(ui);
            beacon.head = head;

            var fader = head.gameObject.AddComponent<Fader>();
            fader.material = FadeMaterial();
        }

        static Material FadeMaterial()
        {
            var path = MatDir + "/Fade.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", Color.black);
            MakeTransparent(m);
            m.renderQueue = 4000;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void Hotspots(params Vector3[] spots)
        {
            var parent = new GameObject("TeleportHotspots").transform;
            int i = 0;
            foreach (var pos in spots)
            {
                var go = new GameObject("Spot_" + i++);
                go.transform.SetParent(parent);
                go.transform.position = pos;
                try { QuickActionsAPI.AddTeleportInteraction(go, TeleportSurfaceType.Hotspot, TeleportHotspotSnap.SnapPosition); }
                catch (Exception e) { log.Add("Hotspot failed: " + e.Message); }
            }
            TeleportFloor(parent, roomExtents);
        }

        /// Free teleport: the whole floor (inset from the walls) is one target area with no snapping, so the player
        /// can go anywhere in the room, not only to the hotspots. The collider is a trigger on Ignore Raycast so it
        /// never blocks grabs, UI rays or the player.
        static void TeleportFloor(Transform parent, Vector3 ext)
        {
            var go = new GameObject("TeleportFloor") { layer = 2 };
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(0, 0.005f, 0);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(ext.x * 2 - 0.6f, 0.01f, ext.z * 2 - 0.6f);
            try { QuickActionsAPI.AddTeleportInteraction(go, TeleportSurfaceType.Hotspot, TeleportHotspotSnap.None); }
            catch (Exception e) { log.Add("Teleport floor failed: " + e.Message); }
        }

        /// Box room shell with a doorway in the +X wall (door hinged at doorZ0) and a bright corridor behind it.
        static Hinge Shell(Vector3 ext, Material wall, Material floor, Material ceiling, float doorZ0, float doorZ1, Material doorMat)
        {
            roomExtents = ext;
            float hx = ext.x, h = ext.y, hz = ext.z, dh = 2.1f;
            Box("Floor", env, new Vector3(0, -0.05f, 0), new Vector3(hx * 2, 0.1f, hz * 2), floor);
            Box("Ceiling", env, new Vector3(0, h + 0.05f, 0), new Vector3(hx * 2, 0.1f, hz * 2), ceiling);
            Box("Wall_Back", env, new Vector3(0, h / 2, hz + 0.05f), new Vector3(hx * 2 + 0.2f, h, 0.1f), wall);
            Box("Wall_Front", env, new Vector3(0, h / 2, -hz - 0.05f), new Vector3(hx * 2 + 0.2f, h, 0.1f), wall);
            Box("Wall_Left", env, new Vector3(-hx - 0.05f, h / 2, 0), new Vector3(0.1f, h, hz * 2), wall);
            float rx = hx + 0.05f;
            Box("Wall_Right_A", env, new Vector3(rx, h / 2, (doorZ1 + hz) / 2), new Vector3(0.1f, h, hz - doorZ1), wall);
            Box("Wall_Right_B", env, new Vector3(rx, h / 2, (doorZ0 - hz) / 2), new Vector3(0.1f, h, doorZ0 + hz), wall);
            Box("Wall_Right_Lintel", env, new Vector3(rx, (dh + h) / 2, (doorZ0 + doorZ1) / 2), new Vector3(0.1f, h - dh, doorZ1 - doorZ0), wall);
            var trim = Mat("DoorFrameDark", new Color(0.2f, 0.21f, 0.23f), 0.5f);
            Box("DoorFrame_Top", env, new Vector3(hx - 0.01f, dh + 0.03f, (doorZ0 + doorZ1) / 2), new Vector3(0.14f, 0.06f, doorZ1 - doorZ0 + 0.12f), trim);
            Box("DoorFrame_L", env, new Vector3(hx - 0.01f, dh / 2, doorZ0 - 0.03f), new Vector3(0.14f, dh, 0.06f), trim);
            Box("DoorFrame_R", env, new Vector3(hx - 0.01f, dh / 2, doorZ1 + 0.03f), new Vector3(0.14f, dh, 0.06f), trim);
            var glow = Mat("CorridorGlow", Color.white, 0.2f, 0, new Color(1.6f, 1.6f, 1.5f));
            float cz = (doorZ0 + doorZ1) / 2;
            Box("Corridor_Floor", env, new Vector3(hx + 1.1f, -0.05f, cz), new Vector3(2.1f, 0.1f, 1.6f), glow);
            Box("Corridor_End", env, new Vector3(hx + 2.2f, 1.4f, cz), new Vector3(0.1f, 2.8f, 1.6f), glow);
            Box("Corridor_SideA", env, new Vector3(hx + 1.1f, 1.4f, cz - 0.75f), new Vector3(2.1f, 2.8f, 0.1f), glow);
            Box("Corridor_SideB", env, new Vector3(hx + 1.1f, 1.4f, cz + 0.75f), new Vector3(2.1f, 2.8f, 0.1f), glow);
            Box("Corridor_Top", env, new Vector3(hx + 1.1f, 2.85f, cz), new Vector3(2.1f, 0.1f, 1.6f), glow);
            var skirt = Mat("Skirting", new Color(0.18f, 0.18f, 0.19f), 0.35f);
            Box("Skirting_Back", env, new Vector3(0, 0.05f, hz - 0.008f), new Vector3(hx * 2, 0.1f, 0.016f), skirt, false);
            Box("Skirting_Front", env, new Vector3(0, 0.05f, -hz + 0.008f), new Vector3(hx * 2, 0.1f, 0.016f), skirt, false);
            Box("Skirting_Left", env, new Vector3(-hx + 0.008f, 0.05f, 0), new Vector3(0.016f, 0.1f, hz * 2), skirt, false);

            var hingeT = new GameObject("DoorHinge").transform;
            hingeT.SetParent(env);
            hingeT.position = new Vector3(hx + 0.02f, 0, doorZ0);
            LocalBox("Door", hingeT, new Vector3(0, dh / 2, (doorZ1 - doorZ0) / 2), new Vector3(0.045f, dh - 0.01f, doorZ1 - doorZ0 - 0.01f), doorMat, true);
            LocalBox("Handle", hingeT, new Vector3(-0.04f, 1.0f, doorZ1 - doorZ0 - 0.1f), new Vector3(0.03f, 0.03f, 0.14f), MetalMat() ?? trim, false);
            var hinge = hingeT.gameObject.AddComponent<Hinge>();
            hinge.openEuler = new Vector3(0, -100, 0);
            hinge.duration = 1.6f;
            return hinge;
        }

        /// Door interaction (ray + trigger on the door, or P in the simulator) with a floating prompt above the doorway.
        static DoorKnock AddKnock(Hinge door, DoorKnock.Mode mode, Vector3 point, string prompt, Vector3 promptPos, float yaw, float maxDistance = 3.8f)
        {
            var go = new GameObject("DoorKnock");
            go.transform.SetParent(props);
            var k = go.AddComponent<DoorKnock>();
            k.mode = mode;
            k.door = door;
            k.point = point;
            k.maxDistance = maxDistance;
            // on the headset there is no keyboard: point at the door and pull the trigger (P still works in the simulator)
            var leaf = door.transform.Find("Door");
            if (leaf && leaf.GetComponent<Collider>())
                UnityEventTools.AddPersistentListener(MakeClickable(leaf.gameObject, null).onClick, k.Knock);
            prompt = prompt.Replace("PRESS  P", "POINT + TRIGGER (OR P)");
            Label(env, prompt, promptPos, Quaternion.Euler(0, yaw, 0), new Vector2(0.9f, 0.12f), new Color(0.36f, 0.94f, 0.69f), true);
            return k;
        }

        /// Real-time point light (not baked, so it can flicker or follow the player). Cheap: no shadows.
        static Light RealtimePoint(string name, Transform parent, Vector3 pos, Color color, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(parent);
            l.transform.position = pos;
            l.type = LightType.Point;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.shadows = LightShadows.None;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            return l;
        }

        /// A failing fluorescent tube on the ceiling: emissive tube mesh + real-time light that flicker together.
        static void FlickerTube(Vector3 pos, float length, Color lightColor, float intensity)
        {
            var glowColor = new Color(1.5f, 1.6f, 1.8f);
            var root = new GameObject("FlickerTube").transform;
            root.SetParent(props);
            root.position = pos;
            LocalBox("Housing", root, new Vector3(0, 0.045f, 0), new Vector3(0.16f, 0.03f, length + 0.12f), Mat("TubeHousing", new Color(0.16f, 0.17f, 0.19f), 0.4f, 0.5f), false);
            var tube = LocalBox("Tube", root, Vector3.zero, new Vector3(0.08f, 0.05f, length), Mat("TubeGlow", Color.white, 0.2f, 0, glowColor), false);
            var light = RealtimePoint("TubeLight", root, pos + Vector3.down * 0.25f, lightColor, intensity, 8f);
            var f = root.gameObject.AddComponent<FlickerLight>();
            f.lights = new[] { light };
            f.glow = tube.GetComponent<Renderer>();
            f.glowColor = glowColor;
        }

        static GameObject Cylinder(string name, Transform parent, Vector3 localPos, float radius, float length, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(radius * 2, length / 2, radius * 2);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static Light AreaLight(Vector3 pos, Vector2 size, Color color, float intensity, Quaternion? rot = null)
        {
            var l = new GameObject("AreaLight").AddComponent<Light>();
            l.transform.SetParent(env);
            l.type = LightType.Rectangle;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.areaSize = size;
            l.color = color;
            l.intensity = intensity;
            l.range = 10;
            l.transform.SetPositionAndRotation(pos, rot ?? Quaternion.Euler(90, 0, 0));
            return l;
        }

        static void KeySpot(Vector3 pos, float intensity)
        {
            var key = new GameObject("Key Light (mixed)").AddComponent<Light>();
            key.transform.SetParent(env);
            key.type = LightType.Spot;
            key.lightmapBakeType = LightmapBakeType.Mixed;
            key.spotAngle = 120;
            key.innerSpotAngle = 60;
            key.range = 6;
            key.intensity = intensity;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.6f;
            key.transform.SetPositionAndRotation(pos, Quaternion.Euler(90, 0, 0));
        }

        static void NightSky(string matName, float exposure, float rotation)
        {
            var hdr = AssetDatabase.LoadAssetAtPath<Texture>(Root + "/Textures/HDRI/modern_buildings_night_2k.hdr");
            var sky = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{matName}.mat");
            if (!sky)
            {
                sky = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(sky, $"{MatDir}/{matName}.mat");
            }
            sky.SetTexture("_MainTex", hdr);
            sky.SetFloat("_Exposure", exposure);
            sky.SetFloat("_Rotation", rotation);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        /// Desk monitor with a world-space canvas (ray + poke). Returns the canvas.
        static RectTransform DeskMonitor(string name, Vector3 pos, float yaw, float scale, bool interactive, out Transform root)
        {
            var bezel = Mat("Bezel", new Color(0.05f, 0.05f, 0.055f), 0.75f);
            root = new GameObject(name).transform;
            root.SetParent(props);
            root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            float w = 1280 * scale, h = 800 * scale;
            LocalBox("Foot", root, new Vector3(0, 0.008f, 0.02f), new Vector3(0.26f, 0.016f, 0.2f), bezel, true);
            LocalBox("Neck", root, new Vector3(0, 0.16f, 0.05f), new Vector3(0.05f, 0.3f, 0.03f), bezel, false);
            var pivot = new GameObject("Screen").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0, 0.12f + h / 2 + 0.03f, 0);
            pivot.localRotation = Quaternion.Euler(8, 0, 0);
            FramedScreen(pivot, Vector2.zero, w, h, bezel, 0f);
            var c = MakeCanvas(name + "Canvas", pivot, Vector3.zero, Quaternion.identity, new Vector2(1280, 800), scale);   // bezel front face is at 0.0015: flush, 0.8 mm out
            if (interactive) AddCanvasInteraction(c.gameObject, root, poke: true);
            else foreach (var g in c.GetComponentsInChildren<GraphicRaycaster>()) Object.DestroyImmediate(g);
            return c;
        }

        static void LockedMonitor(Vector3 pos, float yaw, string user)
        {
            var bezel = Mat("Bezel", new Color(0.05f, 0.05f, 0.055f), 0.75f);
            var lockScreen = Mat("LockScreen", new Color(0.04f, 0.1f, 0.2f), 0.9f, 0, new Color(0.08f, 0.18f, 0.35f));
            var mon = new GameObject("Monitor_" + user).transform;
            mon.SetParent(env);
            mon.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            LocalBox("Foot", mon, new Vector3(0, 0.008f, 0), new Vector3(0.24f, 0.016f, 0.18f), bezel, true);
            LocalBox("Neck", mon, new Vector3(0, 0.15f, 0.03f), new Vector3(0.05f, 0.28f, 0.03f), bezel, false);
            LocalBox("Bezel", mon, new Vector3(0, 0.36f, 0.015f), new Vector3(0.6f, 0.37f, 0.025f), bezel, true);
            LocalBox("Screen", mon, new Vector3(0, 0.36f, 0.0015f), new Vector3(0.575f, 0.345f, 0.002f), lockScreen, false);
            Label(mon, "Locked\n<size=50%>" + user + "</size>", new Vector3(0, 0.36f, -0.003f), Quaternion.identity, new Vector2(0.3f, 0.12f), new Color(0.85f, 0.9f, 1f));
        }

        // =================================================================== MAIN MENU (rooftop)

        public static void BuildMenuScene(bool bake)
        {
            var scene = NewScene();
            NightSky("MenuSky", 0.85f, 200f);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.55f;
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.7f, 0.78f, 1f);
            moon.intensity = 0.9f;
            moon.lightmapBakeType = LightmapBakeType.Mixed;
            moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(40, 150, 0);
            moon.transform.SetParent(env);

            var deck = AcgMat("RoofDeck", "PaintedPlaster017", new Color(0.1f, 0.105f, 0.115f), new Vector2(6, 6), 0.08f, bump: 0.6f);
            var metal = AcgMat("Aluminium", "Metal009", new Color(0.85f, 0.86f, 0.88f), Vector2.one, 0.7f, 0.9f);
            var glass = Mat("RailGlass", new Color(0.6f, 0.75f, 0.9f, 0.18f), 0.95f);
            MakeTransparent(glass);
            Box("Deck", env, new Vector3(0, -0.05f, 0), new Vector3(12, 0.1f, 12), deck);
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0, side * 90, 0);
                var c = rot * new Vector3(0, 0, 4.5f);
                var size = rot * new Vector3(9, 0, 0);
                var s = new Vector3(Mathf.Abs(size.x) + 0.05f, 1.0f, Mathf.Abs(size.z) + 0.05f);
                Box("RailGlass", env, c + Vector3.up * 0.55f, new Vector3(Mathf.Max(s.x, 0.02f), 1.0f, Mathf.Max(s.z, 0.02f)), glass, true);
                Box("RailTop", env, c + Vector3.up * 1.07f, new Vector3(Mathf.Max(s.x, 0.06f), 0.05f, Mathf.Max(s.z, 0.06f)), metal, false);
            }
            // rooftop details: planters and a lit sign
            Model(Root + "/Models/PolyHaven/potted_plant_02/potted_plant_02_1k.fbx", env, new Vector3(-2.2f, 0, 2.2f), 20, height: 1.4f);
            Model(Root + "/Models/PolyHaven/potted_plant_02/potted_plant_02_1k.fbx", env, new Vector3(2.4f, 0, 2.0f), 140, height: 1.2f);
            Model(Root + "/Models/PolyHaven/modern_arm_chair_01/modern_arm_chair_01_1k.fbx", env, new Vector3(-2.6f, 0, -0.6f), 70, height: 0.85f);
            Label(env, "NORTHWIND", new Vector3(0, 2.9f, 3.6f), Quaternion.identity, new Vector2(3.2f, 0.6f), new Color(0.36f, 0.94f, 0.69f), true);
            roomExtents = new Vector3(4.5f, 3f, 4.5f);

            // stairwell hut with the door that leads into the story: knock with P
            var hutWall = AcgMat("HutWall", "PaintedPlaster017", new Color(0.55f, 0.57f, 0.62f), new Vector2(2, 1), 0.1f, bump: 0.4f);
            var stairGlow = Mat("StairGlow", new Color(0.9f, 0.85f, 0.7f), 0.2f, 0, new Color(1.5f, 1.2f, 0.8f));
            const float hx0 = 1.8f, hx1 = 4.2f, hz0 = 2.8f, hz1 = 4.4f, hh = 2.7f, dx0 = 2.55f, dx1 = 3.45f, dh = 2.1f;
            Box("Hut_FrontL", env, new Vector3((hx0 + dx0) / 2, hh / 2, hz0), new Vector3(dx0 - hx0, hh, 0.12f), hutWall);
            Box("Hut_FrontR", env, new Vector3((dx1 + hx1) / 2, hh / 2, hz0), new Vector3(hx1 - dx1, hh, 0.12f), hutWall);
            Box("Hut_Lintel", env, new Vector3((dx0 + dx1) / 2, (dh + hh) / 2, hz0), new Vector3(dx1 - dx0, hh - dh, 0.12f), hutWall);
            Box("Hut_Back", env, new Vector3((hx0 + hx1) / 2, hh / 2, hz1), new Vector3(hx1 - hx0, hh, 0.12f), hutWall);
            Box("Hut_SideL", env, new Vector3(hx0, hh / 2, (hz0 + hz1) / 2), new Vector3(0.12f, hh, hz1 - hz0), hutWall);
            Box("Hut_SideR", env, new Vector3(hx1, hh / 2, (hz0 + hz1) / 2), new Vector3(0.12f, hh, hz1 - hz0), hutWall);
            Box("Hut_Roof", env, new Vector3((hx0 + hx1) / 2, hh + 0.06f, (hz0 + hz1) / 2), new Vector3(hx1 - hx0 + 0.3f, 0.12f, hz1 - hz0 + 0.3f), hutWall);
            Box("Hut_Floor", env, new Vector3((hx0 + hx1) / 2, 0.01f, (hz0 + hz1) / 2), new Vector3(hx1 - hx0 - 0.1f, 0.02f, hz1 - hz0 - 0.1f), stairGlow, false);
            Box("Hut_GlowBack", env, new Vector3((hx0 + hx1) / 2, hh / 2, hz1 - 0.07f), new Vector3(hx1 - hx0 - 0.1f, hh - 0.1f, 0.02f), stairGlow, false);
            Box("Hut_GlowL", env, new Vector3(hx0 + 0.07f, hh / 2, (hz0 + hz1) / 2), new Vector3(0.02f, hh - 0.1f, hz1 - hz0 - 0.1f), stairGlow, false);
            Box("Hut_GlowR", env, new Vector3(hx1 - 0.07f, hh / 2, (hz0 + hz1) / 2), new Vector3(0.02f, hh - 0.1f, hz1 - hz0 - 0.1f), stairGlow, false);
            var frameMat = Mat("DoorFrameDark", new Color(0.2f, 0.21f, 0.23f), 0.5f);
            Box("Hut_FrameL", env, new Vector3(dx0 - 0.03f, dh / 2, hz0 - 0.07f), new Vector3(0.06f, dh, 0.06f), frameMat, false);
            Box("Hut_FrameR", env, new Vector3(dx1 + 0.03f, dh / 2, hz0 - 0.07f), new Vector3(0.06f, dh, 0.06f), frameMat, false);
            Box("Hut_FrameTop", env, new Vector3((dx0 + dx1) / 2, dh + 0.03f, hz0 - 0.07f), new Vector3(dx1 - dx0 + 0.12f, 0.06f, 0.06f), frameMat, false);
            var hutDoorMat = AcgMat("DoorSteel", "Metal009", Palette.Hex("6B7380"), Vector2.one, 0.5f, 0.7f);
            var hingeT = new GameObject("DoorHinge").transform;
            hingeT.SetParent(env);
            hingeT.position = new Vector3(dx0, 0, hz0 - 0.02f);
            LocalBox("Door", hingeT, new Vector3((dx1 - dx0) / 2, dh / 2, 0), new Vector3(dx1 - dx0 - 0.01f, dh - 0.01f, 0.045f), hutDoorMat, true);
            LocalBox("Handle", hingeT, new Vector3(dx1 - dx0 - 0.1f, 1.0f, -0.04f), new Vector3(0.14f, 0.03f, 0.03f), MetalMat() ?? frameMat, false);
            var hutDoor = hingeT.gameObject.AddComponent<Hinge>();
            hutDoor.openEuler = new Vector3(0, -100, 0);
            hutDoor.duration = 1.6f;
            RealtimePoint("StairLight", props, new Vector3((hx0 + hx1) / 2, 2.2f, (hz0 + hz1) / 2), new Color(1f, 0.85f, 0.6f), 2.5f, 4.5f);
            Label(env, "STAIRWELL  -  ENTER", new Vector3((dx0 + dx1) / 2, dh + 0.3f, hz0 - 0.07f), Quaternion.identity, new Vector2(0.9f, 0.14f), new Color(0.36f, 0.94f, 0.69f), true);
            AddKnock(hutDoor, DoorKnock.Mode.Menu, new Vector3((dx0 + dx1) / 2, 1.0f, hz0), "PRESS  P  TO KNOCK", new Vector3((dx0 + dx1) / 2, dh + 0.5f, hz0 - 0.07f), 0, 5.2f);

            var rig = Rig(Vector3.zero, 0);
            var head = FindDeep(rig.transform, "CenterEyeAnchor");
            var game = Managers(0);
            var menuRoot = new GameObject("MainMenu").transform;
            menuRoot.SetParent(ui);
            menuRoot.position = new Vector3(0, 1.55f, 1.7f);
            var canvas = MakeCanvas("MainMenuCanvas", menuRoot, Vector3.zero, Quaternion.identity, new Vector2(1400, 820), 0.0011f);
            AddCanvasInteraction(canvas.gameObject, menuRoot, poke: false);
            menuRoot.gameObject.AddComponent<MainMenu>().canvas = canvas;
            head.gameObject.AddComponent<Fader>().material = FadeMaterial();
            FinishScene(scene, "MainMenu", head, bake);
        }

        // =================================================================== CHAPTER 1 - NIGHT SHIFT (SOC)

        public static void BuildNightShift(bool bake)
        {
            var scene = NewScene();
            var ext = new Vector3(4f, 3.2f, 3f);
            var wall = AcgMat("SocWall", "PaintedPlaster017", Palette.Hex("B4BED2"), new Vector2(3, 1.4f), 0.2f, bump: 0.3f);
            var floor = AcgMat("SocCarpet", "Carpet012", Palette.Hex("93A5D4"), new Vector2(6, 4.5f), 0.05f, bump: 0.6f);
            var ceil = AcgMat("SocCeiling", "OfficeCeiling001", Palette.Hex("8C93A3"), new Vector2(4, 3), 0.1f);
            var doorMat = AcgMat("DoorSteel", "Metal009", Palette.Hex("6B7380"), Vector2.one, 0.5f, 0.7f);
            var door = Shell(ext, wall, floor, ceil, -2.1f, -1.2f, doorMat);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            // night shift: the ceiling panels are mostly off and a failing tube at the far end does the flickering
            RenderSettings.ambientSkyColor = new Color(0.8f, 0.85f, 0.98f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.65f, 0.76f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.36f, 0.43f);
            RenderSettings.skybox = null;
            var strip = Mat("AccentStrip", Color.black, 0.3f, 0, new Color(0.25f, 0.7f, 1.6f));
            Box("AccentStrip_L", env, new Vector3(-ext.x + 0.02f, ext.y - 0.06f, 0), new Vector3(0.03f, 0.03f, ext.z * 2), strip, false);
            Box("AccentStrip_R", env, new Vector3(ext.x - 0.02f, ext.y - 0.06f, 0), new Vector3(0.03f, 0.03f, ext.z * 2), strip, false);
            Box("AccentStrip_F", env, new Vector3(0, 0.12f, ext.z - 0.02f), new Vector3(ext.x * 2, 0.02f, 0.03f), strip, false);
            var panel = Mat("CeilingPanelDim", Color.white, 0.2f, 0, new Color(1.2f, 1.28f, 1.45f));
            foreach (var x in new[] { -2.2f, 0f, 2.2f })
            foreach (var z in new[] { -1.4f, 1.1f })
            {
                Box("CeilingPanel", env, new Vector3(x, ext.y - 0.004f, z), new Vector3(1.2f, 0.01f, 0.3f), panel, false);
                AreaLight(new Vector3(x, ext.y - 0.02f, z), new Vector2(1.2f, 0.3f), new Color(0.92f, 0.96f, 1f), 55f);
            }
            AreaLight(new Vector3(0, 1.9f, ext.z - 0.1f), new Vector2(5.4f, 1.0f), new Color(0.4f, 0.65f, 1f), 8f, Quaternion.Euler(0, 180, 0));
            KeySpot(new Vector3(0, ext.y - 0.1f, 0.2f), 2.6f);
            FlickerTube(new Vector3(-ext.x + 0.7f, ext.y - 0.14f, 0.3f), 2.6f, new Color(0.82f, 0.92f, 1f), 22f);

            // video wall
            var wallKinds = new[] { SocWall.Kind.Traffic, SocWall.Kind.Threat, SocWall.Kind.Feed };
            var bezel = Mat("Bezel", new Color(0.05f, 0.05f, 0.055f), 0.75f);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 1.9f;
                Box("WallScreenBezel", env, new Vector3(x, 1.9f, ext.z - 0.02f), new Vector3(1.82f, 1.04f, 0.04f), bezel, false);
                var c = MakeCanvas("SocWall_" + wallKinds[i], env, new Vector3(x, 1.9f, ext.z - 0.045f), Quaternion.identity, new Vector2(960, 540), 0.00182f);
                foreach (var g in c.GetComponentsInChildren<GraphicRaycaster>()) Object.DestroyImmediate(g);
                var sw = c.gameObject.AddComponent<SocWall>();
                sw.kind = wallKinds[i];
                sw.canvas = c;
            }
            Label(env, "SECURITY OPERATIONS CENTRE", new Vector3(0, 2.75f, ext.z - 0.03f), Quaternion.identity, new Vector2(4f, 0.25f), new Color(0.5f, 0.75f, 1f), true);

            // desks
            var ph = Root + "/Models/PolyHaven/";
            var myDesk = Model(ph + "metal_office_desk/metal_office_desk_1k.fbx", env, new Vector3(0, 0, 0.55f), DeskYaw, width: 1.6f, height: 0.75f);
            float top = BoundsOf(myDesk).max.y;
            foreach (var x in new[] { -2.6f, 2.6f })
            {
                Model(ph + "metal_office_desk/metal_office_desk_1k.fbx", env, new Vector3(x, 0, 1.3f), DeskYaw, width: 1.6f, height: 0.75f);
                LockedMonitor(new Vector3(x - 0.3f, top, 1.6f), 0, x < 0 ? "K. de Vries" : "S. Okafor");
                LockedMonitor(new Vector3(x + 0.35f, top, 1.6f), -10, x < 0 ? "K. de Vries" : "S. Okafor");
                if (x > 0) Keyboard(new Vector3(x, top, 1.1f));   // the infected laptop replaces the left desk's loose keyboard
                OfficeChair(new Vector3(x, 0, 0.55f), 0);
            }
            OfficeChair(new Vector3(0.75f, 0, -0.1f), -25);
            Model(ph + "potted_plant_02/potted_plant_02_1k.fbx", env, new Vector3(-3.5f, 0, 2.5f), 60, height: 1.4f);
            Model(ph + "security_camera_01/security_camera_01_1k.fbx", env, new Vector3(3.8f, 2.9f, 2.8f), 225, height: 0.3f, collider: false);
            Model(ph + "wall_clock/wall_clock_1k.fbx", env, new Vector3(-ext.x + 0.03f, 2.3f, 0f), 90, height: 0.34f, collider: false);
            Model(ph + "fire_alarm/fire_alarm_1k.fbx", env, new Vector3(ext.x - 0.02f, 1.4f, -0.3f), 270, height: 0.13f, collider: false);

            var rig = Rig(new Vector3(0, 0, -0.45f), 0);
            var head = FindDeep(rig.transform, "CenterEyeAnchor");
            var game = Managers(1);

            // my workstation: SIEM in the middle, two side screens
            var siem = DeskMonitor("SIEMMonitor", new Vector3(0, top, 0.78f), 0, 0.00052f, true, out var siemRoot);
            siemRoot.gameObject.AddComponent<SiemConsole>().screen = siem;
            foreach (var s in new[] { -1, 1 })
            {
                var side = DeskMonitor("SideMonitor" + (s < 0 ? "L" : "R"), new Vector3(s * 0.66f, top, 0.7f), s * -28, 0.00036f, false, out _);
                var sw = side.gameObject.AddComponent<SocWall>();
                sw.kind = s < 0 ? SocWall.Kind.Feed : SocWall.Kind.Traffic;
                sw.canvas = side;
            }
            Keyboard(new Vector3(0, top, 0.36f));
            Model(ph + "stationery_supplies/stationery_supplies_1k.fbx", props, new Vector3(0.55f, top, 0.35f), 200, width: 0.22f, collider: false);

            // infected laptop on the colleague's desk: SECURITY BREACH / MALWARE DETECTED
            {
                var lapMat = Mat("LaptopBody", new Color(0.22f, 0.23f, 0.26f), 0.5f, 0.6f);
                var lapRoot = new GameObject("InfectedLaptop").transform;
                lapRoot.SetParent(props);
                lapRoot.SetPositionAndRotation(new Vector3(-2.35f, top, 1.15f), Quaternion.Euler(0, 8, 0));   // base fully on the desk
                LocalBox("Base", lapRoot, new Vector3(0, 0.009f, 0), new Vector3(0.36f, 0.018f, 0.25f), lapMat, true);
                LocalBox("Keys", lapRoot, new Vector3(0, 0.0185f, 0.005f), new Vector3(0.32f, 0.002f, 0.19f), Mat("LaptopKeys", new Color(0.08f, 0.08f, 0.09f), 0.4f), false);
                var hinge = new GameObject("ScreenHinge").transform;
                hinge.SetParent(lapRoot, false);
                hinge.localPosition = new Vector3(0, 0.016f, 0.122f);
                hinge.localRotation = Quaternion.Euler(10, 0, 0);   // clears the monitors behind it
                const float lw = 960 * 0.0004f, lh = 600 * 0.0004f;
                FramedScreen(hinge, new Vector2(0, lh / 2 + 0.01f), lw, lh, lapMat, 0.002f, 0.01f, 0.014f, 0.006f);   // named "Bezel" + frame bars
                var glowMat = Mat("LaptopAlertGlow", Color.black, 0.3f, 0, new Color(1.6f, 0.1f, 0.1f));
                var glowBox = LocalBox("AlertGlow", hinge, new Vector3(0, lh / 2 + 0.01f, 0.0085f), new Vector3(lw, lh, 0.002f), glowMat, false);
                var lapCanvas = MakeCanvas("InfectedLaptopCanvas", hinge, new Vector3(0, lh / 2 + 0.01f, 0.002f), Quaternion.identity, new Vector2(960, 600), 0.0004f);   // canvas plane = frame front face
                AddCanvasInteraction(lapCanvas.gameObject, lapRoot, poke: true);
                var ml = lapRoot.gameObject.AddComponent<MalwareLaptop>();
                ml.screen = lapCanvas;
                ml.glow = glowBox.GetComponent<Renderer>();
                RealtimePoint("LaptopAlertLight", lapRoot, lapRoot.position + new Vector3(0, 0.3f, -0.25f), new Color(1f, 0.15f, 0.12f), 0.6f, 1.8f);
                // VR-sized: 1.8x makes the screen ~69 x 43 cm and its rows ~4.3 cm tall; canvas, colliders and poke surface scale with it
                lapRoot.localScale = Vector3.one * 1.8f;
            }

            // access badge
            var badge = new GameObject("AccessBadge");
            badge.transform.SetParent(props);
            badge.transform.SetPositionAndRotation(new Vector3(-0.42f, top + 0.004f, 0.32f), Quaternion.Euler(0, 15, 0));
            var badgeMat = Mat("BadgeBlue", Palette.Hex("1E4FA0"), 0.55f);
            LocalBox("Card", badge.transform, new Vector3(0, 0.0015f, 0), new Vector3(0.086f, 0.003f, 0.054f), badgeMat, false);
            LocalBox("Photo", badge.transform, new Vector3(-0.025f, 0.0032f, 0.0f), new Vector3(0.022f, 0.0005f, 0.028f), Mat("BadgePhoto", Palette.Hex("C9D6E8"), 0.3f), false);
            var bl = Label(badge.transform, "<b>NORTHWIND</b>\n<size=70%>SOC ANALYST\nnight shift</size>", new Vector3(0.012f, 0.0045f, 0), Quaternion.Euler(90, 0, 0), new Vector2(0.05f, 0.04f), Color.white);
            // forgiving grab collider: bigger than the 8.6 x 5.4 cm card and thick enough to poke above the desk surface
            var bc = badge.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, 0.012f, 0);
            bc.size = new Vector3(0.11f, 0.03f, 0.08f);
            Grab(badge, ItemKind.Badge, "Access badge", "Your Northwind access badge. It opens the doors you are cleared for.", "Keep your badge with you.");

            // badge reader by the door
            var readerRoot = new GameObject("BadgeReader").transform;
            readerRoot.SetParent(env);
            readerRoot.SetPositionAndRotation(new Vector3(ext.x - 0.02f, 1.2f, -0.85f), Quaternion.Euler(0, 90, 0));
            LocalBox("Body", readerRoot, new Vector3(0, 0, -0.015f), new Vector3(0.09f, 0.13f, 0.03f), Mat("ReaderBody", new Color(0.08f, 0.08f, 0.09f), 0.6f), false);
            var ledIdle = Mat("LedIdle", Color.black, 0.5f, 0, new Color(0.2f, 0.45f, 1.5f));
            var ledOk = Mat("LedGreen", Color.black, 0.5f, 0, new Color(0.2f, 2.2f, 0.6f));
            var ledNo = Mat("LedRed", Color.black, 0.5f, 0, new Color(2.4f, 0.2f, 0.2f));
            var led = LocalBox("Led", readerRoot, new Vector3(0, 0.045f, -0.031f), new Vector3(0.05f, 0.008f, 0.004f), ledIdle, false);
            Label(readerRoot, "TAP\nBADGE", new Vector3(0, -0.015f, -0.0315f), Quaternion.identity, new Vector2(0.07f, 0.06f), new Color(0.75f, 0.85f, 1f));
            var trig = readerRoot.gameObject.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.center = new Vector3(0, 0, -0.08f);
            trig.size = new Vector3(0.22f, 0.24f, 0.18f);
            var reader = readerRoot.gameObject.AddComponent<BadgeReader>();
            reader.door = door;
            reader.led = led.GetComponent<Renderer>();
            reader.ledIdle = ledIdle; reader.ledGranted = ledOk; reader.ledDenied = ledNo;
            Label(env, "FLOOR 3  >", new Vector3(ext.x - 0.02f, 2.3f, -1.65f), Quaternion.Euler(0, 90, 0), new Vector2(0.6f, 0.15f), new Color(0.36f, 0.94f, 0.69f), true);
            var knock = AddKnock(door, DoorKnock.Mode.Badge, new Vector3(ext.x, 1.0f, -1.65f), "PRESS  P  AT THE DOOR TO TAP YOUR BADGE", new Vector3(ext.x - 0.02f, 2.6f, -1.65f), 90);
            knock.reader = reader;

            CommonUI(game, head, new Vector3(0, 1.5f, 0.55f), 0);
            Hotspots(new Vector3(0, 0, -0.45f), new Vector3(3.1f, 0, -1.0f), new Vector3(1.6f, 0, -1.4f), new Vector3(-1.6f, 0, -0.6f));
            var d = game.GetComponent<ChapterDirector>();
            d.steps = new[]
            {
                Step(TaskId.SOC_Badge, "Your badge", null, "AccessBadge"),
                Step(TaskId.SOC_OpenAlert, "SIEM console", null, "SIEMMonitor"),
                Step(TaskId.SOC_FindHost, "Read the logs", null, "SIEMMonitor"),
                Step(TaskId.SOC_Classify, "SIEM console", null, "SIEMMonitor"),
                Step(TaskId.SOC_Escalate, "Escalate", null, "SIEMMonitor"),
                Step(TaskId.SOC_Malware, "Infected laptop", null, "InfectedLaptop"),
                Step(TaskId.SOC_Exit, "Badge reader", "BadgeReader", "BadgeReader"),
            };
            d.steps[6].dropLabel = "Tap your badge here";
            d.steps[6].targets = new[] { GameObject.Find("AccessBadge").transform };
            d.steps[6].label = "Take your badge";
            FinishScene(scene, "Ch1_NightShift", head, bake);
        }

        // =================================================================== CHAPTER 3 - SERVER ROOM

        public static void BuildServerRoom(bool bake)
        {
            var scene = NewScene();
            var ext = new Vector3(3.5f, 3.0f, 2.5f);
            var wall = AcgMat("ServerWall", "PaintedPlaster017", Palette.Hex("C8CDD5"), new Vector2(3, 1.3f), 0.2f, bump: 0.3f);
            var floor = AcgMat("RaisedFloor", "OfficeCeiling001", Palette.Hex("A3AAB4"), new Vector2(11.7f, 8.3f), 0.35f);
            var ceil = AcgMat("ServerCeiling", "OfficeCeiling001", Palette.Hex("5E646E"), new Vector2(4, 3), 0.1f);
            var doorMat = AcgMat("DoorSteel", "Metal009", Palette.Hex("6B7380"), Vector2.one, 0.5f, 0.7f);
            var door = Shell(ext, wall, floor, ceil, -2.2f, -1.3f, doorMat);
            // power cut: dim ambient, dead ceiling panels, a red emergency lamp by the door. The player needs the flashlight.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.36f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.22f, 0.3f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.11f, 0.14f);
            RenderSettings.skybox = null;

            var panel = Mat("CeilingPanelOff", new Color(0.3f, 0.31f, 0.33f), 0.2f);
            foreach (var x in new[] { -2.2f, -0.2f, 1.8f })
            {
                Box("CeilingPanel", env, new Vector3(x, ext.y - 0.004f, 0), new Vector3(1.4f, 0.01f, 0.25f), panel, false);
                AreaLight(new Vector3(x, ext.y - 0.02f, 0), new Vector2(1.4f, 0.25f), new Color(0.6f, 0.7f, 1f), 11f);
            }
            Box("CeilingPanel", env, new Vector3(2.6f, ext.y - 0.004f, 1.6f), new Vector3(0.6f, 0.01f, 0.6f), panel, false);
            Box("CeilingPanel", env, new Vector3(1.6f, ext.y - 0.004f, -1.4f), new Vector3(0.6f, 0.01f, 0.6f), panel, false);
            Box("EmergencyLamp", env, new Vector3(ext.x - 0.08f, 2.62f, -1.75f), new Vector3(0.1f, 0.12f, 0.24f),
                Mat("EmergencyRed", Color.black, 0.3f, 0, new Color(2.6f, 0.15f, 0.1f)), false);
            RealtimePoint("EmergencyLight", props, new Vector3(ext.x - 0.5f, 2.55f, -1.6f), new Color(1f, 0.22f, 0.15f), 4.5f, 6.5f);
            RealtimePoint("ColdFill", props, new Vector3(0.5f, 2.6f, 0f), new Color(0.45f, 0.6f, 1f), 3.5f, 9f);
            var strip = Mat("AccentStrip", Color.black, 0.3f, 0, new Color(0.25f, 0.7f, 1.6f));
            Box("FloorStrip_A", env, new Vector3(-0.7f, 0.005f, 0.47f), new Vector3(3.3f, 0.01f, 0.02f), strip, false);
            Box("FloorStrip_B", env, new Vector3(-0.7f, 0.005f, -0.47f), new Vector3(3.3f, 0.01f, 0.02f), strip, false);
            // cable trays
            var tray = AcgMat("Tray", "Metal009", new Color(0.55f, 0.57f, 0.6f), new Vector2(4, 1), 0.6f, 0.8f);
            Box("CableTray_A", env, new Vector3(-0.7f, 2.45f, 1.0f), new Vector3(3.4f, 0.06f, 0.35f), tray, false);
            Box("CableTray_B", env, new Vector3(-0.7f, 2.45f, -1.0f), new Vector3(3.4f, 0.06f, 0.35f), tray, false);

            // racks: row A faces the aisle from +z, row B from -z
            var xs = new[] { -2.25f, -1.6f, -0.95f, -0.3f, 0.35f };
            Transform implantRack = null;
            for (int i = 0; i < xs.Length; i++)
            {
                var a = Rack(new Vector3(xs[i], 0, 1.0f), 180, 100 + i);
                Rack(new Vector3(xs[i], 0, -1.0f), 0, 200 + i);
                if (i == 2) implantRack = a;
            }

            var rig = Rig(new Vector3(2.55f, 0, 0), -90);
            var head = FindDeep(rig.transform, "CenterEyeAnchor");
            var game = Managers(3);

            // the implant on a shelf in rack A3, cabled to the top-of-rack switch
            var shelfY = 1.18f;
            var red = Mat("ImplantCase", Palette.Hex("C8102E"), 0.55f);
            var white = Mat("ImplantLid", Palette.Hex("EDEDED"), 0.5f);
            LocalBox("Shelf", implantRack, new Vector3(0, shelfY - 0.01f, 0.42f), new Vector3(0.48f, 0.01f, 0.2f), tray, true);
            var implant = new GameObject("Implant");
            implant.transform.SetParent(props);
            implant.transform.SetPositionAndRotation(implantRack.TransformPoint(new Vector3(0.08f, shelfY + 0.016f, 0.45f)), implantRack.rotation * Quaternion.Euler(0, 15, 0));
            LocalBox("Case", implant.transform, Vector3.zero, new Vector3(0.095f, 0.03f, 0.066f), red, false);
            LocalBox("Lid", implant.transform, new Vector3(0, 0.0155f, 0), new Vector3(0.08f, 0.002f, 0.05f), white, false);
            var ledOn = Mat("LedRed", Color.black, 0.5f, 0, new Color(2.4f, 0.2f, 0.2f));
            var ledOff = Mat("LedOff", new Color(0.15f, 0.02f, 0.02f), 0.5f);
            var led = LocalBox("Led", implant.transform, new Vector3(0.03f, 0.0175f, 0.02f), new Vector3(0.008f, 0.004f, 0.008f), ledOn, false);
            var socket = new GameObject("Socket").transform;
            socket.SetParent(implant.transform, false);
            socket.localPosition = new Vector3(-0.0475f - 0.012f, 0, 0);
            var ic = implant.AddComponent<BoxCollider>();
            ic.size = new Vector3(0.1f, 0.036f, 0.07f);
            Grab(implant, ItemKind.Implant, "Rogue implant", "A Raspberry Pi with no asset tag, hidden in our rack. This is how the attacker got in.", "Evidence goes in the evidence bag on the table.");
            var ri = implant.AddComponent<RogueImplant>();
            var click = MakeClickable(implant, "A small red box with a blinking LED... this isn't one of ours.");
            UnityEventTools.AddPersistentListener(click.onClick, ri.Inspect);

            var plug = new GameObject("Plug");
            plug.transform.SetParent(props);
            plug.transform.SetPositionAndRotation(socket.position, implant.transform.rotation);
            var yellow = Mat("CableYellow", Palette.Hex("F2C230"), 0.4f);
            LocalBox("Body", plug.transform, Vector3.zero, new Vector3(0.025f, 0.016f, 0.018f), yellow, false);
            plug.AddComponent<BoxCollider>().size = new Vector3(0.04f, 0.03f, 0.03f);
            Grab(plug, ItemKind.Plug, "Network plug", "The implant's network cable. Pull it out!", null);
            plug.GetComponent<Rigidbody>().isKinematic = true;
            var cableStart = new GameObject("CableStart").transform;
            cableStart.SetParent(implantRack, false);
            cableStart.localPosition = new Vector3(0.18f, 1.93f, 0.52f);
            var lr = plug.AddComponent<LineRenderer>();
            lr.widthMultiplier = 0.009f;
            lr.numCapVertices = 2;
            lr.sharedMaterial = yellow;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            ri.led = led.GetComponent<Renderer>();
            ri.ledOn = ledOn; ri.ledOff = ledOff;
            ri.socket = socket; ri.plug = plug.transform; ri.cableStart = cableStart; ri.cable = lr;
            ri.enableWhenUnplugged = implant.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("ISDK_")).Select(t => t.gameObject).ToArray();

            // evidence table + bag
            var table = new GameObject("EvidenceTable").transform;
            table.SetParent(env);
            table.position = new Vector3(1.55f, 0, -1.2f);
            LocalBox("Top", table, new Vector3(0, 0.8f, 0), new Vector3(0.9f, 0.03f, 0.55f), tray, true);
            foreach (var lx in new[] { -0.4f, 0.4f }) foreach (var lz in new[] { -0.23f, 0.23f })
                LocalBox("Leg", table, new Vector3(lx, 0.4f, lz), new Vector3(0.03f, 0.8f, 0.03f), tray, false);
            var bag = new GameObject("EvidenceBag");
            bag.transform.SetParent(props);
            bag.transform.SetPositionAndRotation(table.position + Vector3.up * 0.815f, Quaternion.Euler(0, 180, 0));
            var bagMat = Mat("EvidenceBagPlastic", new Color(0.92f, 0.93f, 0.95f), 0.6f);
            OpenBox(bag.transform, new Vector3(0.34f, 0.12f, 0.26f), bagMat);
            Label(bag.transform, "EVIDENCE", new Vector3(0, 0.07f, -0.138f), Quaternion.identity, new Vector2(0.3f, 0.05f), new Color(0.75f, 0.08f, 0.08f));
            var bz = Zone("EvidenceBagZone", bag.transform, bag.transform.position + Vector3.up * 0.08f, new Vector3(0.34f, 0.16f, 0.26f), "evidence bag", ItemKind.Implant);
            bz.gameObject.AddComponent<ZoneTask>().task = TaskId.SRV_Bag;

            // switch console on the far wall, backup + report on the cart by the entrance
            var sw = WallMonitor("SwitchConsole", new Vector3(-ext.x + 0.03f, 1.45f, 0), Quaternion.Euler(0, -90, 0), 0.0006f);
            sw.parent.parent.gameObject.AddComponent<OpsConsole>().screen = sw;
            sw.parent.parent.GetComponent<OpsConsole>().mode = OpsConsole.Mode.Switch;
            var cart = new GameObject("OpsCart").transform;
            cart.SetParent(env);
            cart.position = new Vector3(2.55f, 0, 1.75f);
            LocalBox("Top", cart, new Vector3(0, 0.85f, 0), new Vector3(1.6f, 0.03f, 0.6f), tray, true);
            foreach (var lx in new[] { -0.75f, 0.75f }) foreach (var lz in new[] { -0.27f, 0.27f })
                LocalBox("Leg", cart, new Vector3(lx, 0.425f, lz), new Vector3(0.03f, 0.85f, 0.03f), tray, false);
            var backup = DeskMonitor("BackupConsole", new Vector3(2.15f, 0.865f, 1.82f), 0, 0.00046f, true, out var bRoot);
            bRoot.gameObject.AddComponent<OpsConsole>().screen = backup;
            bRoot.GetComponent<OpsConsole>().mode = OpsConsole.Mode.Backup;
            var report = DeskMonitor("ReportConsole", new Vector3(2.95f, 0.865f, 1.82f), 0, 0.00046f, true, out var rRoot);
            rRoot.gameObject.AddComponent<OpsConsole>().screen = report;
            rRoot.GetComponent<OpsConsole>().mode = OpsConsole.Mode.Report;
            Label(env, "AUTHORISED STAFF ONLY", new Vector3(ext.x - 0.02f, 2.35f, -1.75f), Quaternion.Euler(0, 90, 0), new Vector2(0.9f, 0.12f), new Color(1f, 0.75f, 0.2f), true);
            Model(Root + "/Models/PolyHaven/security_camera_01/security_camera_01_1k.fbx", env, new Vector3(-3.3f, 2.7f, 2.3f), 135, height: 0.3f, collider: false);
            Model(Root + "/Models/PolyHaven/fire_alarm/fire_alarm_1k.fbx", env, new Vector3(ext.x - 0.02f, 1.4f, 0.9f), 270, height: 0.13f, collider: false);

            // the flashlight lies on the ops cart beside the entrance; grabbing it switches it on
            var torch = new GameObject("Flashlight");
            torch.transform.SetParent(props);
            torch.transform.SetPositionAndRotation(new Vector3(2.55f, 0.895f, 1.79f), Quaternion.Euler(0, 35, 0));
            var torchBody = Mat("TorchGrip", Palette.Hex("F2C230"), 0.4f);
            var torchHead = Mat("TorchHead", Palette.Hex("2B2F36"), 0.5f, 0.6f);
            var torchLens = Mat("TorchLens", new Color(0.85f, 0.85f, 0.8f), 0.9f, 0, Color.black);
            Cylinder("Body", torch.transform, new Vector3(0, 0, -0.02f), 0.018f, 0.14f, torchBody);
            Cylinder("Head", torch.transform, new Vector3(0, 0, 0.07f), 0.028f, 0.05f, torchHead);
            var lensGo = Cylinder("Lens", torch.transform, new Vector3(0, 0, 0.0955f), 0.024f, 0.004f, torchLens);
            var tc = torch.AddComponent<BoxCollider>();
            tc.size = new Vector3(0.06f, 0.06f, 0.2f);
            Grab(torch, ItemKind.Flashlight, "Flashlight", "A torch. It straps to your wrist and points where your hand points. (F or right stick click: on/off)", null);
            var beam = new GameObject("Beam").AddComponent<Light>();
            beam.transform.SetParent(torch.transform, false);
            beam.transform.localPosition = new Vector3(0, 0, 0.1f);
            beam.type = LightType.Spot;
            beam.lightmapBakeType = LightmapBakeType.Realtime;
            beam.shadows = LightShadows.None;
            beam.spotAngle = 70;
            beam.innerSpotAngle = 30;
            beam.range = 14;
            beam.intensity = 26f;
            beam.color = new Color(1f, 0.96f, 0.88f);
            var findMe = RealtimePoint("FindMeGlow", torch.transform, torch.transform.position + Vector3.up * 0.12f, new Color(0.4f, 1f, 0.7f), 0.8f, 1.6f);
            var handGlow = RealtimePoint("HandGlow", torch.transform, torch.transform.position, new Color(1f, 0.95f, 0.85f), 1.4f, 2.4f);
            var fl = torch.AddComponent<Flashlight>();
            fl.beam = beam;
            fl.handGlow = handGlow;
            fl.lens = lensGo.GetComponent<Renderer>();
            fl.findMeGlow = findMe;

            AddKnock(door, DoorKnock.Mode.Exit, new Vector3(ext.x, 1.0f, -1.75f), "PRESS  P  AT THE DOOR WHEN THE INCIDENT IS CLOSED", new Vector3(ext.x - 0.02f, 2.62f, -1.75f), 90);

            CommonUI(game, head, new Vector3(1.45f, 1.55f, 0), -90);
            Hotspots(new Vector3(2.55f, 0, 0), new Vector3(1.0f, 0, 0), new Vector3(-0.7f, 0, 0), new Vector3(-2.7f, 0, 0), new Vector3(2.2f, 0, 0.9f), new Vector3(1.4f, 0, -0.5f));
            var d = game.GetComponent<ChapterDirector>();
            d.steps = new[]
            {
                Step(TaskId.SRV_Flashlight, "Flashlight", null, "Flashlight"),
                Step(TaskId.SRV_Find, "Inspect this", null, "Implant"),
                Step(TaskId.SRV_Unplug, "Pull this plug", null, "Plug"),
                Step(TaskId.SRV_Bag, "The implant", "EvidenceBag", "Implant"),
                Step(TaskId.SRV_Isolate, "Switch console", null, "SwitchConsole"),
                Step(TaskId.SRV_Restore, "Backup console", null, "BackupConsole"),
                Step(TaskId.SRV_Report, "Incident report", null, "ReportConsole"),
            };
            d.steps[1].revealDelay = 30f;
            d.steps[3].dropLabel = "Evidence bag";
            FinishScene(scene, "Ch3_ServerRoom", head, bake);
        }

        /// 42U rack: dark frame, server units with status LEDs, a top-of-rack switch with patch cables.
        static Transform Rack(Vector3 pos, float yaw, int seed)
        {
            var rng = new System.Random(seed);
            var frameMat = AcgMat("RackFrame", "Metal009", Palette.Hex("1E2228"), Vector2.one, 0.55f, 0.8f);
            var unitMat = AcgMat("ServerFace", "Metal009", Palette.Hex("3A3F47"), new Vector2(2, 0.2f), 0.6f, 0.8f);
            var blank = Mat("BlankPanel", Palette.Hex("15181D"), 0.4f);
            var ledG = Mat("LedGreen", Color.black, 0.5f, 0, new Color(0.2f, 2.2f, 0.6f));
            var ledB = Mat("LedBlue", Color.black, 0.5f, 0, new Color(0.2f, 0.6f, 2.4f));
            var ledA = Mat("LedAmber", Color.black, 0.5f, 0, new Color(2.2f, 1.2f, 0.1f));
            var port = Mat("SwitchPort", Palette.Hex("0B0C0E"), 0.3f);
            var r = new GameObject("Rack").transform;
            r.SetParent(env);
            r.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            // side panels + top + back (front stays open so the servers are visible)
            LocalBox("SideL", r, new Vector3(-0.3f, 1.0f, 0), new Vector3(0.02f, 2.0f, 1.0f), frameMat, true);
            LocalBox("SideR", r, new Vector3(0.3f, 1.0f, 0), new Vector3(0.02f, 2.0f, 1.0f), frameMat, true);
            LocalBox("Top", r, new Vector3(0, 1.99f, 0), new Vector3(0.62f, 0.02f, 1.0f), frameMat, false);
            LocalBox("Back", r, new Vector3(0, 1.0f, -0.49f), new Vector3(0.58f, 2.0f, 0.02f), frameMat, true);
            LocalBox("Base", r, new Vector3(0, 0.04f, 0), new Vector3(0.62f, 0.08f, 1.0f), frameMat, false);
            // solid body so the player can't walk through the servers (the front shelf zone at z > 0.3 stays free for items)
            var body = new GameObject("RackBody");
            body.transform.SetParent(r, false);
            var bcol = body.AddComponent<BoxCollider>();
            bcol.center = new Vector3(0, 1.0f, -0.1f);
            bcol.size = new Vector3(0.62f, 2.0f, 0.8f);
            // front of the rack faces local +z (rack local z = 0.5 is the front edge)
            float y = 0.12f;
            while (y < 1.82f)
            {
                int units = rng.Next(1, 3);
                float h = units * 0.0445f - 0.004f;
                bool isBlank = rng.NextDouble() < 0.18;
                LocalBox("Unit", r, new Vector3(0, y + h / 2, 0.47f), new Vector3(0.56f, h, 0.04f), isBlank ? blank : unitMat, false);
                if (!isBlank)
                {
                    int leds = rng.Next(2, 5);
                    for (int i = 0; i < leds; i++)
                    {
                        var m = rng.NextDouble() < 0.12 ? ledA : rng.NextDouble() < 0.5 ? ledB : ledG;
                        LocalBox("Led", r, new Vector3(0.2f - i * 0.022f, y + h * 0.5f, 0.492f), new Vector3(0.008f, 0.006f, 0.004f), m, false);
                    }
                }
                y += units * 0.0445f;
            }
            // top-of-rack switch with ports and a few patch cables
            LocalBox("Switch", r, new Vector3(0, 1.9f, 0.47f), new Vector3(0.56f, 0.042f, 0.04f), unitMat, false);
            for (int i = 0; i < 12; i++)
            {
                LocalBox("Port", r, new Vector3(-0.22f + i * 0.036f, 1.9f, 0.4915f), new Vector3(0.022f, 0.016f, 0.004f), port, false);
                if (rng.NextDouble() < 0.6)
                    LocalBox("Patch", r, new Vector3(-0.22f + i * 0.036f, 1.88f, 0.5f), new Vector3(0.008f, 0.06f, 0.01f), rng.NextDouble() < 0.5 ? ledB : Mat("CableBlue", Palette.Hex("2E5FA8"), 0.4f), false);
            }
            return r;
        }

        static RectTransform WallMonitor(string name, Vector3 pos, Quaternion rot, float scale)
        {
            var bezel = Mat("Bezel", new Color(0.05f, 0.05f, 0.055f), 0.75f);
            var root = new GameObject(name).transform;
            root.SetParent(props);
            root.SetPositionAndRotation(pos, rot);
            var pivot = new GameObject("Screen").transform;
            pivot.SetParent(root, false);
            float w = 1280 * scale, h = 800 * scale;
            FramedScreen(pivot, Vector2.zero, w, h, bezel, -0.015f, 0.02f, 0.03f);
            var c = MakeCanvas(name + "Canvas", pivot, new Vector3(0, 0, -0.015f), Quaternion.identity, new Vector2(1280, 800), scale);   // bezel front face is at -0.015: 0.8 mm out
            AddCanvasInteraction(c.gameObject, root, poke: true);
            return c;
        }
    }
}
