using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CSER
{
    /// Clear hover feedback for ray / poke pointers: a white wash over the button (the default tint is invisible on
    /// dark buttons). Added to every UIKit button.
    public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image wash;
        Selectable sel;

        void Awake()
        {
            sel = GetComponent<Selectable>();
            var go = new GameObject("HoverWash", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            wash = go.AddComponent<Image>();
            wash.color = new Color(1, 1, 1, 0.22f);
            wash.raycastTarget = false;
            go.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (wash && (!sel || sel.IsInteractable())) wash.gameObject.SetActive(true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (wash) wash.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (wash) wash.gameObject.SetActive(false);
        }
    }
}
