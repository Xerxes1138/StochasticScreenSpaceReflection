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

float4 RayMarch(sampler2D tex, float4x4 _ProjectionMatrix, float3 viewDir, int NumSteps, float3 viewPos, float3 screenPos, float2 screenUV, float stepSize, float thickness)
{
	//float3 dirProject = _Project;
	float4 dirProject = float4
	(
		abs(unity_CameraProjection._m00 * 0.5), 
		abs(unity_CameraProjection._m11 * 0.5), 
		((_ProjectionParams.z * _ProjectionParams.y) / (_ProjectionParams.y - _ProjectionParams.z)) * 0.5,
		0.0
	);

	float linearDepth  =  LinearEyeDepth(tex2D(tex, screenUV.xy));

	/*float4 rayProj = mul (_ProjectionMatrix, float4(viewDir + viewPos, 1.0f));
	float3 rayDir = normalize( rayProj.xyz / rayProj.w - screenPos);
	rayDir.xy *= 0.5;*/


	float3 ray = viewPos / viewPos.z;
	float3 rayDir = normalize(float3(viewDir.xy - ray * viewDir.z, viewDir.z / linearDepth) * dirProject);
	rayDir.xy *= 0.5;

	float3 rayStart = float3(screenPos.xy * 0.5 + 0.5,  screenPos.z);

	float3 samplePos = rayStart;

	float project = ( _ProjectionParams.z * _ProjectionParams.y) / (_ProjectionParams.y - _ProjectionParams.z); 

	//float thickness = thickness;//1.0 / _ZBufferParams.x * (float)NumSteps * linearDepth;//project / linearDepth;

	float mask = 0.0;

	float oldDepth = samplePos.z;
	float oldDelta = 0.0;
	float3 oldSamplePos = samplePos;

	UNITY_LOOP
	for (int i = 0;  i < NumSteps; i++)
	{
		float depth = GetDepth (tex, samplePos.xy);
		float delta = samplePos.z - depth;
		//float thickness = dirProject.z / depth;

		if (0.0 < delta)
		{
				if(delta /*< thickness*/)
				{
					mask = 1.0;
					break;
					//samplePos = samplePos;
				}
				/*if(depth - oldDepth > thickness)
				{
					float blend = (oldDelta - delta) / max(oldDelta, delta) * 0.5 + 0.5;
					samplePos = lerp(oldSamplePos, samplePos, blend);
					mask = lerp(0.0, 1.0, blend);
				}*/
		}
		else
		{
			oldDelta = -delta;
			oldSamplePos = samplePos;
		}
		oldDepth = depth; 
		samplePos += rayDir * stepSize;
	}
	
	return float4(samplePos, mask);
}