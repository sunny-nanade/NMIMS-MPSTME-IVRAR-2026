using UnityEngine;

public class MarsGravity : MonoBehaviour
{
    [SerializeField] private float gravity = -3.71f;

    private void Awake()
    {
        Physics.gravity = new Vector3(0f, gravity, 0f);
    }
}
