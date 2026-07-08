using UnityEngine;

/// <summary>
/// Identity asset for a character's cursed-energy aura. Referenced by AuraSource, which
/// AuraManager uses to auto-provision the capture rig(s) — no manual layer/RawImage setup.
/// </summary>
[CreateAssetMenu(fileName = "Character", menuName = "JJK/Character")]
public class Character : ScriptableObject
{
    [Header("Cursed Energy")]
    [Tooltip("Aura material for the body capture. Leave empty if this character has no body aura.")]
    public Material auraMaterial;
    [Tooltip("Aura material for the weapon capture (rendered separately from the body). Leave empty if this character has no weapon aura.")]
    public Material weaponAuraMaterial;
}