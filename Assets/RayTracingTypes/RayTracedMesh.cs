using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.VisualScripting;

public class RayTracedMesh : MonoBehaviour
{
    [Header("Settings")]
    public RayTracingMaterial[] materials;
    [Header("Info")]
    public MeshRenderer meshRenderer;
    public MeshFilter meshFilter;
    public int triangleCount;

    [SerializeField, HideInInspector] int materialObjectID;
    [SerializeField] Mesh mesh;
    MeshChunk[] localChunks;
    MeshChunk[] worldChunks;

    const int maxDepth = 6;
    const int maxTrisPerChunk = 48;
    public MeshChunk[] GetSubMeshes()
    {
        // Split mesh into chunks (if result is not already cached)
        if (meshFilter != null && (true || mesh != meshFilter.sharedMesh || localChunks == null))
        {
            mesh = meshFilter.sharedMesh;
            localChunks = CreateChunks(mesh);
        }

        if (mesh.triangles.Length / 3 > RayTracingManager.TriLimit)
        {
            throw new System.Exception($"Please use a mesh with fewer than {RayTracingManager.TriLimit} triangles");
        }

        if (worldChunks == null || worldChunks.Length != localChunks.Length)
        {
            worldChunks = new MeshChunk[localChunks.Length];
        }

        // Transform to world space
        // TODO: upload matrices to gpu to avoid having to contantly upload all mesh data
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        Vector3 scale = transform.localScale;

        for (int i = 0; i < worldChunks.Length; i++)
        {
            MeshChunk localChunk = localChunks[i];
            if (worldChunks[i] == null || worldChunks[i].triangles?.Length != localChunk.triangles.Length)
            {
                worldChunks[i] = new MeshChunk(new Triangle[localChunk.triangles.Length], localChunk.bounds, localChunk.subMeshIndex);
            }
            UpdateWorldChunkFromLocal(worldChunks[i], localChunk, pos, rot, scale);
        }

        return worldChunks;
    }

    void UpdateWorldChunkFromLocal(MeshChunk worldChunk, MeshChunk localChunk, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        Triangle[] localTris = localChunk.triangles;
        Vector3 boundsMin = PointLocalToWorld(localTris[0].posA, pos, rot, scale);
        Vector3 boundsMax = boundsMin;

        for (int i = 0; i < localTris.Length; i++)
        {
            Vector3 worldA = PointLocalToWorld(localTris[i].posA, pos, rot, scale);
            Vector3 worldB = PointLocalToWorld(localTris[i].posB, pos, rot, scale);
            Vector3 worldC = PointLocalToWorld(localTris[i].posC, pos, rot, scale);
            Vector3 worldNormA = DirLocatToWorld(localTris[i].normalA, rot);
            Vector3 worldNormB = DirLocatToWorld(localTris[i].normalB, rot);
            Vector3 worldNormC = DirLocatToWorld(localTris[i].normalC, rot);
            Triangle worldTri = new Triangle(worldA, worldB, worldC, worldNormA, worldNormB, worldNormC);
            worldChunk.triangles[i] = worldTri;

            boundsMin = Vector3.Min(boundsMin, worldA);
            boundsMax = Vector3.Max(boundsMax, worldA);
            boundsMin = Vector3.Min(boundsMin, worldB);
            boundsMax = Vector3.Max(boundsMax, worldB);
            boundsMin = Vector3.Min(boundsMin, worldC);
            boundsMax = Vector3.Max(boundsMax, worldC);
        }

        worldChunk.bounds = new Bounds((boundsMin + boundsMax) / 2, boundsMax - boundsMin);
        worldChunk.subMeshIndex = localChunk.subMeshIndex;
    }

    static Vector3 PointLocalToWorld(Vector3 point, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        return rot * Vector3.Scale(point, scale) + pos;
    }

    static Vector3 DirLocatToWorld(Vector3 dir, Quaternion rot)
    {
        return rot * dir;
    }

    public RayTracingMaterial GetMaterial(int subMeshIndex)
    {
        return materials[Mathf.Min(subMeshIndex, materials.Length - 1)];
    }

