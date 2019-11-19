#ifndef MYRP_POST_EFFECT_STACK_INCLUDED
	#define MYRP_POST_EFFECT_STACK_INCLUDED
	
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	
	float4 _ProjectionParams;
	
	TEXTURE2D(_CameraColorTexture);
	SAMPLER(sampler_CameraColorTexture);
	
	struct VertexInput
	{
		float4 pos: POSITION;
	};
	
	struct VertexOutput
	{
		float4 clipPos: SV_POSITION;
		float2 uv: TEXCOORD0;
	};
	
	VertexOutput CopyPassVertex(VertexInput input)
	{
		VertexOutput output;
		output.clipPos = float4(input.pos.xy, 0.0, 1.0);
		output.uv = input.pos.xy * 0.5 + 0.5;
		
		//当不使用 OpenGL 时，场景视图窗口和小型相机预览将被翻转
		//检查 ProjectionParams 向量的 x 组件来检测翻转是否发生
		//SetupCameraProperties 会设置 ProjectionParams
		if (_ProjectionParams.x < 0.0)
		{
			output.uv.y = 1.0 - output.uv.y;
		}
		
		return output;
	}
	
	float4 CopyPassFragment(VertexOutput input): SV_TARGET
	{
		return SAMPLE_TEXTURE2D(
			_CameraColorTexture, sampler_CameraColorTexture, input.uv
		);
	}
	
#endif // MYRP_POST_EFFECT_STACK_INCLUDED