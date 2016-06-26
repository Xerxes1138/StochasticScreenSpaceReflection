
	float2 Noise(float2 pos, float2 random)
	{
		return frac(sin(dot(pos.xy + random, float2(12.9898f, 78.233f))) * float2(43758.5453f, 28001.8384f));
	}

	// https://www.shadertoy.com/view/4sBSDW
	float Noise(float2 n,float x){n+=x;return frac(sin(dot(n.xy,float2(12.9898, 78.233)))*43758.5453)*2.0-1.0;}

	float Step1(float2 uv,float n)
	{
		float 
		a = 1.0,
		b = 2.0,
		c = -12.0,
		t = 1.0;
		   
		return (1.0/(a*4.0+b*4.0-c))*(
			  Noise(uv+float2(-1.0,-1.0)*t,n)*a+
			  Noise(uv+float2( 0.0,-1.0)*t,n)*b+
			  Noise(uv+float2( 1.0,-1.0)*t,n)*a+
			  Noise(uv+float2(-1.0, 0.0)*t,n)*b+
			  Noise(uv+float2( 0.0, 0.0)*t,n)*c+
			  Noise(uv+float2( 1.0, 0.0)*t,n)*b+
			  Noise(uv+float2(-1.0, 1.0)*t,n)*a+
			  Noise(uv+float2( 0.0, 1.0)*t,n)*b+
			  Noise(uv+float2( 1.0, 1.0)*t,n)*a+
			 0.0);
	}

	float Step2(float2 uv,float n)
	{
		float a=1.0,b=2.0,c=-2.0,t=1.0;   
		return (4.0/(a*4.0+b*4.0-c))*(
			  Step1(uv+float2(-1.0,-1.0)*t,n)*a+
			  Step1(uv+float2( 0.0,-1.0)*t,n)*b+
			  Step1(uv+float2( 1.0,-1.0)*t,n)*a+
			  Step1(uv+float2(-1.0, 0.0)*t,n)*b+
			  Step1(uv+float2( 0.0, 0.0)*t,n)*c+
			  Step1(uv+float2( 1.0, 0.0)*t,n)*b+
			  Step1(uv+float2(-1.0, 1.0)*t,n)*a+
			  Step1(uv+float2( 0.0, 1.0)*t,n)*b+
			  Step1(uv+float2( 1.0, 1.0)*t,n)*a+
			 0.0);
	}

	float3 Step3(float2 uv)
	{
		float a=Step2(uv,0.07);    
		float b=Step2(uv,0.11);    
		float c=Step2(uv,0.13);
		#if 1
			// Monochrome can look better on stills.
			return float3(a,a,a);
		#else
			return float3(a,b,c);
		#endif
	}

	// Used for temporal dither.
	float3 Step3T(float2 uv, float time)
	{
		float a=Step2(uv,0.07*(frac(time)+1.0));    
		float b=Step2(uv,0.11*(frac(time)+1.0));    
		float c=Step2(uv,0.13*(frac(time)+1.0));
		return float3(a,b,c);
	}
	//