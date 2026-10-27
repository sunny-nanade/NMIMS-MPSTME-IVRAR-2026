using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class V15fObstacleAabb
{
    public string name;
    public float min_x;
    public float max_x;
    public float min_z;
    public float max_z;
}

[Serializable]
public class V15fNavSceneData
{
    public float terrain_origin_x;
    public float terrain_origin_z;
    public float terrain_size_x;
    public float terrain_size_z;

    public float start_x;
    public float start_z;
    public float goal_x;
    public float goal_z;

    public V15fObstacleAabb[] obstacles;
}

public class V15fNavSceneExporter : MonoBehaviour
{
    [Header("References")]
    public Transform roverStart;
    public Transform goal;
    public Terrain terrain;

    [Header("Obstacle layer")]
    [Tooltip("Use the SAME rock/obstacle layer mask as LidarGridSensor.obstacleMask.")]
    public LayerMask obstacleMask;

    [Header("Export")]
    public string outputFileName = "v15f_nav_scene.json";

    [Tooltip("Useful if rocks are spawned/enabled only in Play mode.")]
    public bool exportOnStart = false;

    private void Start()
    {
        if (exportOnStart)
            ExportScene();
    }

    [ContextMenu("Export V1.5f Navigation Scene")]
    public void ExportScene()
    {
        if (roverStart == null)
        {
            Debug.LogError("V15fNavSceneExporter: roverStart is not assigned.");
            return;
        }

        if (goal == null)
        {
            Debug.LogError("V15fNavSceneExporter: goal is not assigned.");
            return;
        }

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        if (terrain == null)
        {
            Debug.LogError("V15fNavSceneExporter: terrain is not assigned and no active Terrain exists.");
            return;
        }

        Collider[] allColliders = FindObjectsOfType<Collider>();
        List<V15fObstacleAabb> rocks = new List<V15fObstacleAabb>();

        foreach (Collider c in allColliders)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                continue;

            int layer = c.gameObject.layer;
            bool isObstacle = (obstacleMask.value & (1 << layer)) != 0;

            if (!isObstacle)
                continue;

            // WheelCollider inherits Collider, but should never be on the obstacle layer.
            Bounds b = c.bounds;

            rocks.Add(new V15fObstacleAabb
            {
                name = c.gameObject.name,
                min_x = b.min.x,
                max_x = b.max.x,
                min_z = b.min.z,
                max_z = b.max.z
            });
        }

        Vector3 tp = terrain.transform.position;
        Vector3 ts = terrain.terrainData.size;

        V15fNavSceneData data = new V15fNavSceneData
        {
            terrain_origin_x = tp.x,
            terrain_origin_z = tp.z,
            terrain_size_x = ts.x,
            terrain_size_z = ts.z,

            start_x = roverStart.position.x,
            start_z = roverStart.position.z,
            goal_x = goal.position.x,
            goal_z = goal.position.z,

            obstacles = rocks.ToArray()
        };

        string dir = Application.streamingAssetsPath;
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, outputFileName);
        File.WriteAllText(path, JsonUtility.ToJson(data, true));

        Debug.Log(
            "V1.5f NAV EXPORT COMPLETE\n" +
            "File: " + path + "\n" +
            "Rover start: (" + data.start_x.ToString("F2") + ", " + data.start_z.ToString("F2") + ")\n" +
            "Goal: (" + data.goal_x.ToString("F2") + ", " + data.goal_z.ToString("F2") + ")\n" +
            "Obstacle colliders exported: " + rocks.Count
        );
    }
}
