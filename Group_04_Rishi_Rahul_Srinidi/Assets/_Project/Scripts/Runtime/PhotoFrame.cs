using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSER
{
    /// Fred's digital photo frame: cycles slides (the cat clue) and can be advanced by pointing at it.
    public class PhotoFrame : MonoBehaviour
    {
        public UnityEngine.UI.Image photo;
        public TMP_Text captionText;
        public Sprite[] slides;
        [TextArea] public string[] captions;
        public float interval = 5f;
        int index;
        float timer;

        void Start() => Show(0);

        void Update()
        {
            timer += Time.deltaTime;
            if (timer >= interval) Next();
        }

        public void Next() => Show(index + 1);

        void Show(int i)
        {
            timer = 0;
            int count = Mathf.Max(slides?.Length ?? 0, captions?.Length ?? 0);
            if (count == 0) return;
            index = (i % count + count) % count;
            if (photo && slides != null && slides.Length > 0)
            {
                photo.sprite = slides[index % slides.Length];
                photo.color = Color.white;
            }
            if (captionText && captions != null && captions.Length > 0) captionText.text = captions[index % captions.Length];
        }
    }
}
