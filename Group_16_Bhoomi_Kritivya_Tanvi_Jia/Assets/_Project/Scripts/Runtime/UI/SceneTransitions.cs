using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RescueSim.UI
{
    /// <summary>Loads scenes with a quick fade, always restoring time scale first.</summary>
    public class SceneTransitions : MonoBehaviour
    {
        [SerializeField] ExposureOverlay overlay;
        bool _loading;

        public void Configure(ExposureOverlay o) { overlay = o; }

        public void Load(string sceneName)
        {
            if (_loading) return;
            _loading = true;
            Time.timeScale = 1f;
            StartCoroutine(LoadRoutine(sceneName));
        }

        public void ReloadCurrent() => Load(SceneManager.GetActiveScene().name);

        IEnumerator LoadRoutine(string sceneName)
        {
            if (overlay != null)
            {
                overlay.FadeTo(1f, 0.4f);
                float t = 0f;
                while (!overlay.IsFadedOut && t < 1f) { t += Time.unscaledDeltaTime; yield return null; }
            }
            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                Debug.LogError($"[SceneTransitions] Scene '{sceneName}' is not in Build Settings.");
                _loading = false;
                overlay?.FadeTo(0f, 0.3f);
                yield break;
            }
            while (!op.isDone) yield return null;
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
