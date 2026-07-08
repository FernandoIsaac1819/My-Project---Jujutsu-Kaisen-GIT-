using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Placeholder cursed energy bar. Subscribes to PlayerController.EnergyChanged and drives
/// a UI Slider's value — no art, no animation, just proves the wiring. Swap out later.
/// </summary>
public class CursedEnergyBarUI : MonoBehaviour
{
    [Tooltip("The player whose energy this bar displays.")]
    [SerializeField] private PlayerController _player;
    [Tooltip("UI Slider acting as the fill bar.")]
    [SerializeField] private Slider _slider;

    private void OnEnable()
    {
        _player.EnergyChanged += OnEnergyChanged;
        OnEnergyChanged(_player.CurrentEnergy, _player.MaxEnergy);   // seed immediately, don't wait for the first tick
    }

    private void OnDisable()
    {
        _player.EnergyChanged -= OnEnergyChanged;
    }

    private void OnEnergyChanged(float current, float max)
    {
        _slider.maxValue = max;
        _slider.value = current;
    }
}
