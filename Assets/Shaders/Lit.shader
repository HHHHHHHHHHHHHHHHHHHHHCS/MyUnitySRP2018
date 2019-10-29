Shader "MyPipeline/Lit"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 1, 1)
	}
	
	SubShader
	{
		Pass
		{
			HLSLPROGRAM
			
			//兼容 导入 gles 2.0 SRP 库  默认GLES 2.0 是不支持的
			//#pragma prefer_hlslcc gles
			
			#pragma target 3.5
			
			#pragma multi_compile_instancing
			//法向量 取消 非均匀缩放的 支持
			#pragma instancing_options assumeuniformscaling
			
			#pragma multi_compile _ _CASCADED_SHADOWS_HARD _CASCADED_SHADOWS_SOFT
			#pragma multi_compile _ _SHADOWS_HARD
			#pragma multi_compile _ _SHADOWS_SOFT
			
			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment
			
			#include "../ShaderLibrary/Lit.hlsl"
			
			ENDHLSL
			
		}
		
		Pass
		{
			Tags { "LightMode" = "ShadowCaster" }
			
			HLSLPROGRAM
			
			#pragma target 3.5
			
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling
			
			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment
			
			#include "../ShaderLibrary/ShadowCaster.hlsl"
			
			ENDHLSL
			
		}
	}
}
