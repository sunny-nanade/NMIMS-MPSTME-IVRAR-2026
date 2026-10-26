using UnityEngine;

namespace CSER
{
    /// Central audio: UI one-shots (Kenney Interface Sounds, CC0) plus procedurally generated room ambience.
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }

        public AudioClip click, confirm, success, error, pickup, drop, open, close, glitch, bong, toast, key, win, shred, knock;
        [Range(0, 1)] public float ambienceVolume = 0.18f;

        AudioSource ui;
        AudioSource ambience;
        public static bool AmbienceOn = true;

        // clear the static when the scene unloads, so later scenes never call into a destroyed instance
        void OnDestroy() { if (I == this) I = null; }

        void Awake()
        {
            I = this;
            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;

            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = MakeAmbience();
            ambience.loop = true;
            ambience.volume = AmbienceOn ? ambienceVolume : 0f;
            ambience.Play();
            if (!shred) shred = MakeShred();
            if (!knock) knock = MakeKnock();
        }

        /// One knuckle rap on a wooden door: a low thud plus a short burst of noise.
        static AudioClip MakeKnock()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.22f);
            var data = new float[n];
            var rng = new System.Random(11);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float thud = Mathf.Sin(2 * Mathf.PI * (95 + 60 * Mathf.Exp(-t * 40)) * t) * Mathf.Exp(-t * 26);
                float tap = ((float)rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 140) * 0.5f;
                data[i] = Mathf.Clamp((thud * 0.9f + tap) * Mathf.Clamp01(t * 800), -1f, 1f);
            }
            var clip = AudioClip.Create("Knock", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static void Play(AudioClip clip, float volume = 1f)
        {
            if (I && clip) I.ui.PlayOneShot(clip, volume);
        }

        public static void PlayAt(AudioClip clip, Vector3 pos, float volume = 1f)
        {
            if (clip) AudioSource.PlayClipAtPoint(clip, pos, volume);
        }

        public void SetAmbience(bool on)
        {
            AmbienceOn = on;
            ambience.volume = on ? ambienceVolume : 0f;
        }

        /// Low office/server-room hum: filtered brown noise + mains hum, seamless 8 s loop.
        static AudioClip MakeAmbience()
        {
            const int rate = 22050;
            int n = rate * 8;
            var data = new float[n];
            var rng = new System.Random(7);
            float brown = 0;
            for (int i = 0; i < n; i++)
            {
                brown = Mathf.Clamp(brown + ((float)rng.NextDouble() * 2 - 1) * 0.02f, -1, 1) * 0.998f;
                float t = (float)i / rate;
                float hum = Mathf.Sin(2 * Mathf.PI * 50 * t) * 0.25f + Mathf.Sin(2 * Mathf.PI * 100 * t) * 0.1f;
                data[i] = brown * 0.6f + hum * 0.35f;
            }
            // crossfade the ends so the loop has no click
            int fade = rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float a = (float)i / fade;
                data[i] = data[i] * a + data[n - fade + i] * (1 - a);
            }
            var clip = AudioClip.Create("Ambience", n - fade, 1, rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }

        static AudioClip MakeShred()
        {
            const int rate = 22050;
            int n = (int)(rate * 1.2f);
            var data = new float[n];
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Clamp01(t * 12) * Mathf.Clamp01((1 - t) * 6);
                float motor = Mathf.Sin(2 * Mathf.PI * 120 * i / rate) * 0.3f;
                float grind = ((float)rng.NextDouble() * 2 - 1) * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 18 * i / rate));
                data[i] = (motor + grind * 0.6f) * env * 0.7f;
            }
            var clip = AudioClip.Create("Shred", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// Short controller rumble for tactile feedback on grab / button press.
        public static void Haptic(float strength = 0.4f, float seconds = 0.06f)
        {
            if (!I) return;
            I.StopAllCoroutines();
            I.StartCoroutine(I.HapticRoutine(strength, seconds));
        }

        System.Collections.IEnumerator HapticRoutine(float strength, float seconds)
        {
            OVRInput.SetControllerVibration(1f, strength, OVRInput.Controller.Touch);
            yield return new WaitForSeconds(seconds);
            OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.Touch);
        }
    }
}
