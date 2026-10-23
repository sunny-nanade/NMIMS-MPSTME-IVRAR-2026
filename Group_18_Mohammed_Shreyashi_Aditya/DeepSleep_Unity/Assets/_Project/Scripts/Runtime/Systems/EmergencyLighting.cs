using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// SCN_MedicalBay and SCN_HallwayDebrief: red pulse every 2 s, occasional flicker dips (0.1 to 0.4 s),
    /// a few cheap spark bursts at damaged consoles. No custom shaders. Sparks are off while SCN_SmokeRoom is loaded.
    /// </summary>
    public class EmergencyLighting : MonoBehaviour
    {
        [SerializeField] Light[] pulseLights;
        [SerializeField] Transform[] sparkPoints;
        [SerializeField] float pulsePeriod = 2f;
        [SerializeField] float baseIntensity = 1.2f;
        [SerializeField] float pulseDepth = 0.7f;
        [SerializeField] Vector2 flickerEvery = new Vector2(3f, 9f);
        [SerializeField] Vector2 flickerDuration = new Vector2(0.1f, 0.4f);
        [SerializeField] Vector2 sparkEvery = new Vector2(4f, 11f);
        [SerializeField] int sparkBurst = 8;
        [SerializeField] Color emergencyColor = new Color(1f, 0.1f, 0.05f);
        [Tooltip("Additive light-pool quads that stand in for the lamps that are not real lights; their colour pulses with the real ones")]
        [SerializeField] Material glowMaterial;
        [SerializeField] float glowStrength = 0.55f;

        public bool SparksEnabled { get; set; } = true;
        public Light[] PulseLights { get => pulseLights; set => pulseLights = value; }
        public Transform[] SparkPoints { get => sparkPoints; set => sparkPoints = value; }

        float _flickerUntil, _nextFlicker, _nextSpark;
        Color _glowOriginal;
        ParticleSystem[] _sparks;
        float[] _lightScale;

        void Start()
        {
            _nextFlicker = Time.time + Random.Range(flickerEvery.x, flickerEvery.y);
            _nextSpark = Time.time + Random.Range(sparkEvery.x, sparkEvery.y);
            if (pulseLights != null)
            {
                _lightScale = new float[pulseLights.Length];
                for (int i = 0; i < pulseLights.Length; i++)
                {
                    _lightScale[i] = pulseLights[i] != null && pulseLights[i].intensity > 0.01f ? pulseLights[i].intensity / baseIntensity : 1f;
                    if (pulseLights[i] != null) { pulseLights[i].color = emergencyColor; pulseLights[i].shadows = LightShadows.None; }
                }
            }
            if (glowMaterial != null) _glowOriginal = glowMaterial.GetColor("_BaseColor");
            BuildSparks();
        }

        void OnDestroy() { if (glowMaterial != null && _glowOriginal.maxColorComponent > 0f) glowMaterial.SetColor("_BaseColor", _glowOriginal); } // the shared material asset is left as it was

        void BuildSparks()
        {
            if (sparkPoints == null) return;
            _sparks = new ParticleSystem[sparkPoints.Length];
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            for (int i = 0; i < sparkPoints.Length; i++)
            {
                if (sparkPoints[i] == null) continue;
                var go = new GameObject("Sparks");
                go.transform.SetParent(sparkPoints[i], false);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = false; main.playOnAwake = false;
                main.startLifetime = 0.5f; main.startSpeed = 2.5f; main.startSize = 0.012f;
                main.startColor = new Color(1f, 0.8f, 0.3f);
                main.gravityModifier = 1f; main.maxParticles = 24;
                var em = ps.emission; em.rateOverTime = 0;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.01f;
                var r = go.GetComponent<ParticleSystemRenderer>();
                if (shader != null) { var m = new Material(shader); m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.4f)); r.sharedMaterial = m; }
                _sparks[i] = ps;
            }
        }

        void Update()
        {
            if (pulseLights != null)
            {
                float phase = (Time.time % pulsePeriod) / pulsePeriod;
                float pulse = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
                float level = baseIntensity * (1f - pulseDepth + pulseDepth * pulse);
                if (Time.time < _flickerUntil) level *= 0.08f;
                for (int i = 0; i < pulseLights.Length; i++)
                    if (pulseLights[i] != null) pulseLights[i].intensity = level * (_lightScale != null ? _lightScale[i] : 1f);
                if (glowMaterial != null)
                {
                    var c = emergencyColor * (glowStrength * level / baseIntensity); c.a = 1f;
                    glowMaterial.SetColor("_BaseColor", c);
                }
            }
            if (Time.time >= _nextFlicker)
            {
                _flickerUntil = Time.time + Random.Range(flickerDuration.x, flickerDuration.y);
                _nextFlicker = _flickerUntil + Random.Range(flickerEvery.x, flickerEvery.y);
            }
            if (Time.time >= _nextSpark)
            {
                _nextSpark = Time.time + Random.Range(sparkEvery.x, sparkEvery.y);
                if (SparksEnabled && SceneFlow.Section != DS.SceneSmokeRoom && _sparks != null && _sparks.Length > 0)
                {
                    var ps = _sparks[Random.Range(0, _sparks.Length)];
                    if (ps != null) ps.Emit(sparkBurst);
                }
            }
        }
    }
}
