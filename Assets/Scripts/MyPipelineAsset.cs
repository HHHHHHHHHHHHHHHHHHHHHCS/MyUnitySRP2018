using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[CreateAssetMenu(menuName = "Rendering/MyPipeline", fileName = "MyPipelineAsset")]
public class MyPipelineAsset : RenderPipelineAsset
{
    public enum ShadowMapSize
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096,
    }

    public enum ShadowCascades
    {
        Zero = 0,
        Two = 2,
        Four = 4,
    }


    //如果都是大物体不用怎么动态合批  不建议勾选  不然Unity会每帧去计算是否要合批
    [SerializeField] private bool dynamicBatching;

    //实例化  减少 相同网格和材质的东西 切换绘制的时间
    [SerializeField] private bool instancing;

    //阴影贴图分辨率
    [SerializeField] private ShadowMapSize shadowMapSize = ShadowMapSize._1024;

    //阴影的距离
    [SerializeField] private float shadowDistance = 100f;

    //阴影级联
    [SerializeField] private ShadowCascades shadowCascades = ShadowCascades.Four;

    //阴影级联 2级距离
    [SerializeField, HideInInspector] private float twoCascadesSplit = 0.25f;

    //阴影级联 4级距离
    [SerializeField, HideInInspector] private Vector3 fourCascadesSplit = new Vector3(0.067f, 0.2f, 0.467f);

    protected override IRenderPipeline InternalCreatePipeline()
    {
        Vector3 shadowCascadeSplit = shadowCascades == ShadowCascades.Four
            ? fourCascadesSplit
            : new Vector3(twoCascadesSplit, 0f);

        return new MyPipeline(dynamicBatching, instancing
            , (int) shadowMapSize, shadowDistance
            , (int) shadowCascades, shadowCascadeSplit);
    }
}