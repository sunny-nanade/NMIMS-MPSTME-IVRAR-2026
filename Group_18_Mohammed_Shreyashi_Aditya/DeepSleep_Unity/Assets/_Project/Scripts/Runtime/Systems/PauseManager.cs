using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fractured_Final
{
    public class PauseManager : MonoBehaviour
    {
        public static PauseManager Instance { get; private set; }
        public bool IsPaused { get; private set; }
        public event Action<bool> PauseChanged;
        TextMesh _label;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (IsPaused) { Time.timeScale = 1f; AudioListener.pause = false; } // never leave the game frozen or silent behind
        }

        void Update()
        {
            if (InputService.Exists && InputService.Instance.MenuPressed && !ExperimenterPanel.IsOpen)
                SetPaused(!IsPaused);
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            DS.Log(paused ? "Paused" : "Resumed");
            PauseChanged?.Invoke(paused);
            ShowLabel(paused);
        }

        void ShowLabel(bool on)
        {
            if (_label == null)
            {
                var go = new GameObject("PauseLabel");
                go.transform.SetParent(transform, false);
                _label = go.AddComponent<TextMesh>();
                _label.text = "PAUSED"; _label.characterSize = 0.02f; _label.fontSize = 100;
                _label.anchor = TextAnchor.MiddleCenter; _label.color = Color.white;
            }
            _label.gameObject.SetActive(on);
            var cam = Camera.main;
            if (on && cam != null)
                _label.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 1.5f, Quaternion.LookRotation(cam.transform.forward));
        }
    }
}
