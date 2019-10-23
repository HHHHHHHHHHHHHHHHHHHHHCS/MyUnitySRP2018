using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline
{
    private const int maxVisibleLights = 16;

    private const string shadowSoftKeyword = "_SHADOWS_SOFT";

    private static int lightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
    private static int visibleLightColorsID = Shader.PropertyToID("_VisibleLightColors");
    private static int visibleLightDirectionsOrPositionsID = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    private static int visibleLightAttenuationsID = Shader.PropertyToID("_VisibleLightAttenuations");
    private static int visibleLightSpotDirectionsID = Shader.PropertyToID("_VisibleLightSpotDirections");
    private static int shadowMapID = Shader.PropertyToID("_ShadowMap");
    private static int shadowBiasID = Shader.PropertyToID("_ShadowBias");
    private static int shadowStrengthID = Shader.PropertyToID("_ShadowStrength");
    private static int shadowMapSizeID = Shader.PropertyToID("_ShadowMapSize");
    private static int worldToShadowMatrixID = Shader.PropertyToID("_WorldToShadowMatrix");

    private readonly CommandBuffer cameraBuffer = new CommandBuffer()
    {
        name = "Render Camera"
    };

    private readonly CommandBuffer shadowBuffer = new CommandBuffer()
    {
        name = "Render Shadows"
    };

    private CullResults cull;
    private Material errorMaterial;
    private DrawRendererFlags drawFlags;

    private Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    private Vector4[] visiblelightDirectionsOrPositions = new Vector4[maxVisibleLights];
    Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
    private Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];

    private RenderTexture shadowMap;
    private int shadowMapSize;

    public MyPipeline(bool dynamicBatching, bool instancing, int _shadowMapSize)
    {
        //Unity 认为光的强度是在伽马空间中定义的，即使我们是在线性空间中工作。
        GraphicsSettings.lightsUseLinearIntensity = true;

        if (dynamicBatching)
        {
            drawFlags = DrawRendererFlags.EnableDynamicBatching;
        }

        if (instancing)
        {
            drawFlags |= DrawRendererFlags.EnableInstancing;
        }

        shadowMapSize = _shadowMapSize;
    }

    public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        base.Render(renderContext, cameras);
        foreach (var camera in cameras)
        {
            Render(renderContext, camera);
        }
    }

    public void Render(ScriptableRenderContext context, Camera camera)
    {
        if (!CullResults.GetCullingParameters(camera, out var cullingParameters))
        {
            return;
        }

#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            //将UI几何体发射到“场景”视图中以进行渲染。
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif

        //CullResults cull = CullResults.Cull(ref cullingParameters, context);
        CullResults.Cull(ref cullingParameters, context, ref cull);

        RenderShadows(context);

        context.SetupCameraProperties(camera);

        CameraClearFlags clearFlags = camera.clearFlags;
        cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0
            , (clearFlags & CameraClearFlags.Color) != 0, Color.clear);

        if (cull.visibleLights.Count > 0)
        {
            ConfigureLights();
        }
        else
        {
            cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
        }

        cameraBuffer.BeginSample("Render Camera");

        cameraBuffer.SetGlobalVectorArray(visibleLightColorsID, visibleLightColors);
        cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsID, visiblelightDirectionsOrPositions);
        cameraBuffer.SetGlobalVectorArray(visibleLightAttenuationsID, visibleLightAttenuations);
        cameraBuffer.SetGlobalVectorArray(visibleLightSpotDirectionsID, visibleLightSpotDirections);

        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        //如果Pass未指定LightMode，Unity会自动将其设置为SRPDefaultUnlit
        //由于CSharp脚本中注册了SRPDefaultUnlit这种LightMode，所以这个Pass也会被渲染出来。
        //如果手动注释掉对SRPDefaultUnlit注册的代码，那么这个Pass就不会被渲染。
        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"))
        {
            flags = drawFlags,
            sorting = {flags = SortFlags.CommonOpaque},
            rendererConfiguration =
                cull.visibleLights.Count > 0
                    ? RendererConfiguration.PerObjectLightIndices8
                    : RendererConfiguration.None
        };
        //因为 Unity 更喜欢将对象空间化地分组以减少overdraw
        var filterSettings = new FilterRenderersSettings(true)
        {
            renderQueueRange = RenderQueueRange.opaque
        };
        context.DrawRenderers(
            cull.visibleRenderers, ref drawSettings, filterSettings);


        context.DrawSkybox(camera);


        drawSettings.sorting.flags = SortFlags.CommonTransparent;
        filterSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(
            cull.visibleRenderers, ref drawSettings, filterSettings);

        DrawDefaultPipeline(context, camera);

        cameraBuffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        context.Submit();

        if (shadowMap)
        {
            RenderTexture.ReleaseTemporary(shadowMap);
            shadowMap = null;
        }
    }

    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    private void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        if (errorMaterial == null)
        {
            Shader errShader = Shader.Find("Hidden/InternalErrorShader");
            errorMaterial = new Material(errShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("ForwardBase"));

        drawSettings.SetShaderPassName(1, new ShaderPassName("PrepassBase"));
        drawSettings.SetShaderPassName(2, new ShaderPassName("Always"));
        drawSettings.SetShaderPassName(3, new ShaderPassName("Vertex"));
        drawSettings.SetShaderPassName(4, new ShaderPassName("VertexLMRGBM"));
        drawSettings.SetShaderPassName(5, new ShaderPassName("VertexLM"));
        drawSettings.SetOverrideMaterial(errorMaterial, 0);

        var filterSettings = new FilterRenderersSettings(true);

        context.DrawRenderers(
            cull.visibleRenderers, ref drawSettings, filterSettings);
    }

    private void ConfigureLights()
    {
        Vector4 attenuation = Vector4.zero;
        attenuation.w = 1f;

        for (int i = 0; i < cull.visibleLights.Count && i < maxVisibleLights; i++)
        {
            VisibleLight light = cull.visibleLights[i];
            visibleLightColors[i] = light.finalColor;

            if (light.lightType == LightType.Directional)
            {
                //光线按照局部Z轴照射  第三列是Z轴旋转
                Vector4 v = light.localToWorld.GetColumn(2);
                //在shader中 我们需要的光的方向是 从表面到光的  所以要求反
                //第四个分量总是零 只用对 x y z 求反
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
                visiblelightDirectionsOrPositions[i] = v;
            }
            else
            {
                //第三个储存的是位置 w是1
                visiblelightDirectionsOrPositions[i]
                    = light.localToWorld.GetColumn(3);

                attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.000001f);

                if (light.lightType == LightType.Spot)
                {
                    //聚光灯需要 方向 拿Z轴  即矩阵第三行
                    Vector4 v = light.localToWorld.GetColumn(2);

                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    visibleLightSpotDirections[i] = v;

                    float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                    //灯光角的一半   外面不显示
                    float outerCos = Mathf.Cos(outerRad);
                    //内圈 不衰减
                    float outerTan = Mathf.Tan(outerRad);
                    //外圈衰减
                    float innerCos =
                        Mathf.Cos(Mathf.Atan(((64f - 18f) / 64f) * outerTan));
                    float angleRange = Mathf.Max(innerCos - outerCos, 0.0001f);
                    attenuation.z = 1f / angleRange;
                    attenuation.w = -outerCos * attenuation.z;
                }
            }

            visibleLightAttenuations[i] = attenuation;
        }

        //剔除额外的光
        int[] lightIndices = cull.GetLightIndexMap();
        for (int i = maxVisibleLights; i < cull.visibleLights.Count; i++)
        {
            lightIndices[i] = -1;
        }

        cull.SetLightIndexMap(lightIndices);
    }

    private void RenderShadows(ScriptableRenderContext context)
    {
        shadowMap = RenderTexture.GetTemporary(
            shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap);
        shadowMap.filterMode = FilterMode.Bilinear;
        shadowMap.wrapMode = TextureWrapMode.Clamp;

        CoreUtils.SetRenderTarget(shadowBuffer, shadowMap
            , RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            , ClearFlag.Depth);
        shadowBuffer.BeginSample("Render Shadows");
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();

        Matrix4x4 viewMatrix, projectionMatrix;
        ShadowSplitData splitData;
        cull.ComputeSpotShadowMatricesAndCullingPrimitives(
            0, out viewMatrix, out projectionMatrix, out splitData);

        shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
        shadowBuffer.SetGlobalFloat(shadowBiasID, cull.visibleLights[0].light.shadowBias);
        shadowBuffer.SetGlobalFloat(shadowStrengthID, cull.visibleLights[0].light.shadowStrength);
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();

        var shadowSettings = new DrawShadowsSettings(cull, 0);
        context.DrawShadows(ref shadowSettings);

        //如果Z是翻转的
        if (SystemInfo.usesReversedZBuffer)
        {
            projectionMatrix.m20 = -projectionMatrix.m20;
            projectionMatrix.m21 = -projectionMatrix.m21;
            projectionMatrix.m22 = -projectionMatrix.m22;
            projectionMatrix.m23 = -projectionMatrix.m23;
        }

        //原来的位置是 [-1,+1]
        //用这个矩阵 先缩放 0.5 在偏移 +0.5
        //var scaleOffset = Matrix4x4.TRS(
        //    Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.5f);
        //上面的运算结果就是下面这个
        var scaleOffset = Matrix4x4.identity;
        scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
        scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;


        //从右到左乘法
        Matrix4x4 worldToShadowMatrix = scaleOffset * (projectionMatrix * viewMatrix);
        shadowBuffer.SetGlobalMatrix(worldToShadowMatrixID, worldToShadowMatrix);

        shadowBuffer.SetGlobalTexture(shadowMapID, shadowMap);

        float invShadowMapSize = 1f / shadowMapSize;
        shadowBuffer.SetGlobalVector(shadowMapSizeID
            , new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));

        //if (cull.visibleLights[0].light.shadows == LightShadows.Soft)
        //{
        //    shadowBuffer.EnableShaderKeyword(shadowSoftKeyword);
        //}
        //else
        //{
        //    shadowBuffer.DisableShaderKeyword(shadowSoftKeyword);
        //}
        //下面是上面的封装
        CoreUtils.SetKeyword(shadowBuffer, shadowSoftKeyword,
            cull.visibleLights[0].light.shadows == LightShadows.Soft);

        shadowBuffer.EndSample("Render Shadows");
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();
    }
}