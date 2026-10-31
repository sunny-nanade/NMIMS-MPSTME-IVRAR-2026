using System;
using System.Collections;
using System.IO;
using System.Linq;
using RescueSim.Core;
using RescueSim.Hazards;
using RescueSim.Survivors;
using RescueSim.Triage;
using RescueSim.UI;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using Object = UnityEngine.Object;

namespace RescueSim.EditorTools.Recording
{
    /// <summary>
    /// EDITOR-ONLY, play-mode-only tool for the SIMULATED first-person demo videos (no headset involved).
    /// It opens a real scene, enters play mode, switches off head/controller tracking drivers (runtime only), moves a
    /// first-person camera and the two controller transforms along a scripted route, and drives the game ONLY through
    /// its own public APIs: the XR Interaction Toolkit manager (SelectEnter/SelectExit on the rig's NearFarInteractor),
    /// the UI button onClick, and the triage manager's Examine/Assign (the same calls the buttons and keyboard make).
    /// Mission/rescue logic is never bypassed. Frames are rendered from the rig's own camera into a 1280x832 texture
    /// and appended to one MJPEG file under Recordings/SimulatedDemos/. Nothing in any scene or asset is modified.
    /// </summary>
    public static class SimulatedDemoRecorder
    {
        const string JobKey = "RescueSim.SimDemo.Job";
        public const int Width = 1280, Height = 832, Fps = 30;

        [MenuItem("Rescue Sim/Recording/Record Flood Simulated Demo")] static void Flood() => Start("flood", "Assets/_Project/Scenes/FloodEvacuation.unity");
        [MenuItem("Rescue Sim/Recording/Record Triage Simulated Demo")] static void Triage() => Start("triage", "Assets/_Project/Scenes/TriageSimulation.unity");
        [MenuItem("Rescue Sim/Recording/Record Factory Simulated Demo")] static void Factory() => Start("factory", "Assets/_Project/Scenes/FactoryRescue.unity");
        [MenuItem("Rescue Sim/Recording/Record Earthquake Simulated Demo")] static void Quake() => Start("quake", "Assets/_Project/Scenes/EarthquakeRescue.unity");
        [MenuItem("Rescue Sim/Recording/Probe (10 s test clip in open scene)")] static void Probe() => Start("probe", null);

