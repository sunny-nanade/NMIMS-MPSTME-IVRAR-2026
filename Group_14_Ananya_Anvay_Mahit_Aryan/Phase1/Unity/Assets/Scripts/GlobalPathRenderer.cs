using System.Globalization;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class GlobalPathRenderer : MonoBehaviour
{
    public Terrain terrain;
    public string csvFileName = "global_path.csv";
    public float yOffset = 0.25f;
    public bool loadOnStart = true;

    private void Start()
    {
        if (loadOnStart) LoadPath();
    }

    public void LoadPath()
    {
        string path = Path.Combine(Application.streamingAssetsPath, csvFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("Global path CSV not found: " + path);
            return;
        }

        List<Vector3> points = new List<Vector3>();
        string[] lines = File.ReadAllLines(path);

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("x"))
                continue;

            string[] p = line.Split(',');
            if (p.Length < 2) continue;

            if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

            float y = terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;
            points.Add(new Vector3(x, y + yOffset, z));
        }

        LineRenderer lr = GetComponent<LineRenderer>();
        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());
    }
}