    void OnValidate()
    {
        if (materials == null || materials.Length == 0)
        {
            materials = new RayTracingMaterial[1];
            materials[0].SetDefaultValues();
        }

        if (meshRenderer == null || meshFilter == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
            meshFilter = GetComponent<MeshFilter>();
        }


        SetUpMaterialDisplay();
        triangleCount = meshFilter.sharedMesh.triangles.Length / 3;
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

        for (int i = 0; i < meshRenderer.sharedMaterials.Length; i++)
        {
            RayTracingMaterial mat = materials[Mathf.Min(i, materials.Length - 1)];
            bool displayEmissiveCol = mat.color.maxColorComponent < mat.emissionColor.maxColorComponent * mat.emissionStrength;
            Color displayCol = displayEmissiveCol ? mat.emissionColor * mat.emissionStrength : mat.color;
            meshRenderer.sharedMaterials[i].color = displayCol;
        }
    }

    static MeshChunk[] CreateChunks(Mesh mesh)
    {
        MeshChunk[] subMeshes = new MeshChunk[mesh.subMeshCount];

        Vector3[] verts = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] indices = mesh.triangles;

        for (int i = 0; i < subMeshes.Length; i++)
        {
            UnityEngine.Rendering.SubMeshDescriptor subMeshInfo = mesh.GetSubMesh(i);
            var subMeshInices = indices.AsSpan(subMeshInfo.indexStart, subMeshInfo.indexCount);
            subMeshes[i] = CreateSubMesh(verts, normals, subMeshInices, i);
        }

        List<MeshChunk> splitChunksList = new List<MeshChunk>();
        foreach (MeshChunk subMesh in subMeshes)
        {
            Split(subMesh, splitChunksList);
        }

        return splitChunksList.ToArray();
    }

    static MeshChunk CreateSubMesh(Vector3[] verts, Vector3[] normals, Span<int> indices, int subMeshIndex)
    {
        Triangle[] triangles = new Triangle[indices.Length / 3];
        Bounds bounds = new Bounds(verts[indices[0]], Vector3.one * 0.01f);
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];

            Vector3 posA = verts[a];
            Vector3 posB = verts[b];
            Vector3 posC = verts[c];
            bounds.Encapsulate(posA);
            bounds.Encapsulate(posB);
            bounds.Encapsulate(posC);

            Vector3 normalA = normals[a];
            Vector3 normalB = normals[b];
            Vector3 normalC = normals[c];

            Triangle triangle = new Triangle(posA, posB, posC, normalA, normalB, normalC);
            triangles[i / 3] = triangle;
        }

        return new MeshChunk(triangles, bounds, subMeshIndex);
    }

    static void Split(MeshChunk currChunk, List<MeshChunk> splitChunks, int depth = 0)
    {
        if (currChunk.triangles.Length > maxTrisPerChunk && depth < maxDepth)
        {
            Vector3 q = currChunk.bounds.size / 4;
            Triangle[] allTriangles = currChunk.triangles;
            HashSet<int> takenTriangles = new();

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        int remainingTris = allTriangles.Length - takenTriangles.Count;
                        if (remainingTris > 0)
                        {
                            Vector3 splitBoundsOffset = new Vector3(q.x * x, q.y * y, q.z * z);
                            Bounds splitBounds = new Bounds(currChunk.bounds.center + splitBoundsOffset, q * 2);

                            MeshChunk splitChunk = Extract(allTriangles, takenTriangles, splitBounds, currChunk.subMeshIndex);
                            if (splitChunk.triangles.Length > 0)
                            {
                                Split(splitChunk, splitChunks, depth + 1);
                            }
                        }
                    }
                }
            }
        }
        else
        {
            splitChunks.Add(currChunk);
        }
    }

    static MeshChunk Extract(Triangle[] triangles, HashSet<int> takenTriangles, Bounds splitBounds, int subMeshIndex)
    {
        List<Triangle> newTris = new List<Triangle>();
        Bounds newBounds = new Bounds(splitBounds.center, splitBounds.size);

        for (int i = 0; i < triangles.Length; i++)
        {
            if (takenTriangles.Contains(i))
            {
                continue;
            }

            if (splitBounds.Contains(triangles[i].posA) || splitBounds.Contains(triangles[i].posB) || splitBounds.Contains(triangles[i].posC))
            {
                newBounds.Encapsulate(triangles[i].posA);
                newBounds.Encapsulate(triangles[i].posB);
                newBounds.Encapsulate(triangles[i].posC);
                newTris.Add(triangles[i]);
                takenTriangles.Add(i);
            }
        }

        return new MeshChunk(newTris.ToArray(), newBounds, subMeshIndex);
    }
}
