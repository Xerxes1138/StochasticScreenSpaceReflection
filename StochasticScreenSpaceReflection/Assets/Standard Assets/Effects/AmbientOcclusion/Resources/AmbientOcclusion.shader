Shader "Hidden/Image Effects/Cinematic/AmbientOcclusion"
{
    Properties
    {
        _MainTex("", 2D) = ""{}
        _OcclusionTexture("", 2D) = ""{}
    }
    SubShader
    {
        // 0: Occlusion estimation with CameraDepthTexture
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #define SOURCE_DEPTH
            #include "AmbientOcclusion.cginc"
            #pragma vertex vert
            #pragma fragment frag_ao
            #pragma target 3.0
            ENDCG
        }
        // 1: Occlusion estimation with CameraDepthNormalsTexture
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #define SOURCE_DEPTHNORMALS
            #include "AmbientOcclusion.cginc"
            #pragma vertex vert
            #pragma fragment frag_ao
            #pragma target 3.0
            ENDCG
        }
        // 2: Occlusion estimation with G-Buffer
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #define SOURCE_GBUFFER
            #include "AmbientOcclusion.cginc"
            #pragma vertex vert
            #pragma fragment frag_ao
            #pragma target 3.0
            ENDCG
        }
        // 3: Separable blur (horizontal pass) with CameraDepthNormalsTexture
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #define SOURCE_DEPTHNORMALS
            #define BLUR_HORIZONTAL
            #define BLUR_SAMPLE_CENTER_NORMAL
            #include "SeparableBlur.cginc"
            #pragma vertex vert
            #pragma fragment frag_blur
            #pragma target 3.0
            ENDCG
        }
        // 4: Separable blur (horizontal pass) with G-Buffer
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #define SOURCE_GBUFFER
            #define BLUR_HORIZONTAL
            #define BLUR_SAMPLE_CENTER_NORMAL
            #include "SeparableBlur.cginc"
            #pragma vertex vert
            #pragma fragment frag_blur
            #pragma target 3.0
            ENDCG
        }
        // 5: Separable blur (vertical pass)
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #define BLUR_VERTICAL
            #include "SeparableBlur.cginc"
            #pragma vertex vert
            #pragma fragment frag_blur
            #pragma target 3.0
            ENDCG
        }
        // 6: Final composition
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #include "Composition.cginc"
            #pragma vertex vert
            #pragma fragment frag_composition
            #pragma target 3.0
            ENDCG
        }
        // 7: Final composition (ambient only mode)
        Pass
        {
            Blend Zero OneMinusSrcColor, Zero OneMinusSrcAlpha
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #include "Composition.cginc"
            #pragma vertex vert_composition_gbuffer
            #pragma fragment frag_composition_gbuffer
            #pragma target 3.0
            ENDCG
        }
        // 8: Debug visualization
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #define DEBUG_COMPOSITION
            #include "Composition.cginc"
            #pragma vertex vert
            #pragma fragment frag_composition
            #pragma target 3.0
            ENDCG
        }
    }
}
