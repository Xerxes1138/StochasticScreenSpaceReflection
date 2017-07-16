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
	
#include "UnityStandardBRDF.cginc"

#define PI 3.141592

uniform sampler2D	_MainTex,
					_ReflectionBuffer,
					_PreviousBuffer,
					_RayCast,
					_RayCastMask;

uniform	sampler2D	_Noise;

uniform sampler2D	_CameraGBufferTexture0,
					_CameraGBufferTexture1,
					_CameraGBufferTexture2,
					_CameraReflectionsTexture;
	
uniform sampler2D	_CameraDepthTexture; // Unity depth
uniform sampler2D	_CameraDepthBuffer;
uniform sampler2D_half _CameraMotionVectorsTexture;

uniform float4		_MainTex_TexelSize;
uniform float4		_ScreenSize;
uniform	float4		_RayCastSize;
uniform	float4		_ResolveSize;
uniform	float4		_NoiseSize;
uniform float4		_Project;
uniform float4		_GaussianDir;
uniform float4		_JitterSizeAndOffset; // x = jitter width / screen width, y = jitter height / screen height, z = random offset, w = random offset

uniform float		_EdgeFactor; 
uniform float		_SmoothnessRange;
uniform float		_BRDFBias;

uniform float		_Thickness;
uniform float		_TScale;
uniform float		_TMinResponse;
uniform float		_TMaxResponse;
uniform float		_TResponse;
uniform float		_StepSize;
uniform float		_Accumulation;
uniform int			_NumSteps;
uniform int			_RayReuse;
uniform int			_MipMapCount;

uniform float4x4	_ProjectionMatrix;
uniform float4x4	_ViewProjectionMatrix;
uniform float4x4	_InverseProjectionMatrix;
uniform float4x4	_InverseViewProjectionMatrix;
uniform float4x4	_WorldToCameraMatrix;
uniform float4x4	_CameraToWorldMatrix;

uniform float4x4	_PrevInverseViewProjectionMatrix;

//Debug Options
uniform int			_UseTemporal;
uniform int			_UseFresnel;
uniform int			_DebugPass;
uniform int			_UseNormalization;
uniform int			_Fireflies;
uniform int			_MaxMipMap;

float sqr(float x)
{
	return x*x;
}
	
float fract(float x)
{
	return x - floor( x );
}

float4 GetSampleColor (sampler2D tex, float2 uv) { return tex2D(tex, uv); }
float4 GetCubeMap (float2 uv) { return tex2D(_CameraReflectionsTexture, uv); }
float4 GetAlbedo (float2 uv) { return tex2D(_CameraGBufferTexture0, uv); }
float4 GetSpecular (float2 uv) { return tex2D(_CameraGBufferTexture1, uv); }
float GetRoughness (float smoothness) { return max(min(_SmoothnessRange, 1 - smoothness), 0.05f); }
float4 GetNormal (float2 uv) 
{ 
	float4 gbuffer2 = tex2D(_CameraGBufferTexture2, uv);

	return gbuffer2*2-1;
}

float4 GetVelocity(float2 uv)    { return tex2D(_CameraMotionVectorsTexture, uv); }
float4 GetReflection(float2 uv)    { return tex2D(_ReflectionBuffer, uv); }

float ComputeDepth(float4 clippos)
{
#if defined(SHADER_TARGET_GLSL) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
	return (clippos.z / clippos.w) * 0.5 + 0.5;
#else
	return clippos.z / clippos.w;
#endif
}

float3 GetViewNormal (float3 normal)
{
	float3 viewNormal =  mul((float3x3)_WorldToCameraMatrix, normal.rgb);
	return normalize(viewNormal);
}

/*float GetDepth (sampler2D tex, float2 uv)
{
	return UNITY_SAMPLE_DEPTH (tex2Dlod(tex, float4(uv, 0, 0)));
}*/


float GetDepth (sampler2D tex, float2 uv)
{
	float z = tex2Dlod(_CameraDepthTexture, float4(uv,0,0));
	#if defined(UNITY_REVERSED_Z)
		z = 1.0f - z;
	#endif
	return z;
}

float3 GetScreenPos (float2 uv, float depth)
{
	return float3(uv.xy * 2 - 1, depth);
}

float3 GetWorlPos (float3 screenPos)
{
	float4 worldPos = mul(_InverseViewProjectionMatrix, float4(screenPos, 1));
	return worldPos.xyz / worldPos.w;
}

float3 GetViewPos (float3 screenPos)
{
	float4 viewPos = mul(_InverseProjectionMatrix, float4(screenPos, 1));
	return viewPos.xyz / viewPos.w;
}
	
float3 GetViewDir (float3 worldPos)
{
	return normalize(worldPos - _WorldSpaceCameraPos);
}

// Deprecated since 5.4
/*float2 ReprojectUV(float3 clipPosition)
{
	float4 previousClipPosition = mul(_PrevInverseViewProjectionMatrix, float4(clipPosition, 1.0f));
	previousClipPosition.xyz /= previousClipPosition.w;

	return float2(previousClipPosition.xy * 0.5f + 0.5f);
}*/

static const float2 offset[4] =
{
	float2(0, 0),
	float2(2, -2),
	float2(-2, -2),
	float2(0, 2)
};

float RayAttenBorder (float2 pos, float value)
{
	float borderDist = min(1.0 - max(pos.x, pos.y), min(pos.x, pos.y));
	return saturate(borderDist > value ? 1.0 : borderDist / value);
}