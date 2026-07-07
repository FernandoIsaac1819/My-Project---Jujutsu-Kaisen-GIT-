using UnityEngine;

[System.Serializable]
public struct ReinforcedStat
{
    [SerializeField] private float _base;
    [SerializeField] private float _reinforced;

    public ReinforcedStat(float baseValue, float reinforcedValue)
    {
        _base = baseValue;
        _reinforced = reinforcedValue;
    }

    public readonly float Get(bool isReinforced) => isReinforced ? _reinforced : _base;
}