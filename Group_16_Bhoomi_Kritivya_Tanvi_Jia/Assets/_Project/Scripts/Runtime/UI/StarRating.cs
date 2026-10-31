using UnityEngine;
using UnityEngine.UI;

namespace RescueSim.UI
{
    /// <summary>Three-star display. Lit stars are gold; unearned stars are dim.</summary>
    public class StarRating : MonoBehaviour
    {
        [SerializeField] Image[] stars = new Image[3];
        public static readonly Color Lit = new Color(1f, 0.82f, 0.2f, 1f);
        public static readonly Color Unlit = new Color(0.25f, 0.28f, 0.33f, 1f);

        public int Value { get; private set; }

        public void Bind(Image[] images) { stars = images; }

        public void SetStars(int count)
        {
            Value = Mathf.Clamp(count, 0, 3);
            if (stars == null) return;
            for (int i = 0; i < stars.Length; i++)
                if (stars[i] != null) stars[i].color = i < Value ? Lit : Unlit;
        }
    }
}
