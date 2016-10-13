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


// David Neubelt and Matt Pettineo, Ready at Dawn Studios, "Crafting a Next-Gen Material Pipeline for The Order: 1886", 2013
float D_GGX(float Roughness, float NdotH)
{
	float m = Roughness * Roughness;
	float m2 = m * m;
	
	float D = m2 / (PI * sqr(sqr(NdotH) * (m2 - 1) + 1));
	
	return D;
}

	// Bruce Walter, Stephen R. Marschner, Hongsong Li, and Kenneth E. Torrance. Microfacet models forrefraction through rough surfaces. In Proceedings of the 18th Eurographics conference on RenderingTechniques, EGSR'07
float G_GGX(float Roughness, float NdotL, float NdotV)
{
	float m = Roughness * Roughness;
	float m2 = m * m;

	float G_L = 1.0f / (NdotL + sqrt(m2 + (1 - m2) * NdotL * NdotL));
	float G_V = 1.0f / (NdotV + sqrt(m2 + (1 - m2) * NdotV * NdotV));
	float G = G_L * G_V;
	
	return G;
}

float BRDF_UE4(float3 V, float3 L, float3 N, float Roughness)
{
		float3 H = normalize(L + V);

		float NdotH = saturate(dot(N,H));
		float NdotL = saturate(dot(N,L));
		float NdotV = saturate(dot(N,V));

		float D = D_GGX(Roughness, NdotH);
		float G = G_GGX(Roughness, NdotL, NdotV);

		return D * G;
}

float BRDF_Unity_Weight(float3 V, float3 L, float3 N, float Roughness)
{
	float3 H = normalize(L + V);

	float NdotH = saturate(dot(N,H));
	float NdotL = saturate(dot(N,L));
	float NdotV = saturate(dot(N,V));

	half G = SmithJointGGXVisibilityTerm (NdotL, NdotV, Roughness);
	half D = GGXTerm (NdotH, Roughness);

	return (D * G) * (UNITY_PI / 4.0);
}

float4 TangentToWorld(float3 N, float4 H)
{
	float3 UpVector = abs(N.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
	float3 T = normalize( cross( UpVector, N ) );
	float3 B = cross( N, T );
				 
	return float4((T * H.x) + (B * H.y) + (N * H.z), H.w);
}

// Brian Karis, Epic Games "Real Shading in Unreal Engine 4"
float4 ImportanceSampleGGX(float2 Xi, float Roughness)
{
	float m = Roughness * Roughness;
	float m2 = m * m;
		
	float Phi = 2 * PI * Xi.x;
				 
	float CosTheta = sqrt((1.0 - Xi.y) / (1.0 + (m2 - 1.0) * Xi.y));
	float SinTheta = sqrt(max(1e-5, 1.0 - CosTheta * CosTheta));
				 
	float3 H;
	H.x = SinTheta * cos(Phi);
	H.y = SinTheta * sin(Phi);
	H.z = CosTheta;
		
	float d = (CosTheta * m2 - CosTheta) * CosTheta + 1;
	float D = m2 / (PI * d * d);
	float pdf = D * CosTheta;

	return float4(H, pdf); 
}
