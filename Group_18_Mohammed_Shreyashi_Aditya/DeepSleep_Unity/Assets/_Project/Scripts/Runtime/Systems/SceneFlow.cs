using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// Scene transitions with fades. See Docs/SCENE_FLOW.md.
    /// One scene (SCN_Main) with the sections SEC_Setup, SEC_MedicalBay, SEC_Hallway; the smoke room is the prefab PF_SmokeSection, created per block.
    /// Every transition places the player on the floor with the head at its real standing height (rig root on the floor).
    /// </summary>
    public class SceneFlow : MonoBehaviour
    {
        public static SceneFlow Instance { get; private set; }

        [SerializeField] float fadeSeconds = 0.6f;
        [SerializeField] string setupSpawnName = "SetupSpawn";
        [SerializeField] string playerSpawnName = "PlayerSpawn";
        [SerializeField] string coreSpawnName = "CoreRoomSpawn";
        [SerializeField] string smokeSpawnName = "SmokeEntrySpawn";
        [SerializeField] string hallwaySpawnName = "HallwaySpawn";

        /// <summary>The part of the one scene the player is in now (a DS.Scene* key).</summary>
        public static string Section { get; private set; } = DS.SceneSetup;

        public bool IsBusy { get; private set; }
        public event Action<string> SceneArrived;

        Renderer _fadeR;
        MaterialPropertyBlock _mpb;
        float _fade;
        GameObject _setup, _bay, _hall, _smoke;
        readonly List<GameObject> _hidden = new List<GameObject>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _mpb = new MaterialPropertyBlock();
            SceneManager.sceneLoaded += OnSceneLoaded;
            var cur = SceneManager.GetActiveScene();
            if (cur.isLoaded) OnSceneLoaded(cur, LoadSceneMode.Single);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }

        // ------------------------------------------------------------------ fading
        void EnsureFader()
        {
            var rig = PlayerRig.Instance;
            if (rig == null || rig.Head == null) return;
            if (_fadeR != null && _fadeR.transform.parent == rig.Head) return;
            var assets = DeepSleepAssets.Instance;
            if (assets == null || assets.fadeMaterial == null) return;
            if (_fadeR != null) Destroy(_fadeR.gameObject);
            var go = new GameObject("ScreenFade");
            go.transform.SetParent(rig.Head, false);
            go.transform.localPosition = new Vector3(0, 0, 0.1f);
            go.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = WorldUI.Quad();
            _fadeR = go.AddComponent<MeshRenderer>();
            _fadeR.sharedMaterial = assets.fadeMaterial;
            _fadeR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ApplyFade();
        }

        void ApplyFade()
        {
            if (_fadeR == null) return;
            _fadeR.enabled = _fade > 0.001f;
            _fadeR.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Fade", _fade);
            _fadeR.SetPropertyBlock(_mpb);
        }

        public IEnumerator FadeTo(float target, float seconds = -1f)
        {
            if (seconds < 0) seconds = fadeSeconds;
            EnsureFader();
            float start = _fade, t = 0;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                _fade = Mathf.Lerp(start, target, Mathf.Clamp01(t / seconds));
                EnsureFader(); ApplyFade();
                yield return null;
            }
            _fade = target; EnsureFader(); ApplyFade();
        }

        void LateUpdate() { if (_fade > 0f) { EnsureFader(); ApplyFade(); } }

        // ------------------------------------------------------------------ public transitions
        public void GoToMedicalBay(Action done = null) => StartCoroutine(SwitchTo(DS.SceneMedicalBay, playerSpawnName, done));
        public void GoToHallway(Action done = null) => StartCoroutine(SwitchTo(DS.SceneHallway, hallwaySpawnName, done));
        public void EnterSmokeRoom(Action done = null) => StartCoroutine(EnterSmokeRoutine(done));
        public void ReturnToCore(Action done = null) => StartCoroutine(ReturnToCoreRoutine(done));

        public int SceneArrivedSubscribers => SceneArrived?.GetInvocationList().Length ?? 0;

        /// <summary>Editor jumps and the experimenter restart: reload the one scene so every section starts fresh, then show the wanted section.</summary>
        public void LoadSceneForJump(string section, Action done = null)
        {
            StartCoroutine(ReloadThenShow(section, section == DS.SceneHallway ? hallwaySpawnName : section == DS.SceneSetup ? setupSpawnName : playerSpawnName, done));
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            Section = DS.SceneSetup;
            _setup = FindRoot(s, "SEC_Setup"); _bay = FindRoot(s, "SEC_MedicalBay"); _hall = FindRoot(s, "SEC_Hallway");
            _smoke = null; _hidden.Clear();
            VoiceGate.NotifyLoaded();
        }

        static GameObject FindRoot(Scene s, string name)
        {
            foreach (var r in s.GetRootGameObjects()) if (r.name == name) return r;
            return null;
        }

        IEnumerator ReloadThenShow(string section, string spawn, Action done)
        {
            while (IsBusy) yield return null;
            IsBusy = true;
            yield return FadeTo(1f);
            var op = SceneManager.LoadSceneAsync(DS.SceneMain, LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return null;
            if (section != DS.SceneSetup) Show(section);
            yield return null;
            ConfigureRigForScene(false);
            PlaceAtSpawn(spawn);
            DS.Log($"Scene loaded: {DS.SceneMain}, section {section}");
            SceneArrived?.Invoke(section);
            yield return FadeTo(0f);
            IsBusy = false;
            done?.Invoke();
        }

        void Show(string section)
        {
            Section = section;
            if (_setup != null) _setup.SetActive(section == DS.SceneSetup);
            if (_bay != null) _bay.SetActive(section == DS.SceneMedicalBay);
            if (_hall != null) _hall.SetActive(section == DS.SceneHallway);
            VoiceGate.NotifyLoaded();
        }

        IEnumerator SwitchTo(string section, string spawnName, Action done)
        {
            if (IsBusy) yield break;
            IsBusy = true;
            yield return FadeTo(1f);
            Show(section);
            yield return null; // let Awake/Start of the section run
            _hidden.Clear();
            ConfigureRigForScene(false);
            PlaceAtSpawn(spawnName);
            DS.Log($"Section shown: {section}");
            SceneArrived?.Invoke(section);
            yield return FadeTo(0f);
            IsBusy = false;
            done?.Invoke();
        }

        IEnumerator EnterSmokeRoutine(Action done)
        {
            if (IsBusy) yield break;
            IsBusy = true;
            yield return FadeTo(1f);
            if (_smoke == null)
            {
                var prefab = Resources.Load<GameObject>("PF_SmokeSection");
                if (prefab != null) { _smoke = Instantiate(prefab); _smoke.name = "SEC_SmokeRoom"; }
                else DS.Warn("PF_SmokeSection not found in Resources");
                yield return null;
            }
            HideOthers();
            Section = DS.SceneSmokeRoom;
            VoiceGate.NotifyLoaded();
            ConfigureRigForScene(true);
            PlaceAtSpawn(smokeSpawnName);
            DS.Log("Entered the smoke room section");
            SceneArrived?.Invoke(DS.SceneSmokeRoom);
            yield return FadeTo(0f);
            IsBusy = false;
            done?.Invoke();
        }

        IEnumerator ReturnToCoreRoutine(Action done)
        {
            if (IsBusy) yield break;
            IsBusy = true;
            yield return FadeTo(1f);
            if (_smoke != null) { Destroy(_smoke); _smoke = null; } // a fresh room for the next block
            yield return null;
            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            _hidden.Clear();
            Section = DS.SceneMedicalBay;
            VoiceGate.NotifyLoaded();
            ConfigureRigForScene(false);
            PlaceAtSpawn(coreSpawnName);
            DS.Log("Returned to the Medical Bay at CoreRoomSpawn");
            SceneArrived?.Invoke(DS.SceneMedicalBay + ":core");
            yield return FadeTo(0f);
            IsBusy = false;
            done?.Invoke();
        }

        void HideOthers()
        {
            _hidden.Clear();
            foreach (var go in new[] { _setup, _bay, _hall })
                if (go != null && go.activeSelf) { go.SetActive(false); _hidden.Add(go); }
        }

        public void ConfigureForSmoke(bool smoke) { ConfigureRigForScene(smoke); }

        void ConfigureRigForScene(bool smoke)
        {
            var rig = PlayerRig.Instance;
            if (rig == null) return;
            var loco = rig.GetComponent<PlayerLocomotion>();
            if (loco != null) loco.ConfigureForScene(smoke);
        }

        public void PlaceAtSpawn(string spawnName)
        {
            var rig = PlayerRig.Instance;
            if (rig == null) { DS.Warn("PlaceAtSpawn: no PlayerRig"); return; }
            var spawn = FindSpawn(spawnName);
            if (spawn != null) rig.PlaceAt(new Vector3(spawn.position.x, spawn.position.y, spawn.position.z), spawn.eulerAngles.y);
            else DS.Warn($"Spawn '{spawnName}' not found; player left where it is");
        }

        static Transform FindSpawn(string name)
        {
            var s = SceneManager.GetActiveScene();
            foreach (var root in s.GetRootGameObjects())
            {
                var t = FindDeep(root.transform, name);
                if (t != null) return t;
            }
            return null;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
