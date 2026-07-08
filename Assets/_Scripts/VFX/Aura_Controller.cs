using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace JJK.CursedEnergy
{
    /// <summary>
    /// Drives the fade in/out of a character's aura (body and, separately, weapon) based on
    /// their reinforcement state. AuraManager calls Initialize() once its capture rigs are
    /// ready — this no longer searches for its RawImage itself.
    /// </summary>
    public class Aura_Controller : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The PlayerController that owns the reinforcement state. Auto-found on the same GameObject if left empty.")]
        [SerializeField] private PlayerController _player;

        [Header("Fade Speeds")]
        [Tooltip("How fast the aura fades in (edge moves toward min / visible).")]
        [SerializeField] private float _fadeOutSpeed = 2f;
        [Tooltip("How fast the aura fades out (edge moves toward max / hidden).")]
        [SerializeField] private float _fadeInSpeed = 2f;

        // Must match the Reference name in the Shader Graph blackboard.
        private const string FadeProperty = "_Fade";

        /// <summary>Runtime fade state for one captured target (body or weapon) — each has its
        /// own material instance and can animate independently.</summary>
        private class FadeTarget
        {
            public Material material;
            public int fadeId;
            public float fadeMin;
            public float fadeMax;
            public float current;
            public Coroutine routine;
        }

        private FadeTarget _body;
        private FadeTarget _weapon;

        private void OnEnable()
        {
            if (_player == null) _player = GetComponent<PlayerController>();
            if (_player == null)
            {
                Debug.LogError("[Aura_Controller] No PlayerController assigned or found on this GameObject. Disabling.", this);
                enabled = false;
                return;
            }
            _player.ReinforceChanged += OnReinforceChanged;
        }

        private void OnDisable()
        {
            if (_player != null) _player.ReinforceChanged -= OnReinforceChanged;
        }

        /// <summary>Called by AuraManager once its capture rig(s) exist for this character.
        /// Either image can be null (e.g. a character with no weapon aura) — that target is
        /// simply skipped everywhere below.</summary>
        public void Initialize(RawImage bodyImage, RawImage weaponImage)
        {
            if (_player == null) _player = GetComponent<PlayerController>();   // may run before OnEnable

            _body = bodyImage != null ? BuildTarget(bodyImage) : null;
            _weapon = weaponImage != null ? BuildTarget(weaponImage) : null;

            // Snap instantly to whatever the player's state already is, rather than always
            // starting hidden — covers reinforcement having toggled on before provisioning finished.
            bool reinforced = _player != null && _player.isReinforced;
            SnapTarget(_body, reinforced);
            SnapTarget(_weapon, reinforced);
        }

        private FadeTarget BuildTarget(RawImage image)
        {
            var t = new FadeTarget
            {
                material = image.material,   // AuraManager already instanced this per-character
                fadeId = Shader.PropertyToID(FadeProperty)
            };

            if (!t.material.HasProperty(t.fadeId))
                Debug.LogWarning($"[Aura_Controller] Material '{t.material.name}' has no property '{FadeProperty}'. " +
                                  "Check the Reference field in the Shader Graph blackboard.");

            GetShaderRange(t.material, FadeProperty, out t.fadeMin, out t.fadeMax);
            return t;
        }

        private void SnapTarget(FadeTarget t, bool reinforced)
        {
            if (t == null) return;
            t.current = reinforced ? t.fadeMax : t.fadeMin;
            t.material.SetFloat(t.fadeId, t.current);
        }

        private void OnReinforceChanged(bool reinforced)
        {
            if (reinforced) ShowAura();
            else HideAura();
        }

        /// <summary>Fades both auras in (visible). This shader's _Fade is higher = more visible.</summary>
        public void ShowAura()
        {
            SetFadeTarget(_body, _body?.fadeMax ?? 0f);
            SetFadeTarget(_weapon, _weapon?.fadeMax ?? 0f);
        }

        /// <summary>Fades both auras out (hidden).</summary>
        public void HideAura()
        {
            SetFadeTarget(_body, _body?.fadeMin ?? 0f);
            SetFadeTarget(_weapon, _weapon?.fadeMin ?? 0f);
        }

        private void SetFadeTarget(FadeTarget t, float target)
        {
            if (t == null) return;

            target = Mathf.Clamp(target, t.fadeMin, t.fadeMax);
            if (Mathf.Approximately(target, t.current)) return;

            float speed = target < t.current ? _fadeOutSpeed : _fadeInSpeed;

            if (t.routine != null) StopCoroutine(t.routine);
            t.routine = StartCoroutine(SmoothMoveFloat(t, target, speed));
        }

        private IEnumerator SmoothMoveFloat(FadeTarget t, float to, float speed)
        {
            float from = t.current;
            float duration = speed > 0f ? Mathf.Abs(to - from) / speed : 0f;
            if (duration <= 0f) { Apply(t, to); yield break; }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                Apply(t, Mathf.SmoothStep(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            Apply(t, to);
        }

        private static void Apply(FadeTarget t, float value)
        {
            t.current = value;
            t.material.SetFloat(t.fadeId, value);
        }

        /// <summary>Reads a property's authored Range(min,max) off the shader so clamping matches the blackboard.</summary>
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
                Debug.LogWarning($"[Aura_Controller] No Range found for '{propertyName}' - defaulting to 0-1. " +
                                 "Check the property exists and is set to Slider mode in the blackboard.");
                min = 0f;
                max = 1f;
            }
        }
    }
}