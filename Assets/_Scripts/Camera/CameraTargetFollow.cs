using UnityEngine;

public class CameraTargetFollow : MonoBehaviour
{
    [SerializeField] private Transform _player;   // the character root
    [SerializeField] private Vector3 _offset = new Vector3(0f, 1.5f, 0f); // chest height

    void LateUpdate()
    {
        if (_player != null)
            transform.position = _player.position + _offset;
        // rotation deliberately left untouched → camera never inherits body spin
    }
}