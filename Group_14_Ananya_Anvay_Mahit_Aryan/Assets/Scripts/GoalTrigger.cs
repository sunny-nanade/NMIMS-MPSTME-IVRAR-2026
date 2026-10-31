using UnityEngine;

public class GoalTrigger : MonoBehaviour
{
    public RoverDrive rover;
    public float successRadius = 2.2f;
    public bool success;

    private void Update()
    {
        if (rover == null) return;
        float d = Vector3.Distance(transform.position, rover.transform.position);
        if (!success && d <= successRadius)
        {
            success = true;
            rover.SetAction(0f, 0f);
            Debug.Log("V1.5f UNITY GOAL SUCCESS");
        }
    }
}
