using UnityEngine;

public struct Triangle
{
    public Vector3 posA;
    public Vector3 posB;
    public Vector3 posC;

    public Vector3 normalA;
    public Vector3 normalB;
    public Vector3 normalC;

    public Vector2 uvA;
    public Vector2 uvB;
    public Vector2 uvC;

    public Triangle(Vector3 posA, Vector3 posB, Vector3 posC, Vector3 normalA, Vector3 normalB, Vector3 normalC, Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        this.posA = posA;
        this.posB = posB;
        this.posC = posC;

        this.normalA = normalA;
        this.normalB = normalB;
        this.normalC = normalC;

        this.uvA = uvA;
        this.uvB = uvB;
        this.uvC = uvC;
    }
}
