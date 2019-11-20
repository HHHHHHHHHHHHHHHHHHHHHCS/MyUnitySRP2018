#ifndef MYRP_POST_EFFECT_STACK_INCLUDED
	#define MYRP_POST_EFFECT_STACK_INCLUDED
	
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	
	float4 _ProjectionParams;
	
	TEXTURE2D(_CameraColorTexture);
	SAMPLER(sampler_CameraColorTexture);
	
	TEXTURE2D(_MainTex);
	SAMPLER(sampler_MainTex);
	
	struct VertexInput
	{
		float4 pos: POSITION;
	};
	
	struct VertexOutput
	{
		float4 clipPos: SV_POSITION;
		float2 uv: TEXCOORD0;
	};
	
	float4 BlurSample(float2 uv, float uOffset = 0.0, float vOffset = 0.0)
	{
		//GPUs会在同一时刻并行运行很多Fragment Shader，但是并不是一个pixel一个pixel去执行的，而是将其组织在2x2的一组pixels分块中，去并行执行
		//在2x2里面 ddx 就是 右边块 - 左边块    ddy 就是 下边块 减 上面块
		uv += float2(uOffset * ddx(uv.x), vOffset * ddy(uv.y));
		return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
	}
	
	VertexOutput DefaultPassVertex(VertexInput input)
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
		return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
	}
	
	float4 BlurPassFragment(VertexOutput input): SV_TARGET
	{
		//因为图片设置了FilterMode.Bilinear 图片会自动双线性过滤
		
		float4 color = BlurSample(input.uv, 0.5, 0.5)
		+ BlurSample(input.uv, -0.5, 0.5)
		+ BlurSample(input.uv, 0.5, -0.5)
		+ BlurSample(input.uv, -0.5, -0.5);
		
		return float4(color.rgb * 0.25, 1);
	}
	
#endif // MYRP_POST_EFFECT_STACK_INCLUDED