using UnityEngine;
using Cinemachine;
using System;

/// <summary>
/// The shared context for every state. Holds all references and runtime state,
/// exposes movement/gravity helpers, and is the ONLY place that calls
/// CharacterController.Move — exactly once per frame, after the machine ticks.
/// States write their intent (planar velocity, vertical velocity) into here;
/// they never move the capsule themselves.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ScriptableObject that broadcasts input events and exposes the sticks.")]
    [SerializeField] private InputReader _input;
    [Tooltip("The Main Camera's transform (the Cinemachine Brain output). Free movement is taken relative to where it faces.")]
    [SerializeField] private Transform _cameraTransform;
    [Tooltip("Animator that plays the movement clips; crossfaded from code per state.")]
    private Animator _animator;
    private CharacterController _cc;
    public bool isReinforced = false;

    public event System.Action<bool> ReinforceChanged;

    // ----- Cursed energy runtime state -----
    public float CurrentEnergy { get; private set; }
    public float MaxEnergy => _energyStats.maxEnergy;
    public event System.Action<float, float> EnergyChanged;   // (current, max) — UI subscribes to this

    private void OnReinforce()
    {
        // Can't switch reinforcement ON with an empty tank; switching OFF is always allowed.
        if (!isReinforced && CurrentEnergy <= 0f) return;
        SetReinforced(!isReinforced);
    }

    /// Central place that flips reinforcement and fires the event — used by the input
    /// toggle above, and by the energy drain in Update() when the reserve hits empty.
    private void SetReinforced(bool value)
    {
        if (isReinforced == value) return;
        isReinforced = value;
        ReinforceChanged?.Invoke(isReinforced);   // fires with the NEW value, already flipped
    }

    [Header("Cinemachine")]
    [Tooltip("Free-movement virtual camera (over-shoulder orbit).")]
    [SerializeField] private CinemachineVirtualCamera _freeVCam;
    [Tooltip("Target-mode virtual camera (behind player, frames the enemy).")]
    [SerializeField] private CinemachineVirtualCamera _targetVCam;
    [Tooltip("Priority given to the active vcam; the inactive one sits at 0.")]
    [SerializeField] private int _activeCameraPriority = 20;

    [Header("Movement Stats")]
    [Tooltip("Authored base/reinforced tuning for this character's movement (walk/run speed, jump height, dash speed & duration).")]
    [SerializeField] private MovementStats _stats;

    [Header("Cursed Energy Stats")]
    [Tooltip("Authored cursed energy tuning (max reserve, drain rate while reinforced).")]
    [SerializeField] private CursedEnergyStats _energyStats;

    [Header("Gravity")]
    [Tooltip("Gravity in m/s^2 (negative). More negative = heavier, snappier falls.")]
    [SerializeField] private float _gravity = -20f;
    [Tooltip("Constant small downward speed while grounded so isGrounded stays stable on steps/slopes.")]
    [SerializeField] private float _groundedStick = -2f;

    [Header("Left stick input")]
    [Range(0f, 1f)]
    [Tooltip("Stick push (0-1) at which walking switches to running.")]
    [SerializeField] private float _runThreshold = 0.7f;
    [Tooltip("Stick magnitude below which input counts as no input.")]
    [SerializeField] private float _moveDeadzone = 0.1f;

    [Header("Free movement - Air Control")]
    [Range(0f, 1f)]
    [Tooltip("How much you can steer mid-air. 0 = locked to takeoff trajectory, 1 = full steering.")]
    [SerializeField] private float _airControl = 0.5f;


    [Header("Landing")]
    [Tooltip("How long the neutral landing recovery holds before returning to idle (seconds).")]
    [SerializeField] private float _landDuration = 0.25f;

    [Header("Stopping")]
    
    [Tooltip("How long the walk-to-stop animation holds before settling into Idle (seconds).")]
    [SerializeField] private float _stopWalkDuration = 0.15f;
    [Tooltip("How long the run-to-stop animation holds before settling into Idle (seconds).")]
    [SerializeField] private float _stopRunDuration = 0.3f;

    [Header("Target Movement - Locomotion")]
    [Tooltip("Run-only movement speed (m/s) while locked on.")]
    [SerializeField] private float _t_RunSpeed = 6f;
    [Tooltip("Closest you can orbit the target (m). Stops rushing through it and the fast spin up close.")]
    [SerializeField] private float _minTargetDistance = 2f;
    [Range(0f, 1f)]
    [Tooltip("Right-stick flick past this magnitude switches to the target in that direction.")]
    [SerializeField] private float _targetSwitchDeadzone = 0.7f;
    [Tooltip("Auto-drop the lock when the target gets further away than this (m).")]
    [SerializeField] private float _lockBreakRange = 15f;
    [Tooltip("After pressing dash with no direction held, how long to wait for a stick flick before defaulting to a forward dash (seconds).")]
    [SerializeField] private float _targetDashWindow = 0.12f;

    [Header("Animation State Names")]
    [Tooltip("Free-movement (idle/walk/run) blend tree state. Must match the Animator state name exactly.")]
    [SerializeField] private string _locomotionState = "Free Movement";
    [Tooltip("Jump (rising) state name.")]
    [SerializeField] private string _jumpState = "Jump";
    [Tooltip("Fall (descending) state name.")]
    [SerializeField] private string _fallState = "Fall";
    [Tooltip("Dash state name.")]
    [SerializeField] private string _dashState = "Dash";
    [Tooltip("Landing recovery state name.")]
    [SerializeField] private string _landState = "Land";
    [Tooltip("Walk-to-stop state name.")]
    [SerializeField] private string _stopWalkState = "Stop Walk";
    [Tooltip("Run-to-stop state name.")]
    [SerializeField] private string _stopRunState = "Stop Run";
    [Tooltip("Target-movement 2D blend tree state name (played while locked on).")]
    [SerializeField] private string _targetMoveState = "Target Movement";
    [Tooltip("Target dash blend tree state name (forward/back/left/right dash).")]
    [SerializeField] private string _targetDashState = "Target Dash";

    [Header("Target Blend Tree")]
    [Tooltip("Animator float param driving the strafe (X) axis of the target 2D blend tree.")]
    [SerializeField] private string _targetBlendX = "TargetX";
    [Tooltip("Animator float param driving the forward/back (Y) axis of the target 2D blend tree.")]
    [SerializeField] private string _targetBlendY = "TargetY";
    [Tooltip("Snap input to the nearest of 8 directions (crisp 8-way) instead of analog blending.")]
    [SerializeField] private bool _snapTo8Directions = false;
    [Tooltip("Smoothing time for the target blend params. 0 = snappy direction changes, higher = smoother.")]
    [SerializeField] private float _targetBlendDamp = 0.1f;

    // ----- Runtime state, read by states -----
    public Vector2 MoveInput { get; private set; }
    public float MoveMagnitude { get; private set; }
    public bool JumpRequested { get; private set; }
    public bool DashRequested { get; private set; }
    public bool IsGrounded => _cc.isGrounded;
    public float VerticalVelocity => _verticalVelocity;



    // ----- Tuning accessors, read by states -----
    // Resolved live off the MovementStats asset + the current isReinforced flag, so
    // toggling reinforcement takes effect immediately with no per-frame caching needed.
    public float WalkSpeed => _stats.walkSpeed;
    public float RotationSpeed => _stats.rotationSpeed.Get(isReinforced);
    public float AirRotationSpeed => _stats.rotationSpeed.Get(isReinforced);
    public float RunSpeed => _stats.runSpeed.Get(isReinforced);
    public float RunThreshold => _runThreshold;
    public float MoveDeadzone => _moveDeadzone;
    public float DashCooldown => _stats.dashCoolDown.Get(isReinforced);
    public float DashSpeed => _stats.dashSpeed.Get(isReinforced);
    public float DashDuration => _stats.dashDuration.Get(isReinforced);
    public float TargetDashWindow => _targetDashWindow;
    public float LandDuration => _landDuration;
    public float StopWalkDuration => _stopWalkDuration;
    public float StopRunDuration => _stopRunDuration;
    public bool CanDash => Time.time >= _dashCooldownEndTime;

    // Set true by Airborne on touchdown; consumed by Grounded.Enter to play the landing recovery.
    public bool JustLanded { get; set; }

    // ----- The state tree -----
    public StateMachine RootMachine { get; private set; }
    public GroundedState Grounded { get; private set; }
    public AirborneState Airborne { get; private set; }
    public TargetGroundedState TargetGrounded { get; private set; }

    // ----- Lock-on state, read by the target state and the camera -----
    public bool IsLockedOn => _isLockedOn;
    public Targetable CurrentTarget => _currentTarget;

    private float _verticalVelocity;
    private Vector3 _planarVelocity;
    private Vector3 _airVelocity;          // horizontal velocity preserved while airborne
    private Vector3 _lastPlanarVelocity;   // last frame's applied horizontal velocity (seed for air momentum)
    private float _dashCooldownEndTime;
    private int _currentAnimHash;          // animator state we last crossfaded to
    private float _moveBlendTarget;        // 0 = idle, 0.5 = walk, 1 = run — drives the locomotion blend tree
    private int _targetBlendXHash;
    private int _targetBlendYHash;

    private Targetable _currentTarget;
    private bool _isLockedOn;
    private bool _lockOnPressed;           // set by the LockOn event, consumed in UpdateLockOn
    private bool _switchArmed = true;      // edge-trigger guard for directional target switching

    public const float WalkBlend = 0.5f;   // walk clip's fixed position in the blend tree
    public const float RunBlend = 1f;      // run clip's fixed position in the blend tree

    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _targetBlendXHash = Animator.StringToHash(_targetBlendX);
        _targetBlendYHash = Animator.StringToHash(_targetBlendY);

        if (_stats == null)
            Debug.LogError($"[PlayerController] No MovementStats assigned on {name}. Movement speeds will all read as 0.", this);

        if (_energyStats == null)
            Debug.LogError($"[PlayerController] No CursedEnergyStats assigned on {name}. Energy will read as 0.", this);
        else
            CurrentEnergy = _energyStats.maxEnergy;   // start full

        RootMachine = new StateMachine();
        Grounded = new GroundedState(this);
        Airborne = new AirborneState(this);
        TargetGrounded = new TargetGroundedState(this);
    }

    private void OnEnable()
    {
        _input.JumpEvent += OnJump;
        _input.DashEvent += OnDash;
        _input.LockOnEvent += OnLockOnPressed;
        _input.ReinforceEvent += OnReinforce;
    }

    private void OnDisable()
    {
        _input.JumpEvent -= OnJump;
        _input.DashEvent -= OnDash;
        _input.LockOnEvent -= OnLockOnPressed;
        _input.ReinforceEvent -= OnReinforce;
    }

    private void Start()
    {
        _animator = GetComponent<Animator>();
        RootMachine.ChangeState(IsGrounded ? Grounded : Airborne);
        SetCameraMode(false);   // begin in free-camera mode
    }

    private void Update()
    {
        PassiveEnergyRecovery();

        float dt = Time.deltaTime;

        // 1. Read input for this frame.
        MoveInput = _input.MoveInput;
        MoveMagnitude = MoveInput.magnitude;

        // 1b. Lock-on: handle toggle, target switching, auto-break, re-acquire, mode swap.
        UpdateLockOn();

        // 1c. Cursed energy: drain while reinforced, force reinforcement off when empty.
        UpdateCursedEnergy(dt);

        // 2. Planar velocity is rebuilt fresh each frame (snappy: release = instant stop).
        _planarVelocity = Vector3.zero;

        // 3. Run the machine — states set planar/vertical velocity via the helpers below.
        RootMachine.Tick(dt);

        // Drive the locomotion blend tree toward the current tier (0 idle / 0.5 walk / 1 run).
        // Tier-based, NOT raw speed — so the blend tree thresholds stay fixed at 0 / 0.5 / 1 and
        // you never touch the Animator when tuning Walk/Run Speed. Ignored by Jump/Fall/Dash.
        if (_animator != null)
            _animator.SetFloat(SpeedHash, _moveBlendTarget, 0.1f, dt);

        // 4. One Move, combining planar + vertical.
        Vector3 motion = _planarVelocity + Vector3.up * _verticalVelocity;
        _cc.Move(motion * dt);
        _lastPlanarVelocity = _planarVelocity;   // remember horizontal speed for air momentum

        // 5. Clear single-frame input requests. (Timed input buffering replaces this later.)
        JumpRequested = false;
        DashRequested = false;

    }


    // ----- Helpers states call -----

    /// Camera-relative world direction from a 2D stick input. Magnitude is preserved
    /// (so stick tilt can drive speed), not normalized.
    public Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        Vector3 forward = _cameraTransform.forward;
        Vector3 right = _cameraTransform.right;
        forward.y = 0f; right.y = 0f;
        forward.Normalize(); right.Normalize();
        return forward * input.y + right * input.x;
    }

    public void RotateTowards(Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, RotationSpeed * dt);
    }

    public void RotateTowardsInAir(Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, AirRotationSpeed * dt);
    }

    public void SetPlanarVelocity(Vector3 velocity) => _planarVelocity = velocity;

    /// Sets the locomotion blend tier (0 idle / 0.5 walk / 1 run). Damped onto the animator in Update.
    public void SetMoveBlend(float target) => _moveBlendTarget = target;

    /// Called when leaving the ground: carry our horizontal speed into the air so jumps
    /// (and jump-canceled dashes) keep their momentum.
    public void SeedAirVelocity()
    {
        _airVelocity = _lastPlanarVelocity;
        _planarVelocity = _airVelocity;   // also apply on this transition frame
    }

    /// Airborne horizontal movement. Preserves momentum and lets the stick steer it,
    /// with Air Control deciding how quickly you can redirect (0 = none, 1 = very responsive).
    public void ApplyAirMovement(float dt)
    {
        if (MoveMagnitude > _moveDeadzone)
        {
            Vector3 dir = GetCameraRelativeDirection(MoveInput).normalized;
            RotateTowardsInAir(dir, dt);
            Vector3 desired = dir * (RunSpeed * Mathf.Clamp01(MoveMagnitude));
            float t = 1f - Mathf.Exp(-_airControl * 12f * dt);   // framerate-independent steering
            _airVelocity = Vector3.Lerp(_airVelocity, desired, t);
        }
        // Stick neutral → _airVelocity is left untouched (momentum carries).
        SetPlanarVelocity(_airVelocity);
    }

    // Crossfade to a state using the Inspector-configured names, so renaming an Animator
    // state is a one-field change here instead of a silent break.
    public void PlayLocomotion() => PlayAnimation(_locomotionState);
    public void PlayJump()       => PlayAnimation(_jumpState);
    public void PlayFall()       => PlayAnimation(_fallState);
    public void PlayDash()       => PlayAnimation(_dashState);
    public void PlayLand()       => PlayAnimation(_landState);
    public void PlayStopWalk()   => PlayAnimation(_stopWalkState);
    public void PlayStopRun()    => PlayAnimation(_stopRunState);
    public void PlayTargetMove() => PlayAnimation(_targetMoveState);
    public void PlayTargetDash() => PlayAnimation(_targetDashState);

    /// Crossfade the animator to a state by name. No-ops if that state is already playing,
    /// so the code state machine is the single source of truth — no animator transitions needed.
    private void PlayAnimation(string stateName, float fade = 0.1f)
    {
        if (_animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (hash == _currentAnimHash) return;
        _currentAnimHash = hash;
        _animator.CrossFadeInFixedTime(hash, fade);
    }

    /// Direction a dash should travel: the stick direction if held, otherwise straight ahead.
    public Vector3 GetDashDirection()
    {
        if (MoveMagnitude > _moveDeadzone)
            return GetCameraRelativeDirection(MoveInput).normalized;
        return transform.forward;
    }

    /// Instantly face a direction (used so a dash visibly snaps to its travel direction).
    public void SnapFacing(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(direction);
    }

    public void BeginDashCooldown() => _dashCooldownEndTime = Time.time + DashCooldown;

    public void ApplyGroundedVertical()
    {
        if (_verticalVelocity < 0f) _verticalVelocity = _groundedStick;
    }

    public void ApplyJumpImpulse()
    {
        float jumpHeight = _stats.jumpHeight.Get(isReinforced);
        _verticalVelocity = Mathf.Sqrt(2f * -_gravity * jumpHeight);
    }

    public void ApplyGravity(float dt) => _verticalVelocity += _gravity * dt;

    // ===== Cursed energy =====

    /// Drains the reserve while reinforced and forces reinforcement off the instant it
    /// hits empty. No regen yet — once it's spent, it stays spent until that's wired up.
    private void UpdateCursedEnergy(float dt)
    {
        if (_energyStats == null || !isReinforced) return;

        SetEnergy(CurrentEnergy - _energyStats.drainPerSecond * dt);
        if (CurrentEnergy <= 0f)
            SetReinforced(false);
            
    }
    public void PassiveEnergyRecovery()
    {
        if (_energyStats == null || isReinforced) return;
        
        SetEnergy(CurrentEnergy + _energyStats.recoveryPerSecond * Time.deltaTime);
    }

    private void SetEnergy(float value)
    {
        CurrentEnergy = Mathf.Clamp(value, 0f, MaxEnergy);
        EnergyChanged?.Invoke(CurrentEnergy, MaxEnergy);
    }

    // ===== Lock-on / target mode =====

    private void OnLockOnPressed() => _lockOnPressed = true;

    private void UpdateLockOn()
    {
        // Toggle
        if (_lockOnPressed)
        {
            _lockOnPressed = false;
            if (_isLockedOn) ExitTargetMode();
            else TryEnterTargetMode();
        }

        if (!_isLockedOn) return;

        // Target despawned/disabled → re-acquire the nearest enemy, or drop to free.
        if (_currentTarget == null || !_currentTarget.isActiveAndEnabled)
        {
            _currentTarget = AcquireNearestEnemy();
            if (_currentTarget == null) { ExitTargetMode(); return; }
            SetCameraMode(true);   // re-point the target camera at the new target
        }

        // Auto-break when the target gets too far.
        if (Vector3.Distance(transform.position, _currentTarget.transform.position) > _lockBreakRange)
        {
            ExitTargetMode();
            return;
        }

        // Directional flick of the right stick switches target (edge-triggered).
        HandleTargetSwitch();
    }

    private void TryEnterTargetMode()
    {
        if (!IsGrounded) return;                  // lock on only while grounded
        Targetable t = AcquireNearestEnemy();
        if (t == null) return;                    // no valid target → stay in free movement
        _currentTarget = t;
        _isLockedOn = true;
        SetCameraMode(true);                      // blend to the target camera
        RootMachine.ChangeState(TargetGrounded);
    }

    /// Drops the lock and returns to free movement.
    public void ExitTargetMode()
    {
        _isLockedOn = false;
        _currentTarget = null;
        SetCameraMode(false);                     // blend back to the free camera
        if (RootMachine.Current == TargetGrounded)
            RootMachine.ChangeState(Grounded);
    }

    /// Swaps which Cinemachine vcam is live. The Brain's Default Blend handles the transition.
    private void SetCameraMode(bool locked)
    {
        if (_freeVCam != null)   _freeVCam.Priority   = locked ? 0 : _activeCameraPriority;
        if (_targetVCam != null) _targetVCam.Priority = locked ? _activeCameraPriority : 0;

        // Point the target vcam at the current enemy so it frames them.
        if (locked && _targetVCam != null && _currentTarget != null)
            _targetVCam.LookAt = _currentTarget.AimPoint;
    }

    private Targetable AcquireNearestEnemy()
    {
        Targetable best = null;
        float bestSqr = float.MaxValue;
        foreach (var t in Targetable.Active)
        {
            if (t == null || t.Team != Team.Enemy) continue;
            float d = (t.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = t; }
        }
        return best;
    }

    private void HandleTargetSwitch()
    {
        Vector2 flick = _input.LookInput;
        if (flick.magnitude < _targetSwitchDeadzone) { _switchArmed = true; return; }
        if (!_switchArmed) return;
        _switchArmed = false;

        // Map the flick into world space using the camera, then pick the enemy best
        // aligned with that direction.
        Vector3 right = _cameraTransform.right; right.y = 0f; right.Normalize();
        Vector3 fwd = _cameraTransform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 flickWorld = (right * flick.x + fwd * flick.y).normalized;

        Targetable best = null;
        float bestDot = 0.2f; // require reasonable alignment with the flick
        foreach (var t in Targetable.Active)
        {
            if (t == null || t.Team != Team.Enemy || t == _currentTarget) continue;
            Vector3 to = t.transform.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.01f) continue;
            float dot = Vector3.Dot(to.normalized, flickWorld);
            if (dot > bestDot) { bestDot = dot; best = t; }
        }
        if (best != null)
        {
            _currentTarget = best;
            SetCameraMode(true);   // re-frame the new target
        }
    }

    /// Snap to face the current target (horizontal only).
    public void FaceTarget()
    {
        if (_currentTarget == null) return;
        Vector3 dir = _currentTarget.AimPoint.position - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    /// 8-directional movement relative to the target: stick Y = toward/away, stick X = strafe.
    /// Run-only at a fixed speed; the approach clamp stops you pushing inside the min orbit distance.
    public void ApplyTargetMovement(float dt)
    {
        if (_currentTarget == null || MoveMagnitude <= _moveDeadzone) { SetPlanarVelocity(Vector3.zero); return; }

        Vector3 toTarget = _currentTarget.AimPoint.position - transform.position; toTarget.y = 0f;
        Vector3 forward = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 move = forward * MoveInput.y + right * MoveInput.x;
        if (move.sqrMagnitude < 0.0001f) { SetPlanarVelocity(Vector3.zero); return; }

        SetPlanarVelocity(ClampApproachToTarget(move.normalized * _t_RunSpeed));
    }

    /// Drives the target 2D blend tree. Since the player faces the target, the stick maps
    /// straight to the blend axes. Snap mode quantises to the nearest of 8 directions; the
    /// damp time controls how snappy vs smooth the transitions between directions are.
    public void UpdateTargetAnimation(float dt)
    {
        if (_animator == null) return;

        Vector2 dir = Vector2.zero;
        if (MoveMagnitude > _moveDeadzone)
        {
            dir = MoveInput.normalized;
            if (_snapTo8Directions) dir = SnapToOcto(dir);
        }
        _animator.SetFloat(_targetBlendXHash, dir.x, _targetBlendDamp, dt);
        _animator.SetFloat(_targetBlendYHash, dir.y, _targetBlendDamp, dt);
    }

    private static Vector2 SnapToOcto(Vector2 v)
    {
        float step = Mathf.PI / 4f;                       // 45 degrees
        float angle = Mathf.Round(Mathf.Atan2(v.x, v.y) / step) * step;
        return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
    }

    /// Nearest of the 4 target-relative cardinals (x = strafe, y = toward/away) to a stick vector.
    public Vector2 ResolveDashCardinal(Vector2 stick)
    {
        if (Mathf.Abs(stick.y) >= Mathf.Abs(stick.x))
            return new Vector2(0f, Mathf.Sign(stick.y));
        return new Vector2(Mathf.Sign(stick.x), 0f);
    }

    /// Converts a target-relative direction (x = strafe, y = toward/away) into a world direction.
    public Vector3 TargetRelativeToWorld(Vector2 dir)
    {
        if (_currentTarget == null) return transform.forward;
        Vector3 toTarget = _currentTarget.AimPoint.position - transform.position; toTarget.y = 0f;
        Vector3 forward = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        return (forward * dir.y + right * dir.x).normalized;
    }

    /// Removes any toward-target component of a velocity when inside the min orbit distance, so
    /// neither movement nor a forward dash can push through the target.
    public Vector3 ClampApproachToTarget(Vector3 velocity)
    {
        if (_currentTarget == null) return velocity;
        Vector3 toTarget = _currentTarget.AimPoint.position - transform.position; toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance > _minTargetDistance || distance < 0.0001f) return velocity;
        Vector3 forward = toTarget / distance;
        float toward = Vector3.Dot(velocity, forward);
        if (toward > 0f) velocity -= forward * toward;
        return velocity;
    }

    /// Sets the target blend params instantly (no damp) — used to pick the dash-direction clip.
    public void SetTargetBlendInstant(Vector2 dir)
    {
        if (_animator == null) return;
        _animator.SetFloat(_targetBlendXHash, dir.x);
        _animator.SetFloat(_targetBlendYHash, dir.y);
    }

    // ----- Input callbacks (set request flags consumed within the same frame) -----
    private void OnJump() => JumpRequested = true;
    private void OnDash() => DashRequested = true;
}