using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 localOffset = new Vector3(0f, 4.5f, -7f);
    public float positionSmooth = 5f;
    public float rotationSmooth = 5f;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.TransformPoint(localOffset);
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionSmooth * Time.deltaTime));

        Quaternion look = Quaternion.LookRotation((target.position + Vector3.up * 0.8f) - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime));
    }
}
