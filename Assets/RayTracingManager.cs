using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static UnityEngine.Mathf;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    // Super slow so please limit triangles
    public const int TriLimit = 2000;

    [Header("Ray Tracing Settings")]
    [SerializeField, Range(0, 32)] int MaxBounceCount = 1;
    [SerializeField, Range(0, 64)] int NumRaysPerPixel = 1;
    [SerializeField] float DefocusStrength = 0;
    [SerializeField] float DivergeStrength = 0;
    [SerializeField] float FocusDistance = 0;

    [SerializeField] EnviornmentSettings enviornmentSettings;
    [SerializeField] bool Accumulate;


    [Header("View Settings")]
    [SerializeField] bool useShaderInSceneView;
    [Header("Refrences")]
    [SerializeField] Shader rayTracingShader;
    [SerializeField] Shader accumulateShader;


    [Header("Info")]
    [SerializeField] int numRenderedFrames;
    [SerializeField] int numMeshChunks;
    [SerializeField] int numTriangles;


    // Materials and renderTextures
    Material rayTraceMaterial;
    Material accumulateMaterial;
    RenderTexture resultTexture;


    // Buffers
    ComputeBuffer sphereBuffer;
    ComputeBuffer triangleBuffer;
    ComputeBuffer meshInfoBuffer;

    List<Triangle> allTriangles;
    List<MeshInfo> allMeshInfo;

    void Start()
    {
        numRenderedFrames = 0;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        bool isSceneCam = Camera.current.name == "SceneCamera";

        if (isSceneCam)
        {
            if (useShaderInSceneView)
            {
                InitFrame();
                Graphics.Blit(null, dest, rayTraceMaterial);
            }
            else
            {
                Graphics.Blit(src, dest); // Draw the unaltered camera render to the screen
            }
        }
        else
        {

            InitFrame();
            if (Accumulate)
            {
                // Create a copy of prev frame
                RenderTexture prevFrame = RenderTexture.GetTemporary(src.width, src.height, 0, GraphicsFormat.R32G32B32A32_SFloat);
                Graphics.Blit(resultTexture, prevFrame);

                // Run raytracer and draw to temp texture
                rayTraceMaterial.SetInt("FrameCount", numRenderedFrames);
                RenderTexture currFrame = RenderTexture.GetTemporary(src.width, src.height, 0, GraphicsFormat.R32G32B32A32_SFloat);
                Graphics.Blit(null, currFrame, rayTraceMaterial);

                // Accumulate
                accumulateMaterial.SetInt("_Frame", numRenderedFrames);
                accumulateMaterial.SetTexture("_PrevFrame", prevFrame);
                Graphics.Blit(currFrame, resultTexture, accumulateMaterial);

                // Draw to screen
                Graphics.Blit(resultTexture, dest);

                // Release temps
                RenderTexture.ReleaseTemporary(prevFrame);
                RenderTexture.ReleaseTemporary(currFrame);
            }
            else
            {
                Graphics.Blit(null, dest, rayTraceMaterial);
            }


            numRenderedFrames += Application.isPlaying ? 1 : 0;
        }
    }

    struct Ray
    {
        public Vector3 start;
        public Vector3 dir;
    };

    void InitFrame()
    {
        InitMaterial(rayTracingShader, ref rayTraceMaterial);
        InitMaterial(accumulateShader, ref accumulateMaterial);

        if (resultTexture == null || !resultTexture.IsCreated() || resultTexture.width != Screen.width || resultTexture.height != Screen.height || resultTexture.graphicsFormat != GraphicsFormat.R32G32B32A32_SFloat)
        {
            if (resultTexture != null)
            {
                resultTexture.Release();
            }
            resultTexture = CreateRenderTexture(Screen.width, Screen.height, FilterMode.Bilinear, GraphicsFormat.R32G32B32A32_SFloat, "Result", 0, false);
        }
        else
        {
            resultTexture.name = "Unnamed";
            resultTexture.wrapMode = TextureWrapMode.Clamp;
            resultTexture.filterMode = FilterMode.Bilinear;
        }

        UpdateShaderParms();
        UpdateCameraParms(Camera.current);
        CreateSpheres();
        CreateMeshes();
    }

    RenderTexture CreateRenderTexture(int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", int depthMode = 0, bool useMipMaps = false)
    {
        RenderTexture texture = new RenderTexture(width, height, (int)depthMode);
        texture.graphicsFormat = format;
        texture.enableRandomWrite = true;
        texture.autoGenerateMips = false;
        texture.useMipMap = useMipMaps;
        texture.Create();

        texture.name = name;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = filterMode;
        return texture;
    }

    void InitMaterial(Shader shader, ref Material mat)
    {
        if (mat == null || (mat.shader != shader && shader != null))
        {
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }

            mat = new Material(shader);
        }
    }

    void UpdateEnviornmentParms()
    {
        rayTraceMaterial.SetInt("EnableEnviornment", enviornmentSettings.Enabled ? 1 : 0);
        rayTraceMaterial.SetVector("SkyColorHorizon", enviornmentSettings.SkyColorHorizon);
        rayTraceMaterial.SetVector("SkyColorZenith", enviornmentSettings.SkyColorZenith);
        rayTraceMaterial.SetVector("SunLightDirection", enviornmentSettings.SunLightDirection);
        rayTraceMaterial.SetFloat("SunFocus", enviornmentSettings.SunFocus);
        rayTraceMaterial.SetFloat("SunIntensity", enviornmentSettings.SunIntensity);
        rayTraceMaterial.SetVector("GroundColor", enviornmentSettings.GroundColor);
    }

    void UpdateCameraParms(Camera cam)
    {
        float planeHeight = FocusDistance * Tan(cam.fieldOfView * 0.5f * Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;

        rayTraceMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, FocusDistance));
        rayTraceMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
    }

    void UpdateShaderParms()
    {
        rayTraceMaterial.SetInt("FrameCount", Time.frameCount);
        rayTraceMaterial.SetInt("MaxBounceCount", MaxBounceCount);
        rayTraceMaterial.SetInt("NumRaysPerPixel", NumRaysPerPixel);
        rayTraceMaterial.SetFloat("DefocusStrength", DefocusStrength);
        rayTraceMaterial.SetFloat("DivergeStrength", DivergeStrength);
        UpdateEnviornmentParms();
    }

    void CreateMeshes()
    {
        RayTracedMesh[] meshObjects = FindObjectsOfType<RayTracedMesh>();



        allTriangles ??= new List<Triangle>();
        allMeshInfo ??= new List<MeshInfo>();
        allTriangles.Clear();
        allMeshInfo.Clear();

        for (int i = 0; i < meshObjects.Length; i++)
        {
            MeshChunk[] chunks = meshObjects[i].GetSubMeshes();
            foreach (MeshChunk chunk in chunks)
            {
                RayTracingMaterial material = meshObjects[i].GetMaterial(chunk.subMeshIndex);
                allMeshInfo.Add(new MeshInfo(allTriangles.Count, chunk.triangles.Length, material, chunk.bounds));
                allTriangles.AddRange(chunk.triangles);
            }
        }

        numMeshChunks = allMeshInfo.Count;
        numTriangles = allTriangles.Count;

        if (allMeshInfo.Count != 0 && allTriangles.Count != 0)
        {
            int triLen = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Triangle));
            if (triangleBuffer != null) triangleBuffer.Release();
            triangleBuffer = new ComputeBuffer(allTriangles.Count, triLen);
            triangleBuffer.SetData(allTriangles);

            int meshInfoLen = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MeshInfo));
            meshInfoBuffer?.Release();

            meshInfoBuffer = new ComputeBuffer(allMeshInfo.Count, meshInfoLen);
            meshInfoBuffer.SetData(allMeshInfo);
        }

        rayTraceMaterial.SetBuffer("Triangles", triangleBuffer);
        rayTraceMaterial.SetBuffer("AllMeshInfo", meshInfoBuffer);
        rayTraceMaterial.SetInt("NumMeshes", allMeshInfo.Count);
    }

    void CreateSpheres()
    {
        RayTracedSphere[] sphereObjects = FindObjectsOfType<RayTracedSphere>();
        if (sphereObjects.Length == 0)
        {
            rayTraceMaterial.SetInt("NumSpheres", 0);
            return;
        }


        Sphere[] spheres = new Sphere[sphereObjects.Length];

        for (int i = 0; i < sphereObjects.Length; i++)
        {
            spheres[i] = new Sphere
            {
                position = sphereObjects[i].transform.position,
                radius = sphereObjects[i].transform.localScale.x * 0.5f,
                material = sphereObjects[i].material
            };
        }

        int sphereLen = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Sphere));
        if (sphereBuffer != null) sphereBuffer.Release();

        sphereBuffer = new ComputeBuffer(spheres.Length, sphereLen);
        sphereBuffer.SetData(spheres);
        rayTraceMaterial.SetBuffer("Spheres", sphereBuffer);
        rayTraceMaterial.SetInt("NumSpheres", spheres.Length);
    }

    void OnDisable()
    {
        resultTexture.Release();
        sphereBuffer.Release();
        triangleBuffer.Release();
        meshInfoBuffer.Release();
    }

    void OnValidate()
    {
        MaxBounceCount = Max(0, MaxBounceCount);
        NumRaysPerPixel = Max(1, NumRaysPerPixel);
        enviornmentSettings.SunFocus = Max(1, enviornmentSettings.SunFocus);
        enviornmentSettings.SunIntensity = Max(0, enviornmentSettings.SunIntensity);
    }
}