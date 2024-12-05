using UnityEngine;

[System.Serializable]
public struct RayTracingMaterial
{

    public Color color;
    public Color emissionColor;
    public float emissionStrength;

    public void SetDefaultValues()
    {
        color = Color.white;
    }
}