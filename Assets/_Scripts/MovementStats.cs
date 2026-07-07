using UnityEngine;

[CreateAssetMenu(fileName = "MovementStats", menuName = "JJK/Movement Stats")]
public class MovementStats : ScriptableObject
{
    [Header("Free - Locomotion")]
    [Tooltip("Walk speed (m/s). No reinforced variant — walking is always walking.")]
    public float walkSpeed = 2f;
    [Tooltip("Run speed (m/s), base vs reinforced.")]
    public ReinforcedStat runSpeed = new ReinforcedStat(1.5f, 6f);

    [Header("Jump")]
    [Tooltip("Peak jump height (m), base vs reinforced.")]
    public ReinforcedStat jumpHeight = new ReinforcedStat(1.5f, 2f);

    [Header("Dash")]
    [Tooltip("Dash burst speed (m/s), base vs reinforced.")]
    public ReinforcedStat dashSpeed = new ReinforcedStat(12f, 16f);
    [Tooltip("Dash burst duration (s), base vs reinforced.")]
    public ReinforcedStat dashDuration = new ReinforcedStat(0.15f, 0.2f);
}