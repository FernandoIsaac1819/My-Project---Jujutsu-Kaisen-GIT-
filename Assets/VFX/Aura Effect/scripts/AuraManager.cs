using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Haipeng.burning_effect_tool
{
    /// <summary>
    /// One entry per character that needs an aura. The manager builds and owns
    /// a dedicated camera + RenderTexture for each slot automatically - you
    /// only need to create the layer and assign the RawImage.
    /// </summary>
    [System.Serializable]
    public class AuraSlot
    {
        [Tooltip("Layer this character's aura camera should render. Create it under " +
                 "Project Settings > Tags and Layers, then put that character on this " +
                 "layer via their Burning_effect_control (matching name).")]
        public string layerName;

        [Tooltip("The RawImage that displays this character's aura. Give it its own dedicated " +
                 "material (so each character can have their own color/settings), but it can be " +
                 "a sibling under the SAME shared Canvas - a separate Canvas per character is not " +
                 "required.")]
        public RawImage targetRawImage;

        // Built and owned at runtime - not set by hand.
        [System.NonSerialized] public Camera runtimeCamera;
        [System.NonSerialized] public RenderTexture runtimeTexture;
        [System.NonSerialized] public bool initialized;
    }

    public class AuraManager : MonoBehaviour
    {
        public static AuraManager instance;

        [Header("Character Aura Slots")]
        [Tooltip("One entry per character that needs an aura. Add a new slot for each character - " +
                 "the manager automatically creates and drives a dedicated camera + render texture " +
                 "for each one, so you never need to duplicate the whole rig by hand again.")]
        public List<AuraSlot> slots = new List<AuraSlot>();

        [Header("Is initialization successful?")]
        [Tooltip("True only if every slot initialized correctly. Check the Console for specifics " +
                 "if this is false.")]
        public bool is_initialization_successful;

        void Awake()
        {
            if (AuraManager.instance == null)
                AuraManager.instance = this;

            bool allOk = slots.Count > 0;
            for (int i = 0; i < slots.Count; i++)
            {
                bool ok = InitializeSlot(slots[i], i);
                allOk &= ok;
            }
            this.is_initialization_successful = allOk;
        }

        private bool InitializeSlot(AuraSlot slot, int index)
        {
            int layerIndex = LayerMask.NameToLayer(slot.layerName);
            if (layerIndex == -1)
            {
                Debug.LogError($"[Burning_effect_manager] Slot {index}: layer '{slot.layerName}' does not " +
                                "exist. Create it under Project Settings > Tags and Layers.");
                return false;
            }

            if (slot.targetRawImage == null)
            {
                Debug.LogError($"[Burning_effect_manager] Slot {index}: no Target Raw Image assigned.");
                return false;
            }

            if (Camera.main == null)
            {
                Debug.LogError($"[Burning_effect_manager] Slot {index}: no Camera.main found in the scene.");
                return false;
            }

            // Build a dedicated camera for this character.
            GameObject camGO = new GameObject($"AuraCamera_{slot.layerName}");
            camGO.transform.SetParent(this.transform, false);
            Camera cam = camGO.AddComponent<Camera>();

            // Solid transparent clear so the shader's alpha-based edge detection
            // sees "nothing here" as truly transparent, not opaque black.
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << layerIndex;

            // Copy the essentials from the main camera so perspective matches.
            cam.orthographic = Camera.main.orthographic;
            if (cam.orthographic) cam.orthographicSize = Camera.main.orthographicSize;
            cam.nearClipPlane = Camera.main.nearClipPlane;
            cam.farClipPlane = Camera.main.farClipPlane;
            cam.fieldOfView = Camera.main.fieldOfView;

            // Distinct depths so all slot cameras render before Camera.main,
            // in a well-defined order relative to each other.
            cam.depth = Camera.main.depth - 1 - index;

            RenderTexture rt = new RenderTexture(Screen.width, Screen.height, 24);
            cam.targetTexture = rt;

            Material mat = slot.targetRawImage.material;
            mat.SetTexture("_Main_texture", rt);

            // Make sure this RawImage's Canvas is set up correctly. If multiple
            // slots share one Canvas, this just re-applies the same settings
            // harmlessly for each slot.
            Canvas canvas = slot.targetRawImage.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = Camera.main;
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                    scaler.referenceResolution = new Vector2(Screen.width, Screen.height);
            }
            else
            {
                Debug.LogWarning($"[Burning_effect_manager] Slot {index}: Target Raw Image has no " +
                                  "parent Canvas - its render mode/camera won't be configured automatically.");
            }

            slot.runtimeCamera = cam;
            slot.runtimeTexture = rt;
            slot.initialized = true;
            return true;
        }

        void LateUpdate()
        {
            if (Camera.main == null) return;

            Transform mainT = Camera.main.transform;

            for (int i = 0; i < slots.Count; i++)
            {
                AuraSlot slot = slots[i];
                if (!slot.initialized || slot.runtimeCamera == null) continue;

                Transform camT = slot.runtimeCamera.transform;
                if (camT.position != mainT.position)
                    camT.position = mainT.position;
                if (camT.rotation != mainT.rotation)
                    camT.rotation = mainT.rotation;
                if (slot.runtimeCamera.fieldOfView != Camera.main.fieldOfView)
                    slot.runtimeCamera.fieldOfView = Camera.main.fieldOfView;
                if (slot.runtimeCamera.orthographic && slot.runtimeCamera.orthographicSize != Camera.main.orthographicSize)
                    slot.runtimeCamera.orthographicSize = Camera.main.orthographicSize;
            }
        }
    }
}