        static void Start(string job, string scene)
        {
            if (EditorApplication.isPlaying) return;
            SessionState.SetString(JobKey, job);
            if (scene != null) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredPlayMode) return;
                string job = SessionState.GetString(JobKey, "");
                if (string.IsNullOrEmpty(job)) return;
                SessionState.SetString(JobKey, "");
                var go = new GameObject("~SimulatedDemoRecorder");
                go.hideFlags = HideFlags.DontSave;
                go.AddComponent<Driver>().Job = job;
            };
        }

        sealed class Driver : MonoBehaviour
        {
            public string Job;
            XROrigin _origin; Camera _cam; Transform _left, _right;
            NearFarInteractor _rh;
            RenderTexture _rt; Texture2D _tex; FileStream _out;
            float _yaw, _pitch;
            int _frames;
            string _path;

            IEnumerator Start()
            {
                yield return null; yield return null;
                _origin = FindFirstObjectByType<XROrigin>();
                _cam = _origin.Camera;
                foreach (var d in FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None)) d.enabled = false;
                foreach (var l in FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None)) l.enabled = false;
                foreach (var c in FindObjectsByType<CharacterController>(FindObjectsSortMode.None)) c.enabled = false;
                foreach (var t in _origin.GetComponentsInChildren<Transform>(true))
                { if (t.name == "Left Controller" && _left == null) _left = t; if (t.name == "Right Controller" && _right == null) _right = t; }
                var hands = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var h in hands) if (!h.gameObject.activeInHierarchy) for (var a = h.transform; a != null && !a.gameObject.activeSelf; a = a.parent) a.gameObject.SetActive(true);
                _rh = hands.FirstOrDefault(i => i.transform.IsChildOf(_right));
                Debug.Log($"[SimulatedDemoRecorder] left={_left?.name} right={_right?.name} nearFar={hands.Length} rightInteractor={_rh?.name}");
                if (_rh == null) throw new InvalidOperationException("No NearFarInteractor under the right controller");

                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "SimulatedDemos"));
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "_" + Job + ".mjpeg");
                _out = new FileStream(_path, FileMode.Create);
                _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                _tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                _cam.transform.localPosition = new Vector3(0f, 1.65f, 0f);   // a tracked headset sits above the origin (VRPanel treats it as tracked)
                _cam.targetTexture = _rt;
                _cam.fieldOfView = 68f;
                Time.captureFramerate = Fps;
                Application.onBeforeRender += ReapplyHands;

                _yaw = 0f; _pitch = 0f;
                Place(_origin.transform.position + Vector3.up * 1.65f, 0, 0);
                yield return Frames(15);   // let the scene settle
                _frames = 0; _out.SetLength(0);

                IEnumerator run = Job == "flood" ? FloodDemo() : Job == "triage" ? TriageDemo() : Job == "factory" ? FactoryDemo() : Job == "quake" ? QuakeDemo() : ProbeDemo();
                yield return run;
                Finish();
            }

            void Finish()
            {
                _out?.Flush(); _out?.Dispose();
                Application.onBeforeRender -= ReapplyHands;
                if (_cam != null) _cam.targetTexture = null;
                Time.captureFramerate = 0;
                Debug.Log($"[SimulatedDemoRecorder] {Job}: {_frames} frames -> {_path}");
                File.WriteAllText(_path + ".done", _frames.ToString());
                EditorApplication.ExitPlaymode();
            }

            Transform ControllerRoot(Transform t)
            {
                var offset = _origin.CameraFloorOffsetObject != null ? _origin.CameraFloorOffsetObject.transform : _origin.transform;
                while (t.parent != null && t.parent != offset && t.parent != _origin.transform) t = t.parent;
                return t;
            }

            // ---- capture ------------------------------------------------------------------------------------------

            IEnumerator Capture()
            {
                yield return new WaitForEndOfFrame();
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                _tex.Apply(false);
                RenderTexture.active = prev;
                byte[] jpg = _tex.EncodeToJPG(92);
                _out.Write(jpg, 0, jpg.Length);
                _frames++;
                if (_frames % 60 == 0) Log($"f{_frames} head={Head:0.0} yaw={_yaw:0}");
            }

            IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return Capture(); }
            IEnumerator Seconds(float s) { return Frames(Mathf.RoundToInt(s * Fps)); }

            // ---- camera + controllers -----------------------------------------------------------------------------

            Vector3 Head => _cam.transform.position;

            void Place(Vector3 head, float yaw, float pitch)
            {
                _yaw = yaw; _pitch = pitch;
                _origin.MoveCameraToWorldLocation(head);
                _cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            float GroundUnder(Vector3 p, float fallback)
            {
                var hits = Physics.RaycastAll(p + Vector3.up * 4f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                foreach (var h in hits)
                {
                    if (h.collider.GetComponentInParent<Survivor>() != null) continue;
                    if (h.collider.GetComponentInParent<XROrigin>() != null) continue;
                    if (h.rigidbody != null && !h.rigidbody.isKinematic) continue;
                    if (h.collider.GetComponentInParent<XRGrabInteractable>() != null) continue;   // held / grabbable props are not floor
                    if (h.point.y > p.y - 0.9f) continue;               // roofs, overhead geometry and anything not below the eyes
                    best = Mathf.Max(best, h.point.y);
                }
                return float.IsNegativeInfinity(best) ? fallback : best;
            }

            /// <summary>Walk the head (eye height 1.65 m over the ground) to a ground position at the given speed, yawing toward travel or a look target.</summary>
            IEnumerator Walk(Vector3 groundTarget, float speed = 2.3f, Vector3? lookAt = null, float pitch = 4f)
            {
                while (true)
                {
                    Vector3 h = Head; Vector3 flat = new Vector3(groundTarget.x - h.x, 0, groundTarget.z - h.z);
                    float dist = flat.magnitude;
                    if (dist < 0.05f) break;
                    Vector3 step = flat.normalized * Mathf.Min(dist, speed / Fps);
                    Vector3 np = h + step;
                    float gy = GroundUnder(np, groundTarget.y);
                    np.y = Mathf.Lerp(h.y, gy + 1.65f, 0.25f);
                    float wantYaw = lookAt.HasValue ? YawTo(lookAt.Value) : Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    float yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, 70f / Fps);
                    float p = Mathf.MoveTowards(_pitch, pitch, 25f / Fps);
                    Place(np, yaw, p);
                    PoseHandsIdle();
                    yield return Capture();
                }
            }

            float YawTo(Vector3 p) { Vector3 d = p - Head; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

            IEnumerator Look(float yaw, float pitch, float seconds)
            {
                float y0 = _yaw, p0 = _pitch; int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
                for (int i = 1; i <= n; i++)
                {
                    float t = Mathf.SmoothStep(0, 1, i / (float)n);
                    Place(Head, Mathf.LerpAngle(y0, yaw, t), Mathf.Lerp(p0, pitch, t));
                    PoseHandsIdle();
                    yield return Capture();
                }
            }

            IEnumerator LookAt(Vector3 p, float seconds)
            {
                Vector3 d = p - Head; float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                yield return Look(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Clamp(pitch, -35f, 45f), seconds);
            }

            bool _leftRaised; Vector3? _rightTarget; bool _rightFollows; Func<Vector3> _rightPos;

            void PoseHandsIdle()
            {
                Quaternion yawRot = Quaternion.Euler(0, _yaw, 0);
                Vector3 h = Head;
                // Left controller: raised into view for the wrist HUD, otherwise resting low at the side.
                Vector3 lp = _leftRaised ? h + yawRot * new Vector3(-0.18f, -0.22f, 0.42f) : h + yawRot * new Vector3(-0.22f, -0.65f, 0.15f);
                Quaternion lr = _leftRaised ? yawRot * Quaternion.Euler(-20f, 0f, 90f) : yawRot * Quaternion.Euler(10f, 0f, 0f);
                _lp = lp; _lr = lr;
                _left.SetPositionAndRotation(lp, lr);
                Vector3 rp = _rightPos != null ? _rightPos() : h + yawRot * new Vector3(0.22f, -0.55f, 0.28f);
                Quaternion rr;
                if (_rightTarget.HasValue) rr = Quaternion.LookRotation((_rightTarget.Value - rp).normalized, Vector3.up);
                else rr = yawRot * Quaternion.Euler(25f, 0f, 0f);
                _rp = rp; _rr = rr;
                _right.SetPositionAndRotation(rp, rr);
            }

            Vector3 _lp, _rp; Quaternion _lr = Quaternion.identity, _rr = Quaternion.identity;

            // Re-applied just before rendering so no other pose source (simulated device input) can override the demo poses.
            void ReapplyHands()
            {
                if (_left == null || _right == null) return;
                _left.SetPositionAndRotation(_lp, _lr);
                _right.SetPositionAndRotation(_rp, _rr);
            }

            IEnumerator RaiseWrist(float seconds) { _leftRaised = true; yield return Seconds(seconds); _leftRaised = false; }

            // ---- interaction (real XRI calls) ---------------------------------------------------------------------

            IEnumerator SelectHold(XRBaseInteractable target, float seconds, Func<Vector3> handPos = null)
            {
                _rightTarget = target.transform.position; _rightPos = handPos ?? (() => Vector3.Lerp(Head, target.transform.position, 0.55f) + Vector3.down * 0.15f);
                yield return Seconds(0.5f);                       // hand reaches / ray settles on the target
                var mgr = _rh.interactionManager;
                mgr.SelectEnter((IXRSelectInteractor)_rh, (IXRSelectInteractable)target);
                int n = Mathf.RoundToInt(seconds * Fps);
                for (int i = 0; i < n; i++)
                {
                    if (!target.isSelected) mgr.SelectEnter((IXRSelectInteractor)_rh, (IXRSelectInteractable)target);
                    PoseHandsIdle();
                    yield return Capture();
                }
            }

            void ReleaseSel(XRBaseInteractable target)
            {
                if (target.isSelected) _rh.interactionManager.SelectExit((IXRSelectInteractor)_rh, (IXRSelectInteractable)target);
            }

            void HandsRest() { _rightTarget = null; _rightPos = null; }

            // ---- demos ----------------------------------------------------------------------------------------------

            void Log(string m) { File.AppendAllText(_path + ".log", m + "\n"); }

            IEnumerator ProbeDemo()
            {
                Log($"left={_left?.name} active={_left?.gameObject.activeInHierarchy} right={_right?.name} cam={_cam.transform.position} fov={_cam.fieldOfView}");
                var hud = FindFirstObjectByType<RescueSim.UI.WristHud>(FindObjectsInactive.Include);
                var mc = FindFirstObjectByType<MissionController>();
                Log($"hud={(hud != null ? hud.name : "null")} mission={mc?.State}");
                var ui = FindFirstObjectByType<MissionUI>();
                if (ui != null) ui.Briefing.GetComponentsInChildren<Button>(true).First().onClick.Invoke();
                yield return Seconds(1f);
                Log($"mission={mc?.State}");
                _leftRaised = true;
                for (int i = 0; i < 60; i++)
                {
                    PoseHandsIdle();
                    yield return Capture();
                    if (i % 15 == 0 && hud != null)
                    {
                        var g = hud.GetComponentInChildren<CanvasGroup>(true);
                        Log($"i={i} leftPos={_left.position} head={Head} hudPos={hud.transform.position} alpha={g?.alpha} leftRenderers={_left.GetComponentsInChildren<Renderer>(true).Length} enabledRend={_left.GetComponentsInChildren<Renderer>().Count(r => r.enabled)}");
                    }
                }
                _leftRaised = false;
            }


            // ---- shared helpers for the Factory / Earthquake demos ----------------------------------------------------

            /// <summary>Walk along a NavMesh path (falls back to a straight line when none exists).</summary>
            IEnumerator WalkNav(Vector3 target, float speed = 2.3f, float pitch = 4f, Vector3? lookAt = null)
            {
                Vector3 from = new Vector3(Head.x, GroundUnder(Head, 0f), Head.z);
                var path = new NavMeshPath();
                if (NavMesh.SamplePosition(from, out var a, 4f, NavMesh.AllAreas) && NavMesh.SamplePosition(target, out var b, 4f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length > 1)
                {
                    Log("nav " + from + " -> " + target + " corners=" + string.Join(";", path.corners.Select(c => c.ToString("0.0"))));
                    for (int i = 1; i < path.corners.Length; i++) yield return Walk(path.corners[i], speed, lookAt, pitch);
                }
                else
                {
                    Log("NavMesh path unavailable, straight walk to " + target);
                    yield return Walk(target, speed, lookAt, pitch);
                }
            }

            IEnumerator Rescue(Survivor s, ExitZone zone, float carrySpeed = 1.9f)
            {
                Vector3 p = s.transform.position;
                Vector3 side = new Vector3(Head.x - p.x, 0, Head.z - p.z).normalized;
                Vector3 stand = p + side * 2.6f;
                yield return WalkNav(stand, 2.2f, 6f, p);
                yield return Walk(p + side * 1.9f, 1.4f, p, 10f);
                yield return Look(YawTo(p), 12f, 1.0f);
                float guard = 0; while (s.State == SurvivorState.Unfound && guard < 3f) { guard += 1f / Fps; yield return Capture(); }
                var si = s.GetComponent<XRSimpleInteractable>();
                yield return SelectHold(si, 3.6f, () => Vector3.Lerp(Head, s.BodyCenter, 0.6f));
                Log($"{s.Name}: state after hold = {s.State}");
                _rightTarget = null; _rightPos = () => Head + Quaternion.Euler(0, _yaw, 0) * new Vector3(0.25f, -0.55f, 0.45f);
                Vector3 dest = zone.transform.position; dest.y = GroundUnder(zone.transform.position, 0f);
                yield return WalkNav(dest, carrySpeed, 28f);
                int g2 = 0; while (!zone.Contains(s.BodyCenter) && g2++ < 6 * Fps) yield return Capture();
                yield return Seconds(1.0f);
                ReleaseSel(si); HandsRest();
                yield return Seconds(2.5f);
                Log($"{s.Name}: final state = {s.State}");
                yield return RaiseWrist(2.0f);
            }

            IEnumerator Prelude(MissionController m, MissionUI ui)
            {
                Place(Head, _yaw, 0f);
                yield return Seconds(4f);
                ui.Briefing.GetComponentsInChildren<Button>(true).First().onClick.Invoke();
                yield return Seconds(1.5f);
                yield return RaiseWrist(3f);
                float y0 = _yaw;
                yield return Look(y0 - 50f, 3f, 2.5f); yield return Look(y0 + 50f, 3f, 3.5f); yield return Look(y0, 3f, 2f);
            }

            IEnumerator FactoryDemo()
            {
                var m = FindFirstObjectByType<MissionController>();
                var ui = FindFirstObjectByType<MissionUI>();
                _yaw = _cam.transform.eulerAngles.y;
                yield return Prelude(m, ui);

                // Cut the power at the main breaker (real select on its lever).
                var breaker = FindFirstObjectByType<RescueSim.Interaction.MainBreaker>();
                var bi = breaker.GetComponent<XRSimpleInteractable>();
                Vector3 bp = breaker.transform.position;
                Vector3 bside = new Vector3(Head.x - bp.x, 0, Head.z - bp.z).normalized;
                yield return WalkNav(bp + bside * 1.3f, 2.3f, 6f, bp);
                yield return LookAt(breaker.transform.position, 1.0f);
                yield return SelectHold(bi, 0.5f); ReleaseSel(bi); HandsRest();
                yield return Seconds(2f);
                Log("power live after breaker: " + breaker.IsLive);

                // Extinguisher: grab it, walk to the electrical fire and spray (trigger handler, real Update spray).
                var ext = FindFirstObjectByType<RescueSim.Interaction.Extinguisher>();
                var eg = ext.GetComponent<XRGrabInteractable>();
                yield return WalkNav(ext.transform.position - Vector3.forward * 1.1f, 2.3f, 12f, ext.transform.position);
                yield return LookAt(ext.transform.position, 1.0f);
                yield return SelectHold(eg, 1.2f);
                var fire = m.Hazards.OfType<FireHazard>().OrderBy(f => f.name.IndexOf("Electric", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1).First();
                Vector3 fp = fire.transform.position;
                Vector3 fside = new Vector3(Head.x - fp.x, 0, Head.z - fp.z).normalized;
                yield return WalkNav(fp + fside * 3.2f, 2.0f, 8f, fp);
                yield return Walk(fp + fside * 2.3f, 1.2f, fp, 10f);
                _rightPos = () => Head + Quaternion.Euler(0, _yaw, 0) * new Vector3(0.2f, -0.35f, 0.5f);
                _rightTarget = fp + Vector3.up * 0.4f;
                yield return Seconds(0.8f);
                eg.activated.Invoke(new ActivateEventArgs());                  // trigger pressed (real XRI activate event)
                int guardF = 0; while (fire.IsActive && guardF++ < 14 * Fps) { PoseHandsIdle(); yield return Capture(); }
                Log($"fire active after spray: {fire.IsActive} (took {guardF / (float)Fps:0.0}s)");
                eg.deactivated.Invoke(new DeactivateEventArgs());
                yield return Seconds(1.5f);
                ReleaseSel(eg); HandsRest();
                yield return Seconds(1f);

                // Rescue the first worker with the new carry mechanic.
                var zone = FindFirstObjectByType<ExitZone>();
                var dana = m.Survivors.OrderBy(x => string.IsNullOrEmpty(typeof(Survivor).GetField("requiresObjective", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(x) as string) ? 0 : 1).First();
                yield return Rescue(dana, zone);
                yield return Look(_yaw, 0f, 1.0f);
                yield return RaiseWrist(3f);
                yield return Seconds(2f);
            }

            IEnumerator QuakeDemo()
            {
                var m = FindFirstObjectByType<MissionController>();
                var ui = FindFirstObjectByType<MissionUI>();
                _yaw = _cam.transform.eulerAngles.y;
                yield return Prelude(m, ui);
                var zone = FindFirstObjectByType<ExitZone>();

                // Walk the corridor through the real zone objectives, then bring the first survivor out.
                yield return WalkNav(new Vector3(0f, 0f, 8.2f), 2.3f, 4f);
                yield return WalkNav(new Vector3(0f, 0f, 12.2f), 2.3f, 4f);
                var free = m.Survivors.OrderBy(x => string.IsNullOrEmpty(typeof(Survivor).GetField("requiresObjective", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(x) as string) ? 0 : 1).ToList();
                yield return Rescue(free[0], zone);

                // Clear the blocked corridor: grab each block and pull it out of the passage (real XRGrabInteractable grabs).
                yield return WalkNav(new Vector3(0f, 0f, 12.0f), 2.3f, 8f);
                var monitor = FindFirstObjectByType<RescueSim.Interaction.PassageMonitor>();
                int k = 0;
                foreach (var d in FindObjectsByType<RescueSim.Interaction.DebrisItem>(FindObjectsSortMode.None).OrderByDescending(x => x.transform.position.y).ToList())
                {
                    var g = d.GetComponent<XRGrabInteractable>();
                    if (g == null || !monitor.Contains(d.transform.position)) continue;
                    yield return LookAt(d.transform.position, 0.6f);
                    float lateral = ((k++ % 4) - 1.5f) * 0.55f;
                    yield return SelectHold(g, 0.3f, () => Vector3.Lerp(Head, d.transform.position, 0.7f));
                    _rightPos = () => Head + Quaternion.Euler(0, _yaw, 0) * new Vector3(lateral, -0.5f, -0.1f) + Vector3.down * 0.0f;
                    _rightTarget = null;
                    yield return Seconds(0.9f);
                    ReleaseSel(g); HandsRest();
                    yield return Seconds(0.5f);
                }
                monitor.Evaluate();
                Log("passage cleared: " + monitor.IsCleared);
                yield return Seconds(1f);
                yield return RaiseWrist(2.5f);

                // Second survivor, now reachable.
                if (m.State != MissionState.Completed && free.Count > 1) yield return Rescue(free[1], zone);
                yield return Look(_yaw, 0f, 1.5f);
                yield return Seconds(8f);
            }

            IEnumerator FloodDemo()
            {
                var m = FindFirstObjectByType<MissionController>();
                var ui = FindFirstObjectByType<MissionUI>();
                Place(Head, 0f, 0f);
                yield return Seconds(4f);                                           // briefing panel in view
                ui.Briefing.GetComponentsInChildren<Button>(true).First().onClick.Invoke();   // real Begin button
                yield return Seconds(1.5f);
                yield return RaiseWrist(3f);                                        // checklist on the wrist
                yield return Look(-45f, 3f, 2.5f); yield return Look(45f, 3f, 3.5f); yield return Look(0f, 3f, 2f);   // look around the flooded street

                // Rescue buoy (real XRGrabInteractable grab).
                var buoy = FindFirstObjectByType<RescueSim.Interaction.PickupObjective>();
                var bg = buoy.GetComponent<XRGrabInteractable>();
                yield return Walk(new Vector3(1.4f, 0f, -0.9f), 1.7f, buoy.transform.position, 12f);
                yield return LookAt(buoy.transform.position, 1.2f);
                yield return SelectHold(bg, 1.6f);
                ReleaseSel(bg); HandsRest();
                yield return Seconds(1.5f);
                yield return RaiseWrist(2.5f);

                // Follow the marked route to each civilian, then carry them to the safe zone.
                yield return Walk(new Vector3(0f, 0f, 14f), 2.4f);
                var zone = FindObjectsByType<ExitZone>(FindObjectsSortMode.None).First();
                foreach (var s in m.Survivors.OrderBy(x => x.transform.position.z))
                {
                    Vector3 p = s.transform.position;
                    Vector3 approach = p + (new Vector3(0, 0, p.z).normalized * 0) + new Vector3(p.x > 0 ? -2.6f : 2.6f, 0, -0.3f);
                    approach.y = p.y;
                    yield return Walk(new Vector3(0f, 0f, p.z - 2f), 2.4f, p, 4f);
                    yield return Walk(approach, 1.8f, p, 8f);
                    yield return LookAt(p, 1.0f);
                    float guard = 0; while (s.State == SurvivorState.Unfound && guard < 3f) { guard += 1f / Fps; yield return Capture(); }
                    var si = s.GetComponent<XRSimpleInteractable>();
                    yield return SelectHold(si, 3.6f, () => Vector3.Lerp(Head, s.BodyCenter, 0.6f));      // check ring, then lift
                    // Carry: hand follows the casualty while the head walks to the safe zone.
                    _rightTarget = null; _rightPos = () => Head + Quaternion.Euler(0, _yaw, 0) * new Vector3(0.25f, -0.55f, 0.45f);
                    Vector3 dest = zone.transform.position; dest.y = 0f;
                    Vector3 rampBase = new Vector3(0f, 0f, 38f);
                    yield return Walk(new Vector3(0f, 0f, p.z), 1.9f, null, 28f);
                    yield return Walk(rampBase, 2.0f, null, 28f);
                    yield return Walk(new Vector3(0f, zone.transform.position.y - 1.5f, zone.transform.position.z - 1f), 1.6f, null, 28f);
                    int guard2 = 0; while (!zone.Contains(s.BodyCenter) && guard2++ < 6 * Fps) yield return Capture();
                    yield return Seconds(1.0f);                                                          // glow + "release to deliver"
                    ReleaseSel(si); HandsRest();
                    yield return Seconds(2.5f);                                                          // delivery notification + counter
                    yield return RaiseWrist(2.0f);
                    if (m.State == MissionState.Completed) break;
                    yield return Walk(new Vector3(0f, zone.transform.position.y - 1.5f, 40f), 1.8f);
                }
                yield return Look(_yaw, 0f, 1.5f);
                yield return Seconds(8f);                                                                // results panel / feedback
            }

            IEnumerator TriageDemo()
            {
                var m = FindFirstObjectByType<STARTTriageSimulationManager>();
                var ui = FindFirstObjectByType<TriageUI>();
                string tmp = Path.Combine(Path.GetTempPath(), "simdemo_triage_" + Guid.NewGuid().ToString("N"));
                m.Logger.SetDirectoryOverride(tmp);
                Place(Head, 0f, 0f);
                yield return Seconds(3f);                                            // start panel
                yield return Look(-60f, 2f, 3f); yield return Look(60f, 2f, 4f); yield return Look(0f, 2f, 2f);     // casualty layout
                Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(b => b.name == "Button_Begin Exercise").onClick.Invoke();
                yield return Seconds(2f);

                // One casualty per START outcome (Green, Yellow, Red, Black), chosen with the exercise's own rule engine.
                var picks = new System.Collections.Generic.List<CasualtyProfile>();
                foreach (var cat in new[] { TriageCategory.Green, TriageCategory.Yellow, TriageCategory.Red, TriageCategory.Black })
                {
                    var found = m.Casualties.FirstOrDefault(x => STARTEvaluator.Evaluate(x.Vitals, m.Perfusion, true).category == cat && !picks.Contains(x));
                    if (found != null) picks.Add(found);
                }
                Log("picks: " + string.Join(", ", picks.Select(x => x.Id)));
                foreach (var c in picks)
                {
                    Vector3 cp = c.transform.position;
                    Vector3 gp = new Vector3(cp.x, 0f, cp.z) - (new Vector3(cp.x, 0, cp.z) - new Vector3(Head.x, 0, Head.z)).normalized * 3.2f;
                    yield return Walk(gp, 1.9f, cp, 10f);
                    yield return Look(YawTo(cp), 10f, 1.0f);
                    var sel = c.GetComponentInChildren<XRSimpleInteractable>(true) ?? c.GetComponentInParent<XRSimpleInteractable>();
                    _rightTarget = cp + Vector3.up * 0.3f; _rightPos = null;
                    yield return Seconds(0.9f);                                       // ray settles on the casualty
                    _rh.interactionManager.SelectEnter((IXRSelectInteractor)_rh, (IXRSelectInteractable)sel);   // trigger press
                    yield return Seconds(0.3f);
                    ReleaseSel(sel);
                    yield return Seconds(1.6f);                                       // assessment panel appears
                    var v = c.Vitals;
                    var res = STARTEvaluator.Evaluate(v, m.Perfusion, true);
                    ExamKind[] exams;
                    switch (res.branch)
                    {
                        case TriageBranch.Walking: exams = new[] { ExamKind.Walking }; break;
                        case TriageBranch.ApneaAirwayNotAttempted:
                        case TriageBranch.ApneaNoResponseToAirway:
                        case TriageBranch.ApneaRespondsToAirway: exams = new[] { ExamKind.Walking, ExamKind.Breathing, ExamKind.Airway }; break;
                        case TriageBranch.RespiratoryRateAbove30: exams = new[] { ExamKind.Walking, ExamKind.Breathing, ExamKind.RespiratoryRate }; break;
                        case TriageBranch.PoorPerfusion: exams = new[] { ExamKind.Walking, ExamKind.Breathing, ExamKind.RespiratoryRate, ExamKind.Perfusion }; break;
                        default: exams = new[] { ExamKind.Walking, ExamKind.Breathing, ExamKind.RespiratoryRate, ExamKind.Perfusion, ExamKind.MentalStatus }; break;
                    }
                    foreach (var e in exams) { m.Examine(e); yield return Seconds(1.6f); }
                    // Tag chosen by the same START rule engine the exercise uses (a scripted, correct demo learner).
                    m.Assign(res.category);
                    yield return Seconds(3.2f);                                       // coloured tag + feedback
                    HandsRest();
                }
                yield return Seconds(2f);
            }

            Vector3 AssessPanelPos(TriageUI ui)
            {
                var f = typeof(TriageUI).GetField("assessPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var p = f?.GetValue(ui) as Component;
                return p != null ? p.transform.position : Head + Vector3.forward;
            }
        }
    }
}
