using UnityEngine;

namespace ShipbreakerVr.Tracking;

public enum ThrustFrame { Controller, Body }

public readonly struct HandRotationSettings
{
    public readonly float TurnDeadzone, TurnRange, RollDeadzone, Curve;
    public HandRotationSettings(float turnDeadzone, float turnRange, float rollDeadzone, float curve)
    {
        TurnDeadzone = turnDeadzone; TurnRange = turnRange; RollDeadzone = rollDeadzone; Curve = curve;
    }
}

public readonly struct HandRotationOutput
{
    public readonly bool Active;
    /// <summary>x yaw right, y pitch up; magnitude at most 1.</summary>
    public readonly Vector2 Turn;
    /// <summary>Positive rolls right; -1..1.</summary>
    public readonly float Roll;
    public HandRotationOutput(bool active, Vector2 turn, float roll)
    {
        Active = active; Turn = turn; Roll = roll;
    }
}

/// <summary>
/// Maps a held controller's rotation from its press-time anchor to body yaw, pitch and roll.
/// Tracking-space degrees in, unit axes out. Roll uses its own deadzone so a deliberate twist
/// is needed. No game types, and only managed Unity math so the desktop test suite can run it.
/// </summary>
public sealed class HandRotationMapper
{
    private bool anchored;
    private Quaternion anchorRotation;

    public bool Anchored => anchored;
    /// <summary>True only on the sample that created a new anchor.</summary>
    public bool Engaged { get; private set; }

    public void Reset() { anchored = false; Engaged = false; }

    public HandRotationOutput Sample(bool held, TrackedPose pose, HandRotationSettings settings)
    {
        Engaged = false;
        if (!held || !pose.IsValid) { anchored = false; return default; }
        if (!anchored)
        {
            anchored = true;
            Engaged = true;
            anchorRotation = Normalize(pose.Rotation);
            return new HandRotationOutput(true, Vector2.zero, 0f);
        }
        var delta = Normalize(pose.Rotation) * Conjugate(anchorRotation);
        var angles = YawPitchRoll(delta);
        var turn = Scale(new Vector3(angles.x, angles.y, 0f), settings.TurnDeadzone, settings.TurnRange, settings.Curve);
        var roll = Scale(new Vector3(angles.z, 0f, 0f), settings.RollDeadzone, settings.TurnRange, settings.Curve).x;
        return new HandRotationOutput(true, new Vector2(turn.x, turn.y), roll);
    }

    /// <summary>Radial deadzone, then travel normalised to the range and shaped by the curve. Direction is preserved.
    /// A range at or below the deadzone becomes on/off past the deadzone.</summary>
    public static Vector3 Scale(Vector3 value, float deadzone, float range, float curve)
    {
        if (!Finite(value)) return Vector3.zero;
        var magnitude = value.magnitude;
        if (magnitude <= deadzone) return Vector3.zero;
        var travel = range <= deadzone ? 1f : Mathf.Clamp01((magnitude - deadzone) / (range - deadzone));
        var shaped = Mathf.Pow(travel, Mathf.Max(curve, .01f));
        return value / magnitude * shaped;
    }

    /// <summary>
    /// Degrees of (yaw right, pitch up, roll right) for a world-space rotation, using Unity's ZXY
    /// convention: +X pitches the nose down, +Y yaws right, +Z rolls left when seen from behind.
    /// Managed replacement for Quaternion.eulerAngles, which is a native call.
    /// </summary>
    public static Vector3 YawPitchRoll(Quaternion q)
    {
        var sinPitch = Mathf.Clamp(2f * (q.w * q.x - q.y * q.z), -1f, 1f);
        var pitchDown = Mathf.Asin(sinPitch) * Mathf.Rad2Deg;
        var yawRight = Mathf.Atan2(2f * (q.w * q.y + q.x * q.z), 1f - 2f * (q.x * q.x + q.y * q.y)) * Mathf.Rad2Deg;
        var rollLeft = Mathf.Atan2(2f * (q.w * q.z + q.x * q.y), 1f - 2f * (q.x * q.x + q.z * q.z)) * Mathf.Rad2Deg;
        return new Vector3(yawRight, -pitchDown, -rollLeft);
    }

    private static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

    internal static Quaternion Normalize(Quaternion q)
    {
        var length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return length > 0f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : new Quaternion(0, 0, 0, 1);
    }

    internal static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
}

public enum RotationMode { Direct, Rate }

public readonly struct DirectRotationOutput
{
    public readonly bool Active;
    /// <summary>Body-local rotation to apply on top of the body orientation captured when the hold began.</summary>
    public readonly Quaternion Delta;
    public DirectRotationOutput(bool active, Quaternion delta) { Active = active; Delta = delta; }
}

