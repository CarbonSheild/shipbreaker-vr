using System;
using BBI.Unity.Game;
using BepInEx.Configuration;
using InControl;
using ShipbreakerVr.Tracking;
using UnityEngine;
using Action = BBI.Unity.Game.GameplayActions.GameplayActionSet;

namespace ShipbreakerVr;

internal sealed class VrMovementControls : MonoBehaviour
{
    private static VrMovementControls instance;
    private static ConfigEntry<bool> enabledSetting, swapSticks, invertPitch;
    private static ConfigEntry<float> deadzone, turnScale;
    private static ConfigEntry<VrButton> ascend, descend, rollLeft, rollRight, brakeExtra;
    private static ConfigEntry<bool> handMovement, handInvertYaw, handInvertRoll;
    private static ConfigEntry<VrButton> handRotateHold, handBrake, handInteractPush, handRetractTether, handRecallTethers;
    private static ConfigEntry<ThrustFrame> handThrustFrame;
    private static ConfigEntry<float> handTurnDeadzone, handTurnRange, handRollDeadzone, handCurve, handDirectGain;
    private static ConfigEntry<RotationMode> handRotationMode;
    private readonly OpenXrTrackingProvider tracking = new OpenXrTrackingProvider();
    private readonly MovementNeutralGate gate = new MovementNeutralGate();
    private readonly ShiftedStickGate shiftedStick = new ShiftedStickGate();
    private readonly HandRotationMapper rotationMapper = new HandRotationMapper();
    private readonly DirectRotationMapper directMapper = new DirectRotationMapper();
    private Quaternion directDelta = Quaternion.identity;
    private bool directActive, directEngaged;
    private InputSystemUpdateBridge bridge;
    private Vector2 move, turn;
    private float vertical;
    private bool up, down, leftRoll, rightRoll, brake, failed;
    private int sampledFrame = -1;
    private string lastStatus;

    internal static void Configure(ConfigFile config)
    {
        enabledSetting = config.Bind("Movement", "Enabled", true, "VR movement using the game's thrusters and body rotation. F3-off restores original input.");
        swapSticks = config.Bind("Movement", "SwapSticks", false, "Swap move and turn sticks (stick layout only).");
        invertPitch = config.Bind("Movement", "InvertPitch", false, "Invert the turning stick's vertical axis. Also applies to hand-rotation pitch.");
        deadzone = config.Bind("Movement", "StickDeadzone", .15f, new ConfigDescription("Ignore stick drift; the game's own deadzone also applies.", new AcceptableValueRange<float>(.05f, .5f)));
        turnScale = config.Bind("Movement", "TurnScale", 1f, new ConfigDescription("Scale input to the game's gamepad turning curve.", new AcceptableValueRange<float>(.1f, 1f)));
        ascend = config.Bind("Movement", "Ascend", VrButton.RightPrimary, "Stick layout: default right lower face button (Pico A / Frame compatibility A).");
        descend = config.Bind("Movement", "Descend", VrButton.RightSecondary, "Stick layout: default right upper face button (Pico B; Frame compatibility groups its top buttons).");
        rollLeft = config.Bind("Movement", "RollLeft", VrButton.LeftStickClick, "Stick layout: default matches gamepad L3. Both roll buttons together brake.");
        rollRight = config.Bind("Movement", "RollRight", VrButton.RightStickClick, "Stick layout: default matches gamepad R3. Both roll buttons together brake.");
        brakeExtra = config.Bind("Movement", "ExtraBrake", VrButton.None, "Optional additional single brake button in either layout, e.g. RightStickClick.");
        handMovement = config.Bind("HandMovement", "Enabled", true,
            "Hand-steered movement: the LEFT stick thrusts in the direction the left controller points (including up/down), and holding RotateHoldButton while turning, tilting or twisting that controller rotates the body. " +
            "The RIGHT stick becomes the D-pad in gameplay; Ascend/Descend/Roll buttons are unused. F10 toggles this in game. False restores the two-stick layout.");
        handRotateHold = config.Bind("HandMovement", "RotateHoldButton", VrButton.LeftGrip, "Hold to rotate the body with the controller. The orientation at the press is neutral; release to stop turning.");
        handRotationMode = config.Bind("HandMovement", "RotationMode", RotationMode.Direct,
            "Direct: while held, the body turns exactly as the controller turns (release, reposition, hold again to continue). Rate: controller angle from the anchor sets a turn speed, like a stick.");
        handDirectGain = config.Bind("HandMovement", "DirectRotationGain", 1f, new ConfigDescription("Direct mode only: body degrees per controller degree. 1 is one to one.", new AcceptableValueRange<float>(.5f, 3f)));
        handThrustFrame = config.Bind("HandMovement", "ThrustFrame", ThrustFrame.Controller, "Controller: stick forward thrusts where the left controller points. Body: stick forward thrusts along the jetpack nose, up/down unavailable.");
        handBrake = config.Bind("HandMovement", "BrakeButton", VrButton.LeftStickClick, "Brake in hand-steered mode. Movement.ExtraBrake also brakes.");
        handTurnDeadzone = config.Bind("HandMovement", "TurnDeadzoneDegrees", 4f, new ConfigDescription("Controller yaw/pitch from the anchor ignored as drift.", new AcceptableValueRange<float>(0f, 30f)));
        handTurnRange = config.Bind("HandMovement", "TurnRangeDegrees", 35f, new ConfigDescription("Controller yaw/pitch from the anchor for full turn rate.", new AcceptableValueRange<float>(10f, 90f)));
        handRollDeadzone = config.Bind("HandMovement", "RollDeadzoneDegrees", 20f, new ConfigDescription("Controller twist from the anchor ignored before roll starts, in both rotation modes. In Rate mode roll then runs at the game's fixed rate.", new AcceptableValueRange<float>(0f, 80f)));
        handCurve = config.Bind("HandMovement", "ResponseCurve", 1.5f, new ConfigDescription("1 is linear; higher values keep small rotations gentle.", new AcceptableValueRange<float>(.5f, 3f)));
        handInteractPush = config.Bind("HandMovement", "InteractPushButton", VrButton.RightSecondary, "Gameplay interact and grapple push (the game's X action) in this layout. Default right upper face button (B).");
        handRetractTether = config.Bind("HandMovement", "GrappleRetractTetherButton", VrButton.RightPrimary, "Grapple: hold to retract a grabbed object, or with an empty grapple hold to preview a tether and release to place it. Default right lower face button (A).");
        handRecallTethers = config.Bind("HandMovement", "RecallTethersButton", VrButton.RightStickClick, "Recall all tethers (D-pad down). Right stick down still works too.");
        handInvertYaw = config.Bind("HandMovement", "InvertYaw", false, "Invert hand-rotation yaw.");
        handInvertRoll = config.Bind("HandMovement", "InvertRoll", false, "Invert hand-rotation roll.");
    }

