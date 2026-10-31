using System.Collections;
using TMPro;
using UnityEngine;

namespace CSER
{
    /// "CHAPTER 2 - Fred's Office" centred in front of the player for a few seconds when a chapter scene opens.
    /// The intro card waits behind it and comes back when the title has faded out.
    public class ChapterTitle : MonoBehaviour
    {
        public float fade = 0.9f;
        public float hold = 3.2f;

        IEnumerator Start()
        {
            var gm = GameManager.I;
            if (!gm || gm.chapter <= 0) yield break;
            yield return null;                          // GameUI has built its intro card by now
            Transform head = null;
            while (!head)
            {
                var cam = Camera.main;
                if (cam) head = cam.transform;
                yield return null;
            }
            yield return new WaitForSeconds(0.9f);      // let the fade-in from black start
            var card = GameUI.I ? GameUI.I.cardRoot : null;
            bool hidCard = card && card.gameObject.activeSelf;
            if (hidCard) card.gameObject.SetActive(false);

            var info = gm.Chapter;
            var go = new GameObject("ChapterTitle");
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            go.transform.position = head.position + fwd * 1.6f + Vector3.up * 0.05f;
            go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = new Vector2(2.6f, 1.1f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = 1.5f;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.text = $"<size=40%><color=#5BF0B0><b>CHAPTER {info.number}</b></color></size>\n<b>{info.title}</b>\n<size=32%><color=#C9D3E3>{info.subtitle}</color></size>";
            t.sortingOrder = 2;

            // dark rounded plate behind the text so it reads against any wall
            var plate = new GameObject("Plate");
            plate.transform.SetParent(go.transform, false);
            plate.transform.localPosition = new Vector3(0, 0, 0.02f);
            var sr = plate.AddComponent<SpriteRenderer>();
            sr.sprite = UIKit.Rounded;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(2.7f, 0.9f);
            sr.sortingOrder = 1;

            void Alpha(float a)
            {
                t.color = new Color(1, 1, 1, a);
                sr.color = new Color(0.04f, 0.07f, 0.12f, 0.82f * a);
            }
            Alpha(0);
            for (float x = 0; x < 1; x += Time.deltaTime / fade) { Alpha(x); yield return null; }
            Alpha(1);
            yield return new WaitForSeconds(hold);
            for (float x = 1; x > 0; x -= Time.deltaTime / fade) { Alpha(x); yield return null; }
            Destroy(go);

            if (hidCard && card && GameManager.I.State == GameState.Intro) card.gameObject.SetActive(true);
        }
    }
}