/// <summary>
/// Locks body orientation to the held controller one to one: the body turns exactly as the controller
/// turns from its press-time orientation. Release, reposition the hand, hold again to continue (a ratchet).
/// A twist deadzone removes small accidental roll. Gain scales the applied angle.
/// </summary>
public sealed class DirectRotationMapper
{
    private bool anchored;
    private Quaternion anchorRotation;

    public bool Anchored => anchored;
    /// <summary>True only on the sample that created a new anchor; the caller captures the body orientation then.</summary>
    public bool Engaged { get; private set; }

    public void Reset() { anchored = false; Engaged = false; }

    public DirectRotationOutput Sample(bool held, TrackedPose pose, float gain, float rollDeadzoneDegrees)
    {
        Engaged = false;
        if (!held || !pose.IsValid) { anchored = false; return default; }
        if (!anchored)
        {
            anchored = true;
            Engaged = true;
            anchorRotation = HandRotationMapper.Normalize(pose.Rotation);
            return new DirectRotationOutput(true, new Quaternion(0, 0, 0, 1));
        }
        var delta = Positive(HandRotationMapper.Normalize(pose.Rotation) * Conjugate(anchorRotation));
        delta = RemoveRollDeadzone(delta, rollDeadzoneDegrees);
        if (!float.IsNaN(gain) && !float.IsInfinity(gain) && Mathf.Abs(gain - 1f) > .0001f) delta = ScaleAngle(delta, gain);
        return new DirectRotationOutput(true, delta);
    }

    /// <summary>Swing-twist split about the body forward axis; the twist (roll) loses its first `deadzoneDegrees`.</summary>
    public static Quaternion RemoveRollDeadzone(Quaternion q, float deadzoneDegrees)
    {
        if (deadzoneDegrees <= 0f) return q;
        q = Positive(q);
        var twistLength = Mathf.Sqrt(q.z * q.z + q.w * q.w);
        if (twistLength < .0001f) return q;
        var twist = new Quaternion(0, 0, q.z / twistLength, q.w / twistLength);
        var swing = q * Conjugate(twist);
        var angle = 2f * Mathf.Atan2(twist.z, twist.w) * Mathf.Rad2Deg;
        var kept = Mathf.Sign(angle) * Mathf.Max(0f, Mathf.Abs(angle) - deadzoneDegrees);
        var half = kept * Mathf.Deg2Rad * .5f;
        return HandRotationMapper.Normalize(swing * new Quaternion(0, 0, Mathf.Sin(half), Mathf.Cos(half)));
    }

    /// <summary>Scales the rotation angle about its own axis; managed replacement for ToAngleAxis.</summary>
    public static Quaternion ScaleAngle(Quaternion q, float gain)
    {
        q = Positive(HandRotationMapper.Normalize(q));
        var sinHalf = Mathf.Sqrt(Mathf.Max(0f, 1f - q.w * q.w));
        if (sinHalf < .0001f) return new Quaternion(0, 0, 0, 1);
        var half = Mathf.Acos(Mathf.Clamp(q.w, -1f, 1f)) * gain;
        var s = Mathf.Sin(half) / sinHalf;
        return new Quaternion(q.x * s, q.y * s, q.z * s, Mathf.Cos(half));
    }

    /// <summary>Angle in degrees of the rotation, for diagnostics and tests.</summary>
    public static float Angle(Quaternion q)
    {
        q = Positive(HandRotationMapper.Normalize(q));
        return 2f * Mathf.Acos(Mathf.Clamp(q.w, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private static Quaternion Positive(Quaternion q) => q.w < 0f ? new Quaternion(-q.x, -q.y, -q.z, -q.w) : q;
    private static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
}

/// <summary>Thumbstick thrust steered by where the stick's controller points.</summary>
public static class HandThrust
{
    /// <summary>
    /// Stick forward thrusts along the controller's aim, stick sideways along its right. Both the aim
    /// rotation and the result are in tracking space, which this mod treats as the jetpack body frame:
    /// x right, y up, z forward. Magnitude is capped at 1.
    /// </summary>
    public static Vector3 Resolve(Vector2 stick, Quaternion aimRotation)
    {
        if (!MovementInput.Finite(stick)) return Vector3.zero;
        var local = new Vector3(stick.x, 0f, stick.y);
        var thrust = HandRotationMapper.Normalize(aimRotation) * local;
        if (!HandRotationMapper.Finite(thrust)) return Vector3.zero;
        var magnitude = thrust.magnitude;
        return magnitude > 1f ? thrust / magnitude : thrust;
    }
}
