// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//The MIT License(MIT)

//Copyright(c) 2016 Charles Greivelding Thomas

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

Shader "Hidden/Stochastic SSR" 
{
	Properties 
	{
		_MainTex ("Base (RGB)", 2D) = "black" {}
	}
	
	CGINCLUDE
	
	#include "UnityCG.cginc"
	#include "UnityPBSLighting.cginc"
    #include "UnityStandardBRDF.cginc"
    #include "UnityStandardUtils.cginc"

	#include "SSRLib.cginc"
	#include "NoiseLib.cginc"
	#include "BRDFLib.cginc"
	#include "RayTraceLib.cginc"

	struct VertexInput 
	{
		float4 vertex : POSITION;
		float2 texcoord : TEXCOORD0;
	};

	struct VertexOutput
	{
		float2 uv : TEXCOORD0;
		float4 vertex : SV_POSITION;
	};

	VertexOutput vert( VertexInput v ) 
	{
		VertexOutput o;
		o.vertex = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord;
		return o;
	}

	// Based on https://github.com/playdeadgames/temporal

	/*The MIT License (MIT)

	Copyright (c) [2015] [Playdead]

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in all
	copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
	SOFTWARE.*/

	void temporal (VertexOutput i, out half4 reflection : SV_Target)
	{	
		float2 uv = i.uv;

		float depth = GetDepth(_CameraDepthTexture, uv);
		float3 screenPos = GetScreenPos(uv, depth);
		float2 velocity = GetVelocity(uv); // 5.4 motion vector
		float2 hitPacked = tex2Dlod(_RayCast, float4(uv, 0.0, 0.0));

		float2 averageDepthMin = min(hitPacked, velocity);
		float2 averageDepthMax = max(hitPacked, velocity);

		//velocity = clamp(velocity, averageDepthMin, averageDepthMax);

		float2 prevUV = uv - velocity;

		float4 current = GetSampleColor(_MainTex, uv);
		float4 previous = GetSampleColor(_PreviousBuffer, prevUV);

		float2 du = float2(1.0 / _ScreenSize.x, 0.0);
		float2 dv = float2(0.0, 1.0 / _ScreenSize.y);

		float4 currentTopLeft = GetSampleColor(_MainTex, uv.xy - dv - du);
		float4 currentTopCenter = GetSampleColor(_MainTex, uv.xy - dv);
		float4 currentTopRight = GetSampleColor(_MainTex, uv.xy - dv + du);
		float4 currentMiddleLeft = GetSampleColor(_MainTex, uv.xy - du);
		float4 currentMiddleCenter = GetSampleColor(_MainTex, uv.xy);
		float4 currentMiddleRight = GetSampleColor(_MainTex, uv.xy + du);
		float4 currentBottomLeft = GetSampleColor(_MainTex, uv.xy + dv - du);
		float4 currentBottomCenter = GetSampleColor(_MainTex, uv.xy + dv);
		float4 currentBottomRight = GetSampleColor(_MainTex, uv.xy + dv + du);

		float4 currentMin = min(currentTopLeft, min(currentTopCenter, min(currentTopRight, min(currentMiddleLeft, min(currentMiddleCenter, min(currentMiddleRight, min(currentBottomLeft, min(currentBottomCenter, currentBottomRight))))))));
		float4 currentMax = max(currentTopLeft, max(currentTopCenter, max(currentTopRight, max(currentMiddleLeft, max(currentMiddleCenter, max(currentMiddleRight, max(currentBottomLeft, max(currentBottomCenter, currentBottomRight))))))));

		float scale = _TScale;

		float4 center = (currentMin + currentMax) * 0.5f;
		currentMin = (currentMin - center) * scale + center;
		currentMax = (currentMax - center) * scale + center;

		previous = clamp(previous, currentMin, currentMax);

		/*float currentLum = Luminance(current.rgb);
		float previousLum = Luminance(previous.rgb);
		float unbiasedDiff = abs(currentLum - previousLum) / max(currentLum, max(previousLum, 0.2f));
		float unbiasedWeight = 1.0 - unbiasedDiff;
		float unbiasedWeightSqr = sqr(unbiasedWeight);

		float response = lerp(_TMinResponse, _TMaxResponse, unbiasedWeightSqr);*/

    	reflection = lerp(current, previous, saturate(_TResponse *  (1 - length(velocity) * 8)) );
	}

	float4 resolve ( VertexOutput i ) : SV_Target
	{
		float2 uv = i.uv;
		int2 pos = uv * _ScreenSize.xy;

		float4 worldNormal = GetNormal (uv);
		float3 viewNormal = GetViewNormal (worldNormal);
		float4 specular = GetSpecular (uv);
		float roughness = GetRoughness (specular.a);

		float depth = GetDepth(_CameraDepthTexture, uv);
		float3 screenPos = GetScreenPos(uv, depth);
		float3 worldPos = GetWorlPos(screenPos);
		float3 viewPos = GetViewPos(screenPos);
		float3 viewDir = GetViewDir(worldPos);

		float2 random = RandN2(pos, _SinTime.xx * _UseTemporal);

		// Blue noise generated by https://github.com/bartwronski/BlueNoiseGenerator/
		float2 blueNoise = tex2D(_Noise, (uv + _JitterSizeAndOffset.zw) * _ScreenSize.xy / _NoiseSize.xy) * 2.0 - 1.0; // works better with [-1, 1] range
		float2x2 offsetRotationMatrix = float2x2(blueNoise.x, blueNoise.y, -blueNoise.y, blueNoise.x);

		/*
		float2x2 offsetRotationMatrix;
		{
			float2 offsetRotation;
			sincos(2.0 * PI * InterleavedGradientNoise(pos, 0.0), offsetRotation.y, offsetRotation.x);
			offsetRotationMatrix = float2x2(offsetRotation.x, offsetRotation.y, -offsetRotation.y, offsetRotation.x);
		}
		*/

		int NumResolve = 1;
		if(_RayReuse == 1)
			NumResolve = 4;

		float NdotV = saturate(dot(worldNormal, -viewDir));
		float coneTangent = lerp(0.0, roughness * (1.0 - _BRDFBias), NdotV * sqrt(roughness));

		float maxMipLevel = (float)_MaxMipMap - 1.0;

		float4 result = 0.0;
        float weightSum = 0.0;	
        for(int i = 0; i < NumResolve; i++)
        {
			float2 offsetUV = offset[i] * (1.0 / _ResolveSize.xy);
			offsetUV =  mul(offsetRotationMatrix, offsetUV);

            // "uv" is the location of the current (or "local") pixel. We want to resolve the local pixel using
            // intersections spawned from neighboring pixels. The neighboring pixel is this one:
			float2 neighborUv = uv + offsetUV;

            // Now we fetch the intersection point and the PDF that the neighbor's ray hit.
            float4 hitPacked = tex2Dlod(_RayCast, float4(neighborUv, 0.0, 0.0));
            float2 hitUv = hitPacked.xy;
            float hitZ = hitPacked.z;
            float hitPDF = hitPacked.w;
			float hitMask = tex2Dlod(_RayCastMask, float4(neighborUv, 0.0, 0.0)).r;

			float3 hitViewPos = GetViewPos(GetScreenPos(hitUv, hitZ));

            // We assume that the hit point of the neighbor's ray is also visible for our ray, and we blindly pretend
            // that the current pixel shot that ray. To do that, we treat the hit point as a tiny light source. To calculate
            // a lighting contribution from it, we evaluate the BRDF. Finally, we need to account for the probability of getting
            // this specific position of the "light source", and that is approximately 1/PDF, where PDF comes from the neighbor.
            // Finally, the weight is BRDF/PDF. BRDF uses the local pixel's normal and roughness, but PDF comes from the neighbor.
			float weight = 1.0;
			if(_UseNormalization == 1)
				 weight =  BRDF_Unity_Weight(normalize(-viewPos) /*V*/, normalize(hitViewPos - viewPos) /*L*/, viewNormal /*N*/, roughness) / max(1e-5, hitPDF);

			float intersectionCircleRadius = coneTangent * length(hitUv - uv);
			float mip = clamp(log2(intersectionCircleRadius * max(_ResolveSize.x, _ResolveSize.y)), 0.0, maxMipLevel);

			float4 sampleColor = float4(0.0,0.0,0.0,1.0);
			sampleColor.rgb = tex2Dlod(_MainTex, float4(hitUv, 0.0, mip)).rgb;
			sampleColor.a = RayAttenBorder (hitUv, _EdgeFactor) * hitMask;

			if(_Fireflies == 1)
				sampleColor.rgb /= 1 + Luminance(sampleColor.rgb);

            result += sampleColor * weight;
            weightSum += weight;
        }
        result /= weightSum;

		if(_Fireflies == 1)
			result.rgb /= 1 - Luminance(result.rgb);

    	return  max(1e-5, result);
	}

	void rayCast ( VertexOutput i, 	out half4 outRayCast : SV_Target0, out half4 outRayCastMask : SV_Target1) 
	{	
		float2 uv = i.uv;
		int2 pos = uv /* _RayCastSize.xy*/;

		float4 worldNormal = GetNormal (uv);
		float3 viewNormal = GetViewNormal (worldNormal);
		float4 specular = GetSpecular (uv);
		float roughness = GetRoughness (specular.a);

		float depth = GetDepth(_CameraDepthTexture, uv);
		float3 screenPos = GetScreenPos(uv, depth);

		float3 worldPos = GetWorlPos(screenPos);
		float3 viewDir = GetViewDir(worldPos);

		float3 viewPos = /*mul(_WorldToCameraMatrix, float4(worldPos, 1.0))*/ GetViewPos(screenPos);

		float2 jitter = tex2Dlod(_Noise, float4((uv + _JitterSizeAndOffset.zw) * _RayCastSize.xy / _NoiseSize.xy, 0, -255)); // Blue noise generated by https://github.com/bartwronski/BlueNoiseGenerator/;

		float2 Xi = jitter;

		Xi.y = lerp(Xi.y, 0.0, _BRDFBias);

		float4 H = TangentToWorld(worldNormal, ImportanceSampleGGX(Xi, roughness));
		float3 dir = reflect(viewDir, H.xyz);
		dir = normalize(mul((float3x3)_WorldToCameraMatrix, dir));

		jitter += 0.5f;

		float stepSize = (1.0 / (float)_NumSteps);
		stepSize = stepSize * (jitter.x + jitter.y) + stepSize;

		float2 rayTraceHit = 0.0;
		float rayTraceZ = 0.0;
		float rayPDF = 0.0;
		float rayMask = 0.0;
		float4 rayTrace = RayMarch(_CameraDepthTexture, _ProjectionMatrix, dir, _NumSteps, viewPos, screenPos, uv, stepSize, 1.0);

		rayTraceHit = rayTrace.xy;
		rayTraceZ = rayTrace.z;
		rayPDF = H.w;
		rayMask = rayTrace.w;

		outRayCast = float4(float3(rayTraceHit, rayTraceZ), rayPDF);
		outRayCastMask = rayMask;
	}

	float4 frag( VertexOutput i ) : SV_Target
	{	 
		float2 uv = i.uv;

		float4 cubemap = GetCubeMap (uv);

		float4 sceneColor = tex2D(_MainTex,  uv);
		sceneColor.rgb = max(1e-5, sceneColor.rgb - cubemap.rgb);

		return sceneColor;
	}

	float4 recursive( VertexOutput i ) : SV_Target
	{	 
		float2 uv = i.uv;

		float depth = GetDepth(_CameraDepthTexture, uv);
		float3 screenPos = GetScreenPos(uv, depth);
		float2 velocity = GetVelocity(uv); // 5.4 motion vector

		float2 prevUV = uv - velocity;

		float4 cubemap = GetCubeMap (uv);

		float4 sceneColor = tex2D(_MainTex,  prevUV);

		return sceneColor;
	}

	float4 combine( VertexOutput i ) : SV_Target
	{	 
		float2 uv = i.uv;

		float depth = GetDepth(_CameraDepthTexture, uv);
		float3 screenPos = GetScreenPos(uv, depth);
		float3 worldPos = GetWorlPos(screenPos);

		float3 cubemap = GetCubeMap (uv);
		float4 worldNormal = GetNormal (uv);

		float4 diffuse =  GetAlbedo(uv);
		float occlusion = diffuse.a;
		float4 specular = GetSpecular (uv);
		float roughness = GetRoughness(specular.a);

		float4 sceneColor = tex2D(_MainTex,  uv);
		sceneColor.rgb = max(1e-5, sceneColor.rgb - cubemap.rgb);

		float4 reflection = GetSampleColor(_ReflectionBuffer, uv);

		float3 viewDir = GetViewDir(worldPos);
		float NdotV = saturate(dot(worldNormal, -viewDir));

		float3 reflDir = normalize( reflect( -viewDir, worldNormal ) );
		float fade = saturate(dot(-viewDir, reflDir) * 2.0);
		float mask = sqr(reflection.a) /* fade*/;

		float oneMinusReflectivity;
		diffuse.rgb = EnergyConservationBetweenDiffuseAndSpecular(diffuse, specular.rgb, oneMinusReflectivity);

        UnityLight light;
        light.color = 0;
        light.dir = 0;
        light.ndotl = 0;

        UnityIndirect ind;
        ind.diffuse = 0;
        ind.specular = reflection;

		if(_UseFresnel == 1)													
			reflection.rgb = UNITY_BRDF_PBS (0, specular.rgb, oneMinusReflectivity, 1-roughness, worldNormal, -viewDir, light, ind).rgb;

		reflection.rgb *= occlusion;

		if(_DebugPass == 0)
			sceneColor.rgb += lerp(cubemap.rgb, reflection.rgb, mask); // Combine reflection and cubemap and add it to the scene color 
		else if(_DebugPass == 1)
			sceneColor.rgb = reflection.rgb * mask;
		else if(_DebugPass == 2)
			sceneColor.rgb = cubemap;
		else if(_DebugPass == 3)
			sceneColor.rgb = lerp(cubemap.rgb, reflection.rgb, mask);
		else if(_DebugPass == 4)
			sceneColor = mask;
		else if(_DebugPass == 5)
			sceneColor.rgb += lerp(0.0, reflection.rgb, mask);
		else if(_DebugPass == 6)
			sceneColor.rgb = GetSampleColor(_RayCast, uv);
		else if(_DebugPass == 7)
		{
			int2 pos = uv;

			float2 random = RandN2(pos, _SinTime.xx * _UseTemporal);

			float2 jitter = tex2Dlod(_Noise, float4((uv + random) * _RayCastSize.xy / _NoiseSize.xy, 0, -255)); // Blue noise generated by https://github.com/bartwronski/BlueNoiseGenerator/

			sceneColor.rg = jitter;
			sceneColor.b = 0;
		}

		return sceneColor;
	}

	float4 depth(VertexOutput i ) : SV_Target
	{
		float2 uv = i.uv;
		return tex2D(_CameraDepthTexture, uv);
	}

	static const int2 offsets[7] = {{-3, -3}, {-2, -2}, {-1, -1}, {0, 0}, {1, 1}, {2, 2}, {3, 3}};

	static const float weights[7] = {0.001f, 0.028f, 0.233f, 0.474f, 0.233f, 0.028f, 0.001f};

	float4 mipMapBlur( VertexOutput i ) : SV_Target
	{	 
		float2 uv = i.uv;

		int NumSamples = 7;

		float4 result = 0.0;
		for(int i = 0; i < NumSamples; i++)
		{
			float2 offset = offsets[i] * _GaussianDir;

			float4 sampleColor = tex2Dlod(_MainTex, float4(uv + offset, 0, _MipMapCount));

			if(_Fireflies == 1)
				sampleColor.rgb /= 1 + Luminance(sampleColor.rgb);

			result += sampleColor * weights[i];
		}

		if(_Fireflies == 1)
			result /= 1 - Luminance(result.rgb);

		return result;
	}

	float4 debug( VertexOutput i ) : SV_Target
	{	 
		float2 uv = i.uv;
		return tex2Dlod(_ReflectionBuffer, float4(uv,0, _SmoothnessRange * 4.0));
	}

	ENDCG 
	
	SubShader 
	{
		ZTest Always Cull Off ZWrite Off

		//0
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment resolve
			ENDCG
		}
		//1
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}
		//2
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment combine
			ENDCG
		}
		//3
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment rayCast
			ENDCG
		}
		//4
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment depth
			ENDCG
		}
		//5
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment temporal
			ENDCG
		}
		//6
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment mipMapBlur
			ENDCG
		}
		//7
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment debug
			ENDCG
		}
		//8
		Pass 
		{
			CGPROGRAM
			#pragma target 3.0

			#ifdef SHADER_API_OPENGL
       			#pragma glsl
    		#endif

			#pragma exclude_renderers nomrt xbox360 ps3 xbox360 ps3

			#pragma vertex vert
			#pragma fragment recursive
			ENDCG
		}
	}
	Fallback Off
}
