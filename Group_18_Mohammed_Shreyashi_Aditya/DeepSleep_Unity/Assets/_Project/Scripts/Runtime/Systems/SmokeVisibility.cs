using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Smoke: visibility about 2 m through exponential-squared fog (no particles), plus a helmet lamp with 1.5 m range on the head.
    /// Fog density for a visibility V (2 % transmittance) is sqrt(ln 50) / V.
    /// </summary>
    public class SmokeVisibility : MonoBehaviour
    {
        [SerializeField] float visibilityMeters = 2f;
        [SerializeField] float lampRange = 1.5f;
        [SerializeField] float lampIntensity = 16f;
        [SerializeField] float lampSpotAngle = 110f;
        [SerializeField] Color fogColor = new Color(0.24f, 0.20f, 0.16f); // dark grey-brown: walls inside 2 m read against it, anything beyond 3 m is swallowed
        [SerializeField] Color ambient = new Color(0.16f, 0.13f, 0.11f);

        GameObject _lamp;
        Camera _cam; CameraClearFlags _prevFlags; Color _prevBackground; bool _camSaved;
        // the room settings before the smoke: SCN_Main is one scene, so nothing reloads them when the player walks back into the Medical Bay
        bool _envSaved, _fogWas; FogMode _fogModeWas; float _fogDensityWas; Color _fogColorWas, _ambientWas; UnityEngine.Rendering.AmbientMode _ambientModeWas; Material _skyWas;

        public float Density { get { return Mathf.Sqrt(Mathf.Log(50f)) / Mathf.Max(0.1f, visibilityMeters); } }
        public float LampRange => lampRange;
        public bool LampOn => _lamp != null && _lamp.activeSelf;

        public void Apply()
        {
            if (!_envSaved)
            {
                _envSaved = true;
                _fogWas = RenderSettings.fog; _fogModeWas = RenderSettings.fogMode; _fogDensityWas = RenderSettings.fogDensity; _fogColorWas = RenderSettings.fogColor;
                _ambientModeWas = RenderSettings.ambientMode; _ambientWas = RenderSettings.ambientLight; _skyWas = RenderSettings.skybox;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Density;
            RenderSettings.fogColor = fogColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.skybox = null;
            var rig = PlayerRig.Instance;
            if (rig != null && rig.Head != null)
            {
                var cam = rig.Head.GetComponent<Camera>();
                if (cam != null)
                {
                    if (!_camSaved) { _cam = cam; _prevFlags = cam.clearFlags; _prevBackground = cam.backgroundColor; _camSaved = true; }
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = fogColor;
                }
            }
            EnsureLamp();
        }

        /// <summary>Applies the helmet lamp settings to a light (also used by the edit-mode screenshot tool so it shows what the player sees).</summary>
        public void ConfigureLamp(Light l)
        {
            l.type = LightType.Spot; l.range = lampRange; l.spotAngle = lampSpotAngle; l.innerSpotAngle = lampSpotAngle * 0.6f;
            l.intensity = lampIntensity; l.color = new Color(1f, 0.95f, 0.85f); l.shadows = LightShadows.None;
        }

        void EnsureLamp()
        {
            var rig = PlayerRig.Instance;
            if (rig == null || rig.Head == null) return;
            if (_lamp == null)
            {
                _lamp = new GameObject("HelmetLamp");
                _lamp.transform.SetParent(rig.Head, false);
                _lamp.transform.localPosition = new Vector3(0f, 0.1f, 0.1f);
                ConfigureLamp(_lamp.AddComponent<Light>());
            }
            _lamp.SetActive(true);
        }

        public void Clear()
        {
            if (_lamp != null) Destroy(_lamp);
            _lamp = null;
            // give the camera back exactly as it was before the smoke room (the other scenes use their own sky)
            if (_camSaved && _cam != null) { _cam.clearFlags = _prevFlags; _cam.backgroundColor = _prevBackground; }
            _camSaved = false;
            if (_envSaved)
            {
                RenderSettings.fog = _fogWas; RenderSettings.fogMode = _fogModeWas; RenderSettings.fogDensity = _fogDensityWas; RenderSettings.fogColor = _fogColorWas;
                RenderSettings.ambientMode = _ambientModeWas; RenderSettings.ambientLight = _ambientWas; RenderSettings.skybox = _skyWas;
                _envSaved = false;
            }
        }

        void OnDestroy() { Clear(); }
    }
}
