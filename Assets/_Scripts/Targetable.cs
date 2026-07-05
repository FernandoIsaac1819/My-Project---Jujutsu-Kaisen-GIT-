using System.Collections.Generic;
using UnityEngine;

public enum Team { Player, Enemy }

/// <summary>
/// Marks something that can be locked onto. Self-registers into a live list while
/// enabled, so familiars (Rika, Megumi's shikigami, etc.) become lockable only
/// while their GameObject is active, and drop out automatically when dismissed.
/// </summary>
public class Targetable : MonoBehaviour
{
    [Tooltip("Which side this belongs to. The player locks onto Enemy targetables only.")]
    [SerializeField] private Team _team = Team.Enemy;
    [Tooltip("Point the camera aims at and the player faces (chest height). Falls back to the root if empty.")]
    [SerializeField] private Transform _aimPoint;   // chest height; falls back to the root

    public Team Team => _team;
    public Transform AimPoint => _aimPoint != null ? _aimPoint : transform;

    public static readonly List<Targetable> Active = new List<Targetable>();

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
}