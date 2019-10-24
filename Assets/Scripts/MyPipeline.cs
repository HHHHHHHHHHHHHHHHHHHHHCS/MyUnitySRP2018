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
    private static int shadowDataID = Shader.PropertyToID("_ShadowData");
    private static int shadowMapSizeID = Shader.PropertyToID("_ShadowMapSize");
    private static int worldToShadowMatricesID = Shader.PropertyToID("_WorldToShadowMatrices");

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
    private Vector4[] shadowData = new Vector4[maxVisibleLights];
    private Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];


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

        if (cull.visibleLights.Count > 0)
        {
            ConfigureLights();
            RenderShadows(context);
        }
        else
        {
            cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
        }

        ConfigureLights();

        context.SetupCameraProperties(camera);

        CameraClearFlags clearFlags = camera.clearFlags;
        cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0
            , (clearFlags & CameraClearFlags.Color) != 0, Color.clear);


        cameraBuffer.BeginSample("Render Camera");

        cameraBuffer.SetGlobalVectorArray(visibleLightColorsID, visibleLightColors);
        cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsID, visiblelightDirectionsOrPositions);
        cameraBuffer.SetGlobalVectorArray(visibleLightAttenuationsID, visibleLightAttenuations);
        cameraBuffer.SetGlobalVectorArray(visibleLightSpotDirectionsID, visibleLightSpotDirections);

        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        //我们必须通过提供相机和一个shader pass 作为draw setting的构造函数的参数。
        //这个相机用来设置排序和裁剪层级(culling layers),
        //而shader pass 控制使用那个shader pass进行渲染。
        //如果Pass未指定LightMode，Unity会自动将其设置为SRPDefaultUnlit
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
        for (int i = 0; i < cull.visibleLights.Count && i < maxVisibleLights; i++)
        {
            VisibleLight light = cull.visibleLights[i];
            visibleLightColors[i] = light.finalColor;

            Vector4 attenuation = Vector4.zero;
            attenuation.w = 1f;
            Vector4 shadow = Vector4.zero;

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

                    Light shadowLight = light.light;
                    Bounds shadowBounds;
                    if (shadowLight.shadows != LightShadows.None)
                    {
                        //这个剔除 如果 没有阴影接受者  或者阴影接受者不再视野内
                        if (cull.GetShadowCasterBounds(i, out shadowBounds))
                        {
                            shadow.x = shadowLight.shadowStrength;
                            shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
                        }
                    }
                }
            }

            visibleLightAttenuations[i] = attenuation;
            shadowData[i] = shadow;
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
        //虽然也可以用tex2DArray 但是不支持一些老机型手机
        //所以这里用图片分割成4*4块
        float tileSize = shadowMapSize / 4;
        Rect tileViewport = new Rect(0f, 0f, tileSize, tileSize);

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

        //原来的位置是 [-1,+1]
        //用这个矩阵 先缩放 0.5 在偏移 +0.5
        //var scaleOffset = Matrix4x4.TRS(
        //    Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.5f);
        //上面的运算结果就是下面这个
        //用于偏移worldToShadow 摄像机的的是中点  但是我们物体转到过去的时候是左下角所以要偏移
        var scaleOffset = Matrix4x4.identity;
        scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
        scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;

        //用于偏移worldToShadow 切图    偏移到正确的阴影切图块
        var tileMatrix = Matrix4x4.identity;
        tileMatrix.m00 = tileMatrix.m11 = scaleOffset.m22 = 0.25f;

        bool haveSoftShadow = false;

        for (int i = 0; i < cull.visibleLights.Count && i < maxVisibleLights; i++)
        {
            //剔除没有强度的 或者不需要的
            if (shadowData[i].x <= 0f)
            {
                continue;
            }

            Matrix4x4 viewMatrix, projectionMatrix;
            ShadowSplitData splitData;

            //是否能生成有效的矩阵
            if (!cull.ComputeSpotShadowMatricesAndCullingPrimitives(
                i, out viewMatrix, out projectionMatrix, out splitData))
            {
                shadowData[i].x = 0f;
                continue;
            }

            //设置渲染到贴图上的区域(起始位置和大小)
            float tileOffsetX = i % 4;
            float tileOffsetY = i / 4;
            tileViewport.x = tileOffsetX * tileSize;
            tileViewport.y = tileOffsetY * tileSize;
            shadowBuffer.SetViewport(tileViewport);
            //启动裁剪  不然采样阴影贴图边界的时候会受到另外一边的贴图的值影响
            //尤其是软阴影的时候
            shadowBuffer.EnableScissorRect(new Rect(
                tileViewport.x + 4f, tileViewport.y + 4f,
                tileSize - 8f, tileSize - 8f
            ));
            shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            shadowBuffer.SetGlobalFloat(shadowBiasID, cull.visibleLights[i].light.shadowBias);
            shadowBuffer.SetGlobalVectorArray(shadowDataID, shadowData);
            context.ExecuteCommandBuffer(shadowBuffer);
            shadowBuffer.Clear();

            var shadowSettings = new DrawShadowsSettings(cull, i);
            context.DrawShadows(ref shadowSettings);

            //如果Z是翻转的
            if (SystemInfo.usesReversedZBuffer)
            {
                projectionMatrix.m20 = -projectionMatrix.m20;
                projectionMatrix.m21 = -projectionMatrix.m21;
                projectionMatrix.m22 = -projectionMatrix.m22;
                projectionMatrix.m23 = -projectionMatrix.m23;
            }


            //从右到左乘法
            worldToShadowMatrices[i] = scaleOffset * (projectionMatrix * viewMatrix);

            scaleOffset.m03 = tileOffsetX * 0.25f;
            scaleOffset.m13 = tileOffsetY * 0.25f;

            worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];

            haveSoftShadow |= cull.visibleLights[i].light.shadows == LightShadows.Soft;
        }

        //渲染完成禁用裁剪   不然平常渲染也会收到影响
        shadowBuffer.DisableScissorRect();
        shadowBuffer.SetGlobalTexture(shadowMapID, shadowMap);

        float invShadowMapSize = 1f / shadowMapSize;
        shadowBuffer.SetGlobalVector(shadowMapSizeID
            , new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));
        shadowBuffer.SetGlobalMatrixArray(worldToShadowMatricesID, worldToShadowMatrices);

        //if (haveSoftShadow == LightShadows.Soft)
        //{
        //    shadowBuffer.EnableShaderKeyword(shadowSoftKeyword);
        //}
        //else
        //{
        //    shadowBuffer.DisableShaderKeyword(shadowSoftKeyword);
        //}
        //下面是上面的封装
        CoreUtils.SetKeyword(shadowBuffer, shadowSoftKeyword, haveSoftShadow);

        shadowBuffer.EndSample("Render Shadows");
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();
    }
}