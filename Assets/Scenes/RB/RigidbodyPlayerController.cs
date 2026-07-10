using UnityEngine;

namespace RigidbodyMovement
{
    /// <summary>
    /// Rebuilt around a movement pattern from a proven working project, not the earlier
    /// root-motion-driven attempt. Locomotion is fully code-driven — input converted to
    /// local-space turn/forward amounts, applied via transform.Rotate + Rigidbody.MovePosition
    /// — same as the CharacterController version's philosophy, just on a physics body.
    ///
    /// Root motion is scoped to the two stop states only (see OnAnimatorMove) — everywhere
    /// else, including Idle, applies none. applyRootMotion itself stays permanently true and
    /// is never toggled per state; the decision of whether THIS frame's motion gets used lives
    /// entirely inside OnAnimatorMove, gated on the exact substate. Animation is otherwise
    /// entirely code-driven via explicit CrossFade calls (see PlayAnimation) instead of
    /// Animator Controller transitions/conditions — keeps the graph clean and avoids silent
    /// failures from a missing transition arrow, at the cost of C# owning state switching explicitly.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class RigidbodyPlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputReader _input;
        [SerializeField] private Transform _cameraTransform;
        [Tooltip("Auto-found in children on Start if left empty.")]
        [SerializeField] private Animator _animator;
        [Tooltip("Same MovementStats asset type as the CC project — reused as-is.")]
        [SerializeField] private MovementStats _stats;

        [Header("Left stick input")]
        [Tooltip("Stick magnitude below which input counts as no input at all (-> Idle).")]
        [SerializeField] private float _moveDeadzone = 0.1f;
        [Range(0f, 1f)]
        [Tooltip("Stick push (0-1) at which walking switches to running. Below this (but above the deadzone): Walk. At or above: Run.")]
        [SerializeField] private float _runThreshold = 0.8f;

        [Header("Ground Check")]
        [Tooltip("How far below the character to check for ground.")]
        [SerializeField] private float _groundCheckDistance = 0.15f;
        [SerializeField] private LayerMask _groundLayers;

        [Header("Air Control")]
        [Tooltip("Velocity change per second applied toward the input direction while airborne.")]
        [SerializeField] private float _airControlAmount = 5f;

        [Header("Fall")]
        [Tooltip("Extra downward acceleration multiplier while already falling. Unity's own " +
                 "gravity still runs the whole time; this adds on top of it once you're falling, " +
                 "for a snappier drop than rise — instead of reimplementing gravity from scratch.")]
        [SerializeField] private float _fallMultiplier = 2.5f;

        [Header("Animation State Names")]
        [Tooltip("Free-movement (idle/walk/run) blend tree state. Must match the Animator state name exactly.")]
        [SerializeField] private string _locomotionState = "Free Movement";
        [Tooltip("Airborne (jump/fall) blend tree state name.")]
        [SerializeField] private string _airborneState = "Airborne";
        [Tooltip("Walk-to-stop state name.")]
        [SerializeField] private string _stopWalkState = "Stop Walk";
        [Tooltip("Run-to-stop state name.")]
        [SerializeField] private string _stopRunState = "Stop Run";

        [Header("Stopping")]
        [Tooltip("How long the walk-to-stop animation holds before settling into Idle (seconds).")]
        [SerializeField] private float _stopWalkDuration = 0.15f;
        [Tooltip("How long the run-to-stop animation holds before settling into Idle (seconds).")]
        [SerializeField] private float _stopRunDuration = 0.3f;

        [Header("Animator Parameters")]
        [Tooltip("Float (0-1) driving the 1D walk blend tree.")]
        [SerializeField] private string _speedParam = "Speed";
        [Tooltip("Float feeding the airborne blend tree (vertical velocity).")]
        [SerializeField] private string _airSpeedParam = "AirbornSpeed";

        private Rigidbody _rb;
        private int _speedHash, _airSpeedHash;
        private int _currentAnimHash;      // animator state we last crossfaded to
        private bool _isGrounded;
        private Vector3 _groundNormal = Vector3.up;
        private float _speedBlendTarget;   // 0 = idle, 0.5 = walk, 1 = run — set by states, damped onto the animator below

