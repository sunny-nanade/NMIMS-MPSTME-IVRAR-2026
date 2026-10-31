using UnityEngine;
using Group10Gestures.Core;
using Group10Gestures.Filtering;
using Group10Gestures.Recognition;

namespace Group10Gestures.UI
{
    /// <summary>
    /// Visualizes 21 3D joints and connecting bones for Left and Right hands.
    /// Provides immediate visual feedback of tracking fidelity and gesture classification.
    /// Joint colors react dynamically to recognized gestures.
    /// </summary>
    public class HandSkeletonRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandFilter _handFilter;
        [SerializeField] private GestureRecognizer _gestureRecognizer;

        [Header("Visual Styling")]
        [SerializeField] private float _jointRadius = 0.012f;
        [SerializeField] private float _boneWidth = 0.006f;

        private static readonly (int, int)[] s_Bones = new (int, int)[]
        {
            (0, 1), (1, 2), (2, 3), (3, 4),        // Thumb
            (0, 5), (5, 6), (6, 7), (7, 8),        // Index
            (5, 9), (9, 10), (10, 11), (11, 12),   // Middle
            (9, 13), (13, 14), (14, 15), (15, 16), // Ring
            (13, 17), (17, 18), (18, 19), (19, 20),// Pinky
            (0, 17)                                 // Palm Base
        };

        private class VisualHand
        {
            public Transform Root;
            public GameObject[] JointSpheres = new GameObject[21];
            public MeshRenderer[] JointRenderers = new MeshRenderer[21];
            public LineRenderer[] BoneLines = new LineRenderer[s_Bones.Length];
            public Material JointMat;
            public Material BoneMat;
        }

        private VisualHand _leftVisual = new VisualHand();
        private VisualHand _rightVisual = new VisualHand();

        private void Start()
        {
            if (_handFilter == null) _handFilter = FindAnyObjectByType<HandFilter>();
            if (_gestureRecognizer == null) _gestureRecognizer = FindAnyObjectByType<GestureRecognizer>();

            BuildVisualHand(Handedness.Left, _leftVisual, new Color(0.0f, 0.9f, 1.0f));
            BuildVisualHand(Handedness.Right, _rightVisual, new Color(0.1f, 1.0f, 0.4f));
        }

        private void BuildVisualHand(Handedness handedness, VisualHand visual, Color defaultColor)
        {
            GameObject root = new GameObject($"HandSkeleton_{handedness}");
            root.transform.SetParent(transform, false);
            visual.Root = root.transform;

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
            if (unlitShader == null) unlitShader = Shader.Find("Standard");

            visual.JointMat = new Material(unlitShader) { color = defaultColor };
            visual.BoneMat = new Material(unlitShader) { color = new Color(defaultColor.r, defaultColor.g, defaultColor.b, 0.7f) };

            // Create 21 joint spheres
            for (int i = 0; i < 21; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Joint_{i}";
                sphere.transform.SetParent(root.transform, false);
                sphere.transform.localScale = Vector3.one * (_jointRadius * 2f);

                // Disable collider for interaction performance
                Collider col = sphere.GetComponent<Collider>();
                if (col != null) Destroy(col);

                MeshRenderer mr = sphere.GetComponent<MeshRenderer>();
                mr.sharedMaterial = visual.JointMat;
                visual.JointSpheres[i] = sphere;
                visual.JointRenderers[i] = mr;
                sphere.SetActive(false);
            }

            // Create 20 connecting bones with LineRenderer
            for (int i = 0; i < s_Bones.Length; i++)
            {
                GameObject boneObj = new GameObject($"Bone_{s_Bones[i].Item1}_{s_Bones[i].Item2}");
                boneObj.transform.SetParent(root.transform, false);

                LineRenderer lr = boneObj.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = _boneWidth;
                lr.endWidth = _boneWidth;
                lr.sharedMaterial = visual.BoneMat;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                visual.BoneLines[i] = lr;
                boneObj.SetActive(false);
            }
        }

        private void Update()
        {
            if (_handFilter == null) return;

            UpdateVisualHand(Handedness.Left, _leftVisual);
            UpdateVisualHand(Handedness.Right, _rightVisual);
        }

        private void UpdateVisualHand(Handedness handedness, VisualHand visual)
        {
            bool tracked = _handFilter.IsTracked(handedness);

            if (!tracked)
            {
                if (visual.Root.gameObject.activeSelf)
                {
                    visual.Root.gameObject.SetActive(false);
                }
                return;
            }

            if (!visual.Root.gameObject.activeSelf)
            {
                visual.Root.gameObject.SetActive(true);
            }

            Vector3[] joints = _handFilter.GetFilteredJoints(handedness);

            // Determine dynamic gesture reaction color
            Color activeColor = handedness == Handedness.Left ? new Color(0.0f, 0.9f, 1.0f) : new Color(0.1f, 1.0f, 0.4f);
            if (_gestureRecognizer != null)
            {
                GestureResult result = handedness == Handedness.Left ? _gestureRecognizer.LeftResult : _gestureRecognizer.RightResult;
                switch (result.Gesture)
                {
                    case GestureType.Pinch:
                        activeColor = new Color(1.0f, 0.85f, 0.0f); // Gold
                        break;
                    case GestureType.Point:
                        activeColor = new Color(0.9f, 0.2f, 1.0f); // Magenta
                        break;
                    case GestureType.Peace:
                        activeColor = new Color(0.2f, 0.6f, 1.0f); // Sky Blue
                        break;
                    case GestureType.Fist:
                        activeColor = new Color(1.0f, 0.15f, 0.25f); // Crimson
                        break;
                    case GestureType.ThumbsUp:
                        activeColor = new Color(0.2f, 1.0f, 0.7f); // Mint Green
                        break;
                    case GestureType.OpenPalm:
                        activeColor = new Color(1.0f, 1.0f, 1.0f); // White
                        break;
                }
            }

            visual.JointMat.color = activeColor;
            visual.BoneMat.color = new Color(activeColor.r, activeColor.g, activeColor.b, 0.75f);

            // Update joint sphere positions
            for (int i = 0; i < 21; i++)
            {
                visual.JointSpheres[i].SetActive(true);
                visual.JointSpheres[i].transform.position = joints[i];
            }

            // Update bone line end points
            for (int i = 0; i < s_Bones.Length; i++)
            {
                int p1 = s_Bones[i].Item1;
                int p2 = s_Bones[i].Item2;

                visual.BoneLines[i].gameObject.SetActive(true);
                visual.BoneLines[i].SetPosition(0, joints[p1]);
                visual.BoneLines[i].SetPosition(1, joints[p2]);
            }
        }
    }
}
