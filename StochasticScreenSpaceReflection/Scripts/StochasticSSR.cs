//Copyright (c) 2015, Charles Greivelding Thomas
//All rights reserved.
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//* Redistributions of source code must retain the above copyright notice, this
//  list of conditions and the following disclaimer.
//
//* Redistributions in binary form must reproduce the above copyright notice,
//  this list of conditions and the following disclaimer in the documentation
//  and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
//AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
//IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
//FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
//DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
//SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
//CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
//OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
//OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

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
    public enum NoiseType
    {
        White,
        Blue,
    };

    [System.Serializable]
    public enum DebugPass
    {
        Combine,
        Reflection,
        Cubemap,
        ReflectionAndCubemap,
        SSRMask,
        CombineNoCubemap,
        RayCast,
    };

    //[ImageEffectAllowedInSceneView]
    //[ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("cCharkes/Image Effects/Rendering/Stochastic Screen Space Reflection")]
    public class StochasticSSR : MonoBehaviour
    {
        //Mesh m_quad;

        [Header("RayCast")]
        [SerializeField]
        ResolutionMode depthMode = ResolutionMode.halfRes;

        [SerializeField]
        ResolutionMode rayMode = ResolutionMode.halfRes;

        [SerializeField]
        FilterMode rayFilterMode = FilterMode.Bilinear;

        [SerializeField]
        NoiseType noiseType = NoiseType.Blue;

        [Range(1, 100)]
        [SerializeField]
        int rayDistance = 70;

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float BRDFBias = 0.7f;

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

        [SerializeField]
        bool jitterMipMap = true;

        [SerializeField]
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

        [SerializeField]
        bool recursiveReflection = true;

        [Header("Debug")]

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float smoothnessRange = 1.0f;

        public DebugPass debugPass = DebugPass.Combine;

        Camera m_camera;

        private Matrix4x4 projectionMatrix;
        private Matrix4x4 viewProjectionMatrix;
        private Matrix4x4 inverseViewProjectionMatrix;
        private Matrix4x4 worldToCameraMatrix;
        private Matrix4x4 cameraToWorldMatrix;

        private Matrix4x4 prevViewProjectionMatrix;

        RenderTexture temporalBuffer1;

        RenderTexture mainBuffer, mainBuffer1;
        RenderTexture mipMapBuffer0, mipMapBuffer1, mipMapBuffer2;

        RenderBuffer[] renderBuffer = new RenderBuffer[2];

        void OnEnable()
        {
            m_camera = GetComponent<Camera>();
            m_camera.depthTextureMode = DepthTextureMode.Depth;
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

            if (temporalBuffer1 != null)
            {
                temporalBuffer1.Release();
                temporalBuffer1 = null;
            }

            if (mainBuffer != null || mainBuffer1 != null)
            {
                mainBuffer.Release();
                mainBuffer = null;
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
            if (temporalBuffer1 != null && temporalBuffer1.width != width)
            {
                ReleaseRenderTargets();
            }

            if (temporalBuffer1 == null || !temporalBuffer1.IsCreated())
            {
                temporalBuffer1 = CreateRenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, false, false, FilterMode.Bilinear);

            }

            if (mainBuffer == null || !mainBuffer.IsCreated())
            {
                mainBuffer = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, false, false, FilterMode.Bilinear);
                mainBuffer1 = CreateRenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR, false, false, FilterMode.Bilinear);
            }

            if (mipMapBuffer0 == null || !mipMapBuffer0.IsCreated())
            {
                mipMapBuffer0 = CreateRenderTexture(1024, 1024, 0, RenderTextureFormat.DefaultHDR, true, true, FilterMode.Bilinear); // Need to be power of two
                mipMapBuffer1 = CreateRenderTexture(1024, 1024, 0, RenderTextureFormat.DefaultHDR, true, false, FilterMode.Bilinear); // Need to be power of two
                mipMapBuffer2 = CreateRenderTexture(1024, 1024, 0, RenderTextureFormat.DefaultHDR, true, false, FilterMode.Bilinear); // Need to be power of two
            }
        }

        void UpdateVariable()
        {
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

            if (!jitterMipMap)
                rendererMaterial.SetInt("_JitterMipMap", 0);
            else
                rendererMaterial.SetInt("_JitterMipMap", 1);

            switch (noiseType)
            {
                case NoiseType.White:
                    rendererMaterial.SetInt("_NoiseType", 0);
                    break;
                case NoiseType.Blue:
                    rendererMaterial.SetInt("_NoiseType", 1);
                    break;
            }

            switch (debugPass)
            {
                case DebugPass.Combine:
                    rendererMaterial.SetInt("_DebugPass", 0);
                    break;
                case DebugPass.Reflection:
                    rendererMaterial.SetInt("_DebugPass", 1);
                    break;
                case DebugPass.Cubemap:
                    rendererMaterial.SetInt("_DebugPass", 2);
                    break;
                case DebugPass.ReflectionAndCubemap:
                    rendererMaterial.SetInt("_DebugPass", 3);
                    break;
                case DebugPass.SSRMask:
                    rendererMaterial.SetInt("_DebugPass", 4);
                    break;
                case DebugPass.CombineNoCubemap:
                    rendererMaterial.SetInt("_DebugPass", 5);
                    break;
                case DebugPass.RayCast:
                    rendererMaterial.SetInt("_DebugPass", 6);
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

            RenderTexture rayCast = CreateTempBuffer(rayWidth, rayHeight, 0, RenderTextureFormat.ARGBHalf);
            RenderTexture rayCastMask = CreateTempBuffer(rayWidth, rayHeight, 0, RenderTextureFormat.R8);
            RenderTexture depthBuffer = CreateTempBuffer(width / (int)depthMode, height / (int)depthMode, 0, RenderTextureFormat.RFloat);
            rayCast.filterMode = rayFilterMode;
            //rayCastMask.filterMode = FilterMode.Point;
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

            if (!recursiveReflection)
                Graphics.Blit(source, mainBuffer, rendererMaterial, 1); // Main buffer without cubemap reflection
            else
                Graphics.Blit(mainBuffer1, mainBuffer, rendererMaterial, 8); // Main buffer without cubemap reflection

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
            //resolvePass.filterMode = FilterMode.Point;

            rendererMaterial.SetVector("_BufferSize", new Vector2((float)rayWidth, (float)rayHeight));
            rendererMaterial.SetInt("_MaxMipMap", maxMipMap);

            if (useMipMap)
            {
                Graphics.Blit(mainBuffer, mipMapBuffer0); // Copy the source frame buffer to the mip map buffer

                Vector2[] mipMapBufferSize = new Vector2[11];
                mipMapBufferSize[0] = new Vector2(1024, 1024);
                mipMapBufferSize[1] = new Vector2(512, 512);
                mipMapBufferSize[2] = new Vector2(256, 256);
                mipMapBufferSize[3] = new Vector2(128, 128);
                mipMapBufferSize[4] = new Vector2(64, 64);
                mipMapBufferSize[5] = new Vector2(32, 32);
                mipMapBufferSize[6] = new Vector2(16, 16);
                mipMapBufferSize[7] = new Vector2(8, 8);
                mipMapBufferSize[8] = new Vector2(4, 4);
                mipMapBufferSize[9] = new Vector2(2, 2);
                mipMapBufferSize[10] = new Vector2(1, 1);

                Graphics.Blit(mipMapBuffer0, mipMapBuffer1);

                for (int i = 0; i < maxMipMap; i++)
                {
                    rendererMaterial.SetVector("_MipMapBufferSize", mipMapBufferSize[i]);
                    rendererMaterial.SetInt("_MipMapCount", i);

                    rendererMaterial.SetVector("_GaussianDir", new Vector2(1.0f, 0.0f));

                    rendererMaterial.SetTexture("_MainTex", mipMapBuffer1);
                    Graphics.SetRenderTarget(mipMapBuffer1, i);
                    rendererMaterial.SetPass(6);
                    DrawFullScreenQuad();
                }

                Graphics.Blit(mipMapBuffer1, mipMapBuffer2);

                for (int i = 0; i < maxMipMap; i++)
                {
                    rendererMaterial.SetVector("_MipMapBufferSize", mipMapBufferSize[i]);
                    rendererMaterial.SetInt("_MipMapCount", i);

                    rendererMaterial.SetVector("_GaussianDir", new Vector2(0.0f, 1.0f));

                    rendererMaterial.SetTexture("_MainTex", mipMapBuffer2);
                    Graphics.SetRenderTarget(mipMapBuffer2, i);
                    rendererMaterial.SetPass(6);
                    DrawFullScreenQuad();
                }

                Graphics.Blit(mipMapBuffer2, resolvePass, rendererMaterial, 0); // Resolve pass using mip map buffer
            }
            else
            {
                Graphics.Blit(mainBuffer, resolvePass, rendererMaterial, 0); // Resolve pass without mip map buffer
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
                //temporalBuffer0.filterMode = FilterMode.Point;

                rendererMaterial.SetTexture("_PreviousBuffer", temporalBuffer1);

                Graphics.Blit(resolvePass, temporalBuffer0, rendererMaterial, 5); // Temporal pass

                rendererMaterial.SetTexture("_ReflectionBuffer", temporalBuffer0);

                Graphics.Blit(temporalBuffer0, temporalBuffer1);

                ReleaseTempBuffer(temporalBuffer0);
            }

            if (recursiveReflection)
            {
                Graphics.Blit(source, mainBuffer1, rendererMaterial, 2);
                Graphics.Blit(mainBuffer1, destination);
            }
            else
                Graphics.Blit(source, destination, rendererMaterial, 2);


            ReleaseTempBuffer(resolvePass);

            prevViewProjectionMatrix = viewProjectionMatrix;

           /* RenderTexture resolvePass = CreateTempBuffer(width, height, 0, RenderTextureFormat.ARGBHalf);

            Graphics.Blit(source, mipMapBuffer0);

            Vector2[] mipMapBufferSize = new Vector2[11];
            mipMapBufferSize[0] = new Vector2(1024, 1024);
            mipMapBufferSize[1] = new Vector2(512, 512);
            mipMapBufferSize[2] = new Vector2(256, 256);
            mipMapBufferSize[3] = new Vector2(128, 128);
            mipMapBufferSize[4] = new Vector2(64, 64);
            mipMapBufferSize[5] = new Vector2(32, 32);
            mipMapBufferSize[6] = new Vector2(16, 16);
            mipMapBufferSize[7] = new Vector2(8, 8);
            mipMapBufferSize[8] = new Vector2(4, 4);
            mipMapBufferSize[9] = new Vector2(2, 2);
            mipMapBufferSize[10] = new Vector2(1, 1);

            Graphics.Blit(mipMapBuffer0, mipMapBuffer1);

            for (int i = 0; i < maxMipMap; i++)
            {
                rendererMaterial.SetVector("_MipMapBufferSize", mipMapBufferSize[i]);
                rendererMaterial.SetInt("_MipMapCount", i);

                rendererMaterial.SetVector("_GaussianDir", new Vector2(1.0f, 0.0f));

                rendererMaterial.SetTexture("_MainTex", mipMapBuffer1);
                Graphics.SetRenderTarget(mipMapBuffer1, i);
                rendererMaterial.SetPass(6);
                DrawFullScreenQuad();
            }
            
            Graphics.Blit(mipMapBuffer1, mipMapBuffer2);

            for (int i = 0; i < maxMipMap; i++)
            {
                rendererMaterial.SetVector("_MipMapBufferSize", mipMapBufferSize[i]);
                rendererMaterial.SetInt("_MipMapCount", i);

                rendererMaterial.SetVector("_GaussianDir", new Vector2(0.0f, 1.0f));

                rendererMaterial.SetTexture("_MainTex", mipMapBuffer2);
                Graphics.SetRenderTarget(mipMapBuffer2, i);
                rendererMaterial.SetPass(6);
                DrawFullScreenQuad();
            }

            rendererMaterial.SetTexture("_ReflectionBuffer", mipMapBuffer2);

            Graphics.Blit(source, destination, rendererMaterial, 7);

            ReleaseTempBuffer(resolvePass);*/
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