        public Vector2 MoveInput { get; private set; }
        public float MoveMagnitude { get; private set; }
        public bool JumpRequested { get; private set; }
        public bool IsGrounded => _isGrounded;
        public float VerticalVelocity => _rb.linearVelocity.y;
        public float MoveDeadzone => _moveDeadzone;
        public float RunThreshold => _runThreshold;

        public const float WalkBlend = 0.5f;   // walk clip's fixed position in the blend tree
        public const float RunBlend = 1f;      // run clip's fixed position in the blend tree

        public float StopWalkDuration => _stopWalkDuration;
        public float StopRunDuration => _stopRunDuration;

        [Tooltip("Not wired to anything yet in this prototype — kept only so MovementStats.Get(bool) compiles.")]
        public bool isReinforced = false;

        public float WalkSpeed => _stats.walkSpeed;
        public float RunSpeed => _stats.runSpeed.Get(isReinforced);
        public float RotationSpeed => _stats.rotationSpeed.Get(isReinforced);

        public RBStateMachine RootMachine { get; private set; }
        public RBGroundedState Grounded { get; private set; }
        public RBAirborneState Airborne { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true;                                    // built-in gravity, boosted on the way down — see ApplyFallMultiplier
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Freeze Rotation X and Z on the Rigidbody in the Inspector. Y is left free —
            // rotation is driven directly via transform.Rotate below, matching the reference.

            _speedHash = Animator.StringToHash(_speedParam);
            _airSpeedHash = Animator.StringToHash(_airSpeedParam);

            if (_stats == null)
                Debug.LogError($"[RigidbodyPlayerController] No MovementStats assigned on {name}.", this);

            RootMachine = new RBStateMachine();
            Grounded = new RBGroundedState(this);
            Airborne = new RBAirborneState(this);
        }

