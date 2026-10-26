using UnityEngine;

namespace CSER
{
    /// Static entry point for automated play-tests through the editor bridge (IReflectionService.InvokeStaticMethod).
    public static class TestHook
    {
        public static string Run(string command)
        {
            var gm = GameManager.I;
            if (!gm) return "no GameManager";
            var p = command.Split(' ');
            switch (p[0])
            {
                case "where":
                {
                    var g = GameObject.Find(p[1]);
                    if (!g) return "no " + p[1];
                    return $"parent={(g.transform.parent ? g.transform.parent.name : "none")} local={g.transform.localPosition} fwd={g.transform.forward} active={g.activeInHierarchy}";
                }
                case "begin": gm.BeginPlaying(); return gm.DebugStatus();
                case "click": return gm.DebugClick(command.Substring(6));
                case "complete": gm.DebugComplete(p[1]); return gm.DebugStatus();
                case "status": return gm.DebugStatus() + " inv=" + (Inventory.I ? Inventory.I.DebugList() : "none");
                case "grab":
                {
                    var g = GameObject.Find(p[1]);
                    if (!g) return "no " + p[1];
                    var item = g.GetComponent<GrabItem>();
                    var fl = g.GetComponent<Flashlight>();
                    if (fl) fl.DebugGrab();
                    else if (item) item.EverHeld = true;
                    return "grabbed " + p[1];
                }
                case "inv":
                    if (!Inventory.I) return "no inventory";
                    if (p[1] == "toggle") { Inventory.I.Toggle(); return "open=" + Inventory.I.IsOpen; }
                    if (p[1] == "store") return Inventory.I.DebugStore(p[2]);
                    if (p[1] == "take") { Inventory.I.DebugTakeOut(int.Parse(p[2])); return Inventory.I.DebugList(); }
                    if (p[1] == "throw") { Inventory.I.DebugThrowOut(int.Parse(p[2])); return Inventory.I.DebugList(); }
                    return Inventory.I.DebugList();
                case "laptop":
                {
                    var l = Object.FindAnyObjectByType<MalwareLaptop>();
                    if (!l) return "no laptop";
                    if (p.Length > 1 && p[1] == "solve") l.DebugSolve();
                    return l.DebugState();
                }
                case "buttons":
                {
                    // buttons <canvas> : every clickable control on that canvas with its label
                    var c = GameObject.Find(p[1]);
                    if (!c) return "no " + p[1];
                    var sb = new System.Text.StringBuilder();
                    foreach (var s in c.GetComponentsInChildren<UnityEngine.UI.Selectable>())
                        sb.Append('[').Append(LabelOf(s)).Append("] ");
                    return sb.ToString();
                }
                case "press":
                {
                    // press <canvas> <label text> : a real UI pointer click on the first control whose label contains the text
                    var c = GameObject.Find(p[1]);
                    if (!c) return "no " + p[1];
                    string want = command.Substring(p[0].Length + p[1].Length + 2).ToLowerInvariant();
                    var all = c.GetComponentsInChildren<UnityEngine.UI.Selectable>();
                    var exact = System.Array.Find(all, x => LabelOf(x).ToLowerInvariant() == want);
                    foreach (var s in exact ? new[] { exact } : all)
                    {
                        if (!LabelOf(s).ToLowerInvariant().Contains(want)) continue;
                        var es = UnityEngine.EventSystems.EventSystem.current;
                        var data = new UnityEngine.EventSystems.PointerEventData(es);
                        UnityEngine.EventSystems.ExecuteEvents.Execute(s.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                        return "pressed [" + LabelOf(s) + "] " + gm.DebugStatus();
                    }
                    return "no control '" + want + "'";
                }
                case "type":
                {
                    var w = Object.FindAnyObjectByType<Workstation>();
                    if (!w) return "no workstation";
                    w.DebugType(command.Substring(5));
                    return "typed";
                }
                case "submit":
                {
                    var w = Object.FindAnyObjectByType<Workstation>();
                    if (w) w.DebugSubmit();
                    return gm.DebugStatus();
                }
                case "token": return AuthToken.I ? AuthToken.I.Current : "no token";
                case "drop": gm.DebugDrop(p[1], p[2]); return "dropped";
                case "knock":
                {
                    var d = Object.FindAnyObjectByType<DoorKnock>();
                    var c = d && d.door ? d.door.GetComponentInChildren<Clickable>() : null;
                    if (!c) return "no clickable door";
                    c.Click();
                    return gm.DebugStatus();
                }
                case "nopush":
                {
                    // nopush 1|0 : disable / enable the head push-back; reports whether the comfort dim is on
                    if (p.Length > 1) PlayerBlocker.DebugNoPush = p[1] == "1";
                    var pb = Object.FindAnyObjectByType<PlayerBlocker>();
                    return $"nopush={PlayerBlocker.DebugNoPush} dimmed={(pb ? pb.Dimmed.ToString() : "no blocker")}";
                }
                case "chapter": SceneFlow.LoadChapter(int.Parse(p[1])); return "loading chapter " + p[1];   // same path as the menu's Play buttons
                case "run": return $"running={RunState.Running} seconds={RunState.Seconds:0.0} checkpoint={RunState.Checkpoint}";
                case "drawers":
                {
                    // drawers : clicks every SlideDrawer (same path as the trigger on the ray)
                    var sb = new System.Text.StringBuilder();
                    foreach (var d in Object.FindObjectsByType<SlideDrawer>(FindObjectsSortMode.None))
                    {
                        d.GetComponent<Clickable>().Click();
                        sb.Append(d.name).Append(d.IsOpen ? "=open " : "=closed ");
                    }
                    return sb.Length > 0 ? sb.ToString() : "no drawers";
                }
                case "hold":
                {
                    // hold <item> : keyboard-grab the item (the desktop path); its hold pose then applies
                    var g = GameObject.Find(p[1]);
                    var item = g ? g.GetComponent<GrabItem>() : null;
                    if (!item || !KeyboardGrab.I) return "no " + p[1];
                    return KeyboardGrab.I.Hold(item) ? "holding " + p[1] + " pose=" + item.holdPose : "can't hold " + p[1];
                }
                case "expire": gm.DebugNearlyOutOfTime(p.Length > 1 ? float.Parse(p[1]) : 1f); return gm.DebugStatus();
                case "ws":
                {
                    // ws login | ws tab <n> : drive Fred's workstation (then "click <label>" presses its buttons)
                    var w = Object.FindAnyObjectByType<Workstation>();
                    if (!w) return "no workstation";
                    if (p[1] == "login") w.DebugLogin();
                    else if (p[1] == "tab") w.DebugTab(int.Parse(p[2]));
                    else if (p[1] == "main") w.DebugMain();
                    return gm.DebugStatus();
                }
                case "keypad":
                {
                    var k = Object.FindAnyObjectByType<DoorKeypad>();
                    if (k) k.DebugEnterCode();
                    return gm.DebugStatus();
                }
                case "pos":
                {
                    var rig = Object.FindAnyObjectByType<OVRCameraRig>();
                    return rig ? $"head={rig.centerEyeAnchor.position:F2} rig={rig.transform.position:F2}" : "no rig";
                }
                case "tp":
                {
                    // tp <x> <z> : teleport event through the locomotor (same path as the teleport arc)
                    var loc = Object.FindAnyObjectByType<Oculus.Interaction.Locomotion.FirstPersonLocomotor>();
                    if (!loc) return "no locomotor";
                    loc.HandleLocomotionEvent(new Oculus.Interaction.Locomotion.LocomotionEvent(0,
                        new Pose(new Vector3(float.Parse(p[1]), 0, float.Parse(p[2])), Quaternion.identity),
                        Oculus.Interaction.Locomotion.LocomotionEvent.TranslationType.Absolute, Oculus.Interaction.Locomotion.LocomotionEvent.RotationType.None));
                    return "tp sent";
                }
                case "walk":
                {
                    // walk <vx> <vz> <seconds> : thumbstick-style smooth movement
                    var loc = Object.FindAnyObjectByType<Oculus.Interaction.Locomotion.FirstPersonLocomotor>();
                    if (!loc) return "no locomotor";
                    gm.StartCoroutine(Walk(loc, new Vector3(float.Parse(p[1]), 0, float.Parse(p[2])), float.Parse(p[3])));
                    return "walking";
                }
                case "stroll":
                {
                    // stroll <vx> <vz> <seconds> : real-world walking (the head moves inside the tracking space, the rig doesn't)
                    var rig = Object.FindAnyObjectByType<OVRCameraRig>();
                    if (!rig) return "no rig";
                    gm.StartCoroutine(Stroll(rig, new Vector3(float.Parse(p[1]), 0, float.Parse(p[2])), float.Parse(p[3])));
                    return "strolling";
                }
                case "release":
                {
                    // release <item> <height> : what an ISDK grab does - lock kinematic, carry the item up into the air, unlock
                    var g = GameObject.Find(p[1]);
                    var item = g ? g.GetComponent<GrabItem>() : null;
                    if (!item) return "no " + p[1];
                    gm.StartCoroutine(Release(item, float.Parse(p[2])));
                    return "carrying " + p[1];
                }
                case "swipe":
                {
                    var r = Object.FindAnyObjectByType<BadgeReader>();
                    if (r) r.DebugSwipe();
                    return gm.DebugStatus();
                }
            }
            return "unknown command";
        }

        static System.Collections.IEnumerator Walk(Oculus.Interaction.Locomotion.FirstPersonLocomotor loc, Vector3 v, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                loc.HandleLocomotionEvent(new Oculus.Interaction.Locomotion.LocomotionEvent(0, new Pose(v, Quaternion.identity),
                    Oculus.Interaction.Locomotion.LocomotionEvent.TranslationType.Velocity, Oculus.Interaction.Locomotion.LocomotionEvent.RotationType.None));
                yield return null;
            }
        }

        static System.Collections.IEnumerator Stroll(OVRCameraRig rig, Vector3 v, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                rig.trackingSpace.localPosition += v * Time.deltaTime;
                yield return null;
            }
        }

        static System.Collections.IEnumerator Release(GrabItem item, float height)
        {
            var rb = item.GetComponent<Rigidbody>();
            item.EverHeld = true;
            Oculus.Interaction.RigidbodyKinematicLockerExtension.LockKinematic(rb);
            var start = item.transform.position;
            for (int i = 1; i <= 10; i++)
            {
                item.Teleport(start + Vector3.up * height * i / 10f, item.transform.rotation);
                yield return new WaitForFixedUpdate();
            }
            Oculus.Interaction.RigidbodyKinematicLockerExtension.UnlockKinematic(rb);
            Debug.Log($"[CSER] released {item.name} at {item.transform.position:F2} kinematic={rb.isKinematic} sleeping={rb.IsSleeping()}");
        }

        static string LabelOf(UnityEngine.UI.Selectable s)
        {
            var t = s.GetComponentInChildren<TMPro.TMP_Text>();
            return (t ? t.text : s.name).Replace('\n', ' ');
        }
    }
}
