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

    public void SetDefaultValues()
    {
        color = Color.white;
    }
}