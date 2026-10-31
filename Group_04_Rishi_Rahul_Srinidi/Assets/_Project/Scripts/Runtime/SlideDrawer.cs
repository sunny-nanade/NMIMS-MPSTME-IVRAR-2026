using System.Collections;
using UnityEngine;

namespace CSER
{
    /// Desk drawer that slides open / closed when clicked (ISDK ray + trigger via Clickable). Pure set dressing:
    /// its contents are parented to it, so they move with it and never fall out.
    public class SlideDrawer : MonoBehaviour
    {
        [Tooltip("Open position relative to the closed one, in the parent's local space")]
        public Vector3 openOffset = new Vector3(0, 0, -0.3f);
        public float duration = 0.4f;
        public bool IsOpen { get; private set; }

        Vector3 closedPos;
        Coroutine routine;

        void Awake() => closedPos = transform.localPosition;

        public void Toggle()
        {
            IsOpen = !IsOpen;
            Sfx.PlayAt(Sfx.I ? (IsOpen ? Sfx.I.open : Sfx.I.close) : null, transform.position, 0.6f);
            Sfx.Haptic(0.3f, 0.05f);
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Slide(IsOpen ? closedPos + openOffset : closedPos));
        }

        IEnumerator Slide(Vector3 target)
        {
            Vector3 from = transform.localPosition;
            for (float t = 0; t < 1; t += Time.deltaTime / duration)
            {
                transform.localPosition = Vector3.Lerp(from, target, 1 - Mathf.Pow(1 - t, 3));
                yield return null;
            }
            transform.localPosition = target;
            routine = null;
        }
    }
}
