using UnityEngine;
using UnityEngine.Rendering;

namespace UnityStandardAssets.CinematicEffects
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Image Effects/Cinematic/Ambient Occlusion")]
#if UNITY_5_4_OR_NEWER
    [ImageEffectAllowedInSceneView]
#endif
    public partial class AmbientOcclusion : MonoBehaviour
    {
        #region Public Properties

        /// Effect settings.
        [SerializeField]
        public Settings settings = Settings.defaultSettings;

        /// Checks if the ambient-only mode is supported under the current settings.
        public bool isAmbientOnlySupported
        {
            get { return targetCamera.hdr && occlusionSource == OcclusionSource.GBuffer; }
        }

        /// Checks if the G-buffer is available
        public bool isGBufferAvailable
        {
            get { return targetCamera.actualRenderingPath == RenderingPath.DeferredShading; }
        }

        #endregion

        #region Private Properties

        // Properties referring to the current settings

        float intensity
        {
            get { return settings.intensity; }
        }

        float radius
        {
            get { return Mathf.Max(settings.radius, 1e-4f); }
        }

        SampleCount sampleCount
        {
            get { return settings.sampleCount; }
        }

        int sampleCountValue
        {
            get
            {
                switch (settings.sampleCount)
                {
                    case SampleCount.Lowest: return 3;
                    case SampleCount.Low:    return 6;
                    case SampleCount.Medium: return 12;
                    case SampleCount.High:   return 20;
                }
                return Mathf.Clamp(settings.sampleCountValue, 1, 256);
            }
        }

        OcclusionSource occlusionSource
        {
            get
            {
                if (settings.occlusionSource == OcclusionSource.GBuffer && !isGBufferAvailable)
                    // An unavailable source was chosen: fallback to DepthNormalsTexture.
                    return OcclusionSource.DepthNormalsTexture;
                else
                    return settings.occlusionSource;
            }
        }

        bool downsampling
        {
            get { return settings.downsampling; }
        }

        bool ambientOnly
        {
            get { return settings.ambientOnly && !settings.debug && isAmbientOnlySupported; }
        }

        // AO shader
        Shader aoShader
        {
            get
            {
                if (_aoShader == null)
                    _aoShader = Shader.Find("Hidden/Image Effects/Cinematic/AmbientOcclusion");
                return _aoShader;
            }
        }

        [SerializeField] Shader _aoShader;

        // Temporary aterial for the AO shader
        Material aoMaterial
        {
            get
            {
                if (_aoMaterial == null)
                    _aoMaterial = ImageEffectHelper.CheckShaderAndCreateMaterial(aoShader);
                return _aoMaterial;
            }
        }

        Material _aoMaterial;

        // Command buffer for the AO pass
        CommandBuffer aoCommands
        {
            get
            {
                if (_aoCommands == null)
                {
                    _aoCommands = new CommandBuffer();
                    _aoCommands.name = "AmbientOcclusion";
                }
                return _aoCommands;
            }
        }

        CommandBuffer _aoCommands;

        // Target camera
        Camera targetCamera
        {
            get { return GetComponent<Camera>(); }
        }

        // Property observer
        PropertyObserver propertyObserver {
            get { return _propertyObserver; }
        }

        PropertyObserver _propertyObserver = new PropertyObserver();

        // Reference to the quad mesh in the built-in assets
        // (used in MRT blitting)
        Mesh quadMesh
        {
            get { return _quadMesh; }
        }

        [SerializeField] Mesh _quadMesh;

        #endregion

        #region Effect Passes

        // Build commands for the AO pass (used in the ambient-only mode).
        void BuildAOCommands()
        {
            var cb = aoCommands;

            var tw = targetCamera.pixelWidth;
            var th = targetCamera.pixelHeight;
            var ts = downsampling ? 2 : 1;
            var format = RenderTextureFormat.ARGB32;
            var rwMode = RenderTextureReadWrite.Linear;
            var filter = FilterMode.Bilinear;

            // AO buffer
            var m = aoMaterial;
            var rtMask = Shader.PropertyToID("_OcclusionTexture1");
            cb.GetTemporaryRT(rtMask, tw / ts, th / ts, 0, filter, format, rwMode);

            // AO estimation
            cb.Blit((Texture)null, rtMask, m, 2);

            // Blur buffer
            var rtBlur = Shader.PropertyToID("_OcclusionTexture2");

            // Separable blur (horizontal pass)
            cb.GetTemporaryRT(rtBlur, tw, th, 0, filter, format, rwMode);
            cb.Blit(rtMask, rtBlur, m, 4);
            cb.ReleaseTemporaryRT(rtMask);

            // Separable blur (vertical pass)
            rtMask = Shader.PropertyToID("_OcclusionTexture");
            cb.GetTemporaryRT(rtMask, tw, th, 0, filter, format, rwMode);
            cb.Blit(rtBlur, rtMask, m, 5);
            cb.ReleaseTemporaryRT(rtBlur);

            // Combine AO to the G-buffer.
            var mrt = new RenderTargetIdentifier[] {
                BuiltinRenderTextureType.GBuffer0,      // Albedo, Occ
                BuiltinRenderTextureType.CameraTarget   // Ambient
            };
            cb.SetRenderTarget(mrt, BuiltinRenderTextureType.CameraTarget);
            cb.SetGlobalTexture("_OcclusionTexture", rtMask);
            cb.DrawMesh(quadMesh, Matrix4x4.identity, m, 0, 7);

            cb.ReleaseTemporaryRT(rtMask);
        }

        // Execute the AO pass immediately (used in the forward mode).
        void ExecuteAOPass(RenderTexture source, RenderTexture destination)
        {
            var tw = source.width;
            var th = source.height;
            var ts = downsampling ? 2 : 1;
            var format = RenderTextureFormat.ARGB32;
            var rwMode = RenderTextureReadWrite.Linear;
            var useGBuffer = occlusionSource == OcclusionSource.GBuffer;

            // AO buffer
            var m = aoMaterial;
            var rtMask = RenderTexture.GetTemporary(tw / ts, th / ts, 0, format, rwMode);

            // AO estimation
            Graphics.Blit(source, rtMask, m, (int)occlusionSource);

            // Separable blur (horizontal pass)
            var rtBlur = RenderTexture.GetTemporary(tw, th, 0, format, rwMode);
            Graphics.Blit(rtMask, rtBlur, m, useGBuffer ? 4 : 3);
            RenderTexture.ReleaseTemporary(rtMask);

            // Separable blur (vertical pass)
            rtMask = RenderTexture.GetTemporary(tw, th, 0, format, rwMode);
            Graphics.Blit(rtBlur, rtMask, m, 5);
            RenderTexture.ReleaseTemporary(rtBlur);

            // Composition
            m.SetTexture("_OcclusionTexture", rtMask);
            Graphics.Blit(source, destination, m, settings.debug ? 8 : 6);
            RenderTexture.ReleaseTemporary(rtMask);

            // Explicitly detach the temporary texture.
            // (This is needed to avoid conflict with CommandBuffer)
            m.SetTexture("_OcclusionTexture", null);
        }

        // Update the common material properties.
        void UpdateMaterialProperties()
        {
            var m = aoMaterial;
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Radius", radius);
            m.SetFloat("_Downsample", downsampling ? 0.5f : 1);
            m.SetInt("_SampleCount", sampleCountValue);
        }

        #endregion

        #region MonoBehaviour Functions

        void OnEnable()
        {
            // Check if the shader is supported in the current platform.
            if (!ImageEffectHelper.IsSupported(aoShader, true, false, this))
            {
                enabled = false;
                return;
            }

            // Register the command buffer if in the ambient-only mode.
            if (ambientOnly)
                targetCamera.AddCommandBuffer(CameraEvent.BeforeReflections, aoCommands);

            // Enable depth textures which the occlusion source requires.
            if (occlusionSource == OcclusionSource.DepthTexture)
                targetCamera.depthTextureMode |= DepthTextureMode.Depth;

            if (occlusionSource != OcclusionSource.GBuffer)
                targetCamera.depthTextureMode |= DepthTextureMode.DepthNormals;
        }

        void OnDisable()
        {
            // Remove the command buffer from the camera.
            if (_aoCommands != null)
                targetCamera.RemoveCommandBuffer(CameraEvent.BeforeReflections, _aoCommands);
        }

        void OnDestroy()
        {
            if (Application.isPlaying)
                Destroy(_aoMaterial);
            else
                DestroyImmediate(_aoMaterial);
        }

        void OnPreRender()
        {
            if (propertyObserver.CheckNeedsReset(settings, targetCamera))
            {
                // Reinitialize all the resources by disabling/enabling itself.
                // This is not very efficient way but just works...
                OnDisable();
                OnEnable();

                // Build the command buffer if in the ambient-only mode.
                if (ambientOnly)
                {
                    aoCommands.Clear();
                    BuildAOCommands();
                }

                propertyObserver.Update(settings, targetCamera);
            }

            // Update the material properties (later used in the AO commands).
            if (ambientOnly) UpdateMaterialProperties();
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (ambientOnly)
            {
                // Do nothing in the ambient-only mode.
                Graphics.Blit(source, destination);
            }
            else
            {
                // Execute the AO pass.
                UpdateMaterialProperties();
                ExecuteAOPass(source, destination);
            }
        }

        #endregion
    }
}
