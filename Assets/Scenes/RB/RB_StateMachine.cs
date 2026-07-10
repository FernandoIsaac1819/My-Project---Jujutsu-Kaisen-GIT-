namespace RigidbodyMovement
{
    /// <summary>
    /// Same generic state machine shape as the CharacterController version, duplicated here
    /// (not shared) so this prototype can never affect the working CC-based system, even
    /// indirectly. Kept intentionally boring — the whole point of this file is to be a copy.
    /// </summary>
    public abstract class RBState
    {
        protected readonly RigidbodyPlayerController controller;
        protected RBState(RigidbodyPlayerController controller) => this.controller = controller;

        public virtual void Enter() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void Exit() { }
    }

    public class RBStateMachine
    {
        public RBState Current { get; private set; }

        public void ChangeState(RBState next)
        {
            if (next == Current) return;
            Current?.Exit();
            Current = next;
            Current?.Enter();
        }

        public void Tick(float deltaTime) => Current?.Tick(deltaTime);

        public void Reset()
        {
            Current?.Exit();
            Current = null;
        }
    }

    public abstract class RBSuperState : RBState
    {
        protected readonly RBStateMachine subMachine = new RBStateMachine();
        public RBStateMachine SubMachine => subMachine;

        protected RBSuperState(RigidbodyPlayerController controller) : base(controller) { }

        public override void Exit() => subMachine.Reset();
    }
}
