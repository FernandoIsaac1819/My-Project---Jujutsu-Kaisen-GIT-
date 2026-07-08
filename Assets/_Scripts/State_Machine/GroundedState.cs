using UnityEngine;

/// <summary>
/// Grounded superstate. Holds the shared grounded logic and the cross-cutting
/// transitions (jump, walk off a ledge, dash) so every grounded substate inherits
/// them — written once here, not in each leaf.
/// </summary>
public class GroundedState : SuperState
{
    public IdleState Idle { get; }
    public LocomotionState Locomotion { get; }
    public DashState Dash { get; }
    public LandState Land { get; }
    public StopWalkState StopWalk { get; }
    public StopRunState StopRun { get; }

    public GroundedState(PlayerController controller) : base(controller)
    {
        Idle = new IdleState(controller, this);
        Locomotion = new LocomotionState(controller, this);
        Dash = new DashState(controller, this);
        Land = new LandState(controller, this);
        StopWalk = new StopWalkState(controller, this);
        StopRun = new StopRunState(controller, this);
    }

    public override void Enter()
    {
        Debug.Log("[State] Grounded");
        // Play the landing recovery only when we actually dropped in from the air,
        // not on spawn. JustLanded is set by the Airborne state on touchdown.
        if (controller.JustLanded)
        {
            controller.JustLanded = false;
            subMachine.ChangeState(Land);
        }
        else
        {
            subMachine.ChangeState(Idle);
        }
    }

    public override void Tick(float dt)
    {
        controller.ApplyGroundedVertical();   // keep the capsule pinned to the floor

        // --- cross-cutting transitions (shared by ALL grounded substates) ---
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
        if (controller.DashRequested && controller.CanDash)
        {
            subMachine.ChangeState(Dash);     // dash from any grounded substate, if off cooldown
        }

        // --- run the active substate ---
        subMachine.Tick(dt);
    }
}

public class IdleState : State
{
    private readonly GroundedState _grounded;
    public IdleState(PlayerController controller, GroundedState grounded) : base(controller) => _grounded = grounded;

    public override void Enter()
    {
        Debug.Log("[State]   Idle");
        controller.PlayLocomotion();   // idle is the 0 end of the blend tree
        controller.SetMoveBlend(0f);
    }

    public override void Tick(float dt)
    {
        if (controller.MoveMagnitude > controller.MoveDeadzone)
            _grounded.SubMachine.ChangeState(_grounded.Locomotion);
    }
}

public class LocomotionState : State
{
    private readonly GroundedState _grounded;
    // Which tier we were actually at last tick. MoveMagnitude is already back below the
    // deadzone by the time we decide to stop, so we can't recompute "running" from it in
    // that same moment — this remembers the tier from the last tick we were still moving.
    private bool _running;

    public LocomotionState(PlayerController controller, GroundedState grounded) : base(controller) => _grounded = grounded;

    public override void Enter()
    {
        Debug.Log("[State]   Locomotion");
        controller.PlayLocomotion();
    }

    public override void Tick(float dt)
    {
        // Stick released → hand off to the matching stop state instead of snapping to Idle.
        if (controller.MoveMagnitude <= controller.MoveDeadzone)
        {
            _grounded.SubMachine.ChangeState(_running ? (State)_grounded.StopRun : _grounded.StopWalk);
            return;
        }

        // Direction the stick points, relative to where the camera faces.
        Vector3 direction = controller.GetCameraRelativeDirection(controller.MoveInput);

        // Turn to face that direction (smoothed by rotation speed).
        controller.RotateTowards(direction, dt);

        // Two fixed speed tiers: push past the run threshold to run, otherwise walk.
        // Fixed speeds (not magnitude-scaled) so each matches its animation — no foot sliding.
        _running = controller.MoveMagnitude >= controller.RunThreshold;
        controller.SetMoveBlend(_running ? PlayerController.RunBlend : PlayerController.WalkBlend);
        controller.SetPlanarVelocity(direction.normalized * (_running ? controller.RunSpeed : controller.WalkSpeed));
    }
}

