using UnityEngine;
[System.Serializable]
public struct EnviornmentSettings
{
    public bool Enabled;
    public Color SkyColorHorizon;
    public Color SkyColorZenith;
    public Vector3 SunLightDirection;
    public float SunFocus;
    [Min(0)]
    public float SunIntensity;
    public Color GroundColor;
}
