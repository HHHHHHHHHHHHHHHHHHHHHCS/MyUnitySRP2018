Shader "Hidden/My Pipeline/PostEffectStack"
{
	SubShader
	{
		Pass
		{
			Cull Off
			ZTest Always
			ZWrite Off
			
			HLSLPROGRAM
			
			#pragma target 3.5
			
			#pragma vertex CopyPassVertex
			#pragma fragment CopyPassFragment
			
			#include "../ShaderLibrary/PostEffectStack.hlsl"
			
			ENDHLSL
			
		}
	}
}
