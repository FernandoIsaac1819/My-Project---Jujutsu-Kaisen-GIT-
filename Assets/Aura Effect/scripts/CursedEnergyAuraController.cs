using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using Haipeng.burning_effect_tool;

namespace JJK.CursedEnergy
{

    public class CursedEnergyAuraController : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Optional: assign this character's dedicated burn-effect RawImage directly. " +
                 "REQUIRED if there is more than one aura rig in the scene (e.g. multiple " +
                 "characters) - auto-discovery only works reliably with a single rig.")]
        [SerializeField] private RawImage Aura_ImageHolder;

        private string edgePropertyName = "_edge_1";
        private string auraSizePropertyName = "_OutlineOffset";
        private string fireColorPropertyName = "_fire_color";

        [Header("Fade In/Out Speeds")]
        [Tooltip("Speed at which the Aura fades in and out.")]
        public float FadeIn_Speed = 2f;
        public float FadeOut_Speed = 2f;

        [Header("Aura Color")]
        [Tooltip("Shows the color of the aura")]
        [ColorUsage(true, true)] // showHDR, allow HDR intensity - matches the [HDR] fire_color property
        [SerializeField] private Color Aura_Color;

        [Header("Toggle")]
        [Tooltip("Key used to manually toggle the aura on/off (for testing).")]
        public KeyCode toggleKey = KeyCode.W;


        private bool instanceMaterial = true;
        private Material mat;
        private int edgeID;
        private int auraSizeID;
        private int fireColorID;

        private float edgeMin;
        private float edgeMax;
        private float auraSizeMin;
        private float currentEdgeValue;

        private Coroutine edgeRoutine;
        private Coroutine auraSizeRoutine;
        private Coroutine colorRoutine;

        private Color lastAppliedFireColor;

        private bool isEffectOn;

        private void Start()
        {
            RawImage rawImageTarget = Aura_ImageHolder != null ? Aura_ImageHolder : FindRawImageFromManager();

            if (rawImageTarget == null)
            {
                Debug.LogError("[CursedEnergyAuraController] Could not find the RawImage. " +
                                "Assign 'Raw Image' explicitly (required if there's more than " +
                                "one aura rig in the scene), or make sure a single " +
                                "Burning_effect_manager exists with its usual Canvas/RawImage " +
                                "children. Disabling script.");
                enabled = false;
                return;
            }

            mat = instanceMaterial ? new Material(rawImageTarget.material) : rawImageTarget.material;
            if (instanceMaterial) rawImageTarget.material = mat;

            edgeID = Shader.PropertyToID(edgePropertyName);
            auraSizeID = Shader.PropertyToID(auraSizePropertyName);
            fireColorID = Shader.PropertyToID(fireColorPropertyName);

            WarnIfMissing(edgeID, edgePropertyName);
            WarnIfMissing(auraSizeID, auraSizePropertyName);
            WarnIfMissing(fireColorID, fireColorPropertyName);

            // Read the real min/max straight from the shader's own Range() so
            // clamping always matches whatever's authored in the Blackboard.
            GetShaderRange(mat, edgePropertyName, out edgeMin, out edgeMax);

            // Read the material EXACTLY as authored. Nothing is written back here -
            // this script only starts changing things once you explicitly call
            // FadeIn()/FadeOut()/GrowAura()/SetFireColor()/etc.
            currentEdgeValue = mat.HasProperty(edgeID) ? mat.GetFloat(edgeID) : edgeMin;
            Aura_Color = mat.HasProperty(fireColorID) ? mat.GetColor(fireColorID) : Aura_Color;
            lastAppliedFireColor = Aura_Color;

            // Infer visible/hidden state from wherever edge currently sits, so the
            // first FadeIn()/FadeOut()/toggle-key press moves the correct direction.
            isEffectOn = currentEdgeValue <= (edgeMin + edgeMax) * 0.5f;
        }

        /// <summary>
        /// Walks Burning_effect_manager.instance's own Canvas/RawImage hierarchy -
        /// the same path it uses internally - to find the RawImage without needing
        /// it manually assigned. Only reliable when there is a single manager/rig
        /// in the scene; with multiple rigs (multiple characters), always assign
        /// 'Raw Image' explicitly instead.
        /// </summary>
        private RawImage FindRawImageFromManager()
        {
            AuraManager manager = AuraManager.instance;
            if (manager == null)
            {
                Debug.LogError("[CursedEnergyAuraController] AuraManager.instance is null. " +
                                "Make sure an AuraManager exists in the scene, or assign " +
                                "'Raw Image' explicitly.");
                return null;
            }

            Transform canvasT = manager.transform.Find("Canvas");
            if (canvasT == null)
            {
                Debug.LogError("[CursedEnergyAuraController] AuraManager has no 'Canvas' child.");
                return null;
            }

            Transform rawImageT = canvasT.Find("RawImage");
            if (rawImageT == null)
            {
                Debug.LogError("[CursedEnergyAuraController] Canvas has no 'RawImage' child.");
                return null;
            }

            RawImage found = rawImageT.GetComponent<RawImage>();
            if (found == null)
            {
                Debug.LogError("[CursedEnergyAuraController] 'RawImage' object has no RawImage component.");
            }

            return found;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                ToggleEffect();
            }

            // Picks up color changes made directly in the Inspector at runtime,
            // so you can drag the swatch during Play mode to test colors live.
            if (Aura_Color != lastAppliedFireColor)
            {
                mat.SetColor(fireColorID, Aura_Color);
                lastAppliedFireColor = Aura_Color;
            }
        }

        // ---------------------------------------------------------------
        // Public control API - call these from ability scripts, etc.
        // ---------------------------------------------------------------

        /// <summary>Flips the aura between fully visible and fully hidden.</summary>
        public void ToggleEffect()
        {
            isEffectOn = !isEffectOn;
            if (isEffectOn) FadeIn(); else FadeOut();
        }

        /// <summary>Makes the aura appear. Moves the edge value toward its shader-defined MINIMUM (more visible), using edgeInSpeed.</summary>
        public void FadeIn() => SetEdgeTarget(edgeMin);

        /// <summary>Makes the aura disappear. Moves the edge value toward its shader-defined MAXIMUM (more hidden), using edgeOutSpeed.</summary>
        public void FadeOut() => SetEdgeTarget(edgeMax);

        /// <summary>
        /// Smoothly moves the edge value toward an arbitrary target (clamped to the shader's range).
        /// Remember: lower = more visible, higher = more hidden.
        /// </summary>
        public void SetEdgeTarget(float target)
        {
            target = Mathf.Clamp(target, edgeMin, edgeMax);
            if (Mathf.Approximately(target, currentEdgeValue)) return;

            // Moving DOWN (toward more visible) uses edgeInSpeed;
            // moving UP (toward more hidden) uses edgeOutSpeed.
            float speed = (target < currentEdgeValue) ? FadeIn_Speed : FadeOut_Speed;
            if (edgeRoutine != null) StopCoroutine(edgeRoutine);
            edgeRoutine = StartCoroutine(SmoothMoveFloat(
                v => { currentEdgeValue = v; mat.SetFloat(edgeID, v); },
                currentEdgeValue, target, speed));
        }


        /// <summary>Sets fire_color immediately (no lerp) and keeps the Inspector field in sync.</summary>
        public void SetFireColor(Color newColor)
        {
            Aura_Color = newColor;
            mat.SetColor(fireColorID, Aura_Color);
            lastAppliedFireColor = Aura_Color;
        }

        /// <summary>Returns the currently applied fire color.</summary>
        public Color GetFireColor() => Aura_Color;

        /// <summary>Smoothly lerps fire_color to targetColor over duration seconds, if you want a transition instead of an instant change.</summary>
        public void LerpFireColor(Color targetColor, float duration)
        {
            if (colorRoutine != null) StopCoroutine(colorRoutine);
            colorRoutine = StartCoroutine(SmoothMoveColor(targetColor, duration));
        }

        // ---------------------------------------------------------------
        // Shader reflection - reads the real Range(min, max) off the shader
        // ---------------------------------------------------------------

        private void GetShaderRange(Material material, string propertyName, out float min, out float max)
        {
            Shader shader = material.shader;
            int index = shader.FindPropertyIndex(propertyName);

            if (index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Range)
            {
                Vector2 range = shader.GetPropertyRangeLimits(index);
                min = range.x;
                max = range.y;
            }
            else
            {
                Debug.LogWarning($"[CursedEnergyAuraController] Could not read a Range for '{propertyName}' " +
                                  $"from the shader - defaulting to 0-1. Check that the property exists and is " +
                                  $"set to Slider mode in the Blackboard.");
                min = 0f;
                max = 1f;
            }
        }

        // ---------------------------------------------------------------
        // Smoothing helpers
        // ---------------------------------------------------------------

        private IEnumerator SmoothMoveFloat(System.Action<float> apply, float from, float to, float speed)
        {
            float distance = Mathf.Abs(to - from);
            float duration = (speed > 0f) ? distance / speed : 0f;

            if (duration <= 0f)
            {
                apply(to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                apply(Mathf.SmoothStep(from, to, t));
                yield return null;
            }

            apply(to);
        }

        private IEnumerator SmoothMoveColor(Color targetColor, float duration)
        {
            Color from = Aura_Color;

            if (duration <= 0f)
            {
                SetFireColor(targetColor);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetFireColor(Color.Lerp(from, targetColor, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }

            SetFireColor(targetColor);
        }

        private void WarnIfMissing(int id, string name)
        {
            if (!mat.HasProperty(id))
            {
                Debug.LogWarning($"[CursedEnergyAuraController] Material '{mat.name}' has no property named '{name}'. " +
                                  $"Check the Reference field on the corresponding property in the Shader Graph Blackboard.");
            }
        }
    }
}