using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityStandardAssets.CinematicEffects
{
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Image Effects/Cinematic/Temporal Anti-aliasing")]
    public class TemporalAntiAliasing : MonoBehaviour
    {
        public enum Sequence
        {
            Halton
        }

        [Serializable]
        public struct JitterSettings
        {
            [Tooltip("The sequence used to generate the points used as jitter offsets.")]
            public Sequence sequence;

            [Tooltip("The diameter (in texels) inside which jitter samples are spread. Smaller values result in crisper but more aliased output, while larger values result in more stable but blurrier output.")]
            [Range(0.1f, 3f)]
            public float spread;

            [Tooltip("Number of temporal samples. A larger value results in a smoother image but takes longer to converge; whereas a smaller value converges fast but allows for less subpixel information.")]
            [Range(4, 64)]
            public int sampleCount;
        }

        [Serializable]
        public struct SharpenFilterSettings
        {
            [Tooltip("Controls the amount of sharpening applied to the color buffer.")]
            [Range(0f, 3f)]
            public float amount;
        }

        [Serializable]
        public struct BlendSettings
        {
            [Tooltip("The blend coefficient for a stationary fragment. Controls the percentage of history sample blended into the final color.")]
            [Range(0f, 1f)]
            public float stationary;

            [Tooltip("The blend coefficient for a fragment with significant motion. Controls the percentage of history sample blended into the final color.")]
            [Range(0f, 1f)]
            public float moving;

            [Tooltip("Amount of motion amplification in percentage. A higher value will make the final blend more sensitive to smaller motion, but might result in more aliased output; while a smaller value might desensitivize the algorithm resulting in a blurry output.")]
            [Range(30f, 100f)]
            public float motionAmplification;
        }

        [Serializable]
        public struct DebugSettings
        {
            [Tooltip("Forces the game view to update automatically while not in play mode.")]
            public bool forceRepaint;
        }

        [Serializable]
        public class Settings
        {
            [AttributeUsage(AttributeTargets.Field)]
            public class LayoutAttribute : PropertyAttribute
            {
            }

            [Layout]
            public JitterSettings jitterSettings;

            [Layout]
            public SharpenFilterSettings sharpenFilterSettings;

            [Layout]
            public BlendSettings blendSettings;

            [Layout]
            public DebugSettings debugSettings;

            private static readonly Settings m_Default = new Settings
            {
                jitterSettings = new JitterSettings
                {
                    sequence = Sequence.Halton,
                    spread = 1f,
                    sampleCount = 8
                },

                sharpenFilterSettings = new SharpenFilterSettings
                {
                    amount = 0.25f
                },

                blendSettings = new BlendSettings
                {
                    stationary = 0.98f,
                    moving = 0.8f,

                    motionAmplification = 60f
                },

                debugSettings = new DebugSettings
                {
                    forceRepaint = false
                }
            };

            public static Settings defaultSettings
            {
                get
                {
                    return m_Default;
                }
            }
        }

        [SerializeField]
        public Settings settings = Settings.defaultSettings;

        private Shader m_Shader;
        public Shader shader
        {
            get
            {
                if (m_Shader == null)
                    m_Shader = Shader.Find("Hidden/Temporal Anti-aliasing");

                return m_Shader;
            }
        }

        private Material m_Material;
        public Material material
        {
            get
            {
                if (m_Material == null)
                {
                    if (shader == null || !shader.isSupported)
                        return null;

                    m_Material = new Material(shader);
                }

                return m_Material;
            }
        }

        private Camera m_Camera;
        public Camera camera_
        {
            get
            {
                if (m_Camera == null)
                    m_Camera = GetComponent<Camera>();

                return m_Camera;
            }
        }

        private void RenderFullScreenQuad()
        {
            GL.PushMatrix();
            GL.LoadOrtho();
            material.SetPass(0);

            //Render the full screen quad manually.
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(0.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(1.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(1.0f, 1.0f, 0.1f);
            GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(0.0f, 1.0f, 0.1f);
            GL.End();

            GL.PopMatrix();
        }

        private RenderTexture m_History;

        private int m_SampleIndex = 0;

#if UNITY_EDITOR
        private double m_NextForceRepaintTime = 0;
#endif

        private float GetHaltonValue(int index, int radix)
        {
            float result = 0.0f;
            float fraction = 1.0f / (float)radix;

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
            Vector2 offset = new Vector2(
                    GetHaltonValue(m_SampleIndex & 1023, 2),
                    GetHaltonValue(m_SampleIndex & 1023, 3));

            if (++m_SampleIndex >= settings.jitterSettings.sampleCount)
                m_SampleIndex = 0;

            return offset;
        }

        // Adapted heavily from PlayDead's TAA code
        // https://github.com/playdeadgames/temporal/blob/master/Assets/Scripts/Extensions.cs
        private Matrix4x4 GetPerspectiveProjectionMatrix(Vector2 offset)
        {
            float vertical = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera_.fieldOfView);
            float horizontal = vertical * camera_.aspect;

            offset.x *= horizontal / (0.5f * camera_.pixelWidth);
            offset.y *= vertical / (0.5f * camera_.pixelHeight);

            float left = (offset.x - horizontal) * camera_.nearClipPlane;
            float right = (offset.x + horizontal) * camera_.nearClipPlane;
            float top = (offset.y + vertical) * camera_.nearClipPlane;
            float bottom = (offset.y - vertical) * camera_.nearClipPlane;

            Matrix4x4 matrix = new Matrix4x4();

            matrix[0, 0] = (2.0f * camera_.nearClipPlane) / (right - left);
            matrix[0, 1] = 0.0f;
            matrix[0, 2] = (right + left) / (right - left);
            matrix[0, 3] = 0.0f;

            matrix[1, 0] = 0.0f;
            matrix[1, 1] = (2.0f * camera_.nearClipPlane) / (top - bottom);
            matrix[1, 2] = (top + bottom) / (top - bottom);
            matrix[1, 3] = 0.0f;

            matrix[2, 0] = 0.0f;
            matrix[2, 1] = 0.0f;
            matrix[2, 2] = -(camera_.farClipPlane + camera_.nearClipPlane) / (camera_.farClipPlane - camera_.nearClipPlane);
            matrix[2, 3] = -(2.0f * camera_.farClipPlane * camera_.nearClipPlane) / (camera_.farClipPlane - camera_.nearClipPlane);

            matrix[3, 0] = 0.0f;
            matrix[3, 1] = 0.0f;
            matrix[3, 2] = -1.0f;
            matrix[3, 3] = 0.0f;

            return matrix;
        }

        void OnEnable()
        {
#if !UNITY_5_4_OR_NEWER
            enabled = false;
#endif

#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += ForceRepaint;
#endif

            camera_.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        }

        void OnDisable()
        {
            if (m_History != null)
            {
                RenderTexture.ReleaseTemporary(m_History);
                m_History = null;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= ForceRepaint;
#endif
            camera_.depthTextureMode &= ~(DepthTextureMode.MotionVectors);
            m_SampleIndex = 0;
        }

        void OnPreCull()
        {
            Vector2 jitter = GenerateRandomOffset();
            jitter *= settings.jitterSettings.spread;

#if UNITY_5_4_OR_NEWER
            camera_.nonJitteredProjectionMatrix = camera_.projectionMatrix;
#endif
            camera_.projectionMatrix = GetPerspectiveProjectionMatrix(jitter);

            jitter.x /= camera_.pixelWidth;
            jitter.y /= camera_.pixelHeight;

            material.SetVector("_Jitter", jitter);
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (camera_.orthographic)
            {
                Graphics.Blit(source, destination);
                return;
            }
            else if (m_History == null || (m_History.width != source.width || m_History.height != source.height))
            {
                if (m_History)
                    RenderTexture.ReleaseTemporary(m_History);

                m_History = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Default);
                m_History.filterMode = FilterMode.Bilinear;

                m_History.hideFlags = HideFlags.HideAndDontSave;

                Graphics.Blit(source, m_History);
            }

            material.SetVector("_SharpenParameters", new Vector4(settings.sharpenFilterSettings.amount, 0f, 0f, 0f));
            material.SetVector("_FinalBlendParameters", new Vector4(settings.blendSettings.stationary, settings.blendSettings.moving, 100f * settings.blendSettings.motionAmplification, 0f));

            material.SetTexture("_MainTex", source);
            material.SetTexture("_HistoryTex", m_History);

            RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Default);
            temporary.filterMode = FilterMode.Bilinear;

            var effectDestination = destination;
            var doesNeedExtraBlit = false;

            if (destination == null)
            {
                effectDestination = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Default);
                effectDestination.filterMode = FilterMode.Bilinear;

                doesNeedExtraBlit = true;
            }

            var renderTargets = new RenderBuffer[2];
            renderTargets[0] = effectDestination.colorBuffer;
            renderTargets[1] = temporary.colorBuffer;

            Graphics.SetRenderTarget(renderTargets, effectDestination.depthBuffer);
            RenderFullScreenQuad();

            RenderTexture.ReleaseTemporary(m_History);
            m_History = temporary;

            if (doesNeedExtraBlit)
            {
                Graphics.Blit(effectDestination, destination);
                RenderTexture.ReleaseTemporary(effectDestination);
            }

            RenderTexture.active = destination;
        }

        public void OnPostRender()
        {
            camera_.ResetProjectionMatrix();
        }

#if UNITY_EDITOR
        private void ForceRepaint()
        {
            if (settings.debugSettings.forceRepaint && !UnityEditor.EditorApplication.isPlaying)
            {
                var time = UnityEditor.EditorApplication.timeSinceStartup;

                if (time > m_NextForceRepaintTime)
                {
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    m_NextForceRepaintTime = time + 0.033333;
                }
            }
        }
#endif
    }
}
