/// <summary>
/// Generic state machine core. No game logic lives here — reuse it for enemies,
/// UI, anything. A state overrides only the hooks it needs.
/// </summary>
public abstract class State
{
    protected readonly PlayerController controller;
    protected State(PlayerController controller) => this.controller = controller;

    public virtual void Enter() { }
    public virtual void Tick(float deltaTime) { }
    public virtual void Exit() { }
}

/// <summary>
/// Holds one active state and swaps between them. Exit-then-Enter on every change.
/// States are created once and reused, so changes never allocate.
/// </summary>
public class StateMachine
{
    public State Current { get; private set; }

    public void ChangeState(State next)
    {
        if (next == Current) return;     // ignore redundant transitions
        Current?.Exit();
        Current = next;
        Current?.Enter();
    }

    public void Tick(float deltaTime) => Current?.Tick(deltaTime);

    /// Exit the active state and clear it, so the next ChangeState always re-enters —
    /// even into the same state. Used when a superstate is left, so its substate's
    /// Enter (and the animation it triggers) runs fresh on the next visit.
    public void Reset()
    {
        Current?.Exit();
        Current = null;
    }
}

/// <summary>
/// A state that owns a nested StateMachine of substates. Concrete superstates
/// (Grounded, Airborne) override Enter to pick a default substate and override
/// Tick to run shared logic + cross-cutting transitions, then tick the substate.
/// </summary>
public abstract class SuperState : State
{
    protected readonly StateMachine subMachine = new StateMachine();
    public StateMachine SubMachine => subMachine;

    protected SuperState(PlayerController controller) : base(controller) { }

    // Leaving a superstate exits AND clears its substate, so the substate's Enter
    // runs fresh next time we enter this superstate.
    public override void Exit() => subMachine.Reset();
}