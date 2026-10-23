using System.Collections;
using TMPro;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// SCN_Setup flow on world-space panels: consent, height notice, directional audio check (S002 tones),
    /// volume + comfort, then start. Advances to Awakening and loads SCN_MedicalBay.
    /// </summary>
    public class SetupController : MonoBehaviour
    {
        public const string ConsentText =
            "This is a research study about guidance audio in a VR evacuation. You will wear a headset for about 18 minutes. " +
            "You can stop at any time by taking the headset off. Only an anonymous participant number is stored. " +
            "If you feel dizzy or unwell, tell the experimenter at once.";

        [SerializeField] Vector2 panelSize = new Vector2(1.7f, 1.15f);
        [SerializeField] float minHeadHeight = 1.4f;
        [SerializeField] float toneFacingToleranceDeg = 40f;
        [SerializeField] float toneDistance = 2f;
        [SerializeField] float toneVolume = 0.35f; // the S002 file is near full scale; overlapping repeats clipped the mix at 1.0
        [SerializeField] float toneGapSeconds = 1.2f;
        [SerializeField] int initialVolume = 100; // the mix is levelled for 100 %; participants turn it down if they want
        [SerializeField] float maxVoiceWaitSeconds = 20f; // upper bound for waiting on Rose's setup lines before the next step

        public enum Page { Consent, Height, AudioCheck, Comfort, Starting }
        public Page Current { get; private set; } = Page.Consent;
        public int TonesConfirmed { get; private set; }
        /// <summary>Index of the tone that is playing now (0 to 3), -1 while Rose is still speaking before the first tone.</summary>
        public int ToneIndex { get; private set; } = -1;
        public bool HeightWarning { get; private set; }
        public bool Started { get; private set; }
        public float ToneBaseYaw { get; private set; }
        public static readonly float[] ToneYawOffsets = { 0f, -90f, 180f, 90f };

        Transform _root;
        TextMeshPro _title, _body;
        WorldUI.WorldButton _next, _vignetteBtn;
        WorldUI.WorldSlider _volume;
        GameObject _warn;
        AudioSource _toneSource;
        static readonly Vector3[] ToneDirs = { Vector3.forward, Vector3.left, Vector3.back, Vector3.right };
        static readonly string[] ToneNames = { "front", "left", "behind", "right" };
        Coroutine _toneRoutine;

        void Start()
        {
            // a stage jump reloads the scene with SEC_Setup active for one frame: it must not wipe the stage the jump just set
            bool fresh = SessionManager.Instance == null || SessionManager.Instance.Current == Stage.Setup;
            if (fresh)
            {
                if (SessionManager.Instance != null) SessionManager.Instance.ResetSession();
                EgressBlockController.ResetStatics();
            }
            Build();
            ShowPage(Page.Consent);
            VoiceBus.Request("R001"); // Rose's first line starts with the scene, not with the consent button
            if (StudyConfig.WasMissing && _warn != null) _warn.SetActive(true);
        }

        void Build()
        {
            _root = new GameObject("SetupPanels").transform;
            _root.SetParent(transform, false);
            UiStyle.Panel(_root, "Back", panelSize, Vector3.zero);
            _title = WorldUI.Text(_root, "", WorldUI.Cm(0.085f), new Vector3(0, panelSize.y * 0.4f, -0.005f), new Vector2(panelSize.x * 0.9f, 0.12f), TextAlignmentOptions.Center, UiStyle.Accent);
            _body = WorldUI.Text(_root, "", WorldUI.Cm(0.058f), new Vector3(0, 0.06f, -0.005f), new Vector2(panelSize.x * 0.88f, panelSize.y * 0.55f), TextAlignmentOptions.Center);
            _next = WorldUI.Button(_root, "Continue", new Vector2(0.44f, 0.11f), new Vector3(0, -panelSize.y * 0.38f, -0.01f), _ => OnNext(), WorldUI.Cm(0.055f));
            _volume = WorldUI.Slider(_root, "Volume", 0.6f, new Vector3(-0.1f, -0.12f, -0.01f), 0, 100);
            _volume.ValueLabel.fontSize = WorldUI.Cm(0.04f);
            _vignetteBtn = WorldUI.Button(_root, "Comfort vignette: ON", new Vector2(0.6f, 0.09f), new Vector3(0, -0.26f, -0.01f), b =>
            {
                ComfortSettings.VignetteEnabled = !ComfortSettings.VignetteEnabled;
                b.Label.text = "Comfort vignette: " + (ComfortSettings.VignetteEnabled ? "ON" : "OFF");
            }, WorldUI.Cm(0.04f));
            _volume.Changed += v => { ComfortSettings.MasterVolume = v / 100f; AudioListener.volume = ComfortSettings.MasterVolume; };

            _warn = new GameObject("StudyConfigWarning");
            _warn.transform.SetParent(_root, false);
            WorldUI.Panel(_warn.transform, "WarnBack", new Vector2(panelSize.x, 0.12f), new Color(0.5f, 0.3f, 0.02f), new Vector3(0, panelSize.y * 0.5f + 0.1f, 0));
            WorldUI.Text(_warn.transform, "StudyConfig.json was missing. A template was written. Tell the experimenter to set the participant number.",
                WorldUI.Cm(0.035f), new Vector3(0, panelSize.y * 0.5f + 0.1f, -0.005f), new Vector2(panelSize.x * 0.95f, 0.1f));
            _warn.SetActive(false);

            var go = new GameObject("ToneSource");
            go.transform.SetParent(transform, false);
            _toneSource = go.AddComponent<AudioSource>();
            _toneSource.spatialBlend = 1f; _toneSource.playOnAwake = false;
            _toneSource.rolloffMode = AudioRolloffMode.Linear; _toneSource.maxDistance = 10f;
            var g = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.Group("SFX") : null;
            if (g != null) _toneSource.outputAudioMixerGroup = g;
        }

        void ShowPage(Page p)
        {
            Current = p;
            _volume.gameObject.SetActive(p == Page.Comfort);
            _vignetteBtn.gameObject.SetActive(p == Page.Comfort);
            _next.gameObject.SetActive(true);
            _next.Interactable = true;
            switch (p)
            {
                case Page.Consent:
                    _title.text = "Protocol: Deep Sleep";
                    _body.text = ConsentText;
                    _next.Label.text = "I agree";
                    break;
                case Page.Height:
                    var rig = PlayerRig.Instance;
                    float h = rig != null ? rig.CameraHeight : DS.EYE_HEIGHT;
                    HeightWarning = h < minHeadHeight;
                    _title.text = "Stand up";
                    _body.text = HeightWarning
                        ? "Your headset looks low (" + h.ToString("0.00") + " m). Please stand up straight. If you cannot stand, tell the experimenter."
                        : "Good. You will stay standing for the whole session.";
                    _next.Label.text = "Continue";
                    break;
                case Page.AudioCheck:
                    _title.text = "Audio check";
                    _body.text = "You will hear one short beep. If you can hear it, continue to the volume page.";
                    _next.Label.text = "Play beep";
                    break;
                case Page.Comfort:
                    _title.text = "Volume and comfort";
                    _body.text = "Set the volume to a comfortable level. Touch the bar. It locks when you let go.";
                    _next.Label.text = "Start";
                    _next.Interactable = false;
                    if (!_volume.HasValue) _volume.SetValue(initialVolume);
                    _next.Interactable = true;
                    break;
                case Page.Starting:
                    _title.text = "Starting";
                    _body.text = "";
                    _next.gameObject.SetActive(false);
                    break;
            }
        }

        public void OnNext()
        {
            switch (Current)
            {
                case Page.Consent: ShowPage(Page.Height); break;
                case Page.Height: ShowPage(Page.AudioCheck); break;
                case Page.AudioCheck:
                    if (_toneRoutine == null) { _next.Interactable = false; _toneRoutine = StartCoroutine(ToneCheck()); }
                    break;
                case Page.Comfort:
                    _volume.Locked = true;
                    ShowPage(Page.Starting);
                    StartCoroutine(Begin());
                    break;
            }
        }

        IEnumerator ToneCheck()
        {
            // one beep, same in both ears, only to confirm that the audio is audible
            var clip = DeepSleepAssets.Get("S002");
            _toneSource.spatialBlend = 0f; _toneSource.spatialize = false;
            if (clip != null) _toneSource.PlayOneShot(clip, toneVolume);
            TonesConfirmed = 1;
            DS.Log("Setup beep played");
            yield return new WaitForSecondsRealtime(clip != null ? Mathf.Min(clip.length, 2f) + 0.3f : 0.3f);
            _toneRoutine = null;
            ShowPage(Page.Comfort);
        }

        IEnumerator Begin()
        {
            Started = true;
            var sm = SessionManager.Instance;
            // R003 must finish inside the Setup stage: nothing said here may carry into the next one
            float waited = 0f;
            while (NarrativeVoiceDirector.Instance != null && NarrativeVoiceDirector.Instance.IsBusy && waited < maxVoiceWaitSeconds) { waited += Time.unscaledDeltaTime; yield return null; }
            if (sm != null) sm.TryAdvance(Stage.Awakening);
            DS.Log($"Setup complete: vol={ComfortSettings.MasterVolume:0.00} vignette={ComfortSettings.VignetteEnabled} tones={TonesConfirmed}");
            yield return new WaitForSecondsRealtime(0.3f);
            if (SceneFlow.Instance != null) SceneFlow.Instance.GoToMedicalBay();
        }
    }
}
