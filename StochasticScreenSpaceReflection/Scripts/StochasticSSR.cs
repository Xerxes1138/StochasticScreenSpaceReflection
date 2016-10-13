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

    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("cCharkes/Image Effects/Rendering/Stochastic Screen Space Reflection")]
    public class StochasticSSR : MonoBehaviour
    {
        Vector4 debug;

        [Header("RayCast")]
        [SerializeField]
        ResolutionMode depthMode = ResolutionMode.halfRes;

        [SerializeField]
        ResolutionMode rayMode = ResolutionMode.halfRes;

        //[SerializeField]
        FilterMode rayFilterMode = FilterMode.Bilinear;

        [Range(1, 100)]
        [SerializeField]
        int rayDistance = 70;

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float BRDFBias = 0.7f;

        [SerializeField]
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
        float minResponse = 0.85f;

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float maxResponse = 0.95f;

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

        Camera m_camera;

        private Matrix4x4 projectionMatrix;
        private Matrix4x4 viewProjectionMatrix;
        private Matrix4x4 inverseViewProjectionMatrix;
        private Matrix4x4 worldToCameraMatrix;
        private Matrix4x4 cameraToWorldMatrix;

        private Matrix4x4 prevViewProjectionMatrix;

        RenderTexture temporalBuffer;

        RenderTexture mainBuffer0, mainBuffer1;
        RenderTexture mipMapBuffer0, mipMapBuffer1, mipMapBuffer2;

        RenderBuffer[] renderBuffer = new RenderBuffer[2];

        void Awake()
        {
            m_camera = GetComponent<Camera>();
            m_camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        }

        static Material m_rendererMaterial = null;
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
            r.generateMips = generateMipMap;
            r.Create();
            return r;
        }

        void OnDestroy()
        {
            Object.DestroyImmediate(rendererMaterial);
        }

        void OnDisable()
        {
            ReleaseRenderTargets();
        }

        void ReleaseRenderTargets()
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

        void UpdateRenderTargets(int width, int height)
        {
            if (temporalBuffer != null && temporalBuffer.width != width)
            {
                ReleaseRenderTargets();
            }

            if (temporalBuffer == null || !temporalBuffer.IsCreated())
            {
                temporalBuffer = CreateRenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, false, false, FilterMode.Bilinear);

            }

            if (mainBuffer0 == null || !mainBuffer0.IsCreated())
            {
                mainBuffer0 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, false, false, FilterMode.Bilinear);
                mainBuffer1 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, false, false, FilterMode.Bilinear);
            }

            if (mipMapBuffer0 == null || !mipMapBuffer0.IsCreated())
            {
                mipMapBuffer0 = CreateRenderTexture(1024, 1024, 0, RenderTextureFormat.DefaultHDR, true, true, FilterMode.Bilinear); // Need to be power of two
                mipMapBuffer1 = CreateRenderTexture(1024, 1024, 0, RenderTextureFormat.DefaultHDR, true, true, FilterMode.Bilinear); // Need to be power of two
                mipMapBuffer2 = CreateRenderTexture(1024, 1024, 0, RenderTextureFormat.DefaultHDR, true, false, FilterMode.Bilinear); // Need to be power of two
            }
        }

        void UpdateVariable()
        {
            rendererMaterial.SetTexture("_Noise", noise);
            rendererMaterial.SetVector("_NoiseSize", new Vector2(noise.width, noise.height));
            rendererMaterial.SetFloat("_BRDFBias", BRDFBias);
            rendererMaterial.SetFloat("_SmoothnessRange", smoothnessRange);
            rendererMaterial.SetFloat("_EdgeFactor", screenFadeSize);
            rendererMaterial.SetInt("_NumSteps", rayDistance);

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
            else
                rendererMaterial.SetInt("_UseTemporal", 1);

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

        void UpdatePrevMatrices(RenderTexture source, RenderTexture destination)
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

        RenderTexture CreateTempBuffer(int x, int y, int depth, RenderTextureFormat format)
        {
            return RenderTexture.GetTemporary(x, y, depth, format);
        }

        void ReleaseTempBuffer(RenderTexture rt)
        {
            RenderTexture.ReleaseTemporary(rt);
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            int width = m_camera.pixelWidth;
            int height = m_camera.pixelHeight;

            UpdatePrevMatrices(source, destination);
            UpdateRenderTargets(width, height);
            UpdateVariable();

            int rayWidth = width / (int)rayMode;
            int rayHeight = height / (int)rayMode;
            debug = new Vector4(width, height, m_camera.nearClipPlane / (m_camera.nearClipPlane - m_camera.farClipPlane), 0.0f);
            rendererMaterial.SetVector("_Project", debug);
            rendererMaterial.SetVector("_RayCastSize", new Vector2((float)rayWidth, (float)rayHeight));

            RenderTexture rayCast = CreateTempBuffer(rayWidth, rayHeight, 0, RenderTextureFormat.ARGBHalf);
            RenderTexture rayCastMask = CreateTempBuffer(rayWidth, rayHeight, 0, RenderTextureFormat.R8);
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
            ReleaseTempBuffer(depthBuffer);
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
                    Graphics.Blit(mainBuffer1, mainBuffer0, rendererMaterial, 8);
                    break;
            }

            // Raycast pass
            renderBuffer[0] = rayCast.colorBuffer;
            renderBuffer[1] = rayCastMask.colorBuffer;
            Graphics.SetRenderTarget(renderBuffer, rayCast.depthBuffer);
            rendererMaterial.SetPass(3);
            DrawFullScreenQuad();
            //

            int resolveWidth = width / (int)resolveMode;
            int resolveHeight = height / (int)resolveMode;

            RenderTexture resolvePass = CreateTempBuffer(resolveWidth, resolveHeight, 0, RenderTextureFormat.ARGBHalf);

            rendererMaterial.SetVector("_BufferSize", new Vector2((float)rayWidth, (float)rayHeight));
            rendererMaterial.SetInt("_MaxMipMap", maxMipMap);

            if (useMipMap)
            {
                Graphics.Blit(mainBuffer0, mipMapBuffer0); // Copy the source frame buffer to the mip map buffer

                Vector2[] dirX = new Vector2[5];
                dirX[0] = new Vector2(1.0f / 1024.0f, 0.0f);
                dirX[1] = new Vector2(1.0f / 256.0f, 0.0f);
                dirX[2] = new Vector2(1.0f / 128.0f, 0.0f);
                dirX[3] = new Vector2(1.0f / 64.0f, 0.0f);
                dirX[4] = new Vector2(1.0f / 32.0f, 0.0f);

                Vector2[] dirY = new Vector2[5];
                dirY[0] = new Vector2(0.0f, 1.0f / 1024.0f);
                dirY[1] = new Vector2(0.0f, 1.0f / 256.0f);
                dirY[2] = new Vector2(0.0f, 1.0f / 128.0f);
                dirY[3] = new Vector2(0.0f, 1.0f / 64.0f);
                dirY[4] = new Vector2(0.0f, 1.0f / 32.0f);

                int[] mipLevel = new int[5];
                mipLevel[0] = 0;
                mipLevel[1] = 2;
                mipLevel[2] = 3;
                mipLevel[3] = 4;
                mipLevel[4] = 5;

                for (int i = 0; i < maxMipMap; i++)
                {
                    rendererMaterial.SetVector("_GaussianDir", dirX[i]);
                    rendererMaterial.SetInt("_MipMapCount", mipLevel[i]);
                    Graphics.Blit(mipMapBuffer0, mipMapBuffer1, rendererMaterial, 6);

                    rendererMaterial.SetVector("_GaussianDir", dirY[i]);
                    rendererMaterial.SetInt("_MipMapCount", mipLevel[i]);
                    Graphics.Blit(mipMapBuffer1, mipMapBuffer0, rendererMaterial, 6);

                    Graphics.SetRenderTarget(mipMapBuffer2, i);
                    DrawFullScreenQuad();
                }

                Graphics.Blit(mipMapBuffer2, resolvePass, rendererMaterial, 0); // Resolve pass using mip map buffer
            }
            else
            {
                Graphics.Blit(mainBuffer0, resolvePass, rendererMaterial, 0); // Resolve pass without mip map buffer
            }

            rendererMaterial.SetTexture("_ReflectionBuffer", resolvePass);

            ReleaseTempBuffer(rayCast);
            ReleaseTempBuffer(rayCastMask);

            if (useTemporal)
            {
                rendererMaterial.SetFloat("_TScale", scale);
                rendererMaterial.SetFloat("_TMinResponse", minResponse);
                rendererMaterial.SetFloat("_TMaxResponse", maxResponse);

                RenderTexture temporalBuffer0 = CreateTempBuffer(width, height, 0, RenderTextureFormat.ARGBHalf);

                rendererMaterial.SetTexture("_PreviousBuffer", temporalBuffer);

                Graphics.Blit(resolvePass, temporalBuffer0, rendererMaterial, 5); // Temporal pass

                rendererMaterial.SetTexture("_ReflectionBuffer", temporalBuffer0);

                Graphics.Blit(temporalBuffer0, temporalBuffer);

                ReleaseTempBuffer(temporalBuffer0);
            }

            switch(debugPass)
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
                    Graphics.Blit(source, mainBuffer1, rendererMaterial, 2);
                    Graphics.Blit(mainBuffer1, destination);
                    break;
            }


            ReleaseTempBuffer(resolvePass);

            prevViewProjectionMatrix = viewProjectionMatrix;

          /*  Graphics.Blit(source, mipMapBuffer0);

            Vector2[] dirX = new Vector2[5];
            dirX[0] = new Vector2(1.0f / 1024.0f, 0.0f);
            dirX[1] = new Vector2(1.0f / 256.0f, 0.0f);
            dirX[2] = new Vector2(1.0f / 128.0f, 0.0f);
            dirX[3] = new Vector2(1.0f / 64.0f, 0.0f);
            dirX[4] = new Vector2(1.0f / 32.0f, 0.0f);

            Vector2[] dirY = new Vector2[5];
            dirY[0] = new Vector2(0.0f, 1.0f / 1024.0f);
            dirY[1] = new Vector2(0.0f, 1.0f / 256.0f);
            dirY[2] = new Vector2(0.0f, 1.0f / 128.0f);
            dirY[3] = new Vector2(0.0f, 1.0f / 64.0f);
            dirY[4] = new Vector2(0.0f, 1.0f / 32.0f);

            int[] mipLevel = new int[5];
            mipLevel[0] = 0;
            mipLevel[1] = 2;
            mipLevel[2] = 3;
            mipLevel[3] = 4;
            mipLevel[4] = 5;

            for (int i = 0; i < maxMipMap; i++)
            {
                rendererMaterial.SetVector("_GaussianDir", dirX[i]);
                rendererMaterial.SetInt("_MipMapCount", mipLevel[i]);
                Graphics.Blit(mipMapBuffer0, mipMapBuffer1, rendererMaterial, 6);

                rendererMaterial.SetVector("_GaussianDir", dirY[i]);
                rendererMaterial.SetInt("_MipMapCount", mipLevel[i]);
                Graphics.Blit(mipMapBuffer1, mipMapBuffer0, rendererMaterial, 6);

                Graphics.SetRenderTarget(mipMapBuffer2, i);
                DrawFullScreenQuad();
             }

            rendererMaterial.SetTexture("_ReflectionBuffer", mipMapBuffer2);

            Graphics.Blit(source, destination, rendererMaterial, 7);*/

        }

        public void DrawFullScreenQuad()
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
