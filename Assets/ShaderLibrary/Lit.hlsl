#ifndef MYRP_LIT_INCLUDED
	#define MYRP_LIT_INCLUDED
	
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"
	
	CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
	CBUFFER_END
	
	CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
	//x 组件包含第二种方法使用时的偏移量
	//y 物体收到几个光的影响
	float4 unity_LightIndicesOffsetAndCount;
	float4 unity_4LightIndices0, unity_4LightIndices1;
	CBUFFER_END
	
	
	#define MAX_VISIBLE_LIGHTS 16
	
	CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
	CBUFFER_END
	
	CBUFFER_START(_ShadowBuffer)
	float4x4 _WorldToShadowMatrices[MAX_VISIBLE_LIGHTS];
	float4 _ShadowData[MAX_VISIBLE_LIGHTS];
	float4 _ShadowMapSize;
	CBUFFER_END
	
	//其实跟texture2D差不多 , 但是OPENGL2.0 不支持阴影深度图比较   但是我们不用支持OPENGL2.0
	TEXTURE2D_SHADOW(_ShadowMap);
	//采样器比较方法  名字规定是sampler+贴图name
	SAMPLER_CMP(sampler_ShadowMap);
	
	#define UNITY_MATRIX_M unity_ObjectToWorld
	
	//包含文件是 UnityInstancing.hlsl，因为它可能重新定义UNITY_MATRIX_M,所以我们必须在自己定义宏之后包含它。
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
	
	UNITY_INSTANCING_BUFFER_START(PerInstance)
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
	UNITY_INSTANCING_BUFFER_END(PerInstance)
	
	struct VertexInput
	{
		float4 pos: POSITION;
		float3 normal: NORMAL;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};
	
	struct VertexOutput
	{
		float4 clipPos: SV_POSITION;
		float3 normal: TEXCOORD0;
		float3 worldPos: TEXCOORD1;
		float3 vertexLighting: TEXCOORD2;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};
	
	float ShadowAttenuation(int index, float3 worldPos)
	{
		if (_ShadowData[index].x <= 0)
		{
			return 1.0;
		}
		
		float4 shadowPos = mul(_WorldToShadowMatrices[index], float4(worldPos, 1.0));
		//得到NDC空间
		shadowPos.xyz /= shadowPos.w;
		//采样阴影贴图 (贴图,比较方法,当前物体在灯光矩阵的位置)
		float attenuation;
		
		if (_ShadowData[index].y == 0)
		{
			attenuation = SAMPLE_TEXTURE2D_SHADOW(_ShadowMap, sampler_ShadowMap, shadowPos.xyz);
		}
		else
		{
			real  tentWeights[9];
			real2 tentUVs[9];
			SampleShadow_ComputeSamples_Tent_5x5(
				_ShadowMapSize, shadowPos.xy, tentWeights, tentUVs
			);
			
			attenuation = 0;
			for (int i = 0; i < 9; i ++)
			{
				attenuation += tentWeights[i] * SAMPLE_TEXTURE2D_SHADOW(_ShadowMap, sampler_ShadowMap, float3(tentUVs[i].xy, shadowPos.z));
			}
		}
		
		return lerp(1, attenuation, _ShadowData[index].x);
	}
	
	float3 DiffuseLight(int index, float3 normal, float3 worldPos, float shadowAttenuation)
	{
		float3 lightColor = _VisibleLightColors[index].rgb;
		float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
		float4 lightAttenuation = _VisibleLightAttenuations[index];
		float3 spotDirection = _VisibleLightSpotDirections[index].xyz;
		
		//平行光w是0
		float3 lightVector = lightPositionOrDirection.xyz - worldPos * lightPositionOrDirection.w;
		float3 lightDirection = normalize(lightVector);
		float diffuse = saturate(dot(normal, lightDirection));
		
		//光照范围(range)阀值衰减
		float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
		rangeFade = saturate(1.0 - rangeFade * rangeFade);
		rangeFade *= rangeFade;
		
		float spotFade = dot(spotDirection, lightDirection);
		
		spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
		
		spotFade *= spotFade;
		
		//平行光距离是1 所以被除以还是原来的值
		float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
		//光照距离衰减
		diffuse *= shadowAttenuation * spotFade * rangeFade / distanceSqr;
		return diffuse * lightColor;
	}
	
	VertexOutput LitPassVertex(VertexInput input)
	{
		VertexOutput output;
		UNITY_SETUP_INSTANCE_ID(input);
		UNITY_TRANSFER_INSTANCE_ID(input, output);
		float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
		output.clipPos = mul(unity_MatrixVP, worldPos);
		output.worldPos = worldPos;
		output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
		
		//第二组光因为影响不严重 所以可以在顶点进行计算
		output.vertexLighting = 0;
		for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 8); i ++)
		{
			int lightIndex = unity_4LightIndices1[i - 4];
			//顶点光 为了减少计算 直接不启用阴影
			output.vertexLighting += DiffuseLight(lightIndex, output.normal, output.worldPos, 1);
		}
		
		return output;
	}
	
	float4 LitPassFragment(VertexOutput input): SV_TARGET
	{
		UNITY_SETUP_INSTANCE_ID(input);
		input.normal = normalize(input.normal);
		float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
		
		//float3 diffuseLight = saturate(dot(input.normal, float3(0, 1, 0)));
		
		float3 diffuseLight = input.vertexLighting;
		for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); i ++)
		{
			int lightIndex = unity_4LightIndices0[i];
			float shadowAttenuation = ShadowAttenuation(i, input.worldPos);
			diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos, shadowAttenuation);
		}
		
		float3 color = diffuseLight * albedo;
		return float4(color, 1);
	}
	
#endif // MYRP_LIT_INCLUDED