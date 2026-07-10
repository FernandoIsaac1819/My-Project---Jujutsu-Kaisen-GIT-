using UnityEngine;

namespace RigidbodyMovement
{
    /// <summary>
    /// Grounded superstate. No manual "grounded stick" needed here — raycast-based grounding
    /// (unlike CharacterController's move-flag-based grounding) is a fresh geometric check
    /// every frame, not dependent on what the last movement call happened to do.
    /// </summary>
    public class RBGroundedState : RBSuperState
    {
        public RBIdleState Idle { get; }
        public RBLocomotionState Locomotion { get; }
        public RBStopWalkState StopWalk { get; }
        public RBStopRunState StopRun { get; }

        public RBGroundedState(RigidbodyPlayerController controller) : base(controller)
        {
            Idle = new RBIdleState(controller, this);
            Locomotion = new RBLocomotionState(controller, this);
            StopWalk = new RBStopWalkState(controller, this);
            StopRun = new RBStopRunState(controller, this);
        }

        public override void Enter()
        {
            Debug.Log("[RBState] Grounded");
            subMachine.ChangeState(Idle);
        }

        public override void Tick(float dt)
        {
            if (controller.JumpRequested)
            {
                controller.ApplyJumpImpulse();
                controller.RootMachine.ChangeState(controller.Airborne);
                return;
            }
            if (!controller.IsGrounded)
            {
                controller.RootMachine.ChangeState(controller.Airborne);
                return;
            }

            subMachine.Tick(dt);
        }
    }

    public class RBIdleState : RBState
    {
        private readonly RBGroundedState _grounded;
        public RBIdleState(RigidbodyPlayerController controller, RBGroundedState grounded) : base(controller) => _grounded = grounded;

        public override void Enter()
        {
            Debug.Log("[RBState]   Idle");
            controller.PlayLocomotion();   // idle is the 0 end of the same blend tree as walk/run
            controller.SetBlendSpeed(0f);
        }

        public override void Tick(float dt)
        {
            if (controller.MoveMagnitude > controller.MoveDeadzone)
                _grounded.SubMachine.ChangeState(_grounded.Locomotion);
        }
    }

    public class RBLocomotionState : RBState
    {
        private readonly RBGroundedState _grounded;
        // Which tier we were actually at last tick. MoveMagnitude is already back below the
        // deadzone by the time we decide to stop, so we can't recompute "running" from it in
        // that same moment — this remembers the tier from the last tick we were still moving.
        private bool _running;

        public RBLocomotionState(RigidbodyPlayerController controller, RBGroundedState grounded) : base(controller) => _grounded = grounded;

        public override void Enter()
        {
            Debug.Log("[RBState]   Locomotion");
            controller.PlayLocomotion();
        }

        public override void Tick(float dt)
        {
            // Stick released → hand off to the matching stop state instead of snapping to Idle.
            if (controller.MoveMagnitude <= controller.MoveDeadzone)
            {
                _grounded.SubMachine.ChangeState(_running ? (RBState)_grounded.StopRun : _grounded.StopWalk);
                return;
            }

            // Two fixed tiers, not a continuous blend: push past RunThreshold to run, otherwise
            // walk. Speed and animator blend position always move together, so the animation
            // and the actual movement speed never disagree — avoids foot-sliding.
            _running = controller.MoveMagnitude >= controller.RunThreshold;
            float speed = _running ? controller.RunSpeed : controller.WalkSpeed;
            controller.SetBlendSpeed(_running ? RigidbodyPlayerController.RunBlend : RigidbodyPlayerController.WalkBlend);
            controller.ApplyGroundedMovement(dt, speed);
        }
    }

    /// <summary>
    /// Shared logic for the walk/run stop states: play a stop animation, hold briefly, then
    /// settle into Idle. If the stick is pressed again before the animation finishes, cancel
    /// straight back into Locomotion — stopping is purely visual, never a responsiveness cost.
    /// Horizontal velocity is explicitly zeroed on entry so there's no residual slide.
    /// </summary>
    public abstract class RBStopStateBase : RBState
    {
        protected readonly RBGroundedState grounded;
        private float _timer;

        protected RBStopStateBase(RigidbodyPlayerController controller, RBGroundedState grounded) : base(controller) => this.grounded = grounded;

        protected abstract void PlayStopAnimation();
        protected abstract float Duration { get; }

        public override void Enter()
        {
            
            PlayStopAnimation();
            controller.StopHorizontalVelocity();
            _timer = Duration;
        }

        public override void Tick(float dt)
        {
            if (controller.MoveMagnitude > controller.MoveDeadzone)
            {
                grounded.SubMachine.ChangeState(grounded.Locomotion);
                return;
            }

            _timer -= dt;
            if (_timer <= 0f)
                grounded.SubMachine.ChangeState(grounded.Idle);
        }
    }

    public class RBStopWalkState : RBStopStateBase
    {
        public RBStopWalkState(RigidbodyPlayerController controller, RBGroundedState grounded) : base(controller, grounded) { }
        protected override void PlayStopAnimation()
        {
            Debug.Log("[RBState]   StopWalk");
            controller.PlayStopWalk();
        }
        protected override float Duration => controller.StopWalkDuration;
    }

    public class RBStopRunState : RBStopStateBase
    {
        public RBStopRunState(RigidbodyPlayerController controller, RBGroundedState grounded) : base(controller, grounded) { }
        protected override void PlayStopAnimation()
        {
            Debug.Log("[RBState]   StopRun");
            controller.PlayStopRun();
        }
        protected override float Duration => controller.StopRunDuration;
    }
}