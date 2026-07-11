using UnityEngine;

/// <summary>
/// Applies Mecanim root motion to a Rigidbody living on a different GameObject
/// (the parent "Player") than the Animator (this "Visual" child).
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimateRootMotion : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody _rigidbody; // assign the PARENT's rigidbody
    [SerializeField] private Animator _animator;

    [Header("Root Motion Control")]
    [SerializeField] private bool _applyRootMotion = true;
    [SerializeField] private bool _applyRootRotation = false;
    [SerializeField] private float _horizontalMultiplier = 1f;

    [Tooltip("Set false while airborne — physics (gravity + air control) fully owns position instead.")]
    public bool ApplyPositionDelta = true;

    public bool ApplyRootMotion { get => _applyRootMotion; set => _applyRootMotion = value; }

    private void Reset() => _animator = GetComponent<Animator>();

    private void OnAnimatorMove()
    {
        if (!_applyRootMotion || _rigidbody == null) return;

        if (ApplyPositionDelta)
        {
            Vector3 delta = _animator.deltaPosition;
            delta.x *= _horizontalMultiplier;
            delta.z *= _horizontalMultiplier;
            _rigidbody.MovePosition(_rigidbody.position + delta);
        }

        if (_applyRootRotation)
        {
            _rigidbody.MoveRotation(_rigidbody.rotation * _animator.deltaRotation);
        }
    }
}