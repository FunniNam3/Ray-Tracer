using UnityEngine;

public struct MeshInfo
{
    public int triangeStartIndex;
    public int triangleCount;
    public RayTracingMaterial material;
    Vector3 boundsMin;
    Vector3 boundsMax;

    public MeshInfo(int triangeStartIndex, int triangleCount, RayTracingMaterial material, Bounds bounds)
    {
        this.triangeStartIndex = triangeStartIndex;
        this.triangleCount = triangleCount;
        this.material = material;
        this.boundsMin = bounds.min;
        this.boundsMax = bounds.max;
    }
}