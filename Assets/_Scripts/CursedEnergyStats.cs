using UnityEngine;

/// <summary>
/// Authored cursed energy tuning: how big the reserve is and how fast it drains while
/// reinforced. Purely data — the live CurrentEnergy value lives on PlayerController,
/// same split as MovementStats/ReinforcedStat.
/// </summary>
[CreateAssetMenu(fileName = "CursedEnergyStats", menuName = "JJK/Cursed Energy Stats")]
public class CursedEnergyStats : ScriptableObject
{
    [Tooltip("Maximum cursed energy reserve.")]
    public float maxEnergy = 100f;
    [Tooltip("Energy drained per second while reinforcement is active.")]
    public float drainPerSecond = 10f;
    [Tooltip("Energy recovered per second while reinforcement is inactive.")]
    public float recoveryPerSecond = 5f;
}
