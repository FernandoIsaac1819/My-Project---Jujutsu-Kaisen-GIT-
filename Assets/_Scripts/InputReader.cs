using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Central input hub. A single shared asset that every gameplay system
/// (movement, camera, VFX, SFX...) references.
///
/// - Buttons (Jump, Dash) are broadcast as C# events you subscribe to.
/// - Sticks (Move, Look) are exposed as polled properties you read each frame.
///
/// Requires a generated input class named "PlayerControls" with an action
/// map named "Player" containing actions: Move, Look, Jump, Dash.
/// </summary>
[CreateAssetMenu(fileName = "InputReader", menuName = "JJK/Input Reader")]
public class InputReader : ScriptableObject, PlayerControls.IPlayerActions
{
    // ----- Button events (discrete) -----
    public event Action JumpEvent;          // fired the frame the button is pressed
    public event Action JumpCanceledEvent;  // fired when released (useful later for variable jump height)
    public event Action DashEvent;          // fired the frame the button is pressed
    public event Action LockOnEvent;        // fired the frame the lock-on button is pressed

    // ----- Stick values (continuous, polled by consumers) -----
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }

    private PlayerControls _controls;

    private void OnEnable()
    {
        if (_controls == null)
        {
            _controls = new PlayerControls();
            _controls.Player.SetCallbacks(this);
        }
        EnableGameplayInput();
    }

    private void OnDisable()
    {
        DisableAllInput();
    }

    // Call these later to swap input contexts (e.g. disable gameplay input while a menu is open).
    public void EnableGameplayInput() => _controls.Player.Enable();
    public void DisableAllInput() => _controls?.Player.Disable();

    // ----- IPlayerActions callbacks -----
    // SetCallbacks routes every phase (started/performed/canceled) of each action here.

    public void OnMove(InputAction.CallbackContext context)
    {
        // ReadValue returns zero automatically on the "canceled" phase (stick released).
        MoveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        LookInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed) JumpEvent?.Invoke();
        else if (context.canceled) JumpCanceledEvent?.Invoke();
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (context.performed) DashEvent?.Invoke();
    }

    public void OnLockOn(InputAction.CallbackContext context)
    {
        if (context.performed) LockOnEvent?.Invoke();
    }

    public void OnReinforce(InputAction.CallbackContext context)
    {
        if (context.performed) LockOnEvent?.Invoke();
    }
}