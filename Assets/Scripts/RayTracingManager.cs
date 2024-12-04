using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    [SerializeField] bool useShaderInSceneView;
    [SerializeField] Shader rayTracingShader;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (Camera.current.name != "SceneCamera" || useShaderInSceneView)
        {

        }
    }
}
