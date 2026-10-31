using System;
using System.Collections;
using UnityEngine;
using Acrophobia.ProceduralEnvironment;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Implements the 5-Tier dynamic platform system and cable jerk perturbation
    /// for the Acrophobia Exposure Therapy application in Unity 6 LTS (URP).
    ///
    /// Tiers:
    /// - Tier 1 (Enclosed Ledge): Front safety railings locked at waist level with solid BoxCollider; forward passage blocked.
    /// - Tier 2 (Open Edge): Front railings smoothly lower into the deck over 2.0s; player can walk directly to the sheer drop.
    /// - Tier 3 (Crane Suspension): Suspended crane platform (width 2.4m) activates with corner cables overhead and gentle sinusoidal sway (0.03m, 0.8Hz).
    /// - Tier 4 (Width Contraction): Smoothly animates platform width shrinking from 2.4m down to 1.3m over 6.0s; side BoxColliders move inward synchronously.
    /// - Tier 5 (Narrow Razor Plank & Glass Floor): Shrinks width to 0.55m; smoothly fades floor material alpha from 1.0 to 0.35 (M_Catwalk_Glass), revealing live NYC traffic directly underfoot.
    ///
    /// Perturbations:
    /// - TriggerCableJerk(magnitude): Instantaneous 0.08m vertical drop and 1.2° pitch roll spring-damping back to level over 0.5s, with cable creak and wind gust sound.
    /// </summary>
    public class DynamicCatwalkController : MonoBehaviour
    {
        [Header("Platform Transforms & Colliders")]
        [Tooltip("The main catwalk platform transform (Industrial_Catwalk).")]
        [SerializeField] private Transform _catwalkPlatform;

        [Tooltip("Front safety railing root object (Safety_Railing_Tier1).")]
        [SerializeField] private Transform _frontRailing;

        [Tooltip("Solid BoxCollider on the front safety railing.")]
        [SerializeField] private BoxCollider _frontRailingCollider;

        [Tooltip("Solid side BoxCollider on the left railing.")]
        [SerializeField] private Transform _leftRailingCollider;

        [Tooltip("Solid side BoxCollider on the right railing.")]
        [SerializeField] private Transform _rightRailingCollider;

        [Tooltip("Walkable floor BoxCollider on the catwalk.")]
        [SerializeField] private BoxCollider _platformFloorCollider;

        [Header("Suspension Overhead Cables")]
        [Tooltip("Parent object containing the 4 corner crane cables.")]
        [SerializeField] private GameObject _cornerCablesGroup;

        [Tooltip("Transforms for the 4 corner cables (FL, FR, BL, BR).")]
        [SerializeField] private Transform[] _cableTransforms;

        [Tooltip("Overhead crane suspension hook point relative to catwalk platform center.")]
        [SerializeField] private Vector3 _overheadHookOffset = new Vector3(0f, 18.0f, 2.0f);

        [Header("Materials")]
        [Tooltip("Original opaque catwalk floor material.")]
        [SerializeField] private Material _opaqueCatwalkMaterial;

        [Tooltip("Transparent glass floor material (M_Catwalk_Glass).")]
        [SerializeField] private Material _glassMaterial;

        [Header("Modular Mesh References")]
        [Tooltip("Prefab containing modular Structure, Railings, and WalkingSurface (Catwalk_Wide_Rails).")]
        [SerializeField] private GameObject _modularCatwalkPrefab;

        [Tooltip("MeshRenderer for the walking surface/floor (fades to glass in Tier 5).")]
        [SerializeField] private MeshRenderer _floorRenderer;

        [Tooltip("MeshRenderers for structural frame, beams, and railings (always remain solid opaque metal).")]
        [SerializeField] private MeshRenderer[] _frameRenderers;

        [Tooltip("Legacy: MeshRenderers whose materials were previously swapped or faded.")]
        [SerializeField] private MeshRenderer[] _catwalkRenderers;

        [Header("Audio Sources & Clips")]
        [SerializeField] private AudioSource _creakAudioSource;
        [SerializeField] private AudioSource _windAudioSource;
        [SerializeField] private AudioClip _cableCreakClip;
        [SerializeField] private AudioClip _windGustClip;

        [Header("Tier Configuration & State")]
        [SerializeField] private int _currentTier = 1;
        [SerializeField] private float _currentWidth = 2.4f;
        [SerializeField] private float _currentFloorAlpha = 1.0f;
        [SerializeField] private bool _isSwayActive = false;

        [Header("Sway Dynamics (Tier 3-5)")]
        [SerializeField] private float _swayAmplitude = 0.03f;
        [SerializeField] private float _swayFrequency = 0.8f;
        [SerializeField] private float _swayRollAngle = 0.35f;

        [Header("Cable Jerk Spring Dynamics")]
        [SerializeField] private float _springStiffness = 180.0f;
        [SerializeField] private float _springDamping = 18.0f;

        // Runtime Positions & Bases (Local Space relative to parent environment)
        private Vector3 _platformBaseLocalPos;
        private Quaternion _platformBaseLocalRot;
        private Vector3 _frontRailingRaisedLocalPos;
        private Vector3 _frontRailingLoweredLocalPos;

        // Dynamic State Variables
        private float _swayTimer = 0f;
        private float _jerkOffsetY = 0f;
        private float _jerkVelY = 0f;
        private float _jerkPitch = 0f;
        private float _jerkVelPitch = 0f;
        private float _jerkRoll = 0f;
        private float _jerkVelRoll = 0f;

        // Material Instance for Runtime URP Transparency Modification
        private Material _activeGlassMaterialInstance;

        // Coroutines
        private Coroutine _railingCoroutine;
        private Coroutine _widthCoroutine;
        private Coroutine _glassFadeCoroutine;

        // Events
        public event Action<int> OnTierChanged;
        public event Action<float> OnWidthChanged;
        public event Action<float> OnCableJerkTriggered;

        public int CurrentTier => _currentTier;
        public float CurrentWidth => _currentWidth;
        public float CurrentFloorAlpha => _currentFloorAlpha;
        public bool IsSwayActive => _isSwayActive;

        private void Awake()
        {
            ResolveReferences();
            InitializeBaselines();
            EnsureCornerCables();
            SetupAudioSources();
        }

        private void Start()
        {
            // Initialize at Tier 1 state
            SetTier(1, true);
        }

        private void Update()
        {
            // 1. Process physics spring-damping for Cable Jerk perturbation
            UpdateJerkSpringDamping();

            // 2. Process gentle sinusoidal suspension sway
            UpdateSuspensionSway();

            // 3. Update corner cables geometry
            UpdateCornerCablesGeometry();

            // 4. Keyboard test inputs for editor validation
            HandleDebugInputs();
        }

        /// <summary>
        /// Transitions the environment to the specified tier (1 through 5).
        /// </summary>
        public void SetTier(int tier, bool immediate = false)
        {
            tier = Mathf.Clamp(tier, 1, 5);
            _currentTier = tier;

            ResolveReferences();
            InitializeBaselines();
            EnsureCornerCables();

            switch (tier)
            {
                case 1:
                    // Tier 1 (Enclosed Ledge): Front safety railings locked at 1.05m height with solid BoxColliders; forward passage blocked.
                    SetRailingRaised(true, immediate);
                    SetSwayActive(false);
                    SetOverheadCablesActive(false);
                    SetPlatformWidth(2.4f, immediate ? 0f : 1.5f);
                    SetFloorGlassFade(1.0f, false, immediate);
                    break;

                case 2:
                    // Tier 2 (Open Edge): Front railings smoothly lower into the deck over 2.0s; player can walk directly to the sheer drop.
                    SetRailingRaised(false, immediate);
                    SetSwayActive(false);
                    SetOverheadCablesActive(false);
                    SetPlatformWidth(2.4f, immediate ? 0f : 1.5f);
                    SetFloorGlassFade(1.0f, false, immediate);
                    break;

                case 3:
                    // Tier 3 (Crane Suspension): Suspended crane platform (width 2.4m) activates with corner cables overhead and gentle sinusoidal sway (0.03m, 0.8Hz).
                    SetRailingRaised(false, immediate);
                    SetOverheadCablesActive(true);
                    SetSwayActive(true);
                    SetPlatformWidth(2.4f, immediate ? 0f : 2.0f);
                    SetFloorGlassFade(1.0f, false, immediate);
                    break;

                case 4:
                    // Tier 4 (Width Contraction): Smoothly animates platform width shrinking from 2.4m down to 1.3m over 6.0 seconds. Synchronously move side BoxColliders inward.
                    SetRailingRaised(false, immediate);
                    SetOverheadCablesActive(true);
                    SetSwayActive(true);
                    SetPlatformWidth(1.3f, immediate ? 0f : 6.0f);
                    SetFloorGlassFade(1.0f, false, immediate);
                    break;

                case 5:
                    // Tier 5 (Narrow Razor Plank & Glass Floor): Shrinks width to 0.55m. Smoothly fades floor material alpha from 1.0 down to 0.35 (M_Catwalk_Glass).
                    SetRailingRaised(false, immediate);
                    SetOverheadCablesActive(true);
                    SetSwayActive(true);
                    SetPlatformWidth(0.55f, immediate ? 0f : 4.0f);
                    SetFloorGlassFade(0.35f, true, immediate);
                    break;
            }

            OnTierChanged?.Invoke(_currentTier);
        }

        /// <summary>
        /// Triggers an instantaneous 0.08m vertical drop and 1.2° pitch roll that spring-damps
        /// back to level over 0.5s, with cable creak and sudden wind gust audio.
        /// </summary>
        public void TriggerCableJerk(float magnitude = 1.0f)
        {
            magnitude = Mathf.Clamp(magnitude, 0.1f, 3.0f);

            // Instantaneous perturbation
            _jerkOffsetY = -0.08f * magnitude;
            _jerkPitch = 1.2f * magnitude;
            _jerkRoll = (UnityEngine.Random.value > 0.5f ? 0.6f : -0.6f) * magnitude;

            // Reset velocities for sharp impulse
            _jerkVelY = 0f;
            _jerkVelPitch = 0f;
            _jerkVelRoll = 0f;

            // Play Cable Creak audio
            if (_creakAudioSource != null && _cableCreakClip != null)
            {
                _creakAudioSource.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
                _creakAudioSource.PlayOneShot(_cableCreakClip, Mathf.Clamp01(0.85f * magnitude));
            }

            // Play Wind Gust audio
            if (_windAudioSource != null && _windGustClip != null)
            {
                _windAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
                _windAudioSource.PlayOneShot(_windGustClip, Mathf.Clamp01(0.75f * magnitude));
            }

            OnCableJerkTriggered?.Invoke(magnitude);
        }

        #region Railing Control

        private void SetRailingRaised(bool raised, bool immediate)
        {
            if (_frontRailing == null) return;

            if (_railingCoroutine != null) StopCoroutine(_railingCoroutine);

            if (immediate)
            {
                _frontRailing.localPosition = raised ? _frontRailingRaisedLocalPos : _frontRailingLoweredLocalPos;
                if (_frontRailingCollider != null)
                {
                    _frontRailingCollider.enabled = raised;
                }
            }
            else
            {
                _railingCoroutine = StartCoroutine(AnimateRailingRoutine(raised, 2.0f));
            }
        }

        private IEnumerator AnimateRailingRoutine(bool raised, float duration)
        {
            Vector3 startPos = _frontRailing.localPosition;
            Vector3 targetPos = raised ? _frontRailingRaisedLocalPos : _frontRailingLoweredLocalPos;

            // If raising, enable collider early
            if (raised && _frontRailingCollider != null)
            {
                _frontRailingCollider.enabled = true;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                _frontRailing.localPosition = Vector3.Lerp(startPos, targetPos, t);
                yield return null;
            }

            _frontRailing.localPosition = targetPos;

            // If lowering, disable collider once below deck
            if (!raised && _frontRailingCollider != null)
            {
                _frontRailingCollider.enabled = false;
            }

            _railingCoroutine = null;
        }

        #endregion

        #region Width & Side Colliders Animation

        public void SetPlatformWidth(float targetWidth, float duration)
        {
            if (_widthCoroutine != null) StopCoroutine(_widthCoroutine);

            if (duration <= 0.001f)
            {
                ApplyWidth(targetWidth);
            }
            else
            {
                _widthCoroutine = StartCoroutine(AnimateWidthRoutine(targetWidth, duration));
            }
        }

        private IEnumerator AnimateWidthRoutine(float targetWidth, float duration)
        {
            float startWidth = _currentWidth;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                ApplyWidth(Mathf.Lerp(startWidth, targetWidth, t));
                yield return null;
            }

            ApplyWidth(targetWidth);
            _widthCoroutine = null;
        }

        private void ApplyWidth(float width)
        {
            _currentWidth = width;

            // 1. Scale Catwalk Platform mesh in X (original mesh is 2.0m wide)
            if (_catwalkPlatform != null)
            {
                float scaleX = _currentWidth / 2.0f;
                Vector3 localScale = _catwalkPlatform.localScale;
                _catwalkPlatform.localScale = new Vector3(scaleX, localScale.y, localScale.z);
            }

            // 2. Synchronously move side BoxColliders inward so player cannot step off
            float halfWidth = _currentWidth * 0.5f;
            float colliderThickness = 0.35f;

            float baseX = _catwalkPlatform != null ? _catwalkPlatform.position.x : _platformBaseLocalPos.x;

            if (_leftRailingCollider != null)
            {
                Vector3 p = _leftRailingCollider.position;
                p.x = baseX - halfWidth - (colliderThickness * 0.5f);
                _leftRailingCollider.position = p;
            }

            if (_rightRailingCollider != null)
            {
                Vector3 p = _rightRailingCollider.position;
                p.x = baseX + halfWidth + (colliderThickness * 0.5f);
                _rightRailingCollider.position = p;
            }

            // 3. Update floor collider width
            if (_platformFloorCollider != null)
            {
                // When catwalk platform scales in X, its child colliders scale automatically,
                // but setting size.x ensures exact sync if non-child
                Vector3 s = _platformFloorCollider.size;
                _platformFloorCollider.size = new Vector3(2.0f, s.y, s.z);
            }

            OnWidthChanged?.Invoke(_currentWidth);
        }

        #endregion

        #region Glass Floor Fade

        private void SetFloorGlassFade(float targetAlpha, bool useGlass, bool immediate)
        {
            if (_glassFadeCoroutine != null) StopCoroutine(_glassFadeCoroutine);

            if (immediate)
            {
                ApplyFloorAlpha(targetAlpha, useGlass);
            }
            else
            {
                _glassFadeCoroutine = StartCoroutine(AnimateGlassFadeRoutine(targetAlpha, useGlass, 3.0f));
            }
        }

        private IEnumerator AnimateGlassFadeRoutine(float targetAlpha, bool useGlass, float duration)
        {
            float startAlpha = _currentFloorAlpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                float a = Mathf.Lerp(startAlpha, targetAlpha, t);
                ApplyFloorAlpha(a, useGlass);
                yield return null;
            }

            ApplyFloorAlpha(targetAlpha, useGlass);
            _glassFadeCoroutine = null;
        }

        private void ApplyFloorAlpha(float alpha, bool useGlass)
        {
            _currentFloorAlpha = alpha;

            if (_floorRenderer == null || _frameRenderers == null || _frameRenderers.Length == 0)
            {
                ResolveReferences();
            }

            Material floorMatToApply;
            if (useGlass && _glassMaterial != null)
            {
                if (_activeGlassMaterialInstance == null)
                {
                    _activeGlassMaterialInstance = new Material(_glassMaterial);
                    _activeGlassMaterialInstance.name = "M_Catwalk_Glass_DynamicInstance";
                }
                floorMatToApply = _activeGlassMaterialInstance;
                CatwalkMaterialController.SetAlpha(floorMatToApply, alpha);
            }
            else
            {
                floorMatToApply = _opaqueCatwalkMaterial;
            }

            // 1. Apply glass material ONLY to the walking surface / floor renderer
            if (_floorRenderer != null && floorMatToApply != null)
            {
                if (_floorRenderer.sharedMaterial != floorMatToApply)
                {
                    _floorRenderer.material = floorMatToApply;
                }
            }

            // 2. Guarantee that structural frame, support struts, and side railings ALWAYS remain solid opaque steel
            if (_opaqueCatwalkMaterial != null && _frameRenderers != null)
            {
                foreach (var mr in _frameRenderers)
                {
                    if (mr != null && mr != _floorRenderer && mr.sharedMaterial != _opaqueCatwalkMaterial)
                    {
                        mr.material = _opaqueCatwalkMaterial;
                    }
                }
            }
        }

        #endregion

        #region Sway & Spring Perturbation

        public void SetSwayActive(bool active)
        {
            _isSwayActive = active;
            if (!active)
            {
                _swayTimer = 0f;
            }
        }

        private void UpdateSuspensionSway()
        {
            if (!_isSwayActive) return;

            _swayTimer += Time.deltaTime;
            float angularFreq = _swayFrequency * 2.0f * Mathf.PI;

            // Gentle compound sinusoidal sway (amplitude: 0.03m, frequency: 0.8Hz)
            float swayX = Mathf.Sin(_swayTimer * angularFreq) * _swayAmplitude;
            float swayZ = Mathf.Cos(_swayTimer * angularFreq * 0.7f) * (_swayAmplitude * 0.4f);
            float swayRoll = Mathf.Sin(_swayTimer * angularFreq) * _swayRollAngle;

            ApplyPlatformMotion(swayX, swayZ, swayRoll);
        }

        private void UpdateJerkSpringDamping()
        {
            // If near rest, no calculation needed
            if (Mathf.Abs(_jerkOffsetY) < 0.0001f && Mathf.Abs(_jerkVelY) < 0.0005f &&
                Mathf.Abs(_jerkPitch) < 0.005f && Mathf.Abs(_jerkVelPitch) < 0.01f &&
                Mathf.Abs(_jerkRoll) < 0.005f && Mathf.Abs(_jerkVelRoll) < 0.01f)
            {
                _jerkOffsetY = 0f;
                _jerkVelY = 0f;
                _jerkPitch = 0f;
                _jerkVelPitch = 0f;
                _jerkRoll = 0f;
                _jerkVelRoll = 0f;

                if (!_isSwayActive && _catwalkPlatform != null)
                {
                    _catwalkPlatform.localPosition = _platformBaseLocalPos;
                    _catwalkPlatform.localRotation = _platformBaseLocalRot;
                }
                return;
            }

            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            // 2nd-order damped harmonic spring equation: F = -k*x - c*v
            float accelY = -_springStiffness * _jerkOffsetY - _springDamping * _jerkVelY;
            _jerkVelY += accelY * dt;
            _jerkOffsetY += _jerkVelY * dt;

            float accelPitch = -_springStiffness * _jerkPitch - _springDamping * _jerkVelPitch;
            _jerkVelPitch += accelPitch * dt;
            _jerkPitch += _jerkVelPitch * dt;

            float accelRoll = -_springStiffness * _jerkRoll - _springDamping * _jerkVelRoll;
            _jerkVelRoll += accelRoll * dt;
            _jerkRoll += _jerkVelRoll * dt;

            ApplyPlatformMotion(0f, 0f, 0f);
        }

        private void ApplyPlatformMotion(float swayX, float swayZ, float swayRoll)
        {
            if (_catwalkPlatform == null) return;

            Vector3 finalPos = _platformBaseLocalPos;
            finalPos.x += swayX;
            finalPos.y += _jerkOffsetY;
            finalPos.z += swayZ;

            Quaternion swayRot = Quaternion.Euler(_jerkPitch, 0f, swayRoll + _jerkRoll);
            Quaternion finalRot = _platformBaseLocalRot * swayRot;

            _catwalkPlatform.localPosition = finalPos;
            _catwalkPlatform.localRotation = finalRot;
        }

        #endregion

        #region Overhead Corner Cables

        public void SetOverheadCablesActive(bool active)
        {
            if (_cornerCablesGroup != null)
            {
                _cornerCablesGroup.SetActive(active);
                if (active) UpdateCornerCablesGeometry();
            }
        }

        private void EnsureCornerCables()
        {
            if (_cornerCablesGroup == null)
            {
                Transform existing = transform.Find("Corner_Crane_Cables");
                if (existing != null)
                {
                    _cornerCablesGroup = existing.gameObject;
                }
                else
                {
                    _cornerCablesGroup = new GameObject("Corner_Crane_Cables");
                    _cornerCablesGroup.transform.SetParent(transform, false);
                }
            }

            string[] cornerNames = { "Cable_FrontLeft", "Cable_FrontRight", "Cable_BackLeft", "Cable_BackRight" };
            _cableTransforms = new Transform[4];

            Material cableMat = _opaqueCatwalkMaterial;
#if UNITY_EDITOR
            if (cableMat == null)
            {
                cableMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Acrophobia Exposure Therapy/M_Skyscraper_Facade.mat");
            }
#endif

            for (int i = 0; i < 4; i++)
            {
                Transform child = _cornerCablesGroup.transform.Find(cornerNames[i]);
                GameObject cableObj;
                if (child == null)
                {
                    cableObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    cableObj.name = cornerNames[i];
                    cableObj.transform.SetParent(_cornerCablesGroup.transform, false);
                    var col = cableObj.GetComponent<Collider>();
                    if (col != null) DestroyImmediate(col);
                }
                else
                {
                    cableObj = child.gameObject;
                    var lr = cableObj.GetComponent<LineRenderer>();
                    if (lr != null) DestroyImmediate(lr);
                }

                var mr = cableObj.GetComponent<MeshRenderer>();
                if (mr != null && cableMat != null)
                {
                    mr.sharedMaterial = cableMat;
                }

                _cableTransforms[i] = cableObj.transform;
            }

            _cornerCablesGroup.SetActive(false);
        }

        private void UpdateCornerCablesGeometry()
        {
            if (_cornerCablesGroup == null || !_cornerCablesGroup.activeInHierarchy || _cableTransforms == null) return;

            Vector3 center = _catwalkPlatform != null ? _catwalkPlatform.position : Vector3.zero;
            Vector3 hookWorld = center + _overheadHookOffset;

            float halfW = _currentWidth * 0.5f;
            float zMin = 0f;
            float zMax = 4.0f;

            // Platform corners in world space
            Vector3 fl = center + _catwalkPlatform.TransformDirection(new Vector3(-halfW, 0.05f, zMax));
            Vector3 fr = center + _catwalkPlatform.TransformDirection(new Vector3(halfW, 0.05f, zMax));
            Vector3 bl = center + _catwalkPlatform.TransformDirection(new Vector3(-halfW, 0.05f, zMin));
            Vector3 br = center + _catwalkPlatform.TransformDirection(new Vector3(halfW, 0.05f, zMin));

            if (_cableTransforms.Length >= 4)
            {
                UpdateCylinderCable(_cableTransforms[0], fl, hookWorld);
                UpdateCylinderCable(_cableTransforms[1], fr, hookWorld);
                UpdateCylinderCable(_cableTransforms[2], bl, hookWorld);
                UpdateCylinderCable(_cableTransforms[3], br, hookWorld);
            }
        }

        private void UpdateCylinderCable(Transform cable, Vector3 start, Vector3 end)
        {
            if (cable == null) return;
            Vector3 dir = end - start;
            float dist = dir.magnitude;
            if (dist < 0.05f) return;

            cable.position = (start + end) * 0.5f;
            cable.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            // Default Unity Cylinder height is 2.0m, radius 0.5m
            cable.localScale = new Vector3(0.045f, dist * 0.5f, 0.045f);
        }

        #endregion

        #region Setup & Helpers

        private void ResolveReferences()
        {
            if (_catwalkPlatform == null)
            {
                var cw = GameObject.Find("Industrial_Catwalk");
                if (cw != null) _catwalkPlatform = cw.transform;
            }

            if (_frontRailing == null)
            {
                var fr = GameObject.Find("Safety_Railing_Tier1");
                if (fr != null) _frontRailing = fr.transform;
            }

            if (_frontRailing != null && _frontRailingCollider == null)
            {
                _frontRailingCollider = _frontRailing.GetComponent<BoxCollider>();
            }

            if (_leftRailingCollider == null)
            {
                var l = GameObject.Find("Railing_Solid_Left");
                if (l != null) _leftRailingCollider = l.transform;
            }

            if (_rightRailingCollider == null)
            {
                var r = GameObject.Find("Railing_Solid_Right");
                if (r != null) _rightRailingCollider = r.transform;
            }

            if (_catwalkPlatform != null && _platformFloorCollider == null)
            {
                _platformFloorCollider = _catwalkPlatform.GetComponent<BoxCollider>();
            }

            EnsureModularCatwalkStructure();

#if UNITY_EDITOR
            if (_glassMaterial == null)
            {
                _glassMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Acrophobia Exposure Therapy/M_Catwalk_Glass.mat");
            }
            if (_cableCreakClip == null)
            {
                _cableCreakClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/Metal_creak.mp3");
            }
            if (_windGustClip == null)
            {
                _windGustClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/Wind_gust_strong.mp3");
            }
#endif
        }

        /// <summary>
        /// Guarantees that the catwalk has an isolated floor renderer (fades to glass in Tier 5)
        /// separate from the structural frame and safety railing renderers (which always stay solid opaque steel).
        /// </summary>
        private void EnsureModularCatwalkStructure()
        {
            if (_catwalkPlatform == null) return;

            Transform walkingSurfaceTrans = null;
            Transform structureTrans = null;
            Transform railingsTrans = null;

            foreach (Transform child in _catwalkPlatform.GetComponentsInChildren<Transform>(true))
            {
                if (child == _catwalkPlatform) continue;

                if (walkingSurfaceTrans == null &&
                    (child.name.Equals("WalkingSurface", StringComparison.OrdinalIgnoreCase) ||
                     child.name.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    walkingSurfaceTrans = child;
                }
                else if (structureTrans == null &&
                    (child.name.Equals("Structure", StringComparison.OrdinalIgnoreCase) ||
                     child.name.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    structureTrans = child;
                }
                else if (railingsTrans == null &&
                    (child.name.Equals("Railings", StringComparison.OrdinalIgnoreCase) ||
                     child.name.IndexOf("Railing", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    railingsTrans = child;
                }
            }

            // If WalkingSurface does not exist yet under _catwalkPlatform, attach the modular deck
            if (walkingSurfaceTrans == null)
            {
#if UNITY_EDITOR
                if (_modularCatwalkPrefab == null)
                {
                    _modularCatwalkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Acrophobia Exposure Therapy/CatwalkModules/Catwalk_Wide_Rails.prefab");
                }
#endif
                if (_modularCatwalkPrefab != null)
                {
                    // Deactivate legacy monolithic LOD meshes so they don't produce ghost meshes
                    foreach (Transform child in _catwalkPlatform)
                    {
                        if (child.name.Contains("LOD") || child.name.Contains("Modular_CatWalk"))
                        {
                            child.gameObject.SetActive(false);
                        }
                    }
                    var rootMr = _catwalkPlatform.GetComponent<MeshRenderer>();
                    if (rootMr != null) rootMr.enabled = false;

                    GameObject modularInstance = Instantiate(_modularCatwalkPrefab, _catwalkPlatform);
                    modularInstance.name = "Modular_Catwalk_Deck";
                    modularInstance.transform.localPosition = Vector3.zero;
                    modularInstance.transform.localRotation = Quaternion.identity;
                    modularInstance.transform.localScale = Vector3.one;

                    walkingSurfaceTrans = modularInstance.transform.Find("WalkingSurface");
                    structureTrans = modularInstance.transform.Find("Structure");
                    railingsTrans = modularInstance.transform.Find("Railings");
                }
            }

            if (walkingSurfaceTrans != null)
            {
                _floorRenderer = walkingSurfaceTrans.GetComponent<MeshRenderer>();
            }

            var frameList = new System.Collections.Generic.List<MeshRenderer>();
            if (structureTrans != null)
            {
                var smr = structureTrans.GetComponent<MeshRenderer>();
                if (smr != null) frameList.Add(smr);
            }
            if (railingsTrans != null)
            {
                var rmr = railingsTrans.GetComponent<MeshRenderer>();
                if (rmr != null) frameList.Add(rmr);
            }

            // Fallback: gather all other active non-floor mesh renderers under platform
            if (frameList.Count == 0)
            {
                foreach (var mr in _catwalkPlatform.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr != _floorRenderer && mr.gameObject.activeInHierarchy && mr.enabled)
                    {
                        frameList.Add(mr);
                    }
                }
            }

            _frameRenderers = frameList.ToArray();

            if (_opaqueCatwalkMaterial == null && _frameRenderers.Length > 0 && _frameRenderers[0] != null)
            {
                _opaqueCatwalkMaterial = _frameRenderers[0].sharedMaterial;
            }
        }

        private void InitializeBaselines()
        {
            if (_catwalkPlatform != null)
            {
                _platformBaseLocalPos = _catwalkPlatform.localPosition;
                _platformBaseLocalRot = _catwalkPlatform.localRotation;
            }
            else
            {
                _platformBaseLocalPos = new Vector3(-13.00f, 104.17f, -12.00f);
                _platformBaseLocalRot = Quaternion.identity;
            }

            if (_frontRailing != null)
            {
                _frontRailingRaisedLocalPos = _frontRailing.localPosition;
                _frontRailingLoweredLocalPos = _frontRailingRaisedLocalPos - new Vector3(0f, 1.37f, 0f); // 1.37m below deck
            }
        }

        private void SetupAudioSources()
        {
            if (_creakAudioSource == null)
            {
                var creakObj = new GameObject("Audio_CableCreak");
                creakObj.transform.SetParent(transform, false);
                _creakAudioSource = creakObj.AddComponent<AudioSource>();
                _creakAudioSource.spatialBlend = 1.0f;
                _creakAudioSource.playOnAwake = false;
            }

            if (_windAudioSource == null)
            {
                var windObj = new GameObject("Audio_WindGust");
                windObj.transform.SetParent(transform, false);
                _windAudioSource = windObj.AddComponent<AudioSource>();
                _windAudioSource.spatialBlend = 0.8f;
                _windAudioSource.playOnAwake = false;
            }
        }

        private void HandleDebugInputs()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) SetTier(1);
            if (kb.digit2Key.wasPressedThisFrame) SetTier(2);
            if (kb.digit3Key.wasPressedThisFrame) SetTier(3);
            if (kb.digit4Key.wasPressedThisFrame) SetTier(4);
            if (kb.digit5Key.wasPressedThisFrame) SetTier(5);
            if (kb.jKey.wasPressedThisFrame) TriggerCableJerk(1.0f);
#else
            try
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) SetTier(1);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SetTier(2);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SetTier(3);
                if (Input.GetKeyDown(KeyCode.Alpha4)) SetTier(4);
                if (Input.GetKeyDown(KeyCode.Alpha5)) SetTier(5);
                if (Input.GetKeyDown(KeyCode.J)) TriggerCableJerk(1.0f);
            }
            catch (System.InvalidOperationException) { }
#endif
        }

        #endregion

        private void OnDestroy()
        {
            if (_activeGlassMaterialInstance != null)
            {
                Destroy(_activeGlassMaterialInstance);
            }
        }
    }
}
