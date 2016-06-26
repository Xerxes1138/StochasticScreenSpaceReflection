float4 RayMarch(sampler2D tex, float4x4 _ProjectionMatrix, float3 R, int NumSteps, float3 viewPos, float3 screenPos, float2 uv, float stepSize)
{

	float4 rayUV = mul (_ProjectionMatrix, float4(viewPos + R, 1.0f));
	rayUV.xyz /= rayUV.w;

	float3 rayDir = normalize( rayUV - screenPos );
	rayDir.xy *= 0.5f;

	float sampleMask = 0.0f;

	float3 rayStart = float3(uv, screenPos.z);

	rayDir *= stepSize;

	float depth = LinearEyeDepth(UNITY_SAMPLE_DEPTH(tex2D(_CameraDepthTexture, uv)));

    float3 project = float3(_ScreenParams.xy, _ProjectionParams.y / (_ProjectionParams.y - _ProjectionParams.z));

	float3 samplePos = rayStart + rayDir;

	float mask = 0;
	for (int i = 0;  i < NumSteps; i++)
	{
		float sampleDepth  = LinearEyeDepth(UNITY_SAMPLE_DEPTH(tex2Dlod (_CameraDepthTexture, float4(samplePos.xy,0,0))));

		if ( sampleDepth < LinearEyeDepth(samplePos.z) )  
		{  
				
			float thickness = LinearEyeDepth(project.z) / depth;
			float delta = LinearEyeDepth(samplePos.z) - sampleDepth;

			if (0.0 < delta && delta < thickness)

			//if (abs(sampleDepth - LinearEyeDepth(samplePos.z) ) < 0.3)
			{
				mask = 1;
				break;
			}
			else
			{
				rayDir *= 0.5;
				samplePos = rayStart + rayDir; 
			} 	                 
		}
		else
		{
		        rayStart = samplePos;
		        rayDir *= 1.1;
		        samplePos += rayDir;
		}
	}
	return float4(samplePos, mask);
}