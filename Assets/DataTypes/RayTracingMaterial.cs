using UnityEngine;

[System.Serializable]
public struct RayTracingMaterial
{

    public Color color;
    public Color emissionColor;
    public float emissionStrength;
    public Color specularColor;
    [Range(0, 1)] public float specularProbability;
    [Range(0, 1)] public float smoothness;
    [Min(1)] public float refractIndx;
    [Range(0, 1)] public float transparency;

    public void SetDefaultValues()
    {
        color = Color.white;
    }
}