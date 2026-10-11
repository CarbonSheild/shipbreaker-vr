using System;
using ShipbreakerVr.Tracking;
using UnityEngine;

internal static class HandMovementChecks
{
    private static readonly Quaternion Identity = new Quaternion(0, 0, 0, 1);
    // Linear curve so expected travel fractions are exact.
    private static readonly HandRotationSettings Linear = new HandRotationSettings(5f, 30f, 5f, 1f);

    // Unit quaternion for a rotation of `degrees` about `axis`, built without native Unity calls.
    private static Quaternion About(Vector3 axis, float degrees)
    {
        var half = degrees * Mathf.Deg2Rad * .5f;
        var s = Mathf.Sin(half);
        return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(half));
    }

    private static TrackedPose Pose(Quaternion rotation) => new TrackedPose(Vector3.zero, rotation);
    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < .001f;
    private static bool Near(Vector3 actual, Vector3 expected) => (actual - expected).magnitude < .001f;

    internal static void Run(Action<bool, string> check)
    {
        var mapper = new HandRotationMapper();
        check(!mapper.Sample(false, Pose(Identity), Linear).Active && !mapper.Anchored, "Hand rotation is inactive until the hold button is pressed");
        check(!mapper.Sample(true, default, Linear).Active, "Holding with no valid pose does not anchor");
        var first = mapper.Sample(true, Pose(Identity), Linear);
        check(first.Active && mapper.Engaged && mapper.Anchored && first.Turn == Vector2.zero && first.Roll == 0f, "First held sample anchors, reports engagement once and produces no rotation");
        var still = mapper.Sample(true, Pose(Identity), Linear);
        check(still.Active && !mapper.Engaged && still.Turn == Vector2.zero, "Holding still stays active without repeating engagement");

        var yaw = mapper.Sample(true, Pose(About(Vector3.up, 20)), Linear);
        check(Near(yaw.Turn.x, .6f) && Near(yaw.Turn.y, 0) && Near(yaw.Roll, 0), "Turning the controller right yaws right without pitch or roll");
        var pitch = mapper.Sample(true, Pose(About(Vector3.right, -20)), Linear);
        check(Near(pitch.Turn.y, .6f) && Near(pitch.Turn.x, 0) && Near(pitch.Roll, 0), "Tilting the controller nose-up pitches up");
        var roll = mapper.Sample(true, Pose(About(Vector3.forward, -20)), Linear);
        check(Near(roll.Roll, .6f) && Near(roll.Turn.x, 0) && Near(roll.Turn.y, 0), "Twisting the controller clockwise rolls right");
        check(mapper.Sample(true, Pose(About(Vector3.up, 3)), Linear).Turn == Vector2.zero, "Small wrist wobble inside the angular deadzone is ignored");
        check(Near(mapper.Sample(true, Pose(About(Vector3.up, -60)), Linear).Turn.x, -1f), "Large left turn clamps to full yaw rate");

        var released = mapper.Sample(false, Pose(About(Vector3.up, 45)), Linear);
        check(!released.Active && !mapper.Anchored, "Releasing the hold stops rotation and drops the anchor");
        var reanchored = mapper.Sample(true, Pose(About(Vector3.up, 45)), Linear);
        check(reanchored.Active && mapper.Engaged && reanchored.Turn == Vector2.zero, "Pressing again anchors at the new orientation");
        check(Near(mapper.Sample(true, Pose(About(Vector3.up, 65)), Linear).Turn.x, .6f), "Rotation is measured from the anchor orientation, not from world forward");

        var stiffRoll = new HandRotationSettings(5f, 30f, 20f, 1f);
        var twist = new HandRotationMapper();
        twist.Sample(true, Pose(Identity), stiffRoll);
        check(twist.Sample(true, Pose(About(Vector3.forward, -15)), stiffRoll).Roll == 0f, "Twist inside the larger roll deadzone does not roll");
        check(Near(twist.Sample(true, Pose(About(Vector3.forward, -25)), stiffRoll).Roll, .5f), "Roll starts only past its own deadzone");
        check(Near(twist.Sample(true, Pose(About(Vector3.up, 20)), stiffRoll).Turn.x, .6f), "A stiffer roll deadzone does not affect yaw");
        var onOff = new HandRotationSettings(5f, 30f, 40f, 1f);
        var hardRoll = new HandRotationMapper();
        hardRoll.Sample(true, Pose(Identity), onOff);
        check(Near(hardRoll.Sample(true, Pose(About(Vector3.forward, 45)), onOff).Roll, -1f), "Roll deadzone beyond the turn range becomes on/off rather than dividing by zero");

        var curved = new HandRotationSettings(5f, 30f, 5f, 2f);
        var shaped = new HandRotationMapper();
        shaped.Sample(true, Pose(Identity), curved);
        check(Near(shaped.Sample(true, Pose(About(Vector3.up, 17.5f)), curved).Turn.x, .25f), "Response curve keeps half travel gentle");

        var lost = new HandRotationMapper();
        lost.Sample(true, Pose(Identity), Linear);
        check(!lost.Sample(true, default, Linear).Active && !lost.Anchored, "Tracking loss while held stops rotation and drops the anchor");
        check(HandRotationMapper.Scale(new Vector3(float.NaN, 0, 0), 5f, 30f, 1f) == Vector3.zero, "Non-finite angles cannot produce rotation");
        var euler = HandRotationMapper.YawPitchRoll(About(Vector3.up, 90) * About(Vector3.right, 10));
        check(Near(euler.x, 90) && Near(euler.y, -10), "Yaw and pitch decompose independently away from gimbal lock");

        var direct = new DirectRotationMapper();
        check(!direct.Sample(false, Pose(Identity), 1f, 0f).Active && !direct.Anchored, "Direct rotation is inactive until the hold button is pressed");
        var directFirst = direct.Sample(true, Pose(About(Vector3.up, 30)), 1f, 0f);
        check(directFirst.Active && direct.Engaged && Near(DirectRotationMapper.Angle(directFirst.Delta), 0f), "Direct hold anchors at the current controller orientation with no immediate turn");
        var directYaw = direct.Sample(true, Pose(About(Vector3.up, 50)), 1f, 0f);
        check(!direct.Engaged && Near(HandRotationMapper.YawPitchRoll(directYaw.Delta).x, 20f), "Direct rotation follows the controller one to one from the anchor");
        check(Near(HandRotationMapper.YawPitchRoll(direct.Sample(true, Pose(About(Vector3.up, 50)), 2f, 0f).Delta).x, 40f), "Gain scales the applied angle about the same axis");
        check(!direct.Sample(false, Pose(About(Vector3.up, 50)), 1f, 0f).Active && !direct.Anchored, "Releasing drops the direct anchor");
        var ratchet = direct.Sample(true, Pose(About(Vector3.up, 0)), 1f, 0f);
        check(ratchet.Active && direct.Engaged && Near(DirectRotationMapper.Angle(ratchet.Delta), 0f), "Hold again after repositioning starts a fresh anchor (ratchet)");
        var directPitch = direct.Sample(true, Pose(About(Vector3.right, -15)), 1f, 0f);
        check(Near(HandRotationMapper.YawPitchRoll(directPitch.Delta).y, 15f), "Direct pitch follows nose-up as pitch up");
        var deadRoll = new DirectRotationMapper();
        deadRoll.Sample(true, Pose(Identity), 1f, 20f);
        check(Near(DirectRotationMapper.Angle(deadRoll.Sample(true, Pose(About(Vector3.forward, -15)), 1f, 20f).Delta), 0f), "Twist inside the roll deadzone produces no direct rotation");
        var partRoll = HandRotationMapper.YawPitchRoll(deadRoll.Sample(true, Pose(About(Vector3.forward, -30)), 1f, 20f).Delta);
        check(Near(partRoll.z, 10f) && Near(partRoll.x, 0f) && Near(partRoll.y, 0f), "Twist beyond the roll deadzone rolls by the excess only");
        var mixed = HandRotationMapper.YawPitchRoll(deadRoll.Sample(true, Pose(About(Vector3.up, 25) * About(Vector3.forward, -10)), 1f, 20f).Delta);
        check(Near(mixed.x, 25f) && Near(mixed.z, 0f), "Roll deadzone removes small twist without disturbing yaw");
        check(Near(DirectRotationMapper.Angle(DirectRotationMapper.ScaleAngle(About(Vector3.up, 40), .5f)), 20f), "Angle scaling halves a rotation");
        check(Near(DirectRotationMapper.Angle(DirectRotationMapper.ScaleAngle(Identity, 3f)), 0f), "Scaling an identity rotation stays identity");
        check(!new DirectRotationMapper().Sample(true, default, 1f, 0f).Active, "Direct hold with no valid pose does not anchor");

        check(Near(HandThrust.Resolve(new Vector2(0, 1), Identity), Vector3.forward), "Stick forward with the controller level thrusts forward");
        check(Near(HandThrust.Resolve(new Vector2(1, 0), Identity), Vector3.right), "Stick right with the controller level thrusts right");
        check(Near(HandThrust.Resolve(new Vector2(0, 1), About(Vector3.right, -90)), Vector3.up), "Stick forward with the controller pointing up thrusts up");
        check(Near(HandThrust.Resolve(new Vector2(0, 1), About(Vector3.right, 90)), Vector3.down), "Stick forward with the controller pointing down thrusts down");
        check(Near(HandThrust.Resolve(new Vector2(0, 1), About(Vector3.up, 90)), Vector3.right), "Stick forward with the controller pointing right thrusts right");
        check(Near(HandThrust.Resolve(new Vector2(0, -.5f), About(Vector3.up, 90)), Vector3.left * .5f), "Stick back is analog and opposes the pointing direction");
        check(Near(HandThrust.Resolve(new Vector2(1, 0), About(Vector3.forward, 90)), Vector3.up), "Controller rolled on its side turns stick sideways into vertical thrust");
        check(HandThrust.Resolve(Vector2.zero, About(Vector3.up, 37)).magnitude == 0f, "Centered stick thrusts nowhere regardless of pointing direction");
        check(HandThrust.Resolve(new Vector2(1, 1), Identity).magnitude <= 1.0001f, "Diagonal stick cannot exceed full thrust");
        check(HandThrust.Resolve(new Vector2(float.NaN, 1), Identity) == Vector3.zero, "Non-finite stick cannot produce thrust");
        check(HandThrust.Resolve(new Vector2(0, 1), new Quaternion(0, 0, 0, 0)) == Vector3.forward, "Zero quaternion falls back to the body frame instead of producing NaN");
    }
}
