using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private enum LocomotionState { Idle, Walk, Run }

    [Header("References")]
    [SerializeField] private InputReader _input;
    [SerializeField] private Rigidbody _rigidbody;
    [SerializeField] private Animator _animator;
    [SerializeField] private AnimateRootMotion _rootMotion;
    [Tooltip("Optional. If set, movement input is relative to this camera's flattened facing.")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Rotation")]
    [SerializeField] private float _rotationSpeedDegPerSec = 540f;
    [SerializeField] private float _moveDeadzone = 0.05f;

    [Header("Locomotion Blend (Speed param: 0=Idle, 1=Walk, 2=Run)")]
    [SerializeField, Range(0f, 1f)] private float _walkEnter = 0.15f;
    [SerializeField, Range(0f, 1f)] private float _walkExit = 0.1f;
    [SerializeField, Range(0f, 1f)] private float _runEnter = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _runExit = 0.55f;
    [SerializeField] private float _speedDampTime = 0.1f;

    [Header("Jump / Airborne")]
    [SerializeField] private float _jumpForce = 8f;
    [SerializeField] private float _ascendGravityMultiplier = 2.2f;
    [SerializeField] private float _fallGravityMultiplier = 3.5f;
    [SerializeField] private float _maxFallSpeed = 25f;
    [SerializeField] private float _minAirborneTime = 0.08f;
    [SerializeField] private float _jumpMomentumMultiplier = 1f;
    [SerializeField] private float _maxInheritedSpeed = 8f;
    [Tooltip("Minimum time between jumps, prevents spamming jump to skip the land animation instantly.")]
    [SerializeField] private float _jumpCooldown = 0.3f;

    [Header("Air Control")]
    [SerializeField] private float _airControlSpeed = 4f;
    [SerializeField] private float _airAcceleration = 20f;

    [Header("Ground Check")]
    [SerializeField] private Vector3 _groundCheckOffset = new Vector3(0f, 0.1f, 0f);
    [SerializeField] private float _groundCheckRadius = 0.25f;
    [SerializeField] private float _groundCheckDistance = 0.25f;
    [SerializeField] private LayerMask _groundMask;
    [SerializeField] private float _coyoteTime = 0.12f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");
    private static readonly int LandHash = Animator.StringToHash("Land");

    private LocomotionState _locomotionState = LocomotionState.Idle;
    private bool _isGrounded = true;
    private float _lastGroundedTime;
    private bool _jumpQueued;
    private float _jumpStartTime;
    private float _lastJumpTime = -999f;

    private Vector3 _previousPosition;
    private Vector3 _groundedVelocity;

    private void OnEnable()
    {
        if (_input != null) _input.JumpEvent += OnJumpPressed;
    }

    private void OnDisable()
    {
        if (_input != null) _input.JumpEvent -= OnJumpPressed;
    }

    private void OnJumpPressed()
    {
        _jumpQueued = true;
    }

    private void Start()
    {
        _rigidbody.useGravity = false;
        if (_rootMotion != null) _rootMotion.ApplyPositionDelta = _isGrounded;
        _previousPosition = _rigidbody.position;
    }

    private void FixedUpdate()
    {
        Vector3 delta = _rigidbody.position - _previousPosition;
        delta.y = 0f;
        _groundedVelocity = delta / Time.fixedDeltaTime;

        UpdateGroundCheck();
        Vector3 moveDir = GetMoveDirection(out float inputMagnitude);

        UpdateLocomotionBlend(inputMagnitude);
        UpdateRotation(moveDir, inputMagnitude);
        HandleJump();
        ApplyCustomGravity();
        ApplyAirControl(moveDir, inputMagnitude);

        _animator.SetFloat(VerticalVelocityHash, _rigidbody.linearVelocity.y);
        _previousPosition = _rigidbody.position;
    }

    private void ApplyAirControl(Vector3 moveDir, float inputMagnitude)
    {
        if (_isGrounded) return;

        Vector3 v = _rigidbody.linearVelocity;
        Vector3 currentHorizontal = new Vector3(v.x, 0f, v.z);
        Vector3 desiredHorizontal = moveDir * inputMagnitude * _airControlSpeed;

        Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, desiredHorizontal, _airAcceleration * Time.fixedDeltaTime);
        _rigidbody.linearVelocity = new Vector3(newHorizontal.x, v.y, newHorizontal.z);
    }

    private void ApplyCustomGravity()
    {
        if (_isGrounded) return;

        Vector3 v = _rigidbody.linearVelocity;
        float multiplier = v.y > 0f ? _ascendGravityMultiplier : _fallGravityMultiplier;
        v.y += Physics.gravity.y * (multiplier - 1f) * Time.fixedDeltaTime;
        v.y = Mathf.Max(v.y, -_maxFallSpeed);
        _rigidbody.linearVelocity = v;
    }

    private Vector3 GetMoveDirection(out float magnitude)
    {
        Vector2 raw = _input != null ? _input.MoveInput : Vector2.zero;
        Vector3 dir;

        if (_cameraTransform != null)
        {
            Vector3 fwd = _cameraTransform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = _cameraTransform.right; right.y = 0f; right.Normalize();
            dir = fwd * raw.y + right * raw.x;
        }
        else
        {
            dir = new Vector3(raw.x, 0f, raw.y);
        }

        magnitude = Mathf.Clamp01(dir.magnitude);
        return dir.normalized;
    }

    private void UpdateLocomotionBlend(float inputMagnitude)
    {
        switch (_locomotionState)
        {
            case LocomotionState.Idle:
                if (inputMagnitude >= _walkEnter) _locomotionState = LocomotionState.Walk;
                break;
            case LocomotionState.Walk:
                if (inputMagnitude >= _runEnter) _locomotionState = LocomotionState.Run;
                else if (inputMagnitude < _walkExit) _locomotionState = LocomotionState.Idle;
                break;
            case LocomotionState.Run:
                if (inputMagnitude < _runExit) _locomotionState = LocomotionState.Walk;
                break;
        }

        float target = _locomotionState switch
        {
            LocomotionState.Idle => 0f,
            LocomotionState.Walk => 1f,
            LocomotionState.Run => 2f,
            _ => 0f
        };

        _animator.SetFloat(SpeedHash, target, _speedDampTime, Time.fixedDeltaTime);
    }

    private void UpdateRotation(Vector3 moveDir, float inputMagnitude)
    {
        if (inputMagnitude < _moveDeadzone) return;

        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        Quaternion newRot = Quaternion.RotateTowards(_rigidbody.rotation, targetRot, _rotationSpeedDegPerSec * Time.fixedDeltaTime);
        _rigidbody.MoveRotation(newRot);
    }

    private void UpdateGroundCheck()
{
    Vector3 origin = _rigidbody.position + _groundCheckOffset;
    bool sphereHit = Physics.SphereCast(origin, _groundCheckRadius, Vector3.down,
        out RaycastHit hit, _groundCheckDistance, _groundMask, QueryTriggerInteraction.Ignore);

    bool wasGrounded = _isGrounded;

    if (_isGrounded)
    {
        _isGrounded = sphereHit; // normal ledge/walk-off detection
    }
    else
    {
        // Don't allow re-grounding while still moving upward or immediately after a jump.
        _isGrounded = sphereHit
            && _rigidbody.linearVelocity.y <= 0.5f
            && (Time.time - _jumpStartTime) >= _minAirborneTime;
    }

    if (_isGrounded) _lastGroundedTime = Time.time;

    if (_isGrounded != wasGrounded)
    {
        _animator.SetBool(IsGroundedHash, _isGrounded);
        if (_rootMotion != null) _rootMotion.ApplyPositionDelta = _isGrounded;

        if (_isGrounded)
        {
            // Kill leftover air momentum so root motion isn't fighting old velocity (prevents sliding).
            Vector3 v = _rigidbody.linearVelocity;
            v.x = 0f;
            v.z = 0f;
            _rigidbody.linearVelocity = v;

            bool hasMoveInput = _input != null && _input.MoveInput.sqrMagnitude > (_walkEnter * _walkEnter);

            if (hasMoveInput)
                _animator.ResetTrigger(LandHash);
            else
                _animator.SetTrigger(LandHash);
        }
    }
}

    private void HandleJump()
    {
        if (!_jumpQueued) return;
        _jumpQueued = false;

        bool canJump = (_isGrounded || (Time.time - _lastGroundedTime <= _coyoteTime))
            && (Time.time - _lastJumpTime >= _jumpCooldown);
        if (!canJump) return;

        Vector3 momentum = _groundedVelocity * _jumpMomentumMultiplier;
        if (momentum.sqrMagnitude > _maxInheritedSpeed * _maxInheritedSpeed)
            momentum = momentum.normalized * _maxInheritedSpeed;

        Vector3 v = _rigidbody.linearVelocity;
        v.x = momentum.x;
        v.z = momentum.z;
        v.y = _jumpForce;
        _rigidbody.linearVelocity = v;

        _isGrounded = false;
        _jumpStartTime = Time.time;
        _lastJumpTime = Time.time;
        _animator.SetBool(IsGroundedHash, false);
        if (_rootMotion != null) _rootMotion.ApplyPositionDelta = false;
    }
}