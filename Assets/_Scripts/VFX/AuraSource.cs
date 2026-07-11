using System;
using System.Collections.Generic;
using UnityEngine;

namespace JJK.CursedEnergy
{
    /// <summary>
    /// Attach to any character GameObject that needs a cursed-energy aura. Self-registers
    /// into a live list while enabled — same pattern as Targetable.Active — so AuraManager
    /// can discover and provision it automatically. No manual layer, no manual RawImage,
    /// no manually-typed layer name: just drag in the Character asset and the renderers.
    /// </summary>
    public class AuraSource : MonoBehaviour
    {
        [Tooltip("Identity asset holding this character's aura material(s).")]
        //[SerializeField] private Character _character;
        //[Tooltip("Renderer(s) captured for the body aura silhouette.")]
        [SerializeField] private Renderer[] _bodyRenderers;
        [Tooltip("Renderer(s) captured for the weapon aura silhouette (separate capture from the body). Leave empty if this character has no weapon aura.")]
        [SerializeField] private Renderer[] _weaponRenderers;

        //public Character Character => _character;
        public Renderer[] BodyRenderers => _bodyRenderers;
        public Renderer[] WeaponRenderers => _weaponRenderers;

        public static readonly List<AuraSource> Active = new List<AuraSource>();

        /// AuraManager subscribes to these instead of polling every frame for new/removed
        /// characters. It also sweeps Active on its own Awake, so registration order
        /// (this vs. AuraManager) never matters.
        public static event Action<AuraSource> Registered;
        public static event Action<AuraSource> Unregistered;

        private void OnEnable()
        {
            Active.Add(this);
            Registered?.Invoke(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
            Unregistered?.Invoke(this);
        }
    }
}
