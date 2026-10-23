#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Drives the player through InputService.Scripted* only (no direct transform edits; movement goes through the
    /// normal locomotion code). Used by PlaythroughBot and the PlayMode tests.
    /// </summary>
    public class ScriptedPlayer : MonoBehaviour
    {
        static ScriptedPlayer _i;
        public static ScriptedPlayer Get()
        {
            if (_i == null)
            {
                var go = new GameObject("ScriptedPlayer");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<ScriptedPlayer>();
                InputService.Instance.SetScripted(true);
            }
            return _i;
        }

        public static void Dispose()
        {
            if (_i != null) { InputService.Instance.SetScripted(false); Destroy(_i.gameObject); _i = null; }
        }

        InputService In => InputService.Instance;
        PlayerRig Rig => PlayerRig.Instance;

        Vector3 _rHandPos, _rOrigin, _rDir = Vector3.forward;
        Vector2 _rStick, _lStick;
        bool _rTrigger, _rGrip, _lGrip;
        bool _menu;

        void RestPose()
        {
            if (Rig == null || Rig.Head == null) return;
            var h = Rig.Head;
            _rOrigin = h.position; _rDir = h.forward; _rHandPos = h.position + h.forward * 0.35f;
        }

        void Update() { Push(); }

        void Push()
        {
            if (Rig == null || Rig.Head == null) return;
            var hd = Rig.Head;
            if (_rDir == Vector3.forward && _rOrigin == Vector3.zero) RestPose();
            In.ScriptedSetHand(Hand.Right, _rStick, _rTrigger, _rGrip, _rHandPos, _rOrigin, _rDir);
            Vector3 lp = hd.position + hd.forward * 0.35f - hd.right * 0.25f - hd.up * 0.2f;
            In.ScriptedSetHand(Hand.Left, _lStick, false, _lGrip, lp, lp, hd.forward);
            In.ScriptedSetMenu(_menu);
        }

        public void SetMenu(bool on) { _menu = on; }

        // ---------------------------------------------------------------- basics
        public IEnumerator Wait(float seconds)
        {
            float t = 0; while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        public IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        public IEnumerator Until(Func<bool> cond, float timeout = 30f)
        {
            float t = 0;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        // ---------------------------------------------------------------- head
        public IEnumerator LookAt(Vector3 worldPoint, float timeout = 2f)
        {
            float t = 0;
            while (t < timeout)
            {
                var hd = Rig.Head;
                Vector3 d = (worldPoint - hd.position).normalized;
                float wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float curYaw = Mathf.Atan2(hd.forward.x, hd.forward.z) * Mathf.Rad2Deg;
                float yawErr = Mathf.DeltaAngle(curYaw, wantYaw);
                float wantPitch = -Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
                float curPitch = Mathf.DeltaAngle(0f, hd.localEulerAngles.x);
                float pitchErr = wantPitch - curPitch;
                if (Mathf.Abs(yawErr) < 1.5f && Mathf.Abs(pitchErr) < 2f) yield break;
                In.ScriptedSetLook(new Vector2(Mathf.Clamp(yawErr, -8f, 8f), Mathf.Clamp(-pitchErr, -6f, 6f)));
                t += Time.deltaTime;
                yield return null;
            }
        }

        public IEnumerator LookYaw(float worldYawDegrees, float timeout = 2f)
        {
            float t = 0;
            while (t < timeout)
            {
                var hd = Rig.Head;
                float curYaw = Mathf.Atan2(hd.forward.x, hd.forward.z) * Mathf.Rad2Deg;
                float err = Mathf.DeltaAngle(curYaw, worldYawDegrees);
                if (Mathf.Abs(err) < 1f) yield break;
                In.ScriptedSetLook(new Vector2(Mathf.Clamp(err, -10f, 10f), 0));
                t += Time.deltaTime;
                yield return null;
            }
        }

        // ---------------------------------------------------------------- hands
        public IEnumerator SnapTurn(int dir)
        {
            _rStick = new Vector2(dir, 0); yield return Frames(3);
            _rStick = Vector2.zero; yield return Frames(4);
        }

        /// <summary>Right stick forward aimed at a floor point, then released (teleport).</summary>
        public IEnumerator TeleportTo(Vector3 floorPoint, float handLift = 0f)
        {
            var hd = Rig.Head;
            _rOrigin = hd.position + Vector3.down * 0.4f + Vector3.up * handLift;
            _rDir = (floorPoint - _rOrigin).normalized;
            _rHandPos = _rOrigin + _rDir * 0.3f;
            _rStick = new Vector2(0, 1); yield return Frames(4);
            _rStick = Vector2.zero; yield return Frames(4);
            RestPose();
        }

        /// <summary>Diagnostic: what a ray from the hand towards the target hits first.</summary>
        public string DescribeRay(Vector3 target)
        {
            var o = Rig.Head.position + Vector3.down * 0.3f;
            var d = (target - o).normalized;
            var hits = Physics.RaycastAll(o, d, 20f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            var sb = new System.Text.StringBuilder("origin " + o.ToString("F2") + " target " + target.ToString("F2") + ":");
            for (int i = 0; i < hits.Length && i < 6; i++) sb.Append(" [" + hits[i].collider.transform.parent?.name + "/" + hits[i].collider.name + " d=" + hits[i].distance.ToString("F2") + "]");
            return sb.ToString();
        }

        public IEnumerator PointAt(Vector3 target)
        {
            var hd = Rig.Head;
            _rOrigin = hd.position + Vector3.down * 0.3f;
            _rDir = (target - _rOrigin).normalized;
            _rHandPos = _rOrigin + _rDir * 0.3f;
            yield return Frames(2);
        }

        public IEnumerator ClickRay(Vector3 target)
        {
            yield return PointAt(target);
            _rTrigger = true; yield return Frames(3);
            _rTrigger = false; yield return Frames(3);
        }

        public IEnumerator Poke(Vector3 target, Vector3 fromDirection)
        {
            Vector3 approach = target + fromDirection.normalized * 0.12f;
            _rOrigin = Rig.Head.position; _rDir = (target - _rOrigin).normalized;
            _rHandPos = approach; yield return Frames(3);
            _rTrigger = true;
            _rHandPos = target; yield return Frames(6);
            _rHandPos = approach; yield return Frames(3);
            _rTrigger = false; yield return Frames(2);
            RestPose();
        }

        public IEnumerator GrabObject(Grabbable g)
        {
            _rHandPos = g.transform.position; _rOrigin = Rig.Head.position; _rDir = (g.transform.position - _rOrigin).normalized;
            yield return Frames(3);
            _rGrip = true; yield return Frames(4);
        }

        /// <summary>Carries a grabbable to a point (hand follows) and lets go there.</summary>
        public IEnumerator PlaceGrabbable(Grabbable g, Vector3 target)
        {
            yield return GrabObject(g);
            yield return MoveHand(g.transform.position + Vector3.up * 0.25f, 0.3f);
            yield return MoveHand(target + Vector3.up * 0.25f, 0.5f);
            yield return MoveHand(target + Vector3.up * 0.03f, 0.3f);
            yield return ReleaseGrip();
        }

        public IEnumerator MoveHand(Vector3 pos, float seconds = 0.4f)
        {
            Vector3 a = _rHandPos; float t = 0;
            while (t < seconds) { t += Time.deltaTime; _rHandPos = Vector3.Lerp(a, pos, Mathf.Clamp01(t / seconds)); yield return null; }
            _rHandPos = pos;
        }

        public IEnumerator ReleaseGrip() { _rGrip = false; yield return Frames(4); RestPose(); }

        public void SetGrip(bool on) { _rGrip = on; }
        public Vector3 HandPos => _rHandPos;
        public void SetHandPos(Vector3 p) { _rHandPos = p; }

        // ---------------------------------------------------------------- smooth walking (SCN_SmokeRoom)
        public IEnumerator WalkTo(Vector3 target, float tolerance = 0.2f, float timeout = 12f)
        {
            float t = 0;
            while (t < timeout)
            {
                var hd = Rig.Head;
                Vector3 to = target - hd.position; to.y = 0;
                if (to.magnitude <= tolerance) break;
                float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float curYaw = Mathf.Atan2(hd.forward.x, hd.forward.z) * Mathf.Rad2Deg;
                float err = Mathf.DeltaAngle(curYaw, wantYaw);
                In.ScriptedSetLook(new Vector2(Mathf.Clamp(err, -12f, 12f), 0));
                _lStick = Mathf.Abs(err) < 25f ? new Vector2(0, 1) : Vector2.zero;
                t += Time.deltaTime;
                yield return null;
            }
            _lStick = Vector2.zero;
            yield return null;
        }

        public void StopWalking() { _lStick = Vector2.zero; }
    }
}
#endif
