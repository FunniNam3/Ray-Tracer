using UnityEngine;

public class Model : MonoBehaviour
{
    public MeshFilter meshFilter;
    public RayTracingMaterial material;
    public MeshRenderer meshRenderer;
    [SerializeField, HideInInspector] int materialObjectID;

    public Mesh Mesh => meshFilter.sharedMesh;

    private void OnValidate()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        SetUpMaterialDisplay();
    }

    void SetUpMaterialDisplay()
    {
        if (gameObject.GetInstanceID() != materialObjectID)
        {
            materialObjectID = gameObject.GetInstanceID();
            Material[] originalMaterials = meshRenderer.sharedMaterials;
            Material[] newMaterials = new Material[originalMaterials.Length];
            Shader shader = Shader.Find("Standard");
            for (int i = 0; i < meshRenderer.sharedMaterials.Length; i++)
            {
                newMaterials[i] = new Material(shader);
            }
            meshRenderer.sharedMaterials = newMaterials;
        }

        RayTracingMaterial mat = material;
        bool displayEmissiveCol = mat.color.maxColorComponent < mat.emissionColor.maxColorComponent * mat.emissionStrength;
        Color displayCol = displayEmissiveCol ? mat.emissionColor * mat.emissionStrength : mat.color;
        meshRenderer.sharedMaterial.color = displayCol;
    }
}