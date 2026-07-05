using UnityEngine;

/// <summary>
/// Throwaway harness to verify input is wired correctly.
/// Drop it on any GameObject in the scene and assign your InputReader asset.
/// - Button presses log to the Console.
/// - Live stick values draw in the top-left corner during Play.
/// Delete once movement/camera take over.
/// </summary>
public class InputTester : MonoBehaviour
{
    [SerializeField] private InputReader _input;

    [Header("Debug Display")]
    [SerializeField] private int _fontSize = 24;
    [SerializeField] private Color _textColor = Color.white;
    private GUIStyle _style;

    private void OnEnable()
    {
        _input.JumpEvent += OnJump;
        _input.JumpCanceledEvent += OnJumpReleased;
        _input.DashEvent += OnDash;
    }

    private void OnDisable()
    {
        _input.JumpEvent -= OnJump;
        _input.JumpCanceledEvent -= OnJumpReleased;
        _input.DashEvent -= OnDash;
    }

    private void OnJump() => Debug.Log("Jump pressed");
    private void OnJumpReleased() => Debug.Log("Jump released");
    private void OnDash() => Debug.Log("Dash pressed");


    private void OnGUI()
{
    if (_style == null) _style = new GUIStyle(GUI.skin.label);
    _style.fontSize = _fontSize;            // updated every frame, so you can tune it live in Play
    _style.normal.textColor = _textColor;

    float h = _fontSize * 1.5f;             // row height scales with font so text never clips
    GUI.Label(new Rect(10, 10, 1000, h), $"Move: {_input.MoveInput}", _style);
    GUI.Label(new Rect(10, 10 + h, 1000, h), $"Look: {_input.LookInput}", _style);
}
}