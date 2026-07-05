using UnityEngine;

/// <summary>
/// Lock-on movement mode. A parallel root superstate beside Grounded/Airborne, with two
/// substates: TargetMove (orbital movement) and TargetDash (directional dash). The player
/// always faces the target. No jump. Free movement is untouched.
/// </summary>
public class TargetGroundedState : SuperState
{
    public TargetMoveState Move { get; }
    public TargetDashState Dash { get; }

    public TargetGroundedState(PlayerController controller) : base(controller)
    {
        Move = new TargetMoveState(controller, this);
        Dash = new TargetDashState(controller, this);
    }

    public override void Enter()
    {
        Debug.Log("[State] TargetGrounded");
        subMachine.ChangeState(Move);
    }

    public override void Tick(float dt)
    {
        controller.ApplyGroundedVertical();

        // Only the toggle / auto-break / lost target leaves target mode (handled by the controller).
        if (!controller.IsLockedOn)
            return;

        controller.FaceTarget();   // always face the target (shared by move and dash)

        // Cross-cutting: dash is available from any target substate, gated by the shared cooldown.
        if (controller.DashRequested && controller.CanDash)
            subMachine.ChangeState(Dash);

        subMachine.Tick(dt);
    }
}

/// <summary>Orbital movement relative to the target, driving the 2D movement blend tree.</summary>
public class TargetMoveState : State
{
    private readonly TargetGroundedState _parent;
    public TargetMoveState(PlayerController controller, TargetGroundedState parent) : base(controller) => _parent = parent;

    public override void Enter()
    {
        Debug.Log("[State]   TargetMove");
        controller.PlayTargetMove();
    }

    public override void Tick(float dt)
    {
        controller.ApplyTargetMovement(dt);
        controller.UpdateTargetAnimation(dt);
    }
}

/// <summary>
/// Directional dash in target mode. Fixed-speed burst toward one of 4 target-relative
/// cardinals (forward/back/left/right), driving the dash blend tree.
///
/// Input: if a direction is already held on press, it fires instantly that way. If the stick
/// is neutral, it opens a short window to flick a direction — flicking commits immediately,
/// and if the window expires with no input it defaults to a forward dash (toward the target).
/// </summary>
public class TargetDashState : State
{
    private enum Phase { Windup, Bursting }

    private readonly TargetGroundedState _parent;
    private Phase _phase;
    private float _windupTimer;
    private float _burstTimer;
    private Vector3 _worldDir;

    public TargetDashState(PlayerController controller, TargetGroundedState parent) : base(controller) => _parent = parent;

    public override void Enter()
    {
        Debug.Log("[State]   TargetDash");
        if (controller.MoveMagnitude > controller.MoveDeadzone)
            StartBurst(controller.MoveInput);            // direction already held → instant dash
        else
        {
            _phase = Phase.Windup;                       // wait briefly for a direction flick
            _windupTimer = controller.TargetDashWindow;
        }
    }

    public override void Tick(float dt)
    {
        if (_phase == Phase.Windup)
        {
            if (controller.MoveMagnitude > controller.MoveDeadzone)
                StartBurst(controller.MoveInput);        // a flick commits the dash early
            else
            {
                _windupTimer -= dt;
                if (_windupTimer <= 0f)
                    StartBurst(new Vector2(0f, 1f));     // timeout → forward (toward target)
            }
            return;                                      // hold still during the selection window
        }

        // Bursting: fixed-speed burst, clamped so a forward dash can't push through the target.
        controller.SetPlanarVelocity(controller.ClampApproachToTarget(_worldDir * controller.DashSpeed));
        _burstTimer -= dt;
        if (_burstTimer <= 0f)
            _parent.SubMachine.ChangeState(_parent.Move);
    }

    private void StartBurst(Vector2 stick)
    {
        _phase = Phase.Bursting;
        _burstTimer = controller.DashDuration;

        Vector2 cardinal = controller.ResolveDashCardinal(stick);  // nearest of 4
        _worldDir = controller.TargetRelativeToWorld(cardinal);
        controller.SetTargetBlendInstant(cardinal);                // pick the dash-direction clip
        controller.PlayTargetDash();
    }

    public override void Exit()
    {
        controller.BeginDashCooldown();   // shares the free-dash cooldown
    }
}