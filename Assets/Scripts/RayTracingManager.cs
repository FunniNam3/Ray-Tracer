using System;
using UnityEngine;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    [SerializeField] bool useShaderInSceneView;
    [SerializeField] ComputeShader computeShader;
    public RenderTexture renderTexture;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Camera cam = Camera.current;
        if (cam == null) return;

        if (cam.name != "SceneCamera" || useShaderInSceneView)
        {
            if (renderTexture == null || renderTexture.width != src.width || renderTexture.height != src.height || renderTexture.format != src.format)
            {
                if (renderTexture != null) renderTexture.Release();
                renderTexture = new RenderTexture(src.width, src.height, 24, src.format);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();
            }

            computeShader.SetTexture(0, "Result", renderTexture);
            UpdateCameraParms(cam);

            // Ensure dispatch count covers the entire image size
            int threadGroupsX = Mathf.CeilToInt(renderTexture.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(renderTexture.height / 8.0f);

            computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
            Graphics.Blit(renderTexture, dest);
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
        float planeHeight = cam.nearClipPlane * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2.0f;
        float planeWidth = planeHeight * cam.aspect;

        computeShader.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        computeShader.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
        computeShader.SetVector("WorldSpaceCameraPos", cam.transform.position);
        computeShader.SetFloat("Resolution", renderTexture.width);
    }
}
