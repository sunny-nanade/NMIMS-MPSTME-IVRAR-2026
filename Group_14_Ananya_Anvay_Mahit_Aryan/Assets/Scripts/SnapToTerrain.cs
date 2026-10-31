using UnityEngine;

public class SnapToTerrain : MonoBehaviour
{
    public Terrain terrain;
    public float yOffset = 0.5f;
    public bool snapOnStart = true;

    private void Start()
    {
        if (snapOnStart) Snap();
    }

    [ContextMenu("Snap To Terrain")]
    public void Snap()
    {
        if (terrain == null) terrain = Terrain.activeTerrain;
        if (terrain == null) return;

        Vector3 p = transform.position;
        p.y = terrain.SampleHeight(p) + terrain.transform.position.y + yOffset;
        transform.position = p;
    }
}
