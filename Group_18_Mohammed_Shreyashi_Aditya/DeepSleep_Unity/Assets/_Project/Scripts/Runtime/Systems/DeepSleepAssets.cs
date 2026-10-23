using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Fractured_Final
{
    /// <summary>
    /// Runtime lookup for things that must be reachable from code created at runtime:
    /// audio clips by ID (never by extension), the mixer, shared materials and data files.
    /// Lives in Assets/_Project/Resources/DeepSleepAssets.asset and is rebuilt by DeepSleep/Tools/Rebuild Asset Library.
    /// </summary>
    [CreateAssetMenu(menuName = "DeepSleep/Assets")]
    public class DeepSleepAssets : ScriptableObject
    {
        [Serializable]
        public struct AudioEntry
        {
            public string id;
            public AudioClip clip;
            public bool placeholder;
        }

        public List<AudioEntry> audio = new List<AudioEntry>();
        public AudioMixer mixer;
        public Material outlineMaterial;
        public Material fadeMaterial;
        public Material vignetteMaterial;
        public Material unlitMaterial;
        public TextAsset voiceManifest;
        public TextAsset mazeLayoutA;
        public TextAsset mazeLayoutB;
        /// <summary>Gain trim (dB) applied to condition M so its level matches S (set by the level-match tool).</summary>
        public float stereoGainTrimDb = 0f;
        /// <summary>Level-match curve: gain trim (dB) for condition M at these source distances (m), measured against the spatializer output. Empty = use stereoGainTrimDb.</summary>
        public float[] stereoTrimDistances = new float[0];
        public float[] stereoTrimDb = new float[0];

        public float StereoTrimAt(float meters)
        {
            if (stereoTrimDistances == null || stereoTrimDb == null || stereoTrimDistances.Length < 2 || stereoTrimDistances.Length != stereoTrimDb.Length) return stereoGainTrimDb;
            if (meters <= stereoTrimDistances[0]) return stereoTrimDb[0];
            for (int i = 1; i < stereoTrimDistances.Length; i++)
                if (meters <= stereoTrimDistances[i]) return Mathf.Lerp(stereoTrimDb[i - 1], stereoTrimDb[i], Mathf.InverseLerp(stereoTrimDistances[i - 1], stereoTrimDistances[i], meters));
            return stereoTrimDb[stereoTrimDb.Length - 1];
        }

        static DeepSleepAssets _inst;
        Dictionary<string, AudioEntry> _map;

        public static DeepSleepAssets Instance
        {
            get
            {
                if (_inst == null) _inst = Resources.Load<DeepSleepAssets>("DeepSleepAssets");
                return _inst;
            }
        }

        void BuildMap()
        {
            _map = new Dictionary<string, AudioEntry>();
            foreach (var e in audio)
            {
                if (string.IsNullOrEmpty(e.id) || e.clip == null) continue;
                // real files always win over placeholders
                if (_map.TryGetValue(e.id, out var old) && !old.placeholder && e.placeholder) continue;
                _map[e.id] = e;
            }
        }

        public AudioClip Clip(string id)
        {
            if (_map == null) BuildMap();
            return _map.TryGetValue(id, out var e) ? e.clip : null;
        }

        public bool IsPlaceholder(string id)
        {
            if (_map == null) BuildMap();
            return _map.TryGetValue(id, out var e) && e.placeholder;
        }

        public bool Has(string id)
        {
            if (_map == null) BuildMap();
            return _map.ContainsKey(id);
        }

        public AudioMixerGroup Group(string name)
        {
            if (mixer == null) return null;
            var g = mixer.FindMatchingGroups(name);
            return g != null && g.Length > 0 ? g[0] : null;
        }

        public void InvalidateCache() { _map = null; }

        public static AudioClip Get(string id)
        {
            var a = Instance;
            return a != null ? a.Clip(id) : null;
        }
    }
}
