using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[CreateAssetMenu(menuName = "Rendering/MyPipeline", fileName = "MyPipelineAsset")]
public class MyPipelineAsset : RenderPipelineAsset
{
    //如果都是大物体不用怎么动态合批  不建议勾选  不然Unity会每帧去计算是否要合批
    [SerializeField] private bool dynamicBatching;

    //实例化  减少 相同网格和材质的东西 切换绘制的时间
    [SerializeField] private bool instancing;

    protected override IRenderPipeline InternalCreatePipeline()
    {
        return new MyPipeline(dynamicBatching, instancing);
    }
}