public class DashState : State
{
    private readonly GroundedState _grounded;
    private float _timer;
    private Vector3 _direction;
    public DashState(PlayerController controller, GroundedState grounded) : base(controller) => _grounded = grounded;

    public override void Enter()
    {
        Debug.Log("[State]   Dash");
        controller.PlayDash();
        _timer = controller.DashDuration;
        _direction = controller.GetDashDirection();  // locked for the whole burst — no steering mid-dash
        controller.SnapFacing(_direction);           // snap to face where we're dashing
    }

    public override void Tick(float dt)
    {
        // Fixed-speed burst in the locked direction (no analog scaling — a dash is a dash).
        controller.SetPlanarVelocity(_direction * controller.DashSpeed);

        _timer -= dt;
        if (_timer <= 0f)
        {
            State next = controller.MoveMagnitude > controller.MoveDeadzone ? _grounded.Locomotion : _grounded.Idle;
            _grounded.SubMachine.ChangeState(next);
        }
    }

    public override void Exit()
    {
        // Cooldown starts when the dash ends — including when it's jump-canceled — so cancels can't spam dashes.
        controller.BeginDashCooldown();
    }
}

public class LandState : State
{
    private readonly GroundedState _grounded;
    private float _timer;
    public LandState(PlayerController controller, GroundedState grounded) : base(controller) => _grounded = grounded;

    public override void Enter()
    {
        Debug.Log("[State]   Land");
        controller.PlayLand();
        _timer = controller.LandDuration;
    }

    public override void Tick(float dt)
    {
        // Cancel the recovery into movement immediately if the stick is held — no stutter
        // on running landings. (Jump and dash already cancel it via the Grounded superstate.)
        if (controller.MoveMagnitude > controller.MoveDeadzone)
        {
            _grounded.SubMachine.ChangeState(_grounded.Locomotion);
            return;
        }

        _timer -= dt;
        if (_timer <= 0f)
            _grounded.SubMachine.ChangeState(_grounded.Idle);
    }
}

/// <summary>
/// Shared logic for the walk/run stop states: play a stop animation and hold briefly
/// before settling into Idle. If the stick is pressed again before the animation finishes,
/// cancel straight back into Locomotion — stopping is purely visual, never a responsiveness
/// cost. Position doesn't slide during this (planar velocity is already zero, same as
/// LandState); this only adds the animation and a brief hold.
/// </summary>
public abstract class StopStateBase : State
{
    protected readonly GroundedState grounded;
    private float _timer;

    protected StopStateBase(PlayerController controller, GroundedState grounded) : base(controller) => this.grounded = grounded;

    protected abstract void PlayStopAnimation();
    protected abstract float Duration { get; }

    public override void Enter()
    {
        PlayStopAnimation();
        _timer = Duration;
    }

    public override void Tick(float dt)
    {
        if (controller.MoveMagnitude > controller.MoveDeadzone)
        {
            grounded.SubMachine.ChangeState(grounded.Locomotion);   // cancel early — stick pressed again
            return;
        }

        _timer -= dt;
        if (_timer <= 0f)
            grounded.SubMachine.ChangeState(grounded.Idle);
    }
}

public class StopWalkState : StopStateBase
{
    public StopWalkState(PlayerController controller, GroundedState grounded) : base(controller, grounded) { }
    protected override void PlayStopAnimation()
    {
        Debug.Log("[State]   StopWalk");
        controller.PlayStopWalk();
    }
    protected override float Duration => controller.StopWalkDuration;
}

public class StopRunState : StopStateBase
{
    public StopRunState(PlayerController controller, GroundedState grounded) : base(controller, grounded) { }
    protected override void PlayStopAnimation()
    {
        Debug.Log("[State]   StopRun");
        controller.PlayStopRun();
    }
    protected override float Duration => controller.StopRunDuration;
}