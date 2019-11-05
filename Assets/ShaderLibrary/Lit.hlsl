#ifndef MYRP_LIT_INCLUDED
	#define MYRP_LIT_INCLUDED
	
	
	
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"
	
	CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
	CBUFFER_END
	
	CBUFFER_START(UnityPerCamera)
	float3 _WorldSpaceCameraPos;
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
	float4x4 _WorldToShadowCascadeMatrices[5];
	float4 _CascadeCullingSpheres[4];
	//x:是否有灯光   y:是hard 还是soft 阴影  Z:主光源阴影/阴影Tile X偏移  W:阴影Tile Y偏移
	float4 _ShadowData[MAX_VISIBLE_LIGHTS];
	float4 _ShadowMapSize;
	float4 _CascadedShadowMapSize;
	float4 _GlobalShadowData;
	float _CascadedShadowStrength;
	CBUFFER_END
	
	CBUFFER_START(UnityPerMaterial)
	float4 _MainTex_ST;
	float _Cutoff;
	CBUFFER_END
	
	//其实跟texture2D差不多 , 但是OPENGL2.0 不支持阴影深度图比较   但是我们不用支持OPENGL2.0
	TEXTURE2D_SHADOW(_ShadowMap);
	//采样器比较方法  名字规定是sampler+贴图name
	SAMPLER_CMP(sampler_ShadowMap);
	
	TEXTURE2D_SHADOW(_CascadedShadowMap);
	SAMPLER_CMP(sampler_CascadedShadowMap);
	
	TEXTURE2D(_MainTex);
	SAMPLER(sampler_MainTex);
	
	float DistanceToCameraSqr(float3 worldPos)
	{
		float3 cameraToFragment = worldPos - _WorldSpaceCameraPos;
		return dot(cameraToFragment, cameraToFragment);
	}
	
	float HardShadowAttenuation(float4 shadowPos, bool cascade = false)
	{
		if (cascade)
		{
			return SAMPLE_TEXTURE2D_SHADOW(_CascadedShadowMap, sampler_CascadedShadowMap, shadowPos.xyz);
		}
		else
		{
			return SAMPLE_TEXTURE2D_SHADOW(_ShadowMap, sampler_ShadowMap, shadowPos.xyz);
		}
	}
	
	float SoftShadowAttenuation(float4 shadowPos, bool cascade = false)
	{
		real tentWeights[9];
		real2 tentUVs[9];
		float4 size = cascade?_CascadedShadowMapSize: _ShadowMapSize;
		SampleShadow_ComputeSamples_Tent_5x5(
			size, shadowPos.xy, tentWeights, tentUVs
		);
		float attenuation = 0;
		for (int i = 0; i < 9; i ++)
		{
			attenuation += tentWeights[i] * HardShadowAttenuation(float4(tentUVs[i].xy, shadowPos.z, 0), cascade);
		}
		return attenuation;
	}
	
	
	float ShadowAttenuation(int index, float3 worldPos)
	{
		#if !defined(_RECEIVE_SHADOWS)
			return 1.0;
		#elif !defined(_SHADOWS_HARD) && !defined(_SHADOWS_SOFT)
			return 1.0;
		#endif
		
		if(_ShadowData[index].x <= 0
		|| DistanceToCameraSqr(worldPos) > _GlobalShadowData.y)
		{
			return 1.0;
		}
		
		float4 shadowPos = mul(_WorldToShadowMatrices[index], float4(worldPos, 1.0));
		//得到NDC空间
		shadowPos.xyz /= shadowPos.w;
		shadowPos.xy = saturate(shadowPos.xy);
		shadowPos.xy = shadowPos.xy * _GlobalShadowData.x + _ShadowData[index].zw;
		//采样阴影贴图 (贴图,比较方法,当前物体在灯光矩阵的位置)
		float attenuation;
		
		#if defined(_SHADOWS_HARD)
			#if defined(_SHADOWS_SOFT)
				if (_ShadowData[index].y == 0)
				{
					attenuation = HardShadowAttenuation(shadowPos);
				}
				else
				{
					attenuation = SoftShadowAttenuation(shadowPos);
				}
			#else
				attenuation = HardShadowAttenuation(shadowPos);
			#endif
		#else
			attenuation = SoftShadowAttenuation(shadowPos);
		#endif
		
		return lerp(1, attenuation, _ShadowData[index].x);
	}
	
	float InsideCascadeCullingSphere(int index, float3 worldPos)
	{
		float4 s = _CascadeCullingSpheres[index];
		return dot(worldPos - s.xyz, worldPos - s.xyz) < s.w;
	}
	
	float CascadedShadowAttenuation(float3 worldPos)
	{
		#if !defined(_RECEIVE_SHADOWS)
			return 1.0;
		#elif !defined(_CASCADED_SHADOWS_HARD) && !defined(_CASCADED_SHADOWS_SOFT)
			return 1.0;
		#endif
		
		//太远不计算阴影
		if (DistanceToCameraSqr(worldPos) > _GlobalShadowData.y)
		{
			return 1.0;
		}
		
		float4 cascadeFlags = float4(InsideCascadeCullingSphere(0, worldPos),
		InsideCascadeCullingSphere(1, worldPos),
		InsideCascadeCullingSphere(2, worldPos),
		InsideCascadeCullingSphere(3, worldPos));
		
		//挪位相减 得出要的等级
		cascadeFlags.yzw = saturate(cascadeFlags.yzw - cascadeFlags.xyz);
		//反着计算
		float cascadeIndex = 4 - dot(cascadeFlags, float4(4, 3, 2, 1));
		float4 shadowPos = mul(_WorldToShadowCascadeMatrices[cascadeIndex], float4(worldPos, 1.0));
		float attenuation;
		#if defined(_CASCADED_SHADOWS_HARD)
			attenuation = HardShadowAttenuation(shadowPos, true);
		#else
			attenuation = SoftShadowAttenuation(shadowPos, true);
		#endif
		
		return lerp(1, attenuation, _CascadedShadowStrength);
	}
	
	float3 MainLight(float3 normal, float3 worldPos)
	{
		float shadowAttenuation = CascadedShadowAttenuation(worldPos);
		float3 lightColor = _VisibleLightColors[0].rgb;
		float3 lightDirection = _VisibleLightDirectionsOrPositions[0].xyz;
		float diffuse = saturate(dot(normal, lightDirection));
		diffuse *= shadowAttenuation;
		return diffuse * lightColor;
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
		float2 uv: TEXCOORD0;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};
	
	struct VertexOutput
	{
		float4 clipPos: SV_POSITION;
		float3 normal: TEXCOORD0;
		float3 worldPos: TEXCOORD1;
		float3 vertexLighting: TEXCOORD2;
		float2 uv: TEXCOORD3;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};
	
	VertexOutput LitPassVertex(VertexInput input)
	{
		VertexOutput output;
		UNITY_SETUP_INSTANCE_ID(input);
		UNITY_TRANSFER_INSTANCE_ID(input, output);
		float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
		output.clipPos = mul(unity_MatrixVP, worldPos);
		output.worldPos = worldPos.xyz;
		output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
		
		//第二组光因为影响不严重 所以可以在顶点进行计算
		output.vertexLighting = 0;
		for (int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); i ++)
		{
			int lightIndex = unity_4LightIndices1[i - 4];
			//顶点光 为了减少计算 直接不启用阴影
			output.vertexLighting += DiffuseLight(lightIndex, output.normal, output.worldPos, 1);
		}
		
		output.uv = TRANSFORM_TEX(input.uv, _MainTex);
		return output;
	}
	
	float4 LitPassFragment(VertexOutput input, FRONT_FACE_TYPE isFrontFace: FRONT_FACE_SEMANTIC): SV_TARGET
	{
		UNITY_SETUP_INSTANCE_ID(input);
		input.normal = normalize(input.normal);
		input.normal = IS_FRONT_VFACE(isFrontFace, input.normal, -input.normal);
		
		float4 albedoAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
		albedoAlpha *= UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
		
		#if defined(_CLIPPING_ON)
		clip(albedoAlpha.a - _Cutoff);
		#endif

		//float3 diffuseLight = saturate(dot(input.normal, float3(0, 1, 0)));
		
		float3 diffuseLight = input.vertexLighting;
		
		#if defined(_CASCADED_SHADOWS_HARD) || defined(_CASCADED_SHADOWS_SOFT)
			diffuseLight += MainLight(input.normal, input.worldPos);
		#endif
		
		for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); i ++)
		{
			int lightIndex = unity_4LightIndices0[i];
			float shadowAttenuation = ShadowAttenuation(lightIndex, input.worldPos);
			diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos, shadowAttenuation);
		}
		
		float3 color = diffuseLight * albedoAlpha.rgb;
		return float4(color, albedoAlpha.a);
	}
	
#endif // MYRP_LIT_INCLUDED
