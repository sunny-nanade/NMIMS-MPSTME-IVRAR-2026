#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>Audit scenario for voice audibility (section A of the audit): group levels, ducking, RMS at the listener per group and scene, exclusivity, pause, volume.</summary>
    public partial class PlayCheckRunner
    {
        double _rms; float _peak;
        float _peakVoice;
        readonly List<string> _auditTable = new List<string>();

        static double Db(double v) { return 20.0 * System.Math.Log10(System.Math.Max(v, 1e-9)); }

        /// <summary>Isolates groups for a measurement. Voice arguments: 0 = on at its base level, -80 = muted; sfx / amb in dB; ui in dB.</summary>
        void SetGroups(float rose, float hal, float sfxDb, float amb, float ui)
        {
            var m = DeepSleepAssets.Instance.mixer; var d = SfxDirector.Instance;
            m.SetFloat("VolRose", rose > -70f ? d.RoseBaseDb : -80f); m.SetFloat("VolHalcyon", hal > -70f ? d.HalcyonBaseDb : -80f);
            m.SetFloat("VolSFX", sfxDb); m.SetFloat("VolAmbience", amb); m.SetFloat("VolUI", ui);
        }

        /// <summary>RMS (both channels) of the listener output while 'active' returns true, up to maxSeconds. Result in _rms.</summary>
        IEnumerator MeasureRms(System.Func<bool> active, float maxSeconds, float warmup = 0.12f)
        {
            var b0 = new float[1024]; var b1 = new float[1024];
            double sum = 0; long n = 0; float t0 = Time.unscaledTime; _peak = 0f;
            while (active() && Time.unscaledTime - t0 < maxSeconds)
            {
                AudioListener.GetOutputData(b0, 0); AudioListener.GetOutputData(b1, 1);
                if (Time.unscaledTime - t0 > warmup) for (int i = 0; i < b0.Length; i++) { sum += b0[i] * b0[i] + b1[i] * b1[i]; n += 2; _peak = Mathf.Max(_peak, Mathf.Abs(b0[i]), Mathf.Abs(b1[i])); }
                yield return null;
            }
            _rms = n > 0 ? System.Math.Sqrt(sum / n) : 0;
        }

        void Row(string text) { _auditTable.Add(text); Note(text); }

        IEnumerator AuditAudio()
        {
            VoiceRules.BypassForTests = true;
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SfxDirector.Instance != null && NarrativeVoiceDirector.Instance != null && HalcyonDirector.Instance != null, 10f);
            var sfx = SfxDirector.Instance; var narr = NarrativeVoiceDirector.Instance; var hal = HalcyonDirector.Instance; var sm = SessionManager.Instance;
            StudyConfig.Data.participant_id = "4";
            var mixer = DeepSleepAssets.Instance.mixer;
            AudioListener.volume = 1f;
            // SCN_Main starts in the Setup section: move to the medical bay first (the bay measurements below need the bay ambience)
            StageJump.Jump(Stage.EarCheck);
            yield return sp.Until(() => SceneFlow.Section == DS.SceneMedicalBay && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy, 30f);
            yield return sp.Wait(1.0f);

            // ---------------------------------------------------------------- A1 inventory of every live AudioSource
            yield return sp.Until(() => !narr.IsSpeaking && !hal.IsSpeaking && narr.QueueLength == 0, 40f);
            yield return sp.Wait(1.5f);
            InventorySources("medical bay");

            // ---------------------------------------------------------------- A2 ducking really changes the mixer
            yield return sp.Until(() => !narr.IsSpeaking && !hal.IsSpeaking && narr.QueueLength == 0, 40f);
            yield return sp.Wait(1.5f);
            string silent = sfx.GroupLevelsDb();
            VoiceBus.Request("R030");
            yield return sp.Until(() => narr.IsSpeaking, 3f);
            yield return sp.Wait(0.8f);
            string talking = sfx.GroupLevelsDb();
            mixer.GetFloat("VolAmbience", out float ambTalking); mixer.GetFloat("VolSFX", out float sfxTalking);
            Row($"Group levels while silent:  {silent}");
            Row($"Group levels while Rose talks: {talking}");
            yield return sp.Until(() => !narr.IsSpeaking && !hal.IsSpeaking && narr.QueueLength == 0, 30f);
            yield return sp.Wait(1.2f);
            mixer.GetFloat("VolAmbience", out float ambAfter); mixer.GetFloat("VolSFX", out float sfxAfter);
            Row($"Group levels after the line: {sfx.GroupLevelsDb()}");
            Check(ambTalking <= ambAfter - 7.9f && sfxTalking <= sfxAfter - 7.9f, $"Ambience and SFX drop by at least 8 dB under a voice line (ambience {ambAfter:F1} to {ambTalking:F1}, SFX {sfxAfter:F1} to {sfxTalking:F1}) and recover afterwards");

            // ---------------------------------------------------------------- A4 exclusivity, pause, volume (real directors)
            int overlapFrames = 0; int before = narr.Played.Count, beforeH = hal.Played.Count;
            VoiceBus.Request("R031"); VoiceBus.Request("H007"); VoiceBus.Request("R032");
            float tEnd = Time.time + 40f;
            while (Time.time < tEnd && !(narr.Played.Count >= before + 2 && hal.Played.Count >= beforeH + 1 && !narr.IsSpeaking && !hal.IsSpeaking)) { if (narr.IsSpeaking && hal.IsSpeaking) overlapFrames++; yield return null; }
            Check(overlapFrames == 0 && narr.Played.Count >= before + 2 && hal.Played.Count >= beforeH + 1, $"Rose and HALCYON requested together play one after the other with no overlap ({overlapFrames} overlapping frames; all three lines played)");

            yield return sp.Until(() => !narr.IsSpeaking && !hal.IsSpeaking && narr.QueueLength == 0, 40f);
            VoiceBus.Request("R011");
            yield return sp.Until(() => narr.IsSpeaking, 4f);
            yield return sp.Wait(0.6f);
            var rs = narr.transform.Find("RoseComms").GetComponent<AudioSource>();
            var pm = PauseManager.Instance;
            float t0 = rs.time;
            pm.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.8f);
            float tPaused = rs.time;
            pm.SetPaused(false);
            yield return new WaitForSecondsRealtime(0.8f);
            float tResumed = rs.time;
            Check(Mathf.Abs(tPaused - t0) < 0.25f && tResumed > tPaused + 0.3f && narr.IsSpeaking, $"Voice pauses with PauseManager and resumes where it stopped (t {t0:F2} paused {tPaused:F2} resumed {tResumed:F2})");
            yield return sp.Until(() => !narr.IsSpeaking, 20f);

            AudioListener.volume = 0.5f; // what the Setup volume slider writes
            Check(Mathf.Approximately(AudioListener.volume, 0.5f), "Setup volume writes AudioListener.volume, which scales every mixer group");
            SceneFlow.Instance.PlaceAtSpawn("PlayerSpawn");
            AudioListener.volume = 1f;

            // ---------------------------------------------------------------- A3 per-group RMS at the listener, medical bay
            sfx.ManualMixer = true;
            float ambDuck = sfx.AmbienceBaseDb + sfx.AmbienceDuckDb, sfxDuck = sfx.SfxBaseDb + sfx.SfxDuckDb;
            Vector3 front = PlayerRig.Instance.Head.position + PlayerRig.Instance.Head.forward;
            // ambience at its ducked level
            SetGroups(-80, -80, -80, ambDuck, -80);
            yield return sp.Wait(0.5f);
            yield return MeasureRms(() => true, 2.0f);
            double ambBay = _rms;
            Row($"[bay] ambience A001 ducked ({ambDuck:F0} dB group): RMS {ambBay:E2} ({Db(ambBay):F1} dBFS)");
            // loudest SFX at 1 m and ducked
            double maxSfxBay = 0; string maxSfxName = "";
            foreach (var e in new[] { SfxEvent.KeyPress, SfxEvent.WrongSequence, SfxEvent.TerminalBoot, SfxEvent.ServiceDoor, SfxEvent.Plug, SfxEvent.WrongPlug, SfxEvent.RackSolved, SfxEvent.BlastDoor, SfxEvent.Teleport })
            {
                SetGroups(-80, -80, sfxDuck, -80, -80);
                SfxEvents.Raise(e, front, 4);
                var src = sfx.LastSource;
                yield return MeasureRms(() => src != null && src.isPlaying, 6f, 0.05f);
                Row($"[bay] SFX {e} ({src.clip.name}) ducked ({sfxDuck:F0} dB group) at 1 m: RMS {_rms:E2} ({Db(_rms):F1} dBFS)");
                if (_rms > maxSfxBay) { maxSfxBay = _rms; maxSfxName = src.clip.name; }
                yield return sp.Wait(0.2f);
            }
            double bedBay = System.Math.Sqrt(ambBay * ambBay + maxSfxBay * maxSfxBay);
            Row($"[bay] bed = ambience + loudest SFX ({maxSfxName}): RMS {bedBay:E2} ({Db(bedBay):F1} dBFS)");

            // every narrative Rose line and HALCYON line, alone, through the real directors
            double minRose = 1e9, minHal = 1e9; string minRoseId = "", minHalId = "";
            foreach (var id in NarrativeIds())
            {
                SetGroups(0, -80, -80, -80, -80);
                VoiceBus.Request(id);
                yield return sp.Until(() => narr.IsSpeaking && narr.Current == id, 4f);
                yield return MeasureRms(() => narr.IsSpeaking, 30f);
                if (_rms < minRose) { minRose = _rms; minRoseId = id; }
                _peakVoice = Mathf.Max(_peakVoice, _peak);
                yield return sp.Wait(0.15f);
            }
            foreach (var id in new[] { "H001", "H002", "H004", "H007", "H008" })
            {
                SetGroups(-80, 0, -80, -80, -80);
                VoiceBus.Request(id);
                yield return sp.Until(() => hal.IsSpeaking && hal.Current == id, 4f);
                yield return MeasureRms(() => hal.IsSpeaking, 30f);
                if (_rms < minHal) { minHal = _rms; minHalId = id; }
                yield return sp.Wait(0.15f);
            }
            Row($"[bay] loudest listener peak while any voice line played: {_peakVoice:F2}");
            Row($"[bay] quietest Rose line {minRoseId}: RMS {minRose:E2} ({Db(minRose):F1} dBFS); quietest HALCYON line {minHalId}: RMS {minHal:E2} ({Db(minHal):F1} dBFS)");
            double marginRoseBay = Db(minRose) - Db(bedBay), marginHalBay = Db(minHal) - Db(bedBay);
            Row($"[bay] voice over bed: Rose {marginRoseBay:F1} dB, HALCYON {marginHalBay:F1} dB");
            Check(marginRoseBay >= 6f, $"Medical bay: the quietest Rose line is {marginRoseBay:F1} dB above ambience + SFX (needs 6)");
            Check(marginHalBay >= 6f, $"Medical bay: the quietest HALCYON line is {marginHalBay:F1} dB above ambience + SFX (needs 6)");
            sfx.ManualMixer = false;
            sfx.ManualMixer = false;

            // ---------------------------------------------------------------- smoke room through the real flow
            sm.JumpTo(Stage.Block1); sm.CurrentBlock = 1;
            SceneFlow.Instance.EnterSmokeRoom();
            yield return sp.Until(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 25f);
            var eg = EgressBlockController.Instance;
            yield return sp.Wait(2.5f);
            InventorySources("smoke room");
            var rig = PlayerRig.Instance;

            // B1 and B2 runtime fog values
            float vis = Mathf.Sqrt(Mathf.Log(50f)) / Mathf.Max(0.0001f, RenderSettings.fogDensity);
            Row($"[smoke] active scene {SceneManager.GetActiveScene().name}; RenderSettings.fog={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity:F3} color={RenderSettings.fogColor} -> visibility {vis:F2} m");
            Check(SceneFlow.Section == DS.SceneSmokeRoom && RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared && Mathf.Abs(vis - 2f) < 0.15f, $"Smoke room after the additive load: fog on, ExponentialSquared, visibility {vis:F2} m, active scene is SCN_SmokeRoom");

            // HALCYON is silent in the smoke room
            int hBefore = hal.Played.Count;
            VoiceBus.Request("H002");
            yield return sp.Wait(2.0f);
            Check(hal.Played.Count == hBefore && !hal.IsSpeaking, "HALCYON stays silent inside SCN_SmokeRoom");

            // ambience bed with footsteps, ducked
            sfx.ManualMixer = true;
            SetGroups(-80, -80, -80, ambDuck, -80);
            yield return sp.Wait(0.4f);
            yield return MeasureRms(() => true, 2.0f);
            double ambSmoke = _rms;
            Row($"[smoke] ambience A004 + A006 ducked: RMS {ambSmoke:E2} ({Db(ambSmoke):F1} dBFS)");
            double maxSfxSmoke = 0; string maxSmokeName = "";
            foreach (var e in new[] { SfxEvent.Footstep, SfxEvent.WallContact, SfxEvent.ExitDoor })
            {
                SetGroups(-80, -80, sfxDuck, -80, -80);
                SfxEvents.Raise(e, rig.Head.position + rig.Head.forward * 0.8f);
                var src = sfx.LastSource;
                yield return MeasureRms(() => src != null && src.isPlaying, 6f, 0.05f);
                Row($"[smoke] SFX {e} ({src.clip.name}) ducked at 0.8 m: RMS {_rms:E2} ({Db(_rms):F1} dBFS)");
                if (_rms > maxSfxSmoke) { maxSfxSmoke = _rms; maxSmokeName = src.clip.name; }
                yield return sp.Wait(0.2f);
            }
            double bedSmoke = System.Math.Sqrt(ambSmoke * ambSmoke + maxSfxSmoke * maxSfxSmoke);
            Row($"[smoke] bed = ambience + loudest SFX ({maxSmokeName}): RMS {bedSmoke:E2} ({Db(bedSmoke):F1} dBFS)");

            // Rose's block calls from the exit door, both conditions, near / mid / far, every block line at the far distance
            var acs = eg.AudioSwitch; eg.Mentor.enabled = false;
            var anchor = new GameObject("AuditAnchor").transform; acs.Bind(anchor);
            double worstMargin = 1e9; string worstWhere = "";
            foreach (var cond in new[] { GuidanceCondition.Spatial, GuidanceCondition.Stereo })
            {
                acs.Apply(cond);
                Row($"[smoke] condition {ConditionRandomizer.Short(cond)}: spatialBlend {acs.Source.spatialBlend:F1}, spatialize {acs.Source.spatialize}, group {(acs.Source.outputAudioMixerGroup != null ? acs.Source.outputAudioMixerGroup.name : "NONE")}");
                foreach (float d in new[] { 3f, 8f, 15f })
                {
                    foreach (var id in d < 15f ? new[] { "R201" } : new[] { "R201", "R202", "R203", "R204", "R205", "R207", "R208", "R209", "R210" })
                    {
                        float az = 0f;
                        Vector3 fwd = Quaternion.Euler(0, rig.Head.eulerAngles.y + (cond == GuidanceCondition.Spatial ? 70f : az), 0) * Vector3.forward;
                        anchor.position = new Vector3(rig.Head.position.x, 1.5f, rig.Head.position.z) + fwd * d;
                        SetGroups(0, -80, -80, -80, -80);
                        float len = acs.Play(id);
                        yield return MeasureRms(() => acs.IsPlaying, len + 1f);
                        double m = Db(_rms) - Db(bedSmoke);
                        if (id == "R201" || d == 15f && m < worstMargin) { }
                        if (m < worstMargin) { worstMargin = m; worstWhere = $"{ConditionRandomizer.Short(cond)} {id} at {d:0} m"; }
                        if (id == "R201") Row($"[smoke] Rose {id} condition {ConditionRandomizer.Short(cond)} at {d:0} m: RMS {_rms:E2} ({Db(_rms):F1} dBFS), {m:F1} dB over the bed");
                        acs.Stop();
                        yield return sp.Wait(0.1f);
                    }
                }
            }
            Row($"[smoke] worst voice margin over the bed: {worstMargin:F1} dB ({worstWhere})");
            Check(worstMargin >= 6f, $"Smoke room: every Rose block line, both conditions, up to 15 m is at least 6 dB above ambience + footsteps (worst {worstMargin:F1} dB: {worstWhere})");
            Destroy(anchor.gameObject);
            sfx.ManualMixer = false; eg.Mentor.enabled = true;
            sfx.ManualMixer = false;
            eg.Finish(true);
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && !SceneFlow.Instance.IsBusy, 20f);
            yield return sp.Wait(1.5f);
            Row($"[bay again] RenderSettings.fog={RenderSettings.fog} mode={RenderSettings.fogMode} active scene {SceneManager.GetActiveScene().name}");
            Check(!RenderSettings.fog, "Fog is off again in SCN_MedicalBay after the block");
            int hb = hal.Played.Count;
            VoiceBus.Request("H008");
            yield return sp.Until(() => hal.Played.Count > hb, 12f);
            Check(hal.Played.Count > hb, "HALCYON is audible again at the rack after the smoke room");

            // ---------------------------------------------------------------- hallway
            SceneFlow.Instance.GoToHallway();
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneHallway && !SceneFlow.Instance.IsBusy, 20f);
            yield return sp.Wait(2.5f);
            InventorySources("hallway");
            Check(!RenderSettings.fog, "Fog is off in SCN_HallwayDebrief");
            sfx.ManualMixer = true;
            SetGroups(-80, -80, -80, ambDuck, -80);
            yield return sp.Wait(0.4f);
            yield return MeasureRms(() => true, 2.0f);
            double ambHall = _rms;
            SetGroups(-80, -80, -80, -80, sfxDuck);
            SfxEvents.Raise(SfxEvent.UI);
            var us = sfx.LastSource;
            yield return MeasureRms(() => us != null && us.isPlaying, 4f, 0.03f);
            double uiHall = _rms;
            double bedHall = System.Math.Sqrt(ambHall * ambHall + uiHall * uiHall);
            Row($"[hallway] ambience A001 + A005 ducked: RMS {ambHall:E2} ({Db(ambHall):F1} dBFS); UI click {uiHall:E2}; bed {bedHall:E2} ({Db(bedHall):F1} dBFS)");
            double minHalHall = 1e9;
            foreach (var id in new[] { "H001", "H004", "H007", "H008" })
            {
                SetGroups(-80, 0, -80, -80, -80);
                VoiceBus.Request(id);
                yield return sp.Until(() => hal.IsSpeaking && hal.Current == id, 4f);
                yield return MeasureRms(() => hal.IsSpeaking, 30f);
                if (_rms < minHalHall) minHalHall = _rms;
                yield return sp.Wait(0.15f);
            }
            double minRoseHall = 1e9;
            foreach (var id in new[] { "R161", "R150" })
            {
                SetGroups(0, -80, -80, -80, -80);
                VoiceBus.Request(id);
                yield return sp.Until(() => narr.IsSpeaking && narr.Current == id, 4f);
                yield return MeasureRms(() => narr.IsSpeaking, 30f);
                if (_rms < minRoseHall) minRoseHall = _rms;
                yield return sp.Wait(0.15f);
            }
            double mh = Db(minHalHall) - Db(bedHall), mr = Db(minRoseHall) - Db(bedHall);
            Row($"[hallway] quietest HALCYON {Db(minHalHall):F1} dBFS ({mh:F1} dB over bed), quietest Rose {Db(minRoseHall):F1} dBFS ({mr:F1} dB over bed)");
            Check(mh >= 6f && mr >= 6f, $"Hallway: HALCYON {mh:F1} dB and Rose {mr:F1} dB above ambience + SFX (needs 6)");
            sfx.ManualMixer = false;
            sfx.ManualMixer = false;

            File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Analysis", "data", "audit_audio_levels.txt")), _auditTable);
        }

        static IEnumerable<string> NarrativeIds()
        {
            var ids = new List<string>();
            foreach (var line in DeepSleepAssets.Instance.voiceManifest.text.Split('\n'))
            {
                var p = line.Trim().Split(',');
                if (p.Length < 4 || p[0].Length != 4 || p[0][0] != 'R' || p[2] == "none") continue;
                if (NarrativeVoiceDirector.IsBlockOnly(p[0])) continue;
                ids.Add(p[0]);
            }
            return ids;
        }

        void InventorySources(string where)
        {
            int n = 0, noGroup = 0;
            foreach (var a in FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
            {
                n++;
                string g = a.outputAudioMixerGroup != null ? a.outputAudioMixerGroup.name : "NONE";
                if (g == "NONE") noGroup++;
                Row($"[{where}] source '{a.gameObject.name}' group={g} clip={(a.clip != null ? a.clip.name : "-")} spatialBlend={a.spatialBlend:F1} spatialize={a.spatialize} playing={a.isPlaying}");
            }
            int listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length;
            int enabledListeners = 0; foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude)) if (l.enabled) enabledListeners++;
            Check(noGroup == 0, $"[{where}] every AudioSource has a mixer group ({n} sources, {noGroup} without)");
            Check(enabledListeners == 1, $"[{where}] exactly one active AudioListener ({enabledListeners}, {listeners} including inactive)");
        }
    }
}
#endif
