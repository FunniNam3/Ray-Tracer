using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    public enum VisMode
    {
        Default = 0,
        TriangleTestCount = 1,
        BoxTestCount = 2,
        Distance = 3,
        Normal = 4
    }

    [Header("Ray Tracing Settings")]
    [SerializeField] bool rayTracingEnabled = true;
    [SerializeField, Range(0, 64)] int MaxBounceCount = 1;
    [SerializeField, Range(0, 128)] int NumRaysPerPixel = 1;
    [SerializeField, Min(0)] float DefocusStrength = 0;
    [SerializeField, Min(0)] float DivergeStrength = 0;
    [SerializeField, Min(0)] float FocusDistance = 0;
    [SerializeField] bool Accumulate;

    public bool useSky;
    [SerializeField] float sunFocus = 500;
    [SerializeField] float sunIntensity = 10;
    [SerializeField] Color sunColor = Color.white;

    [Header("Debug Settings")]
    [SerializeField] VisMode visMode;
    [SerializeField] int triTestVisScale;
    [SerializeField] int boxTestVisScale;
    [SerializeField] float distanceTestVisScale;
    [SerializeField] bool useSceneView;

    [Header("Refrences")]
    [SerializeField] Shader rayTracingShader;
    [SerializeField] Shader accumulateShader;


    [Header("Info")]
    [SerializeField] int numAccumFrames;


    // Materials and renderTextures
    public Material rayTraceMaterial;
    public Material accumulateMaterial;
    public RenderTexture resultTexture;

    // Buffers
    ComputeBuffer triangleBuffer;
    ComputeBuffer nodeBuffer;
    ComputeBuffer modelBuffer;

    MeshInfo[] meshInfo;
    Model[] models;
    public bool hasBVH;

    void OnEnable()
    {
        numAccumFrames = 0;
        hasBVH = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            numAccumFrames = 1;
            Debug.Log("Reset render");
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "screencap_ray.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("Screenshot: " + path);
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        bool isSceneCam = Camera.current.name == "SceneCamera";
        // Debug.Log("Rendering... isscenecam = " + isSceneCam + "  " + Camera.current.name);
        if (isSceneCam)
        {
            if (rayTracingEnabled && useSceneView)
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
            Camera.current.cullingMask = rayTracingEnabled ? 0 : 2147483647;
            if (rayTracingEnabled && !useSceneView)
            {
                InitFrame();

                if (Accumulate && visMode == VisMode.Default)
                {
                    RenderTexture prevFrame = RenderTexture.GetTemporary(src.width, src.height, 0, GraphicsFormat.R32G32B32A32_SFloat);
                    Graphics.Blit(resultTexture, prevFrame);

                    // Run the ray tracing shader and draw the result to a temp texture
                    rayTraceMaterial.SetInt("FrameCount", numAccumFrames);
                    RenderTexture currentFrame = RenderTexture.GetTemporary(src.width, src.height, 0, GraphicsFormat.R32G32B32A32_SFloat);
                    Graphics.Blit(null, currentFrame, rayTraceMaterial);

                    // Accumulate
                    accumulateMaterial.SetInt("_Frame", numAccumFrames);
                    accumulateMaterial.SetTexture("_PrevFrame", prevFrame);
                    Graphics.Blit(currentFrame, resultTexture, accumulateMaterial);


                    // Draw result to screen
                    Graphics.Blit(resultTexture, dest);

                    // Release temps
                    RenderTexture.ReleaseTemporary(currentFrame);
                    RenderTexture.ReleaseTemporary(prevFrame);
                    numAccumFrames += Application.isPlaying ? 1 : 0;
                }
                else
                {
                    numAccumFrames = 0;
                    Graphics.Blit(null, dest, rayTraceMaterial);
                }
            }
            else
            {
                Graphics.Blit(src, dest); // Draw the unaltered camera render to the screen
            }
        }
    }

    void InitFrame()
    {
        // Create materials used in blits
        InitMaterial(rayTracingShader, ref rayTraceMaterial);
        InitMaterial(accumulateShader, ref accumulateMaterial);
        if (resultTexture == null)
        {
            resultTexture = CreateRenderTexture(Screen.width, Screen.height, FilterMode.Bilinear, GraphicsFormat.R32G32B32A32_SFloat, "Result");
            resultTexture.Create();
        }
        models = FindObjectsByType<Model>(0);
        if (!hasBVH)
        {
            var data = CreateAllMeshData(models);
            hasBVH = true;

            meshInfo = data.meshInfo.ToArray();
            int meshInfoLen = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MeshInfo));
            if (modelBuffer == null || !modelBuffer.IsValid() || modelBuffer.count != meshInfo.Length || modelBuffer.stride != meshInfoLen)
            {
                if (modelBuffer != null)
                {
                    modelBuffer.Release();
                }
                modelBuffer = new ComputeBuffer(meshInfo.Length, meshInfoLen);
            }

            // Triangles buffer
            int triangleLen = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Triangle));
            if (triangleBuffer == null || !triangleBuffer.IsValid() || triangleBuffer.count != data.triangles.Count || triangleBuffer.stride != triangleLen)
            {
                if (triangleBuffer != null)
                {
                    triangleBuffer.Release();
                }
                triangleBuffer = new ComputeBuffer(data.triangles.Count, triangleLen);
            }
            triangleBuffer.SetData(data.triangles);

            rayTraceMaterial.SetBuffer("Triangles", triangleBuffer);
            rayTraceMaterial.SetInt("triangleCount", triangleBuffer.count);

            // Node buffer
            int nodeLen = System.Runtime.InteropServices.Marshal.SizeOf(typeof(BVH.Node));
            if (nodeBuffer == null || !nodeBuffer.IsValid() || nodeBuffer.count != data.nodes.Count || nodeBuffer.stride != nodeLen)
            {
                if (nodeBuffer != null)
                {
                    nodeBuffer.Release();
                }
                nodeBuffer = new ComputeBuffer(data.nodes.Count, nodeLen);
            }
            nodeBuffer.SetData(data.nodes);

            rayTraceMaterial.SetBuffer("Nodes", nodeBuffer);

        }
        UpdateModels();
        // Update data
        UpdateCameraParms(Camera.current);
        UpdateShaderParms();
    }

    RenderTexture CreateRenderTexture(int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", int depthMode = 0, bool useMipMaps = false)
    {
        RenderTexture texture = new RenderTexture(width, height, depthMode);
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

    void UpdateShaderParms()
    {
        if (visMode != VisMode.Default)
        {
            rayTraceMaterial.EnableKeyword("DEBUG_VIS");
        }
        else
        {
            rayTraceMaterial.DisableKeyword("DEBUG_VIS");
        }
        rayTraceMaterial.SetInt("visMode", (int)visMode);
        float debugVisScale = visMode switch
        {
            VisMode.TriangleTestCount => triTestVisScale,
            VisMode.BoxTestCount => boxTestVisScale,
            VisMode.Distance => distanceTestVisScale,
            _ => triTestVisScale
        };
        rayTraceMaterial.SetFloat("debugVisScale", debugVisScale);
        rayTraceMaterial.SetInt("FrameCount", numAccumFrames);
        rayTraceMaterial.SetInt("MaxBounceCount", MaxBounceCount);
        rayTraceMaterial.SetInt("NumRaysPerPixel", NumRaysPerPixel);
        rayTraceMaterial.SetFloat("DefocusStrength", DefocusStrength);
        rayTraceMaterial.SetFloat("DivergeStrength", DivergeStrength);

        rayTraceMaterial.SetInt("UseSky", useSky ? 1 : 0);
        rayTraceMaterial.SetFloat("SunFocus", sunFocus);
        rayTraceMaterial.SetFloat("SunIntensity", sunIntensity);
        rayTraceMaterial.SetColor("SunColor", sunColor);
    }


    void UpdateCameraParms(Camera cam)
    {
        float planeHeight = FocusDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;

        rayTraceMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, FocusDistance));
        rayTraceMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
    }

    void UpdateModels()
    {
        if (meshInfo == null || meshInfo.Length == 0)
        {
            Debug.LogError("meshInfo is not initialized or empty!");
            return;
        }

        if (models == null || models.Length == 0)
        {
            Debug.LogError("No models found!");
            return;
        }

        for (int i = 0; i < models.Length; i++)
        {
            meshInfo[i].WorldToLocalMatrix = models[i].transform.worldToLocalMatrix;
            meshInfo[i].LocalToWorldMatrix = models[i].transform.localToWorldMatrix;
            meshInfo[i].Material = models[i].material;
        }
        modelBuffer.SetData(meshInfo);
        rayTraceMaterial.SetBuffer("ModelInfo", modelBuffer);
        rayTraceMaterial.SetInt("modelCount", models.Length);
    }

    MeshDataLists CreateAllMeshData(Model[] models)
    {
        MeshDataLists allData = new();
        Dictionary<Mesh, (int nodeOffset, int triOffset)> meshLookup = new();

        foreach (Model model in models)
        {
            if (!meshLookup.ContainsKey(model.Mesh))
            {
                meshLookup.Add(model.Mesh, (allData.nodes.Count, allData.triangles.Count));

                BVH bvh = new(model.Mesh.vertices, model.Mesh.triangles, model.Mesh.normals);

                allData.triangles.AddRange(bvh.GetTriangles());
                allData.nodes.AddRange(bvh.GetNodes());
            }

            allData.meshInfo.Add(new MeshInfo()
            {
                NodeOffset = meshLookup[model.Mesh].nodeOffset,
                TriangleOffset = meshLookup[model.Mesh].triOffset,
                WorldToLocalMatrix = model.transform.worldToLocalMatrix,
                LocalToWorldMatrix = model.transform.localToWorldMatrix,
                Material = model.material,
            });
        }

        return allData;
    }

    class MeshDataLists
    {
        public List<Triangle> triangles = new();
        public List<BVH.Node> nodes = new();
        public List<MeshInfo> meshInfo = new();
    }

    void OnDestroy()
    {
        if (modelBuffer != null)
        {
            modelBuffer.Release(); // Release model buffer when object is destroyed
        }
        if (triangleBuffer != null)
        {
            triangleBuffer.Release();
        }
        if (nodeBuffer != null)
        {
            nodeBuffer.Release();
        }
        if (resultTexture != null)
        {
            resultTexture.Release();
        }
        DestroyImmediate(rayTraceMaterial);
        DestroyImmediate(accumulateMaterial);
    }

    void OnValidate()
    {
    }


    struct MeshInfo
    {
        public int NodeOffset;
        public int TriangleOffset;
        public Matrix4x4 WorldToLocalMatrix;
        public Matrix4x4 LocalToWorldMatrix;
        public RayTracingMaterial Material;
    }
}