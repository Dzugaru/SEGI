﻿Shader "Hidden/SEGIVoxelizeSceneNoShadows_C" {
	Properties
	{
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_EmissionColor("Color", Color) = (0,0,0)
		_EmissionMap("Emission", 2D) = "white" {}
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
		_BlockerValue ("Blocker Value", Range(0, 10)) = 0
	}
	SubShader 
	{
		Cull Off
		ZTest Always
		
		Pass
		{
			CGPROGRAM
			
				#pragma target 5.0
				#pragma vertex vert
				#pragma fragment frag
				#pragma geometry geom
				#pragma multi_compile_instancing
				#include "UnityCG.cginc"
				#include "SEGIUnityShadowInput.cginc"
				#include "SEGI_C.cginc"

				UNITY_INSTANCING_BUFFER_START(Props)
				UNITY_DEFINE_INSTANCED_PROP(half4, _Color)
				UNITY_DEFINE_INSTANCED_PROP(half4, _EmissionColor)
				UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
				UNITY_DEFINE_INSTANCED_PROP(float, _BlockerValue)
				UNITY_DEFINE_INSTANCED_PROP(sampler2D, _EmissionMap)
				UNITY_INSTANCING_BUFFER_END(Props)

				RWTexture2DArray<uint> RG0;
				//RWStructuredBuffer<colorStruct> RG0Buffer;

				int LayerToVisualize;
				
				float4x4 SEGIVoxelViewFront;
				float4x4 SEGIVoxelViewLeft;
				float4x4 SEGIVoxelViewTop;
				
				sampler2D _MainTex_ST;
				//sampler2D _EmissionMap;
				//float _Cutoff;
				//half4 _EmissionColor;

				float SEGISecondaryBounceGain;
				
				//float _BlockerValue;
				
				
				struct v2g
				{
					float4 pos : SV_POSITION;
					half4 uv : TEXCOORD0;
					float3 normal : TEXCOORD1;
					float angle : TEXCOORD2;

					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};
				
				struct g2f
				{
					float4 pos : SV_POSITION;
					half4 uv : TEXCOORD0;
					float3 normal : TEXCOORD1;
					float angle : TEXCOORD2;

					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};
				
				//half4 _Color;
				
				v2g vert(appdata_full v)
				{
					v2g o;

					UNITY_SETUP_INSTANCE_ID(o);
					UNITY_INITIALIZE_OUTPUT(v2g, o);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o)
					
					float4 vertex = v.vertex;
					
					o.normal = UnityObjectToWorldNormal(v.normal);
					//float3 absNormal = abs(o.normal);
					
					o.pos = vertex;
					
					o.uv = float4(v.texcoord.xy, 1.0, 1.0);
					//o.uv = float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), 1.0, 1.0);
					
					
					return o;
				}
				
				int SEGIVoxelResolution;
				int SEGIVoxelAA;
				#define VoxelResolution (SEGIVoxelResolution * (1 + SEGIVoxelAA))

				float4x4 SEGIVoxelVPFront;
				float4x4 SEGIVoxelVPLeft;
				float4x4 SEGIVoxelVPTop;
				
				[maxvertexcount(3)]
				void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
				{
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

					v2g p[3];
					int i = 0;
					for (i = 0; i < 3; i++)
					{
						p[i] = input[i];
						p[i].pos = UnityObjectToClipPos(p[i].pos);	
					}
					
					

					float3 realNormal = float3(0.0, 0.0, 0.0);
					
					float3 V = p[1].pos.xyz - p[0].pos.xyz;
					float3 W = p[2].pos.xyz - p[0].pos.xyz;
					
					realNormal.x = (V.y * W.z) - (V.z * W.y);
					realNormal.y = (V.z * W.x) - (V.x * W.z);
					realNormal.z = (V.x * W.y) - (V.y * W.x);
					
					float3 absNormal = abs(realNormal);
					

					
					int angle = 0;
					if (absNormal.z > absNormal.y && absNormal.z > absNormal.x)
					{
						angle = 0;
					}
					else if (absNormal.x > absNormal.y && absNormal.x > absNormal.z)
					{
						angle = 1;
					}
					else if (absNormal.y > absNormal.x && absNormal.y > absNormal.z)
					{
						angle = 2;
					}
					else
					{
						angle = 0;
					}
					
					for (i = 0; i < 3; i ++)
					{
						float3 op = p[i].pos.xyz * float3(1.0, 1.0, 1.0);
						op.z = op.z * 2.0 - 1.0;

						if (angle == 0)
						{
							p[i].pos.xyz = op.xyz;	
						}
						else if (angle == 1)
						{
							p[i].pos.xyz = op.zyx * float3(1.0, 1.0, -1.0);
						}
						else
						{
							p[i].pos.xyz = op.xzy * float3(1.0, 1.0, -1.0);
						}

						p[i].pos.z = p[i].pos.z * 0.5 + 0.5;
						
						#if defined(UNITY_REVERSED_Z)
						p[i].pos.z = 1.0 - p[i].pos.z;
						#else
						p[i].pos.z *= -1.0;
						#endif
						
						p[i].angle = (float)angle;
					}
					
					triStream.Append(p[0]);
					triStream.Append(p[1]);
					triStream.Append(p[2]);
				}

				//float4 SEGISunlightVector;
				//float4 GISunColor;
				float4 SEGIVoxelSpaceOriginDelta;
				
				sampler3D SEGICurrentIrradianceVolume;
				int SEGIInnerOcclusionLayers;

				float SEGIShadowBias;
				uint SEGICurrentClipmapIndex;

				float4 frag (g2f input) : SV_TARGET
				{
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
					int3 coord = int3((int)(input.pos.x), (int)(input.pos.y), (int)(input.pos.z * VoxelResolution));
					
					//float3 absNormal = abs(input.normal);
					
					int angle = 0;
					
					angle = (int)round(input.angle);
					
					if (angle == 1)
					{
						coord.xyz = coord.zyx;
						coord.z = VoxelResolution - coord.z - 1;
					}
					else if (angle == 2)
					{
						coord.xyz = coord.xzy;
						coord.y = VoxelResolution - coord.y - 1;
					}
					
					float3 fcoord = (float3)(coord.xyz) / VoxelResolution;

					//float3 minCoord = (SEGIClipmapOverlap.xyz * 1.0 + 0.5) - SEGIClipmapOverlap.w * 0.5;
					//minCoord += 16.0 / VoxelResolution;
					//float3 maxCoord = (SEGIClipmapOverlap.xyz * 1.0 + 0.5) + SEGIClipmapOverlap.w * 0.5;
					//maxCoord -= 16.0 / VoxelResolution;

					//if (
					//	fcoord.x > minCoord.x && fcoord.x < maxCoord.x &&
					//	fcoord.y > minCoord.y && fcoord.y < maxCoord.y &&
					//	fcoord.z > minCoord.z && fcoord.z < maxCoord.z)
					//{
					//	discard;
					//}

					float sunNdotL = saturate(dot(input.normal, -SEGISunlightVector.xyz));
					
					float4 tex = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, input.uv);
					float4 emissionTex = UNITY_SAMPLE_SCREENSPACE_TEXTURE(UNITY_ACCESS_INSTANCED_PROP(Props, _EmissionMap), input.uv);
					
					float4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);

					if (length(UNITY_ACCESS_INSTANCED_PROP(Props, _Color).rgb) < 0.0001)
					{
						color.rgb = float3(1, 1, 1);
					}
					else
					{
						color.rgb *= color.a;
					}
					
					//float4 prevBounce = tex3D(SEGICurrentIrradianceVolume, fcoord + SEGIVoxelSpaceOriginDelta.xyz);
					//float3 sunShadow = saturate(sunNdotL.xxx + prevBounce.rgb * 0.2 * SEGISecondaryBounceGain * tex.rgb * color.rgb);
					//TODO sunShadow missing skylight?

					//float3 col = sunVisibility.xxx * sunNdotL * color.rgb * tex.rgb * GISunColor.rgb * GISunColor.a + _EmissionColor.rgb * 0.9 * emissionTex.rgb;
					
					float3 col = color.rgb * tex.rgb;
					float3 colShaded = sunNdotL * GISunColor.rgb * GISunColor.a;
					float3 colEmission = UNITY_ACCESS_INSTANCED_PROP(Props, _EmissionColor).rgb * 0.9 * emissionTex.rgb;

					//col *= colShaded;

					 
					float4 result = float4(col.rgb, 2.0);
					
					const float sqrt2 = sqrt(2.0) * 1.2;

					coord /= (uint)SEGIVoxelAA + 1u;


					if (UNITY_ACCESS_INSTANCED_PROP(Props, _BlockerValue) > 0.01)
					{
						result.a += 20.0;
						result.a += UNITY_ACCESS_INSTANCED_PROP(Props, _BlockerValue);
						result.rgb = float3(0.0, 0.0, 0.0);
					}

					const float4 gridSize = SEGI_GRID_SIZE;

					uint2 coord2D = uint2(coord.x + gridSize.x*(coord.z%gridSize.w), coord.y + gridSize.y*(coord.z / gridSize.w));

					uint3 coordOcclusion = coord - int3(input.normal * sqrt2);
					uint2 coord2DOcclusion1 = uint2(coordOcclusion.x + gridSize.x*(coordOcclusion.z%gridSize.w), coordOcclusion.y + gridSize.y*(coordOcclusion.z / gridSize.w));

					coordOcclusion = coord - int3(input.normal * sqrt2 * 2.0);
					uint2 coord2DOcclusion2 = uint2(coordOcclusion.x + gridSize.x*(coordOcclusion.z%gridSize.w), coordOcclusion.y + gridSize.y*(coordOcclusion.z / gridSize.w));

					interlockedAddFloat4(RG0, coord2D, result, colShaded, colEmission, input.normal, SEGICurrentClipmapIndex);

					if (SEGIInnerOcclusionLayers > 0)
					{
						interlockedAddFloat4c(RG0, coord2DOcclusion1, float4(0.0, 0.0, 0.0, 14.0 * tex.a));
					}

					if (SEGIInnerOcclusionLayers > 1)
					{
						interlockedAddFloat4c(RG0, coord2DOcclusion2, float4(0.0, 0.0, 0.0, 22.0 * tex.a));
					}
					
					return float4(0.0, 0.0, 0.0, 0.0);
				}
			
			ENDCG
		}
	} 
	FallBack Off
}