    private static bool Enabled => instance && instance.isActiveAndEnabled && enabledSetting.Value && VrInputMode.MotionActive;
    private static bool ContextAllowed => Enabled && GameSession.CurrentGameState == GameSession.GameState.Gameplay &&
        !EquipmentController.ToolMenuOpen && !VrMenuControls.PauseInputConsumed && Application.isFocused && LynxControls.Instance && LynxControls.Instance.IsGameFocused &&
        LynxControls.Instance.TryGetLoadedActionSet(LynxControls.PlayerActionSetTypes.GameplayActions)?.Enabled == true &&
        VrCamera.ViewCamera && VrCamera.ViewCamera.isActiveAndEnabled;
    /// <summary>Hand-steered layout is configured on; other adapters move their bindings to the right controller.</summary>
    internal static bool HandLayoutActive => handMovement != null && handMovement.Value && enabledSetting != null && enabledSetting.Value;
    /// <summary>Hand-steered movement frees the right stick, so gameplay treats it as the D-pad without a modifier.</summary>
    internal static bool RightStickIsDpad => HandLayoutActive;
    internal static VrButton HandInteractPushButton => handInteractPush.Value;
    internal static VrButton HandRetractTetherButton => handRetractTether.Value;
    internal static VrButton HandRecallTethersButton => handRecallTethers.Value;

    /// <summary>F10: switch between the two-stick layout and hand-steered movement. The choice is saved to the config.</summary>
    internal static void ToggleHandMovement()
    {
        if (handMovement == null) return;
        handMovement.Value = !handMovement.Value;
        if (instance) { instance.ResetInput(); instance.sampledFrame = -1; }
        Debug.Log("[ShipbreakerVr] Movement layout: " + (handMovement.Value ? $"hand-steered (left stick thrusts where the controller points; hold {handRotateHold.Value} to rotate)" : "two sticks") + ".");
    }

    /// <summary>Direct rotation target for this frame, consumed by the orientation postfix. False when not holding.</summary>
    internal static bool DirectRotation(out Quaternion delta, out bool engaged)
    {
        delta = Quaternion.identity; engaged = false;
        if (!instance || !instance.directActive || !ContextAllowed) return false;
        delta = instance.directDelta;
        engaged = instance.directEngaged;
        return true;
    }

    private void Awake() => instance = this;
    internal static void InputOwnerChanged()
    {
        if (instance) { instance.ResetInput(); instance.sampledFrame = -1; }
    }
    private void Update() { if (!ContextAllowed) ResetInput(); }

