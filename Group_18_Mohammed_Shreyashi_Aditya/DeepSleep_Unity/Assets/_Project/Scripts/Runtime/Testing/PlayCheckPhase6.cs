#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        IEnumerator Phase6()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && SessionManager.Instance != null && SceneFlow.Instance != null
                && NarrativeVoiceDirector.Instance != null && HalcyonDirector.Instance != null && SfxDirector.Instance != null, 10f);
            var narr = NarrativeVoiceDirector.Instance; var hal = HalcyonDirector.Instance; var sfx = SfxDirector.Instance;
            Check(narr != null && hal != null && sfx != null, "NarrativeVoiceDirector, HalcyonDirector and SfxDirector exist (created by Bootstrap)");
            if (narr == null || hal == null || sfx == null) yield break;
            var sm = SessionManager.Instance;
            StudyConfig.Data.participant_id = "4";

            // every manifest line resolves to a real clip
            int manifestIds = 0, missing = 0, empty = 0;
            var assets = DeepSleepAssets.Instance;
            foreach (var line in assets.voiceManifest.text.Split('\n'))
            {
                var parts = line.Trim().Split(',');
                if (parts.Length < 2 || parts[0] == "ID" || parts[0].Length < 4) continue;
                manifestIds++;
                if (!assets.Has(parts[0])) missing++;
                else if (assets.Clip(parts[0]).length < 0.05f) empty++;
            }
            Check(manifestIds >= 60 && missing == 0 && empty == 0, $"All {manifestIds} manifest IDs resolve to non-empty clips ({missing} missing, {empty} empty)");

            // arrival lines: Rose first, HALCYON after her, never overlapping
            bool overlap = false;
            float tEnd = Time.time + 22f;
            while (Time.time < tEnd && !(narr.Played.Contains("R010") && hal.Played.Contains("H001") && !narr.IsSpeaking && !hal.IsSpeaking))
            {
                if (narr.IsSpeaking && hal.IsSpeaking) overlap = true;
                yield return null;
            }
            Check(narr.Played.Contains("R010") && hal.Played.Contains("H001"), "R010 (Rose) and H001 (HALCYON) play on arrival");
            Check(!overlap, "Rose and HALCYON never speak over each other");
            Check(narr.Played.IndexOf("R010") >= 0 && hal.Played.IndexOf("H001") >= 0, "Both voices went through their own director");

            // ducking follows a voice line
            VoiceBus.Request("R011");
            yield return sp.Until(() => narr.IsSpeaking, 3f);
            yield return sp.Wait(0.4f);
            Check(sfx.IsDucked, "Ambience ducks (Ducked snapshot) while Rose speaks");
            yield return sp.Until(() => !narr.IsSpeaking && narr.QueueLength == 0 && !hal.IsSpeaking, 25f);
            yield return sp.Until(() => !sfx.IsDucked, 4f);
            Check(!sfx.IsDucked, "Ambience returns to Normal when the line ends");

            // queue order, block-only lines ignored, cancel
            int before = narr.Played.Count;
            VoiceBus.Request("R030"); VoiceBus.Request("R031"); VoiceBus.Request("R201"); VoiceBus.Request("R032"); VoiceBus.Cancel("R032");
            yield return sp.Until(() => narr.Played.Count >= before + 2 && !narr.IsSpeaking, 30f);
            yield return sp.Wait(0.5f);
            var tail = narr.Played.GetRange(before, narr.Played.Count - before);
            Check(tail.Count == 2 && tail[0] == "R030" && tail[1] == "R031", $"Queued lines play in order, R201 is ignored and a cancelled line is dropped ({string.Join(",", tail)})");

            // H004 at most once per 30 s
            VoiceBus.Request("H004");
            yield return sp.Until(() => hal.Played.Contains("H004"), 10f);
            yield return sp.Until(() => !hal.IsSpeaking, 10f);
            VoiceBus.Request("H004");
            yield return sp.Wait(2.5f);
            int h004 = hal.Played.FindAll(x => x == "H004").Count;
            Check(h004 == 1, $"H004 plays once within 30 s ({h004})");

            // sound effects: clip, pitch, 2D vs 3D
            Vector3 pos = PlayerRig.Instance.Head.position + PlayerRig.Instance.Head.forward;
            SfxEvents.Raise(SfxEvent.KeyPress, pos, 0);
            var src0 = sfx.LastSource; float pitch0 = src0.pitch;
            Check(src0.clip != null && src0.clip.name == "S040" && src0.spatialBlend > 0.99f, "Key press plays S040 as a 3D sound");
            SfxEvents.Raise(SfxEvent.KeyPress, pos, 8);
            float pitch8 = sfx.LastSource.pitch;
            Check(pitch8 > pitch0 * 1.5f, $"Key press pitch rises across the nine keys ({pitch0:F2} to {pitch8:F2})");
            var map = new (SfxEvent e, string id)[] { (SfxEvent.WrongSequence, "S041"), (SfxEvent.TerminalBoot, "S042"), (SfxEvent.ServiceDoor, "S050"), (SfxEvent.Plug, "S060"),
                (SfxEvent.WrongPlug, "S062"), (SfxEvent.RackSolved, "S063"), (SfxEvent.Lever, "S063"), (SfxEvent.BlastDoor, "S080"), (SfxEvent.ExitDoor, "S022"), (SfxEvent.WallContact, "S020") };
            int okMap = 0;
            foreach (var m in map) { SfxEvents.Raise(m.e, pos); if (sfx.LastSource.clip != null && sfx.LastSource.clip.name == m.id) okMap++; yield return null; }
            Check(okMap == map.Length, $"SFX events map to their clips ({okMap}/{map.Length})");
            SfxEvents.Raise(SfxEvent.UI);
            Check(sfx.LastSource.clip.name == "S091" && sfx.LastSource.spatialBlend < 0.01f, "UI sound S091 is 2D");
            yield return sp.TeleportTo(new Vector3(-1.2f, 0f, -0.5f));
            yield return sp.Wait(0.2f);
            Check(sfx.Played.Contains("S090"), "Teleport plays S090");
            Check(sfx.AmbienceIds.Length == 1 && sfx.AmbienceIds[0] == "A001" && sfx.AmbienceSources.Count == 1 && sfx.AmbienceSources[0].isPlaying, "SCN_MedicalBay loops A001");

            // ambience in the smoke room: identical in both ears
            sm.JumpTo(Stage.Block1); sm.CurrentBlock = 1;
            SceneFlow.Instance.EnterSmokeRoom();
            yield return sp.Until(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 20f);
            yield return sp.Wait(2f);
            bool mono = sfx.AmbienceSources.Count == 2;
            foreach (var a in sfx.AmbienceSources) mono &= a != null && a.clip.channels == 1 && a.spatialBlend < 0.01f && a.isPlaying;
            Check(sfx.AmbienceIds.Length == 2 && sfx.AmbienceIds[0] == "A004" && sfx.AmbienceIds[1] == "A006" && mono, "Smoke room plays A004 + A006, mono and 2D (identical in both ears)");
            EgressBlockController.Instance.Finish(true);
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && !SceneFlow.Instance.IsBusy, 15f);
            yield return sp.Wait(2.5f);
            Check(sfx.AmbienceIds.Length == 1 && sfx.AmbienceIds[0] == "A001" && sfx.AmbienceSources.Count == 1, "Back in the bay only A001 remains (the smoke ambience faded out and was removed)");

            // hallway
            SceneFlow.Instance.GoToHallway();
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneHallway && !SceneFlow.Instance.IsBusy, 15f);
            yield return sp.Wait(2.5f);
            Check(sfx.AmbienceIds.Length == 2 && sfx.AmbienceIds[0] == "A001" && sfx.AmbienceIds[1] == "A005", "Hallway plays A001 + A005");
            sm.ResetSession(); EgressBlockController.ResetStatics();
        }
    }
}
#endif
