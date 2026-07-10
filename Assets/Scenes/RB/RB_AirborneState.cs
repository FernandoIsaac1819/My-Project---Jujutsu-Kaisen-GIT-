using UnityEngine;

namespace RigidbodyMovement
{
    /// <summary>
    /// Airborne superstate. Vertical is Unity's own gravity (boosted by ApplyFallMultiplier
    /// once falling) — no manually tracked vertical velocity field needed, Rigidbody already
    /// owns that via linearVelocity.y.
    /// </summary>
    public class RBAirborneState : RBSuperState
    {
        public RBJumpState Jump { get; }
        public RBFallState Fall { get; }

        public RBAirborneState(RigidbodyPlayerController controller) : base(controller)
        {
            Jump = new RBJumpState(controller, this);
            Fall = new RBFallState(controller, this);
        }

        public override void Enter()
        {
            Debug.Log("[RBState] Airborne");
            controller.PlayAirborne();   // one crossfade — Jump/Fall are blend positions inside it, not separate states
            subMachine.ChangeState(controller.VerticalVelocity > 0f ? (RBState)Jump : Fall);
        }

        public override void Tick(float dt)
        {
            controller.ApplyFallMultiplier(dt);
            controller.ApplyAirControl(dt);

            if (controller.IsGrounded && controller.VerticalVelocity <= 0f)
            {
                controller.RootMachine.ChangeState(controller.Grounded);
                return;
            }

            subMachine.Tick(dt);
        }
    }

    public class RBJumpState : RBState
    {
        private readonly RBAirborneState _airborne;
        public RBJumpState(RigidbodyPlayerController controller, RBAirborneState airborne) : base(controller) => _airborne = airborne;

        public override void Enter() => Debug.Log("[RBState]   Jump");

        public override void Tick(float dt)
        {
            if (controller.VerticalVelocity <= 0f)
                _airborne.SubMachine.ChangeState(_airborne.Fall);
        }
    }

    public class RBFallState : RBState
    {
        public RBFallState(RigidbodyPlayerController controller, RBAirborneState airborne) : base(controller) { }

        public override void Enter() => Debug.Log("[RBState]   Fall");
    }
}