    private void ResetInput()
    {
        move = turn = Vector2.zero;
        vertical = 0f;
        up = down = leftRoll = rightRoll = brake = false;
        gate.Sample(false, Vector2.zero, Vector2.zero, false, deadzone.Value);
        rotationMapper.Reset();
        directMapper.Reset();
        directActive = directEngaged = false;
        directDelta = Quaternion.identity;
        bridge?.Dispose();
        bridge = null;
        tracking.Clear();
    }

    private void Sample()
    {
        if (!ContextAllowed || failed) { ResetInput(); return; }
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        try
        {
            if (bridge == null) bridge = new InputSystemUpdateBridge();
            bridge.UpdateIfNeeded();
            tracking.Sample();
            var left = tracking.LeftHand.Inputs;
            var right = tracking.RightHand.Inputs;
            var extra = MovementInput.Button(brakeExtra.Value, left, right);
            var status = handMovement.Value ? SampleHandMovement(left, right, extra) : SampleSticks(left, right, extra);
            if (lastStatus != status)
            {
                lastStatus = status;
                Debug.Log($"[ShipbreakerVr] Movement: {status}; leftStick={left.StickAvailable}; rightStick={right.StickAvailable}");
            }
        }
        catch (Exception error)
        {
            failed = true;
            ResetInput();
            Debug.LogError($"[ShipbreakerVr] Movement input stopped after sampling failure. F3-off restores original input. {error}");
        }
    }

    private string SampleSticks(ControllerInputs left, ControllerInputs right, bool extra)
    {
        rotationMapper.Reset();
        directMapper.Reset();
        directActive = directEngaged = false;
        var leftMovement = shiftedStick.ForMovement(VrMenuControls.DpadShifted(left, right), left.Stick, deadzone.Value);
        var rawMove = swapSticks.Value ? right.Stick : leftMovement;
        var rawTurn = swapSticks.Value ? leftMovement : right.Stick;
        up = MovementInput.Button(ascend.Value, left, right);
        down = MovementInput.Button(descend.Value, left, right);
        leftRoll = MovementInput.Button(rollLeft.Value, left, right);
        rightRoll = MovementInput.Button(rollRight.Value, left, right);
        var tracked = tracking.Head.IsValid && tracking.LeftHand.Grip.IsValid && tracking.RightHand.Grip.IsValid && left.StickAvailable && right.StickAvailable;
        var armed = gate.Sample(tracked, rawMove, rawTurn, up || down || leftRoll || rightRoll || extra, deadzone.Value);
        move = armed ? MovementInput.Deadzone(rawMove, deadzone.Value) : Vector2.zero;
        turn = armed ? MovementInput.Deadzone(rawTurn, deadzone.Value) * turnScale.Value : Vector2.zero;
        if (invertPitch.Value) turn.y = -turn.y;
        brake = armed && ((leftRoll && rightRoll) || extra);
        up &= armed; down &= armed; leftRoll &= armed && !extra; rightRoll &= armed && !extra;
        vertical = (up ? 1f : 0f) - (down ? 1f : 0f);
        return !tracked ? "tracking/sticks unavailable" : !armed ? "center sticks and release movement buttons" : "ready: gamepad-style movement";
    }

