using System;
using System.Collections;
using Haipeng.burning_effect_tool;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace JJK.CursedEnergy
{
    
    public class CE_Controller : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The PlayerController that owns the reinforcement state. Auto-found on the same GameObject if left empty.")]
        PlayerController _player;

        [Tooltip("This character's aura RawImage. Required when more than one aura rig exists in the scene; " +
                 "otherwise it is auto-discovered from the AuraManager.")]
        [SerializeField] private RawImage _auraImage;

        [Header("Fade Speeds")]
        [Tooltip("How fast the aura fades in (edge moves toward min / visible).")]
        [SerializeField] private float _fadeOutSpeed = 2f;

        [Tooltip("How fast the aura fades out (edge moves toward max / hidden).")]
        [SerializeField] private float _fadeInSpeed = 2f;

        // Shader property references (must match the Reference names in the Shader Graph blackboard).
        private const string FadeProperty = "_Fade";
        private const string OutlineOffsetProperty = "_OutlineOffset";
        
        private const bool InstanceMaterial = true;   // clone the material so each character animates independently

        private Material _material;
        private int _fadeID;
        private int _outlineOffsetId;

        private float _fadeMin;
        private float _fadeMax;
        private float _currentFadeValue;

        private Coroutine _fadeRoutine;

        /// <summary>Resolves the player reference and subscribes to its reinforcement event.</summary>
        private void OnEnable()
        {
            if (_player == null) _player = GetComponent<PlayerController>();

            if (_player == null)
            {
                Debug.LogError("[CE_Controller] No PlayerController assigned or found on this GameObject. Disabling.");
                enabled = false;
                return;
            }

            _player.ReinforceChanged += OnReinforceChanged;
        }

        /// <summary>Unsubscribes to avoid dangling handlers.</summary>
        private void OnDisable()
        {
            if (_player != null) _player.ReinforceChanged -= OnReinforceChanged;
        }

        /// <summary>Instances the material, caches property IDs and the edge range, and sets the correct starting state.</summary>
        private void Start()
        {
            _player = _player != null ? _player : GetComponent<PlayerController>();

            RawImage target = _auraImage != null ? _auraImage : FindRawImageFromManager();
            if (target == null)
            {
                Debug.LogError("[CE_Controller] Could not find the aura RawImage. Assign it explicitly. Disabling.");
                enabled = false;
                return;
            }

            _material = InstanceMaterial ? new Material(target.material) : target.material;
            if (InstanceMaterial) target.material = _material;

            _outlineOffsetId = Shader.PropertyToID(OutlineOffsetProperty);
            _fadeID = Shader.PropertyToID(FadeProperty);


            WarnIfMissing(_fadeID, FadeProperty);
            WarnIfMissing(_outlineOffsetId, OutlineOffsetProperty);

            GetShaderRange(_material, FadeProperty, out _fadeMax, out _fadeMin);

            // Start matching the player's state: reinforced = visible (min), otherwise hidden (max).
            // Keep _currentFadeValue in sync so the first fade computes its direction correctly.
            _currentFadeValue = _player.isReinforced ? _fadeMin : _fadeMax;
            _material.SetFloat(_fadeID, _currentFadeValue);
        }

        /// <summary>Reacts to a reinforcement change by fading the aura in or out.</summary>
        private void OnReinforceChanged(bool reinforced)
        {
            if (reinforced) ShowAura();
            else HideAura();
        }

        /// <summary>Fades the aura in (edge toward min / visible).</summary>
        public void ShowAura() => SetFadeTarget(_fadeMax);


        /// <summary>Fades the aura out (edge toward max / hidden).</summary>
        public void HideAura() => SetFadeTarget(_fadeMin);

        /// <summary>Smoothly moves the edge value to a target (clamped to the shader's range). Lower = more visible.</summary>
        public void SetFadeTarget(float target)
        {
            if (_material == null) return;

            target = Mathf.Clamp(target, _fadeMin, _fadeMax);
            if (Mathf.Approximately(target, _currentFadeValue)) return;

            float speed = target < _currentFadeValue ? _fadeOutSpeed : _fadeInSpeed;

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(SmoothMoveFloat(
                value => { _currentFadeValue = value; _material.SetFloat(_fadeID, value); },
                _currentFadeValue, target, speed));
        }

        /// <summary>Walks the AuraManager's Canvas/RawImage hierarchy to auto-find the RawImage (single-rig scenes only).</summary>
        private RawImage FindRawImageFromManager()
        {
            AuraManager manager = AuraManager.instance;
            
            if (manager == null)
            {
                Debug.LogError("[CE_Controller] AuraManager.instance is null. Assign the RawImage explicitly.");
                return null;
            }

            Transform canvas = manager.transform.Find("Canvas");
            Transform rawImage = canvas != null ? canvas.Find("RawImage") : null;
            RawImage found = rawImage != null ? rawImage.GetComponent<RawImage>() : null;

            if (found == null)
                Debug.LogError("[CE_Controller] Could not locate Canvas/RawImage under the AuraManager.");

            return found;
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
                Debug.LogWarning($"[CE_Controller] No Range found for '{propertyName}' - defaulting to 0-1. " +
                                 "Check the property exists and is set to Slider mode in the blackboard.");
                min = 0f;
                max = 1f;
            }
        }

        /// <summary>Lerps a float from 'from' to 'to' at 'speed' units/sec, applying each step via the action.</summary>
        private IEnumerator SmoothMoveFloat(Action<float> apply, float from, float to, float speed)
        {
            float duration = speed > 0f ? Mathf.Abs(to - from) / speed : 0f;
            if (duration <= 0f) { apply(to); yield break; }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                apply(Mathf.SmoothStep(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            apply(to);
        }

        /// <summary>Logs a warning if the instanced material lacks an expected property.</summary>
        private void WarnIfMissing(int id, string propertyName)
        {
            if (!_material.HasProperty(id))
                Debug.LogWarning($"[CE_Controller] Material '{_material.name}' has no property '{propertyName}'. " +
                                 "Check the Reference field in the Shader Graph blackboard.");
        }
    }
}