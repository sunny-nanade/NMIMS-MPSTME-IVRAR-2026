using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fractured_Final
{
    /// <summary>
    /// One colour grade per section. SCN_Main holds every section at once, so the ambient colour and the key light are set here when the player arrives
    /// in a section (and again when the player comes back from a smoke block). The smoke room keeps the exact light it was calibrated with:
    /// its preset is captured from the scene at start and is never changed.
    /// </summary>
    public class SectionLook : MonoBehaviour
    {
        [Serializable]
        public class Preset
        {
            public string section;
            public Color ambient;
            public Color keyColor;
            public float keyIntensity;
            public Vector3 keyEuler;
            [Range(0f, 1f)] public float reflectionIntensity = 1f;
        }

        [SerializeField]
        Preset[] presets =
        {
            new Preset { section = DS.SceneSetup, ambient = new Color(0.30f, 0.35f, 0.46f), keyColor = new Color(0.80f, 0.88f, 1f), keyIntensity = 0.55f, keyEuler = new Vector3(52f, -25f, 0f) },
            new Preset { section = DS.SceneMedicalBay, ambient = new Color(0.23f, 0.22f, 0.29f), keyColor = new Color(0.58f, 0.68f, 0.92f), keyIntensity = 0.5f, keyEuler = new Vector3(60f, -20f, 0f) },
            new Preset { section = DS.SceneHallway, ambient = new Color(0.24f, 0.21f, 0.25f), keyColor = new Color(0.80f, 0.64f, 0.58f), keyIntensity = 0.45f, keyEuler = new Vector3(52f, 12f, 0f) },
        };

        [Tooltip("Light post-processing (neutral tonemapping and a subtle bloom, from the section's Volume). Always off in the smoke room, which keeps the exact look it was calibrated with.")]
        [SerializeField] bool postProcessing = true;

        public static SectionLook Instance { get; private set; }
        public string AppliedSection { get; private set; }

        Light _key;
        Preset _smoke;
        Color _bayAmbientFallback;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            FindKey();
            CaptureSmokeBaseline();
            TrySubscribe();
            Apply(SceneFlow.Section);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (SceneFlow.Instance != null) SceneFlow.Instance.SceneArrived -= OnArrived;
            Instance = null;
        }

        bool _subscribed;
        void TrySubscribe()
        {
            if (_subscribed || SceneFlow.Instance == null) return;
            SceneFlow.Instance.SceneArrived += OnArrived;
            _subscribed = true;
        }

        bool _postDone;

        void Update()
        {
            if (!_subscribed) TrySubscribe();
            if (_key == null) { FindKey(); if (_key != null) Apply(SceneFlow.Section); }
            if (!_postDone && PlayerRig.Instance != null) { _postDone = true; ApplyPost(AppliedSection != DS.SceneSmokeRoom && postProcessing); } // the rig can appear after the first Apply
        }

        void FindKey()
        {
            float best = -1f; _key = null;
            foreach (var l in FindObjectsByType<Light>())
            {
                if (l.type != LightType.Directional) continue;
                float v = l.intensity * l.color.maxColorComponent;
                if (v > best) { best = v; _key = l; }
            }
        }

        void CaptureSmokeBaseline()
        {
            if (_key == null) return;
            // exactly what the smoke room was calibrated with (SmokeVisibility sets its own ambient and fog)
            _smoke = new Preset { section = DS.SceneSmokeRoom, ambient = RenderSettings.ambientLight, keyColor = _key.color, keyIntensity = _key.intensity, keyEuler = _key.transform.eulerAngles, reflectionIntensity = RenderSettings.reflectionIntensity };
        }

        void OnArrived(string scene)
        {
            string section = scene;
            int colon = scene.IndexOf(':');
            if (colon >= 0) section = scene.Substring(0, colon);
            Apply(section);
        }

        public void Apply(string section)
        {
            if (_key == null) return;
            Preset p = null;
            if (section == DS.SceneSmokeRoom) p = _smoke;
            else foreach (var x in presets) if (x.section == section) p = x;
            if (p == null) return;
            AppliedSection = section;
            _key.color = p.keyColor; _key.intensity = p.keyIntensity; _key.transform.rotation = Quaternion.Euler(p.keyEuler);
            RenderSettings.reflectionIntensity = p.reflectionIntensity;
            if (section != DS.SceneSmokeRoom)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = p.ambient;
            }
            ApplyPost(section != DS.SceneSmokeRoom && postProcessing);
        }

        void ApplyPost(bool on)
        {
            var rig = PlayerRig.Instance;
            if (rig == null) return;
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                var d = cam.GetUniversalAdditionalCameraData();
                if (d != null) d.renderPostProcessing = on;
            }
        }
    }
}
