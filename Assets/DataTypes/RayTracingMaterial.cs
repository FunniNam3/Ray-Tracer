using UnityEngine;

[System.Serializable]
public struct RayTracingMaterial
{
    [Range(0, 1)] public int UseTexture;
    public int TextureIndex;
    public Color color;
    public Color emissionColor;
    public float emissionStrength;
    public Color specularColor;
    [Range(0, 1)] public float specularProbability;
    [Range(0, 1)] public float smoothness;
    [Min(0)] public float refractIndx;
    [Range(0, 1)] public float transparency;

    public void SetDefaultValues()
    {
        color = Color.white;
    }
}