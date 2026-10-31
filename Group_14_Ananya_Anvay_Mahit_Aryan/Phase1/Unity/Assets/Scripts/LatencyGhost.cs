using System.Collections.Generic;
using UnityEngine;

public class LatencyGhost : MonoBehaviour
{
    public Rigidbody actualRover;
    public Transform delayedRoverVisual;
    public Transform predictiveGhostVisual;

    [Range(1.5f, 5f)] public float latencySeconds = 3f;
    public float historySeconds = 8f;

    private struct Snapshot
    {
        public float t;
        public Vector3 p;
        public Quaternion r;
        public Vector3 v;
        public Vector3 w;
    }

    private readonly Queue<Snapshot> history = new Queue<Snapshot>();

    private void Update()
    {
        if (actualRover == null) return;

        history.Enqueue(new Snapshot
        {
            t = Time.time,
            p = actualRover.position,
            r = actualRover.rotation,
            v = actualRover.linearVelocity,
            w = actualRover.angularVelocity
        });

        while (history.Count > 0 && Time.time - history.Peek().t > historySeconds)
            history.Dequeue();

        Snapshot delayed = GetDelayedSnapshot();

        if (delayedRoverVisual != null)
        {
            delayedRoverVisual.position = delayed.p;
            delayedRoverVisual.rotation = delayed.r;
        }

        if (predictiveGhostVisual != null)
        {
            float dt = Mathf.Max(0f, Time.time - delayed.t);
            Vector3 predictedPos = delayed.p + delayed.v * dt;

            float yawDeltaDeg = delayed.w.y * Mathf.Rad2Deg * dt;
            Quaternion predictedRot = Quaternion.AngleAxis(yawDeltaDeg, Vector3.up) * delayed.r;

            predictiveGhostVisual.position = predictedPos;
            predictiveGhostVisual.rotation = predictedRot;
        }
    }

    private Snapshot GetDelayedSnapshot()
    {
        Snapshot best = new Snapshot
        {
            t = Time.time,
            p = actualRover.position,
            r = actualRover.rotation,
            v = actualRover.linearVelocity,
            w = actualRover.angularVelocity
        };

        float target = Time.time - latencySeconds;

        foreach (Snapshot s in history)
        {
            best = s;
            if (s.t >= target) break;
        }

        return best;
    }
}