    private string SampleHandMovement(ControllerInputs left, ControllerInputs right, bool extra)
    {
        var holdButton = handRotateHold.Value;
        var rotateHand = IsLeftHand(holdButton) ? tracking.LeftHand : tracking.RightHand;
        var thrustHand = tracking.LeftHand;
        var held = MovementInput.Button(holdButton, left, right);
        var brakeHeld = MovementInput.Button(handBrake.Value, left, right) || extra;
        var useController = handThrustFrame.Value == ThrustFrame.Controller;
        var tracked = tracking.Head.IsValid && rotateHand.Grip.IsValid && left.StickAvailable && (!useController || thrustHand.Aim.IsValid);
        // The right stick is the D-pad here; it must still be centred to arm so a held direction cannot bleed across contexts.
        var armed = gate.Sample(tracked, left.Stick, right.Stick, held || brakeHeld, deadzone.Value);
        var stick = armed ? MovementInput.Deadzone(left.Stick, deadzone.Value) : Vector2.zero;
        var thrust = useController ? HandThrust.Resolve(stick, thrustHand.Aim.Rotation) : new Vector3(stick.x, 0f, stick.y);
        move = new Vector2(thrust.x, thrust.z);
        vertical = thrust.y;
        if (handRotationMode.Value == RotationMode.Direct)
        {
            rotationMapper.Reset();
            var direct = directMapper.Sample(armed && held, rotateHand.Grip, handDirectGain.Value, handRollDeadzone.Value);
            directActive = direct.Active;
            directEngaged = directMapper.Engaged;
            directDelta = direct.Active ? direct.Delta : Quaternion.identity;
            if (directEngaged) VrHaptics.Pulse(IsLeftHand(holdButton), .25f, .04f);
            turn = Vector2.zero;
            leftRoll = rightRoll = false;
        }
        else
        {
            directMapper.Reset();
            directActive = directEngaged = false;
            var settings = new HandRotationSettings(handTurnDeadzone.Value, handTurnRange.Value, handRollDeadzone.Value, handCurve.Value);
            var rotation = rotationMapper.Sample(armed && held, rotateHand.Grip, settings);
            if (rotationMapper.Engaged) VrHaptics.Pulse(IsLeftHand(holdButton), .25f, .04f);
            turn = new Vector2(handInvertYaw.Value ? -rotation.Turn.x : rotation.Turn.x, invertPitch.Value ? -rotation.Turn.y : rotation.Turn.y) * turnScale.Value;
            var roll = handInvertRoll.Value ? -rotation.Roll : rotation.Roll;
            leftRoll = roll < 0f;
            rightRoll = roll > 0f;
        }
        up = down = false;
        brake = armed && brakeHeld;
        return !tracked ? (useController && !thrustHand.Aim.IsValid ? "left controller aim unavailable for thrust" : "tracking/sticks unavailable")
            : !armed ? "center sticks and release the rotate and brake buttons" : $"ready: hand-steered movement ({handRotationMode.Value} rotation, hold {holdButton})";
    }

    private static bool IsLeftHand(VrButton button) =>
        button == VrButton.LeftPrimary || button == VrButton.LeftSecondary || button == VrButton.LeftStickClick || button == VrButton.LeftGrip;

    private static bool Allowed(LynxControls controls, Action action)
    {
        var id = LynxControls.ActionSetAndId.FromEnum(action);
        return controls.TryGetLoadedActionSet(LynxControls.PlayerActionSetTypes.GameplayActions)?.Enabled == true &&
            controls.TryGetLoadedPlayerAction(id)?.Enabled == true;
    }

    private static float Axis(LynxControls controls, float value, Action negative, Action positive) =>
        value < 0f ? (Allowed(controls, negative) ? value : 0f) : (Allowed(controls, positive) ? value : 0f);

    internal static float OneAxis(LynxControls controls, LynxControls.ActionSetAndId action)
    {
        if (!Enabled) return controls.GetOneAxisInputControlValue(action);
        instance.Sample();
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.ThrustMoveLeftRightComposite)))
            return Axis(controls, instance.move.x, Action.ThrustMoveLeft, Action.ThrustMoveRight);
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.ThrustMoveBackForwardComposite)))
            return Axis(controls, instance.move.y, Action.ThrustMoveBackward, Action.ThrustMoveForward);
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.ThrustMoveUpDownComposite)))
            return Axis(controls, instance.vertical, Action.ThrustMoveDown, Action.ThrustMoveUp);
        return controls.GetOneAxisInputControlValue(action);
    }

    internal static Vector2 TwoAxis(LynxControls controls, LynxControls.ActionSetAndId action)
    {
        if (!Enabled || !action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.RotateBodyComposite))) return controls.GetTwoAxisInputControlVector(action);
        instance.Sample();
        return new Vector2(Axis(controls, instance.turn.x, Action.RotateBodyLeft, Action.RotateBodyRight),
            Axis(controls, instance.turn.y, Action.RotateBodyDown, Action.RotateBodyUp));
    }

    internal static bool Pressed(LynxControls controls, LynxControls.ActionSetAndId action)
    {
        if (!Enabled) return controls.GetInputIsPressed(action);
        instance.Sample();
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.RollBodyLeft))) return instance.leftRoll && Allowed(controls, Action.RollBodyLeft);
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.RollBodyRight))) return instance.rightRoll && Allowed(controls, Action.RollBodyRight);
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.ThrustBrakeLeft))) return instance.brake && Allowed(controls, Action.ThrustBrakeLeft);
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.ThrustBrakeRight))) return instance.brake && Allowed(controls, Action.ThrustBrakeRight);
        // This keyboard modifier must not divert the VR turning stick to keyboard roll input.
        if (action.Equals(LynxControls.ActionSetAndId.FromEnum(Action.ModifiedRoll))) return false;
        return controls.GetInputIsPressed(action);
    }

    internal static BindingSourceType InputType(LynxControls controls) => Enabled ? BindingSourceType.DeviceBindingSource : controls.LastInputType;
    private void OnDisable() => ResetInput();
    private void OnDestroy() { if (instance == this) instance = null; }
}
