using UnityEngine;

public class CameraTargetFollow : MonoBehaviour
{
    [SerializeField] private Transform _player;   // the character root
    [SerializeField] private Vector3 _offset = new Vector3(0f, 1.5f, 0f); // chest height
    [Tooltip("Smoothing time for following the player. Filters out Rigidbody physics-frequency " +
             "jitter (resting under gravity causes small per-physics-step correction) before it " +
             "ever reaches Cinemachine. Same technique ThirdPersonCamera already uses.")]
    [SerializeField] private float _followSmoothTime = 0.05f;

    private Vector3 _currentVelocity;   // SmoothDamp's internal velocity state

    void LateUpdate()
    {
        if (_player == null) return;

        Vector3 desired = _player.position + _offset;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref _currentVelocity, _followSmoothTime);
        // rotation deliberately left untouched → camera never inherits body spin
    }
}