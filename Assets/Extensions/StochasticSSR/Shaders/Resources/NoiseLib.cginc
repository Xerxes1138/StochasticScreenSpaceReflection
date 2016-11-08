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

float RandN(float2 pos, float2 random)
{
	return frac(sin(dot(pos.xy + random, float2(12.9898, 78.233))) * 43758.5453);

}

float2 RandN2(float2 pos, float2 random)
{
	return frac(sin(dot(pos.xy + random, float2(12.9898, 78.233))) * float2(43758.5453, 28001.8384));
}

float RandS(float2 pos, float2 random)
{
	return RandN(pos, random) * 2.0 - 1.0;
}

// [Jimenez 2014] "Next Generation Post Processing In Call Of Duty Advanced Warfare"  
float InterleavedGradientNoise (float2 pos, float2 random)
{
	float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	return frac(magic.z * frac(dot(pos.xy + random, magic.xy)));
}


// https://www.shadertoy.com/view/4sBSDW
float Step1(float2 uv,float n)
{
	float 
	a = 1.0,
	b = 2.0,
	c = -12.0,
	t = 1.0;
		   
	return (1.0/(a*4.0+b*4.0-c))*(
			  RandS(uv+float2(-1.0,-1.0)*t,n)*a+
			  RandS(uv+float2( 0.0,-1.0)*t,n)*b+
			  RandS(uv+float2( 1.0,-1.0)*t,n)*a+
			  RandS(uv+float2(-1.0, 0.0)*t,n)*b+
			  RandS(uv+float2( 0.0, 0.0)*t,n)*c+
			  RandS(uv+float2( 1.0, 0.0)*t,n)*b+
			  RandS(uv+float2(-1.0, 1.0)*t,n)*a+
			  RandS(uv+float2( 0.0, 1.0)*t,n)*b+
			  RandS(uv+float2( 1.0, 1.0)*t,n)*a+
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

float3 Step3T(float2 uv, float time)
{
	float a=Step2(uv, 0.07*(frac(time)+1.0));    
	float b=Step2(uv, 0.11*(frac(time)+1.0));    
	float c=Step2(uv, 0.13*(frac(time)+1.0));
	return float3(a,b,c);
}
