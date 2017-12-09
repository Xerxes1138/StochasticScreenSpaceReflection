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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace cCharkes
{
    [System.Serializable]
    public enum ResolutionMode
    {
        halfRes = 2,
        fullRes = 1,
    };

    [System.Serializable]
    public enum SSRDebugPass
    {
        Combine,
        Reflection,
        Cubemap,
        ReflectionAndCubemap,
        SSRMask,
        CombineNoCubemap,
        RayCast,
        Jitter,
    };

    // Too much broken
    /*[ExecuteInEditMode]

    #if UNITY_5_4_OR_NEWER
        [ImageEffectAllowedInSceneView]
    #endif*/


    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("cCharkes/Image Effects/Rendering/Stochastic Screen Space Reflection")]
    public class StochasticSSR : MonoBehaviour
    {
        [Header("RayCast")]
        [SerializeField]
        ResolutionMode depthMode = ResolutionMode.halfRes;

        [SerializeField]
        ResolutionMode rayMode = ResolutionMode.halfRes;

        //[SerializeField]
        FilterMode rayFilterMode = FilterMode.Point;

        [Range(1, 100)]
        [SerializeField]
        int rayDistance = 70; // Good range is 70-80

        //[Range(0.00001f, 1.0f)]
        //[SerializeField]
        float thickness = 0.1f;

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float BRDFBias = 0.7f;

        //[SerializeField]
        Texture noise;

        [Header("Resolve")]
        [SerializeField]
        ResolutionMode resolveMode = ResolutionMode.fullRes;

        [SerializeField]
        bool rayReuse = true;

        [SerializeField]
        bool normalization = true;

        [SerializeField]
        bool reduceFireflies = true;

        [SerializeField]
        bool useMipMap = true;

        //[SerializeField]
        int maxMipMap = 5;

        [Header("Temporal")]
        [SerializeField]
        bool useTemporal = true;

		[SerializeField]
        float scale = 2.0f;

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float response = 0.85f;

        //[Range(0.0f, 1.0f)]
        //[SerializeField]
        float minResponse = 0.85f;

        //[Range(0.0f, 1.0f)]
        //[SerializeField]
        float maxResponse = 0.95f;

        [SerializeField, Tooltip("Use Unity's Motion Vectors (May cause smudging)")]
        bool useUnityMotion;

        [Header("General")]
        [SerializeField]
        bool useFresnel = true;

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float screenFadeSize = 0.25f;

        [Header("Debug")]

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float smoothnessRange = 1.0f;

        public SSRDebugPass debugPass = SSRDebugPass.Combine;

        private Camera m_camera;
        private Matrix4x4 projectionMatrix;
        private Matrix4x4 viewProjectionMatrix;
        private Matrix4x4 inverseViewProjectionMatrix;
        private Matrix4x4 worldToCameraMatrix;
        private Matrix4x4 cameraToWorldMatrix;

        private Matrix4x4 prevViewProjectionMatrix;

        private RenderTexture temporalBuffer;
        private RenderTexture mainBuffer0, mainBuffer1;
        private RenderTexture mipMapBuffer0, mipMapBuffer1, mipMapBuffer2;

        private RenderBuffer[] renderBuffer = new RenderBuffer[2];

        private Vector4 project;

        private Vector2[] dirX = new Vector2[5];

        private Vector2[] dirY = new Vector2[5];

        private int[] mipLevel = new int[5] { 0, 2, 3, 4, 5 };

        private Vector2 jitterSample;

        private void Awake()
        {
            noise = Resources.Load("tex_BlueNoise_1024x1024_UNI") as Texture2D;
            m_camera = GetComponent<Camera>();

            if (Application.isPlaying)
                m_camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            else
                m_camera.depthTextureMode = DepthTextureMode.Depth;
        }

        private static Material m_rendererMaterial = null;
        protected Material rendererMaterial
        {
            get
            {
                if (m_rendererMaterial == null)
                {
                    m_rendererMaterial = new Material(Shader.Find("Hidden/Stochastic SSR"));
                    m_rendererMaterial.hideFlags = HideFlags.DontSave;
                }
                return m_rendererMaterial;
            }
        }

        public static RenderTexture CreateRenderTexture(int w, int h, int d, RenderTextureFormat f, bool useMipMap, bool generateMipMap, FilterMode filterMode)
        {
            RenderTexture r = new RenderTexture(w, h, d, f);
            r.filterMode = filterMode;
            r.useMipMap = useMipMap;
            r.autoGenerateMips = generateMipMap;
            r.Create();
            return r;
        }

        private void OnDestroy()
        {
            Object.DestroyImmediate(rendererMaterial);
        }

        private void OnDisable()
        {
            ReleaseRenderTargets();
        }

        private void ReleaseRenderTargets()
        {

            if (temporalBuffer != null)
            {
                temporalBuffer.Release();
                temporalBuffer = null;
            }

            if (mainBuffer0 != null || mainBuffer1 != null)
            {
                mainBuffer0.Release();
                mainBuffer0 = null;
                mainBuffer1.Release();
                mainBuffer1 = null;
            }

            if (mipMapBuffer0 != null)
            {
                mipMapBuffer0.Release();
                mipMapBuffer0 = null;
            }
        }

        private void UpdateRenderTargets(int width, int height)
        {
            if (temporalBuffer != null && temporalBuffer.width != width)
            {
                ReleaseRenderTargets();
            }

            if (temporalBuffer == null || !temporalBuffer.IsCreated())
            {
                temporalBuffer = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, false, false, FilterMode.Bilinear);

            }

            if (mainBuffer0 == null || !mainBuffer0.IsCreated())
            {
                mainBuffer0 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, false, false, FilterMode.Bilinear);
                mainBuffer1 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, false, false, FilterMode.Bilinear);
            }

            if (mipMapBuffer0 == null || !mipMapBuffer0.IsCreated())
            {
                mipMapBuffer0 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, true, true, FilterMode.Bilinear); // Need to be power of two
                mipMapBuffer1 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, true, true, FilterMode.Bilinear); // Need to be power of two
                mipMapBuffer2 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, true, false, FilterMode.Bilinear); // Need to be power of two
            }
        }

        private void UpdateVariable()
        {
            rendererMaterial.SetTexture("_Noise", noise);
            rendererMaterial.SetVector("_NoiseSize", new Vector2(noise.width, noise.height));
            rendererMaterial.SetFloat("_BRDFBias", BRDFBias);
            rendererMaterial.SetFloat("_SmoothnessRange", smoothnessRange);
            rendererMaterial.SetFloat("_EdgeFactor", screenFadeSize);
            rendererMaterial.SetInt("_NumSteps", rayDistance);
            rendererMaterial.SetFloat("_Thickness", thickness);


            if (!rayReuse)
                rendererMaterial.SetInt("_RayReuse", 0);
            else
                rendererMaterial.SetInt("_RayReuse", 1);

            if (!normalization)
                rendererMaterial.SetInt("_UseNormalization", 0);
            else
                rendererMaterial.SetInt("_UseNormalization", 1);

            if (!useFresnel)
                rendererMaterial.SetInt("_UseFresnel", 0);
            else
                rendererMaterial.SetInt("_UseFresnel", 1);

            if (!useTemporal)
                rendererMaterial.SetInt("_UseTemporal", 0);
            else if (useTemporal && Application.isPlaying)
                rendererMaterial.SetInt("_UseTemporal", 1);

            if (!useUnityMotion)
                rendererMaterial.SetInt("_ReflectionVelocity", 1);
            else if (useTemporal)
                rendererMaterial.SetInt("_ReflectionVelocity", 0);

            if (!reduceFireflies)
                rendererMaterial.SetInt("_Fireflies", 0);
            else
                rendererMaterial.SetInt("_Fireflies", 1);

            switch (debugPass)
            {
                case SSRDebugPass.Combine:
                    rendererMaterial.SetInt("_DebugPass", 0);
                    break;
                case SSRDebugPass.Reflection:
                    rendererMaterial.SetInt("_DebugPass", 1);
                    break;
                case SSRDebugPass.Cubemap:
                    rendererMaterial.SetInt("_DebugPass", 2);
                    break;
                case SSRDebugPass.ReflectionAndCubemap:
                    rendererMaterial.SetInt("_DebugPass", 3);
                    break;
                case SSRDebugPass.SSRMask:
                    rendererMaterial.SetInt("_DebugPass", 4);
                    break;
                case SSRDebugPass.CombineNoCubemap:
                    rendererMaterial.SetInt("_DebugPass", 5);
                    break;
                case SSRDebugPass.RayCast:
                    rendererMaterial.SetInt("_DebugPass", 6);
                    break;
                case SSRDebugPass.Jitter:
                    rendererMaterial.SetInt("_DebugPass", 7);
                    break;
            }
        }

        private void UpdatePrevMatrices(RenderTexture source, RenderTexture destination)
        {
            worldToCameraMatrix = m_camera.worldToCameraMatrix;
            cameraToWorldMatrix = worldToCameraMatrix.inverse;

            projectionMatrix = GL.GetGPUProjectionMatrix(m_camera.projectionMatrix, false);

            viewProjectionMatrix = projectionMatrix * worldToCameraMatrix;
            inverseViewProjectionMatrix = viewProjectionMatrix.inverse;

            rendererMaterial.SetMatrix("_ProjectionMatrix", projectionMatrix);
            rendererMaterial.SetMatrix("_ViewProjectionMatrix", viewProjectionMatrix);
            rendererMaterial.SetMatrix("_InverseProjectionMatrix", projectionMatrix.inverse);
            rendererMaterial.SetMatrix("_InverseViewProjectionMatrix", inverseViewProjectionMatrix);
            rendererMaterial.SetMatrix("_WorldToCameraMatrix", worldToCameraMatrix);
            rendererMaterial.SetMatrix("_CameraToWorldMatrix", cameraToWorldMatrix);

            rendererMaterial.SetMatrix("_PrevViewProjectionMatrix", prevViewProjectionMatrix);
            rendererMaterial.SetMatrix("_PrevInverseViewProjectionMatrix", prevViewProjectionMatrix * Matrix4x4.Inverse(viewProjectionMatrix));
        }

        private RenderTexture CreateTempBuffer(int x, int y, int depth, RenderTextureFormat format)
        {
            return RenderTexture.GetTemporary(x, y, depth, format);
        }

        private void ReleaseTempBuffer(RenderTexture rt)
        {
            RenderTexture.ReleaseTemporary(rt);
        }

        // From Unity TAA
        private int m_SampleIndex = 0;
        private const int k_SampleCount = 64;

        private float GetHaltonValue(int index, int radix)
        {
            float result = 0f;
            float fraction = 1f / (float)radix;

            while (index > 0)
            {
                result += (float)(index % radix) * fraction;

                index /= radix;
                fraction /= (float)radix;
            }

            return result;
        }

        private Vector2 GenerateRandomOffset()
        {
            var offset = new Vector2(
                    GetHaltonValue(m_SampleIndex & 1023, 2),
                    GetHaltonValue(m_SampleIndex & 1023, 3));

            if (++m_SampleIndex >= k_SampleCount)
                m_SampleIndex = 0;

            return offset;
        }
        //

        private void OnPreCull()
        {
            jitterSample = GenerateRandomOffset();
        }

        /*
        Any Image Effect with this attribute will be rendered after opaque geometry but before transparent geometry.
        This allows for effects which extensively use the depth buffer (SSAO, etc) to affect only opaque pixels. This attribute can be used to reduce the amount of visual artifacts in a scene with post processing.
        */
        [ImageEffectOpaque]
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            int width = m_camera.pixelWidth;
            int height = m_camera.pixelHeight;

            int rayWidth = width / (int)rayMode;
            int rayHeight = height / (int)rayMode;

            int resolveWidth = width / (int)resolveMode;
            int resolveHeight = height / (int)resolveMode;

            rendererMaterial.SetVector("_JitterSizeAndOffset",
            new Vector4
                (
                    (float)rayWidth / (float)noise.width,
                    (float)rayHeight / (float)noise.height,
                    jitterSample.x,
                    jitterSample.y
                )
            );

            rendererMaterial.SetVector("_ScreenSize", new Vector2((float)width, (float)height));
            rendererMaterial.SetVector("_RayCastSize", new Vector2((float)rayWidth, (float)rayHeight));
            rendererMaterial.SetVector("_ResolveSize", new Vector2((float)resolveWidth, (float)resolveHeight));

            UpdatePrevMatrices(source, destination);
            UpdateRenderTargets(width, height);
            UpdateVariable();

            project = new Vector4(Mathf.Abs(m_camera.projectionMatrix.m00 * 0.5f), Mathf.Abs(m_camera.projectionMatrix.m11 * 0.5f), ((m_camera.farClipPlane * m_camera.nearClipPlane) / (m_camera.nearClipPlane - m_camera.farClipPlane)) * 0.5f, 0.0f);

            rendererMaterial.SetVector("_Project", project);

            RenderTexture rayCast = CreateTempBuffer(rayWidth, rayHeight, 0, RenderTextureFormat.ARGBHalf);
            RenderTexture rayCastMask = CreateTempBuffer(rayWidth, rayHeight, 0, RenderTextureFormat.RHalf);
            RenderTexture depthBuffer = CreateTempBuffer(width / (int)depthMode, height / (int)depthMode, 0, RenderTextureFormat.RFloat);
            rayCast.filterMode = rayFilterMode;
            depthBuffer.filterMode = FilterMode.Point;

            rendererMaterial.SetTexture("_RayCast", rayCast);
            rendererMaterial.SetTexture("_RayCastMask", rayCastMask);
            rendererMaterial.SetTexture("_CameraDepthBuffer", depthBuffer);

            // Depth Buffer
            Graphics.SetRenderTarget(depthBuffer);
            rendererMaterial.SetPass(4);
            DrawFullScreenQuad();
            //

            switch (debugPass)
            {
                case SSRDebugPass.Reflection:
                case SSRDebugPass.Cubemap:
                case SSRDebugPass.CombineNoCubemap:
                case SSRDebugPass.RayCast:
                case SSRDebugPass.ReflectionAndCubemap:
                case SSRDebugPass.SSRMask:
                case SSRDebugPass.Jitter:
                    Graphics.Blit(source, mainBuffer0, rendererMaterial, 1);
                    break;
                case SSRDebugPass.Combine:
                    if (Application.isPlaying)
                        Graphics.Blit(mainBuffer1, mainBuffer0, rendererMaterial, 8);
                    else
                        Graphics.Blit(source, mainBuffer0, rendererMaterial, 1);
                    break;
            }

            // Raycast pass
            renderBuffer[0] = rayCast.colorBuffer;
            renderBuffer[1] = rayCastMask.colorBuffer;
            Graphics.SetRenderTarget(renderBuffer, rayCast.depthBuffer);
            //Graphics.Blit(null, rendererMaterial, 3);
            rendererMaterial.SetPass(3);
            DrawFullScreenQuad();
            //

            ReleaseTempBuffer(depthBuffer);

            RenderTexture resolvePass = CreateTempBuffer(resolveWidth, resolveHeight, 0, RenderTextureFormat.DefaultHDR);

            if (useMipMap)
            {
                dirX[0] = new Vector2(width, 0.0f);
                dirX[1] = new Vector2(dirX[0].x / 4.0f, 0.0f);
                dirX[2] = new Vector2(dirX[1].x / 2.0f, 0.0f);
                dirX[3] = new Vector2(dirX[2].x / 2.0f, 0.0f);
                dirX[4] = new Vector2(dirX[3].x / 2.0f, 0.0f);


                dirY[0] = new Vector2(0.0f, height);
                dirY[1] = new Vector2(0.0f, dirY[0].y / 4.0f);
                dirY[2] = new Vector2(0.0f, dirY[1].y / 2.0f);
                dirY[3] = new Vector2(0.0f, dirY[2].y / 2.0f);
                dirY[4] = new Vector2(0.0f, dirY[3].y / 2.0f);

                rendererMaterial.SetInt("_MaxMipMap", maxMipMap);

                Graphics.Blit(mainBuffer0, mipMapBuffer0); // Copy the source frame buffer to the mip map buffer

                for (int i = 0; i < maxMipMap; i++)
                {
                    rendererMaterial.SetVector("_GaussianDir", new Vector2(1.0f / dirX[i].x, 0.0f));
                    rendererMaterial.SetInt("_MipMapCount", mipLevel[i]);
                    Graphics.Blit(mipMapBuffer0, mipMapBuffer1, rendererMaterial, 6);

                    rendererMaterial.SetVector("_GaussianDir", new Vector2(0.0f, 1.0f / dirY[i].y));
                    rendererMaterial.SetInt("_MipMapCount", mipLevel[i]);
                    Graphics.Blit(mipMapBuffer1, mipMapBuffer0, rendererMaterial, 6);

                    Graphics.SetRenderTarget(mipMapBuffer2, i);
                    DrawFullScreenQuad();
                }

                Graphics.Blit(mipMapBuffer2, resolvePass, rendererMaterial, 0); // Resolve pass using mip map buffer

                //ReleaseTempBuffer(mainBuffer0);
            }
            else
            {
                Graphics.Blit(mainBuffer0, resolvePass, rendererMaterial, 0); // Resolve pass without mip map buffer

                //ReleaseTempBuffer(mainBuffer0);
            }

            rendererMaterial.SetTexture("_ReflectionBuffer", resolvePass);

            ReleaseTempBuffer(rayCast);
            ReleaseTempBuffer(rayCastMask);

            if (useTemporal && Application.isPlaying)
            {
                rendererMaterial.SetFloat("_TScale", scale);
                rendererMaterial.SetFloat("_TResponse", response);
                rendererMaterial.SetFloat("_TMinResponse", minResponse);
                rendererMaterial.SetFloat("_TMaxResponse", maxResponse);

                RenderTexture temporalBuffer0 = CreateTempBuffer(width, height, 0, RenderTextureFormat.DefaultHDR);

                rendererMaterial.SetTexture("_PreviousBuffer", temporalBuffer);

                Graphics.Blit(resolvePass, temporalBuffer0, rendererMaterial, 5); // Temporal pass

                rendererMaterial.SetTexture("_ReflectionBuffer", temporalBuffer0);

                Graphics.Blit(temporalBuffer0, temporalBuffer);

                ReleaseTempBuffer(temporalBuffer0);
            }

            switch (debugPass)
            {
                case SSRDebugPass.Reflection:
                case SSRDebugPass.Cubemap:
                case SSRDebugPass.CombineNoCubemap:
                case SSRDebugPass.RayCast:
                case SSRDebugPass.ReflectionAndCubemap:
                case SSRDebugPass.SSRMask:
                case SSRDebugPass.Jitter:
                    Graphics.Blit(source, destination, rendererMaterial, 2);
                    break;
                case SSRDebugPass.Combine:
                    if (Application.isPlaying)
                    {
                        Graphics.Blit(source, mainBuffer1, rendererMaterial, 2);
                        Graphics.Blit(mainBuffer1, destination);
                    }
                    else
                        Graphics.Blit(source, destination, rendererMaterial, 2);
                    break;
            }

            ReleaseTempBuffer(resolvePass);

            prevViewProjectionMatrix = viewProjectionMatrix;
        }

        private void DrawFullScreenQuad()
        {
            GL.PushMatrix();
            GL.LoadOrtho();

            GL.Begin(GL.QUADS);
            GL.MultiTexCoord2(0, 0.0f, 0.0f);
            GL.Vertex3(0.0f, 0.0f, 0.0f); // BL

            GL.MultiTexCoord2(0, 1.0f, 0.0f);
            GL.Vertex3(1.0f, 0.0f, 0.0f); // BR

            GL.MultiTexCoord2(0, 1.0f, 1.0f);
            GL.Vertex3(1.0f, 1.0f, 0.0f); // TR

            GL.MultiTexCoord2(0, 0.0f, 1.0f);
            GL.Vertex3(0.0f, 1.0f, 0.0f); // TL

            GL.End();
            GL.PopMatrix();
        }
    }
}
