using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static UnityEngine.Mathf;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    [Header("Ray Tracing Settings")]
    [SerializeField, Range(0, 32)] int MaxBounceCount = 1;
    [SerializeField, Range(0, 64)] int NumRaysPerPixel = 1;
    [SerializeField] EnviornmentSettings enviornmentSettings;

    [Header("View Settings")]
    [SerializeField] bool useShaderInSceneView;
    [Header("Refrences")]
    [SerializeField] Shader rayTracingShader;
    [SerializeField] Shader accumulateShader;
    // [SerializeField] ComputeShader currShader;

    [Header("Info")]
    [SerializeField] int numRenderedFrames;

    // Materials and renderTextures
    Material rayTraceMaterial;
    Material accumulateMaterial;
    RenderTexture resultTexture;

    // Buffers
    ComputeBuffer sphereBuffer;

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
            resultTexture.name = "UnNamed";
            resultTexture.wrapMode = TextureWrapMode.Clamp;
            resultTexture.filterMode = FilterMode.Bilinear;
        }

        UpdateShaderParms();
        UpdateCameraParms(Camera.current);
        CreateSpheres();
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
        float planeHeight = 2.0f * cam.nearClipPlane * Tan(cam.fieldOfView * 0.5f * Deg2Rad);
        float planeWidth = planeHeight * cam.aspect;

        rayTraceMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        rayTraceMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
        rayTraceMaterial.SetVector("WorldSpaceCameraPos", cam.transform.position);
        rayTraceMaterial.SetVector("Resolution", new Vector2(resultTexture.width, resultTexture.height));
    }

    void UpdateShaderParms()
    {
        rayTraceMaterial.SetInt("FrameCount", Time.frameCount);
        rayTraceMaterial.SetInt("MaxBounceCount", MaxBounceCount);
        rayTraceMaterial.SetInt("NumRaysPerPixel", NumRaysPerPixel);
        UpdateEnviornmentParms();
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
    }

    void OnValidate()
    {
        MaxBounceCount = Max(0, MaxBounceCount);
        NumRaysPerPixel = Max(1, NumRaysPerPixel);
        enviornmentSettings.SunFocus = Max(1, enviornmentSettings.SunFocus);
        enviornmentSettings.SunIntensity = Max(0, enviornmentSettings.SunIntensity);
    }
}