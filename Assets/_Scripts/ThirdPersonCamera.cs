using UnityEngine;

/// <summary>
/// Third-person orbit-follow camera, driven by the right stick (LookInput).
/// Place this on the Main Camera. It follows a target, orbits around it with
/// yaw/pitch, smooths its follow position, and clamps how far up/down you can look.
///
/// Step 4 (movement) reads this camera's flattened forward/right so the player
/// moves relative to where the camera is looking.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ScriptableObject input source. The right stick drives orbit (free) / target switch (locked).")]
    [SerializeField] private InputReader _input;
    [Tooltip("The player to follow.")]
    [SerializeField] private Transform _target;
    [Tooltip("The player's PlayerController, for reading lock-on state and the current target.")]
    [SerializeField] private PlayerController _player;

    [Header("Target Mode Camera")]
    [Tooltip("Height of the locked camera above the player (m).")]
    [SerializeField] private float _targetCamHeight = 2f;
    [Tooltip("Minimum distance the camera sits behind the player while locked (m).")]
    [SerializeField] private float _targetBaseDistance = 4f;
    [Tooltip("Extra pull-back per metre of player-to-enemy gap, so both stay framed.")]
    [SerializeField] private float _targetDistanceScale = 0.3f;
    [Tooltip("Maximum locked camera distance (m), so a far enemy doesn't zoom the camera out forever.")]
    [SerializeField] private float _targetMaxDistance = 9f;
    [Range(0.5f, 1f)]
    [Tooltip("Where the camera looks between player (0.5) and enemy (1). Higher centres the enemy more.")]
    [SerializeField] private float _enemyBias = 0.65f;
    [Tooltip("Seconds to blend between free and target framing when you toggle lock-on.")]
    [SerializeField] private float _lockTransition = 0.2f;

    [Header("Framing")]
    [Tooltip("Offset from the player the camera aims at (chest/head height, not feet).")]
    [SerializeField] private Vector3 _targetOffset = new Vector3(0f, 1.5f, 0f);
    [Tooltip("How far behind the player the free-orbit camera sits (m).")]
    [SerializeField] private float _distance = 5f;

    [Header("Look Sensitivity")]
    [Tooltip("Horizontal orbit speed (deg/sec) at full stick deflection.")]
    [SerializeField] private float _yawSpeed = 200f;
    [Tooltip("Vertical orbit speed (deg/sec) at full stick deflection.")]
    [SerializeField] private float _pitchSpeed = 150f;
    [Tooltip("Invert the vertical look axis.")]
    [SerializeField] private bool _invertY = false;

    [Header("Pitch Limits")]
    [Tooltip("How far up the camera can look (degrees, negative = up).")]
    [SerializeField] private float _minPitch = -30f;
    [Tooltip("How far down the camera can look (degrees).")]
    [SerializeField] private float _maxPitch = 70f;

    [Header("Follow Smoothing")]
    [Tooltip("Smoothing time for the camera following the player. Higher = floatier/laggier.")]
    [SerializeField] private float _followSmoothTime = 0.05f;

    [Header("Rotation Smoothing")]
    [Tooltip("Smoothing time for the orbit rotation. 0 = instant/snappy turns, higher = smoother/laggier.")]
    [SerializeField] private float _rotationSmoothTime = 0.05f;

    [Header("Rotation Offset (Tilt)")]
    [Tooltip("Constant view tilt. X = forward/back, Y = turn left/right, Z = bank (tilt) left/right.")]
    [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

    private float _yaw;
    private float _pitch = 15f;          // slight downward starting tilt
    private float _currentYaw;           // smoothed yaw actually applied to the camera
    private float _currentPitch;         // smoothed pitch actually applied
    private float _yawVelocity;          // used by SmoothDampAngle
    private float _pitchVelocity;
    private Vector3 _currentTargetPos;
    private Vector3 _followVelocity;     // used by SmoothDamp
    private float _lockBlend;            // 0 = free framing, 1 = target framing
    private Vector3 _lastTargetPos;      // cached target-mode pose so we can blend out cleanly
    private Quaternion _lastTargetRot = Quaternion.identity;

    private void Start()
    {
        // Seed values so the camera doesn't snap on the first frame.
        if (_target != null) _currentTargetPos = _target.position + _targetOffset;
        _yaw = transform.eulerAngles.y;
        _currentYaw = _yaw;
        _currentPitch = _pitch;
    }

    private void LateUpdate()
    {
        if (_target == null) return;
        float dt = Time.deltaTime;

        bool locked = _player != null && _player.IsLockedOn && _player.CurrentTarget != null;
        _lockBlend = Mathf.MoveTowards(_lockBlend, locked ? 1f : 0f, dt / Mathf.Max(_lockTransition, 0.0001f));

        // ---------- FREE framing (unchanged behaviour) ----------
        // Only feed the right stick into yaw/pitch while free has weight, so it doesn't drift when locked.
        if (_lockBlend < 1f)
        {
            Vector2 look = _input.LookInput;
            _yaw += look.x * _yawSpeed * dt;
            float pitchDelta = (_invertY ? look.y : -look.y) * _pitchSpeed * dt;
            _pitch = Mathf.Clamp(_pitch + pitchDelta, _minPitch, _maxPitch);
        }

        Vector3 desiredTargetPos = _target.position + _targetOffset;
        _currentTargetPos = Vector3.SmoothDamp(
            _currentTargetPos, desiredTargetPos, ref _followVelocity, _followSmoothTime);

        _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _yaw, ref _yawVelocity, _rotationSmoothTime);
        _currentPitch = Mathf.SmoothDampAngle(_currentPitch, _pitch, ref _pitchVelocity, _rotationSmoothTime);

        Quaternion orbitRotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        Vector3 freePos = _currentTargetPos - orbitRotation * Vector3.forward * _distance;
        Quaternion freeRot = orbitRotation * Quaternion.Euler(_rotationOffset);

        // ---------- TARGET framing ----------
        if (locked) ComputeTargetFraming(out _lastTargetPos, out _lastTargetRot);

        // ---------- blend & apply ----------
        if (_lockBlend <= 0f)
        {
            transform.SetPositionAndRotation(freePos, freeRot);
        }
        else
        {
            Vector3 pos = Vector3.Lerp(freePos, _lastTargetPos, _lockBlend);
            Quaternion rot = Quaternion.Slerp(freeRot, _lastTargetRot, _lockBlend);
            transform.SetPositionAndRotation(pos, rot);
        }

        // Keep free orbit angles aligned with the live camera while locked, so unlocking
        // resumes from behind the player rather than the pre-lock angle.
        if (locked)
        {
            Vector3 e = transform.eulerAngles;
            _yaw = _currentYaw = e.y;
            _pitch = _currentPitch = NormalizePitch(e.x);
        }
    }

    private void ComputeTargetFraming(out Vector3 pos, out Quaternion rot)
    {
        Vector3 player = _target.position + _targetOffset;
        Vector3 enemy = _player.CurrentTarget.AimPoint.position;

        Vector3 flat = enemy - player; flat.y = 0f;
        float gap = flat.magnitude;
        Vector3 dir = gap > 0.001f ? flat / gap : _target.forward;

        // Behind the player (opposite the enemy), pulled back with the gap but capped.
        float dist = Mathf.Min(_targetBaseDistance + gap * _targetDistanceScale, _targetMaxDistance);
        pos = player - dir * dist + Vector3.up * _targetCamHeight;

        // Look at a point biased toward the enemy so they sit more centred than the player.
        Vector3 lookPoint = Vector3.Lerp(player, enemy, _enemyBias);
        rot = Quaternion.LookRotation((lookPoint - pos).normalized, Vector3.up);
    }

    private static float NormalizePitch(float eulerX) => eulerX > 180f ? eulerX - 360f : eulerX;
}