using System.IO;
using UnityEditor;
using UnityEngine;

namespace CSER.EditorTools
{
    /// Dev-only automation: the Meta XR MCP bridge was removed in SDK 207, so tests and builds are driven through
    /// a command file instead. Write one line to Temp/cser_cmd.txt:
    ///   menu <menu path>      runs an editor menu item (e.g. "menu CSER/Build All Scenes (story mode)")
    ///   hook <command>        runs CSER.TestHook.Run (play mode), e.g. "hook complete SOC_Escalate"
    ///   state                 reads AlertStateManager.State / GameManager.State
    ///   dump <object> | findc <Type> | ray <vx> <vy> | cams | toggle <object>   diagnostics
    /// The result is written to Temp/cser_out.txt (prefixed with the command's sequence number).
    [InitializeOnLoad]
    static class FileCommandRunner
    {
        static readonly string Cmd = Path.Combine("Temp", "cser_cmd.txt");
        static readonly string Out = Path.Combine("Temp", "cser_out.txt");
        static double next;

        static FileCommandRunner() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || !File.Exists(Cmd)) return;
            next = EditorApplication.timeSinceStartup + 0.3;
            string line;
            try { line = File.ReadAllText(Cmd).Trim(); File.Delete(Cmd); }
            catch { return; }
            if (line.Length == 0) return;
            string result;
            try { result = Execute(line); }
            catch (System.Exception e) { result = "EXCEPTION " + e; }
            File.WriteAllText(Out, result);
        }

        static void Dump(Transform t, int depth, System.Text.StringBuilder sb)
        {
            if (depth > 6) return;
            var comps = new System.Text.StringBuilder();
            foreach (var c in t.GetComponents<Component>())
                if (c && !(c is Transform) && !(c is CanvasRenderer)) comps.Append(c.GetType().Name).Append(' ');
            var r = t.GetComponent<Renderer>();
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} lp={t.localPosition:F4} lr={t.localEulerAngles:F1} ls={t.localScale:F5} wp={t.position:F3}{(r ? " rend=" + r.enabled : "")} [{comps}]");
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, sb);
        }

        static string Execute(string line)
        {
            int sp = line.IndexOf(' ');
            string verb = sp < 0 ? line : line.Substring(0, sp);
            string arg = sp < 0 ? "" : line.Substring(sp + 1);
            switch (verb)
            {
                case "play": if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode(); return "playing";
                case "stop": if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); return "stopped";
                case "menu": return EditorApplication.ExecuteMenuItem(arg) ? "ok" : "menu item not found: " + arg;
                case "hook": return CSER.TestHook.Run(arg);
                case "dump":
                {
                    var go = GameObject.Find(arg);
                    if (!go) return "no object " + arg;
                    var sb = new System.Text.StringBuilder();
                    Dump(go.transform, 0, sb);
                    return sb.ToString();
                }
                case "ray":
                {
                    // ray <vx> <vy> : all hits (colliders) along the centre-eye ray through a viewport point; also logs camera data
                    var a = arg.Split(' ');
                    var cam = GameObject.Find("CenterEyeAnchor").GetComponent<Camera>();
                    var r = cam.ViewportPointToRay(new Vector3(float.Parse(a[0]), float.Parse(a[1]), 0));
                    var sb = new System.Text.StringBuilder($"cam near={cam.nearClipPlane} far={cam.farClipPlane} fov={cam.fieldOfView} aspect={cam.aspect} pos={cam.transform.position:F3} fwd={cam.transform.forward:F3}" + System.Environment.NewLine);
                    foreach (var h in Physics.RaycastAll(r, 20f, ~0, QueryTriggerInteraction.Collide))
                        { var par = h.collider.transform.parent; string pn = par ? par.name : string.Empty; sb.AppendLine($"{h.distance:F3} {h.collider.name} <- {pn} trig={h.collider.isTrigger}"); }
                    return sb.ToString();
                }
                case "toggle":
                {
                    var go = GameObject.Find(arg);
                    if (!go) return "no object " + arg;
                    go.SetActive(!go.activeSelf);
                    return arg + " active=" + go.activeSelf;
                }
                case "cams":
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                        sb.AppendLine($"{c.name} enabled={c.enabled} mask={c.cullingMask} eye={c.stereoTargetEye} depth={c.depth} near={c.nearClipPlane} far={c.farClipPlane} msaa={c.allowMSAA} hdr={c.allowHDR} tag={c.tag} target={(c.targetTexture ? c.targetTexture.name : string.Empty)}");
                    var rig = Object.FindAnyObjectByType<OVRCameraRig>();
                    if (rig) sb.AppendLine("usePerEyeCameras=" + rig.usePerEyeCameras);
                    sb.AppendLine("stereoRenderingPath/xr: " + UnityEngine.XR.XRSettings.enabled + " " + UnityEngine.XR.XRSettings.stereoRenderingMode + " device=" + UnityEngine.XR.XRSettings.loadedDeviceName);
                    return sb.ToString();
                }
                case "findc":
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var c in Object.FindObjectsByType<Component>(FindObjectsInactive.Include))
                        if (c.GetType().Name == arg)
                        {
                            var tr = c.transform; string path = tr.name;
                            while (tr.parent) { tr = tr.parent; path = tr.name + "/" + path; }
                            sb.AppendLine(path + " enabled=" + (c is Behaviour b ? b.enabled.ToString() : "n/a"));
                        }
                    return sb.Length == 0 ? "none of " + arg : sb.ToString();
                }
                case "colliders":
                {
                    // non-trigger colliders that stand in the walking space (between knee and head height)
                    var sb = new System.Text.StringBuilder();
                    foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
                    {
                        if (c.isTrigger) continue;
                        var b = c.bounds;
                        if (b.max.y < 0.3f || b.min.y > 1.9f) continue;
                        var tr = c.transform; string path = tr.name;
                        while (tr.parent) { tr = tr.parent; path = tr.name + "/" + path; }
                        sb.AppendLine($"{path} {c.GetType().Name} min={b.min:F2} max={b.max:F2}");
                    }
                    return sb.ToString();
                }
                case "isdk":
                {
                    // state of every ISDK ray / poke interactor (reflection keeps this independent of SDK generics)
                    var sb = new System.Text.StringBuilder();
                    foreach (var c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                    {
                        var ty = c.GetType();
                        if (ty.Name != "RayInteractor" && ty.Name != "PokeInteractor") continue;
                        object P(string n) { var pi = ty.GetProperty(n); try { return pi?.GetValue(c); } catch { return "?"; } }
                        var cand = P("Candidate") as Component; var sel = P("SelectedInteractable") as Component;
                        sb.AppendLine($"{ty.Name} {c.transform.parent?.parent?.name}/{c.name} active={c.isActiveAndEnabled} state={P("State")} cand={(cand ? cand.transform.parent.name + "/" + cand.name : "-")} sel={(sel ? sel.transform.parent.name : "-")} ray={P("Ray")} end={P("End")}");
                    }
                    return sb.ToString();
                }
                case "state":
                    return "alert=" + (CSER.AlertStateManager.I ? CSER.AlertStateManager.I.State.ToString() : "none") +
                           " game=" + (CSER.GameManager.I ? CSER.GameManager.I.State.ToString() : "none");
            }
            return "unknown verb " + verb;
        }
    }
}
