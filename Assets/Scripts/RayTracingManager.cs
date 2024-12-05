using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    [Header("Ray Tracing Settings")]
    [SerializeField, Range(0, 32)] int MaxBounceCount = 1;

    [Header("View Settings")]
    [SerializeField] bool useShaderInSceneView;
    [Header("Refs")]
    [SerializeField] ComputeShader computeShader;
    public RenderTexture renderTexture;

    // Buffers
    private ComputeBuffer sphereBuffer;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Camera cam = Camera.current;
        if (cam == null) return;

        if (useShaderInSceneView)
        {
            if (renderTexture == null || renderTexture.width != src.width || renderTexture.height != src.height || renderTexture.format != src.format)
            {
                if (renderTexture != null) renderTexture.Release();
                renderTexture = new RenderTexture(src.width, src.height, 24, src.format);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();
            }

            int frameCount = Time.frameCount;
            computeShader.SetInt("FrameCount", frameCount);
            computeShader.SetInt("MaxBounceCount", MaxBounceCount);

            computeShader.SetTexture(0, "Result", renderTexture);
            UpdateCameraParms(cam);
            CreateSpheres();

            int threadGroupsX = Mathf.CeilToInt(renderTexture.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(renderTexture.height / 8.0f);

            computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
            Graphics.Blit(renderTexture, dest);
            renderTexture.Release();
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }


    struct Ray
    {
        public Vector3 start;
        public Vector3 dir;
    };

    void UpdateCameraParms(Camera cam)
    {
        float planeHeight = 2.0f * cam.nearClipPlane * Tan(cam.fieldOfView * 0.5f * Deg2Rad);
        float planeWidth = planeHeight * cam.aspect;

        computeShader.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        computeShader.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
        computeShader.SetVector("WorldSpaceCameraPos", cam.transform.position);
        computeShader.SetVector("Resolution", new Vector2(renderTexture.width, renderTexture.height));
    }

    void CreateSpheres()
    {
        RayTracedSphere[] sphereObjects = FindObjectsOfType<RayTracedSphere>();
        if (sphereObjects.Length == 0)
        {
            computeShader.SetInt("NumSpheres", 0);
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
        computeShader.SetBuffer(0, "Spheres", sphereBuffer);
        computeShader.SetInt("NumSpheres", spheres.Length);
    }

    void OnDestroy()
    {
        if (renderTexture != null) renderTexture.Release();
        if (sphereBuffer != null) sphereBuffer.Release();
    }

}