        private void Start()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator != null) _animator.applyRootMotion = true;   // permanently on — see OnAnimatorMove for why this must never be toggled

            CheckGrounded();
            RootMachine.ChangeState(_isGrounded ? (RBState)Grounded : Airborne);
        }

        private void OnEnable() => _input.JumpEvent += OnJump;
        private void OnDisable() => _input.JumpEvent -= OnJump;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            MoveInput = _input.MoveInput;
            MoveMagnitude = MoveInput.magnitude;

            CheckGrounded();
            RootMachine.Tick(dt);
            UpdateAnimatorParams();

            JumpRequested = false;
        }

        private void CheckGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _groundCheckDistance + 0.1f, _groundLayers))
            {
                _isGrounded = true;
                _groundNormal = hit.normal;
            }
            else
            {
                _isGrounded = false;
                _groundNormal = Vector3.up;
            }
        }

        /// Converts camera-relative world input into local-space turn/forward amounts and
        /// applies both — rotating to steer, moving forward by however much of the input is
        /// actually aligned with current facing. The character gradually turns into new
        /// directions rather than snapping instantly, matching the reference's feel.
        public void ApplyGroundedMovement(float dt, float speed)
        {
            // Normalized — magnitude already did its job deciding walk vs run in the state above.
            // From here only DIRECTION matters; without this, actual movement speed would also
            // scale with how far the stick is pushed even within a fixed tier, fighting the
            // fixed-speed animation (exactly the "sliding while walking" symptom).
            Vector3 worldInput = GetCameraRelativeDirection(MoveInput).normalized;
            Vector3 local = transform.InverseTransformDirection(worldInput);
            local = Vector3.ProjectOnPlane(local, _groundNormal);   // slope-aware, matches the reference

            float turnAmount = Mathf.Atan2(local.x, local.z);
            float forwardAmount = local.z;

            // Matches the reference's exact formula (including its radians/degrees mixing) —
            // RotationSpeed is tuned by feel against THIS formula, not a literal deg/sec rate.
            transform.Rotate(0f, turnAmount * RotationSpeed * dt, 0f);

            Vector3 worldMove = transform.forward * forwardAmount * speed * dt;
            _rb.MovePosition(_rb.position + worldMove);
        }

        public void ApplyAirControl(float dt)
        {
            if (MoveMagnitude <= _moveDeadzone) return;
            Vector3 dir = GetCameraRelativeDirection(MoveInput).normalized;
            _rb.AddForce(dir * (_airControlAmount * dt), ForceMode.VelocityChange);
        }

        public void ApplyFallMultiplier(float dt)
        {
            if (_rb.linearVelocity.y < 0f)
                _rb.linearVelocity += Vector3.up * Physics.gravity.y * (_fallMultiplier - 1f) * dt;
        }

        public void ApplyJumpImpulse()
        {
            float jumpHeight = _stats.jumpHeight.Get(isReinforced);
            float jumpSpeed = Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight);
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, jumpSpeed, _rb.linearVelocity.z);
        }

        /// The single, permanent home for applying root motion. Only StopWalk/StopRun actually
        /// want it — checking the SUBSTATE specifically, not just "is Grounded", matters: Idle
        /// and Locomotion are also Grounded, and gating on the superstate alone let Idle's own
        /// clip leak tiny root motion every frame on a previous attempt, causing the character
        /// to drift forever even while nominally standing still. Horizontal only — vertical
        /// stays fully owned by gravity/collision, so nothing here can fight that.
        private void OnAnimatorMove()
        {
            if (_animator == null) return;

            bool wantsRootMotion = RootMachine.Current == Grounded &&
                (Grounded.SubMachine.Current == Grounded.StopWalk || Grounded.SubMachine.Current == Grounded.StopRun);

            if (!wantsRootMotion) return;

            Vector3 delta = _animator.deltaPosition;
            _rb.MovePosition(_rb.position + new Vector3(delta.x, 0f, delta.z));
            _rb.MoveRotation(_rb.rotation * _animator.deltaRotation);
        }

        /// Sets which fixed tier (0/0.5/1) the animator should blend toward. States decide the
        /// tier explicitly rather than this reading raw stick magnitude — keeps the animation
        /// and the actual movement speed always in agreement, same reasoning as the CC version.
        public void SetBlendSpeed(float target) => _speedBlendTarget = target;

        // Crossfade to a state using the Inspector-configured names — same pattern as the CC
        // project's PlayerController. The Animator Controller no longer needs any transition
        // arrows or conditions for top-level switching; C# is the single source of truth for
        // which state is playing, and the graph only needs the blend trees themselves.
        public void PlayLocomotion() => PlayAnimation(_locomotionState);
        public void PlayAirborne()   => PlayAnimation(_airborneState);
        public void PlayStopWalk()   => PlayAnimation(_stopWalkState);
        public void PlayStopRun()    => PlayAnimation(_stopRunState);

        private void PlayAnimation(string stateName, float fade = 0.1f)
        {
            if (_animator == null) return;
            int hash = Animator.StringToHash(stateName);
            if (hash == _currentAnimHash) return;
            _currentAnimHash = hash;
            _animator.CrossFadeInFixedTime(hash, fade);
        }

        /// Zeroes horizontal velocity, preserving vertical. Rigidbody.MovePosition can leave a
        /// residual velocity behind once something stops calling it every frame (unlike the CC
        /// version, where planar velocity reset to zero automatically every frame) — the stop
        /// states call this explicitly so there's never any ambiguity about sliding on entry.
        public void StopHorizontalVelocity()
        {
            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(0f, v.y, 0f);
        }

        private void UpdateAnimatorParams()
        {
            if (_animator == null) return;
            _animator.SetFloat(_speedHash, _speedBlendTarget, 0.1f, Time.fixedDeltaTime);
            _animator.SetFloat(_airSpeedHash, _rb.linearVelocity.y);
        }

        public Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            Vector3 forward = _cameraTransform.forward;
            Vector3 right = _cameraTransform.right;
            forward.y = 0f; right.y = 0f;
            forward.Normalize(); right.Normalize();
            return forward * input.y + right * input.x;
        }

        private void OnJump() => JumpRequested = true;
    }
}