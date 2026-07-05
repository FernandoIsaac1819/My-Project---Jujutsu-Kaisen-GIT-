using UnityEngine;

/// <summary>
/// Airborne superstate. Applies gravity (shared) and owns the landing transition.
/// On entry it picks Jump or Fall based on whether we're rising or falling.
/// </summary>
public class AirborneState : SuperState
{
    public JumpState Jump { get; }
    public FallState Fall { get; }

    public AirborneState(PlayerController controller) : base(controller)
    {
        Jump = new JumpState(controller, this);
        Fall = new FallState(controller, this);
    }

    public override void Enter()
    {
        Debug.Log("[State] Airborne");
        controller.SeedAirVelocity();
        subMachine.ChangeState(controller.VerticalVelocity > 0f ? (State)Jump : Fall);
    }

    public override void Tick(float dt)
    {
        controller.ApplyGravity(dt);   // shared airborne logic

        // --- cross-cutting transition: landed ---
        if (controller.IsGrounded && controller.VerticalVelocity <= 0f)
        {
            controller.JustLanded = true;   // tell Grounded to play the landing recovery
            controller.RootMachine.ChangeState(controller.Grounded);
            return;
        }

        controller.ApplyAirMovement(dt);   // momentum + Air Control steering

        subMachine.Tick(dt);
    }
}

public class JumpState : State
{
    private readonly AirborneState _airborne;
    public JumpState(PlayerController controller, AirborneState airborne) : base(controller) => _airborne = airborne;

    public override void Enter()
    {
        Debug.Log("[State]   Jump");
        controller.PlayJump();
    }

    public override void Tick(float dt)
    {
        // Once we stop rising, hand off to Fall.
        if (controller.VerticalVelocity <= 0f)
            _airborne.SubMachine.ChangeState(_airborne.Fall);
    }
}

public class FallState : State
{
    private readonly AirborneState _airborne;
    public FallState(PlayerController controller, AirborneState airborne) : base(controller) => _airborne = airborne;

    public override void Enter()
    {
        Debug.Log("[State]   Fall");
        controller.PlayFall();
    }

    public override void Tick(float dt)
    {
        // Landing is handled by the Airborne superstate. Nothing leaf-specific yet.
    }
}