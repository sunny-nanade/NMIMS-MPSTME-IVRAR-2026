using System.Collections;
using UnityEngine;

namespace CSER
{
    /// Black quad just in front of the eyes; every scene starts black and fades in (comfortable VR transitions).
    public class Fader : MonoBehaviour
    {
        public static Fader I { get; private set; }
        public Material material; // URP Unlit, transparent black (created by the scene builder)
        public float fadeInSeconds = 1.2f;

        Material instance;
        Renderer quad;
        float fade;                 // scene-transition alpha
        float comfort, comfortTarget; // soft "head in a wall" dim, never full black

        // clear the static when the scene unloads, so later scenes never call into a destroyed instance
        void OnDestroy() { if (I == this) I = null; }

        void Awake()
        {
            I = this;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = "FadeQuad";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 0, 0.08f);
            go.transform.localScale = new Vector3(1f, 1f, 1f);
            quad = go.GetComponent<Renderer>();
            instance = new Material(material);
            quad.sharedMaterial = instance;
            quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.receiveShadows = false;
            SetAlpha(1f);
        }

        IEnumerator Start()
        {
            yield return null;
            yield return FadeTo(0f, fadeInSeconds);
        }

        /// Comfort dim when the head is inside geometry (PlayerBlocker): 0 = off. Eases over ~0.2 s.
        public void SetComfort(float alpha) => comfortTarget = Mathf.Clamp(alpha, 0f, 0.75f);

        void Update()
        {
            if (Mathf.Approximately(comfort, comfortTarget)) return;
            comfort = Mathf.MoveTowards(comfort, comfortTarget, Time.unscaledDeltaTime / 0.2f);
            Apply();
        }

        public IEnumerator FadeTo(float target, float seconds)
        {
            float start = fade;
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds))
            {
                SetAlpha(Mathf.Lerp(start, target, t * t * (3 - 2 * t)));
                yield return null;
            }
            SetAlpha(target);
        }

        void SetAlpha(float a)
        {
            fade = a;
            Apply();
        }

        void Apply()
        {
            float a = Mathf.Max(fade, comfort);
            instance.SetColor("_BaseColor", new Color(0, 0, 0, a));
            quad.enabled = a > 0.001f;
        }
    }
}
