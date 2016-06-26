
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

	float BRDF(float3 V, float3 L, float3 N, float Roughness)
	{
		float3 H = normalize(L + V);

		float NdotH = saturate(dot(N,H));
		float NdotL = saturate(dot(N,L));
		float NdotV = saturate(dot(N,V));

		float D = D_GGX(Roughness, NdotH);
		float G = G_GGX(Roughness, NdotL, NdotV);

		return D * G;
	}

	float4 TangentToWorld(float3 N, float4 H)
	{
		float3 UpVector = abs(N.z) < 0.999f ? float3(0.0f,0.0f,1.0f) : float3(1.0f,0.0f,0.0f);
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
				 
		float CosTheta = sqrt((1 - Xi.y) / (1 + (m2 - 1) * Xi.y));
		float SinTheta = sqrt(max(1e-5, 1 - CosTheta * CosTheta));  // We had a NaN here
				 
		float3 H;
		H.x = SinTheta * cos(Phi);
		H.y = SinTheta * sin(Phi);
		H.z = CosTheta;
		
		float d = (CosTheta * m2 - CosTheta) * CosTheta + 1;
		float D = m2 / (PI * d * d);
		float pdf = D * CosTheta;

		return float4(H, pdf); 
	}

	// http://roar11.com/2015/07/screen-space-glossy-reflections/
	float specularPowerToConeAngle(float specularPower)
	{
		// based on phong distribution model
		/*if(specularPower >= exp2(2048))
		{
			return 0.0f;
		}*/
		const float xi = 0.244f;
		float exponent = 1.0f / (specularPower + 1.0f);
		return acos(pow(xi, exponent));
	}
 
	float isoscelesTriangleOpposite(float adjacentLength, float coneTheta)
	{
		// simple trig and algebra - soh, cah, toa - tan(theta) = opp/adj, opp = tan(theta) * adj, then multiply * 2.0f for isosceles triangle base
		return 2.0f * tan(coneTheta) * adjacentLength;
	}
 
	float isoscelesTriangleInRadius(float a, float h)
	{
		float a2 = a * a;
		float fh2 = 4.0f * h * h;
		return (a * (sqrt(a2 + fh2) - a)) / (4.0f * h);
	}
 
	float isoscelesTriangleNextAdjacent(float adjacentLength, float incircleRadius)
	{
		// subtract the diameter of the incircle to get the adjacent side of the next level on the cone
		return adjacentLength - (incircleRadius * 2.0f);
	}
	//