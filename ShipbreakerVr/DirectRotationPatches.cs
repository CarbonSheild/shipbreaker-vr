using BBI;
using HarmonyLib;
using UnityEngine;

namespace ShipbreakerVr;

/// <summary>
/// Applies hand-steered direct rotation after the game's own axis rotation each frame. The game
/// finishes HandleAxisRotation with Rigidbody.MoveRotation, so setting the target here overrides
/// that frame's integration. While held, the game's turning velocities and the rigidbody's angular
/// velocity are zeroed so release leaves no coasting. Outside a hold nothing is touched.
/// </summary>
[HarmonyPatch(typeof(OrientationController), "HandleAxisRotation")]
internal static class DirectRotationPatches
{
    private static Quaternion bodyAnchor;
    private static bool anchoredThisHold;
    private static bool loggedFailure, loggedInterpolation;

    [HarmonyPostfix]
    private static void ApplyDirectRotation(Rigidbody ___mRigidbodyReference, ref Vector3 ___mAxisVelocityX, ref Vector3 ___mAxisVelocityY, ref Vector3 ___mAxisVelocityZ, bool ___mIsReorienting)
    {
        try
        {
            if (!VrMovementControls.DirectRotation(out var delta, out var engaged)) { anchoredThisHold = false; return; }
            if (!___mRigidbodyReference || ___mIsReorienting) return;
            if (engaged || !anchoredThisHold)
            {
                bodyAnchor = ___mRigidbodyReference.rotation;
                anchoredThisHold = true;
            }
            ___mAxisVelocityX = Vector3.zero;
            ___mAxisVelocityY = Vector3.zero;
            ___mAxisVelocityZ = Vector3.zero;
            ___mRigidbodyReference.angularVelocity = Vector3.zero;
            var target = bodyAnchor * delta;
            ___mRigidbodyReference.MoveRotation(target);
            // MoveRotation only takes effect at the next physics step. Without interpolation the rendered
            // body would then lag the hand by up to a physics step, which reads as jitter at low frame rates.
            // Set the transform for this frame as well; physics syncs to it at the next step. With
            // interpolation enabled the rigidbody drives the transform smoothly itself, so leave it alone.
            if (___mRigidbodyReference.interpolation == RigidbodyInterpolation.None)
                ___mRigidbodyReference.transform.rotation = target;
            if (!loggedInterpolation)
            {
                loggedInterpolation = true;
                Debug.Log($"[ShipbreakerVr] Direct rotation active; body rigidbody interpolation={___mRigidbodyReference.interpolation}; fixed step={Time.fixedDeltaTime:F4}s; transform sync={(___mRigidbodyReference.interpolation == RigidbodyInterpolation.None ? "per frame" : "by interpolation")}.");
            }
        }
        catch (System.Exception error)
        {
            anchoredThisHold = false;
            if (loggedFailure) return;
            loggedFailure = true;
            Debug.LogError("[ShipbreakerVr] Direct rotation failed; the game's own rotation remains in charge. " + error);
        }
    }
}
