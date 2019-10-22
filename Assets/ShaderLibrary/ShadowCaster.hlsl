#ifndef MYRP_SHADOWCASTER_INCLUDED
	#define MYRP_SHADOWCASTER_INCLUDED
	
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	
	CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
	CBUFFER_END
	
	CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
	CBUFFER_END
	
	#define UNITY_MATRIX_M unity_ObjectToWorld
	
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
	
	struct VertexInput
	{
		float4 pos: POSITION;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};
	
	struct VertexOutput
	{
		float4 clipPos: SV_POSITION;
	};
	
	VertexOutput ShadowCasterPassVertex(VertexInput input)
	{
		VertexOutput output;
		UNITY_SETUP_INSTANCE_ID(input);
		float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
		output.clipPos = mul(unity_MatrixVP, worldPos);
		
		//如果距离过近  则可能会出现 阴影漏洞   则使用这个这个方式限制
		#if UNITY_REVERSED_Z //OPENGL的Z可能会翻转
			output.clipPos.z = min(output.clipPos.z, output.clipPos.w * UNITY_NEAR_CLIP_VALUE);
		#else
			output.clipPos.z = max(output.clipPos.z, output.clipPos.w * UNITY_NEAR_CLIP_VALUE);
		#endif
		
		
		return output;
	}
	
	float4 ShadowCasterPassFragment(VertexOutput input): SV_TARGET
	{
		return 0;
	}
	
#endif // MYRP_SHADOWCASTER_INCLUDED