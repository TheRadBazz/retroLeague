using UnityEngine;

namespace RetrowaveRocket
{
    public readonly struct RetrowaveBallHitSimulationResult
    {
        public RetrowaveBallHitSimulationResult(Vector3 contactPoint, float impactIntensity)
        {
            ContactPoint = contactPoint;
            ImpactIntensity = impactIntensity;
        }

        public Vector3 ContactPoint { get; }
        public float ImpactIntensity { get; }
    }

    public static class RetrowaveBallSimulationCore
    {
        public const float MaxBallSpeed = 44f;

        private const float BaseHitSpeed = 1.8f;
        private const float MinimumTouchSpeed = 0.85f;
        private const float SpeedTransferScale = 0.46f;
        private const float ContactVelocityInfluence = 0.44f;
        private const float FrontHitInfluence = 0.22f;
        private const float UpwardBias = 0.14f;
        private const float TopTouchLift = 0.52f;
        private const float CenterTouchLift = 0.16f;
        private const float MaxHitVelocityChange = 12.5f;
        private const float MaxHitSpin = 18f;
        private const float TopTouchJuggleBoost = 0.8f;
        private const float StrongHitMomentumThreshold = 10.5f;
        private const float StrongHitApproachThreshold = 8f;
        private const float PlayerMomentumCarry = 0.24f;
        private const float PlayerMinimumCarry = 0.75f;
        private const float PlayerForwardRetention = 0.94f;

        public static void ClampVelocity(Rigidbody body)
        {
            if (body == null)
            {
                return;
            }

            if (body.linearVelocity.sqrMagnitude > MaxBallSpeed * MaxBallSpeed)
            {
                body.linearVelocity = body.linearVelocity.normalized * MaxBallSpeed;
            }
        }

        public static bool ShouldReset(Vector3 position)
        {
            return position.y < -14f || position.magnitude > 240f;
        }

        public static void ResetBall(Rigidbody body, Transform ballTransform, RetrowaveBallStateController stateController)
        {
            if (body == null || ballTransform == null)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            stateController?.ResetStateServer();
            ballTransform.SetPositionAndRotation(RetrowaveArenaConfig.BallSpawnPoint, Quaternion.identity);
            body.position = RetrowaveArenaConfig.BallSpawnPoint;
            body.rotation = Quaternion.identity;
            Physics.SyncTransforms();
        }

        public static RetrowaveBallHitSimulationResult? ApplyPlayerHit(
            Rigidbody ballBody,
            Transform ballTransform,
            RetrowaveBallStateController stateController,
            RetrowavePlayerController player,
            Collision collision,
            RetrowaveBallTouchResult touchResult)
        {
            if (ballBody == null || ballTransform == null || player == null || player.Body == null)
            {
                return null;
            }

            var contactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : ballTransform.position;
            var contactVelocity = player.Body.GetPointVelocity(contactPoint);
            var relativeContactVelocity = contactVelocity - ballBody.GetPointVelocity(contactPoint);
            var playerVelocity = player.Body.linearVelocity;
            var playerForward = player.transform.forward;
            var hitOffset = ballTransform.position - contactPoint;
            var contactNormal = hitOffset.sqrMagnitude > 0.0001f
                ? hitOffset.normalized
                : (ballTransform.position - player.transform.position).normalized;
            var relativeVelocityDirection = relativeContactVelocity.sqrMagnitude > 0.0001f
                ? relativeContactVelocity.normalized
                : playerForward;
            var localBallPosition = player.transform.InverseTransformPoint(ballTransform.position);
            var frontFactor = Mathf.Clamp01((localBallPosition.z + 0.35f) / 1.45f);
            var topFactor = Mathf.Clamp01((localBallPosition.y + 0.05f) / 1.2f);
            var centerFactor = 1f - Mathf.Clamp01(Mathf.Abs(localBallPosition.x) / 0.95f);

            if (contactNormal.sqrMagnitude < 0.001f)
            {
                contactNormal = playerForward;
            }

            var liftAmount = UpwardBias + topFactor * TopTouchLift + centerFactor * CenterTouchLift;
            var launchDirection = (
                contactNormal * 0.62f
                + relativeVelocityDirection * ContactVelocityInfluence
                + playerForward * (FrontHitInfluence + frontFactor * 0.18f)
                + Vector3.up * liftAmount).normalized;

            if (topFactor > 0.35f && centerFactor > 0.25f)
            {
                var juggleDirection = (Vector3.up * 0.82f + playerForward * 0.28f).normalized;
                launchDirection = Vector3.Slerp(launchDirection, juggleDirection, topFactor * centerFactor * 0.45f);
            }

            var playerSpeedContribution = playerVelocity.magnitude * SpeedTransferScale;
            var closingContribution = Mathf.Max(0f, Vector3.Dot(relativeContactVelocity, contactNormal));
            var forwardContribution = Mathf.Max(0f, Vector3.Dot(contactVelocity, playerForward));
            var verticalContribution = Mathf.Max(0f, contactVelocity.y);
            var rawVelocityChange = BaseHitSpeed
                                    + playerSpeedContribution
                                    + closingContribution * 0.18f
                                    + forwardContribution * 0.08f
                                    + verticalContribution * 0.16f
                                    + topFactor * centerFactor * TopTouchJuggleBoost;
            var impactStrength = Mathf.InverseLerp(
                2.25f,
                StrongHitMomentumThreshold,
                playerVelocity.magnitude + closingContribution * 0.65f + forwardContribution * 0.2f);
            var approachStrength = Mathf.InverseLerp(1.5f, StrongHitApproachThreshold, closingContribution);
            var touchStrength = Mathf.Clamp01(impactStrength * 0.7f + approachStrength * 0.3f);
            var desiredVelocityChange = Mathf.Lerp(MinimumTouchSpeed, rawVelocityChange, touchStrength);
            desiredVelocityChange = Mathf.Clamp(desiredVelocityChange, MinimumTouchSpeed, MaxHitVelocityChange);
            desiredVelocityChange *= touchResult.HitMultiplier;
            desiredVelocityChange *= player.GetBallHitPowerMultiplier(ballTransform.position);

            if (!player.IsGroundedForHud)
            {
                player.AwardStyleServer(RetrowaveStyleEvent.AerialTouch);
            }
            else
            {
                player.AwardStyleServer(RetrowaveStyleEvent.ControlledTouch);
            }

            stateController?.ModifyHitServer(
                player,
                touchResult.IsTeamCombo,
                ref launchDirection,
                ref desiredVelocityChange,
                touchStrength);
            desiredVelocityChange = Mathf.Clamp(desiredVelocityChange, MinimumTouchSpeed, MaxHitVelocityChange * 1.45f);
            var impactIntensity = Mathf.Clamp01(touchStrength + desiredVelocityChange / (MaxHitVelocityChange * 1.45f) * 0.35f);

            ballBody.AddForce(launchDirection * desiredVelocityChange, ForceMode.VelocityChange);
            PreservePlayerMomentum(player, playerForward, contactNormal, desiredVelocityChange, frontFactor, centerFactor);

            var spinAxis = Vector3.Cross(contactNormal, relativeVelocityDirection);
            var hitSpin = spinAxis * Mathf.Min(relativeContactVelocity.magnitude * 0.42f + player.Body.angularVelocity.magnitude * 0.28f, MaxHitSpin);

            if (float.IsFinite(hitSpin.x) && float.IsFinite(hitSpin.y) && float.IsFinite(hitSpin.z))
            {
                ballBody.AddTorque(hitSpin, ForceMode.VelocityChange);
            }

            return new RetrowaveBallHitSimulationResult(contactPoint, impactIntensity);
        }

        private static void PreservePlayerMomentum(
            RetrowavePlayerController player,
            Vector3 playerForward,
            Vector3 contactNormal,
            float desiredVelocityChange,
            float frontFactor,
            float centerFactor)
        {
            if (player.Body == null)
            {
                return;
            }

            var currentVelocity = player.Body.linearVelocity;
            var forwardSpeed = Mathf.Max(0f, Vector3.Dot(currentVelocity, playerForward));
            var retainVelocity = Mathf.Max(0f, forwardSpeed * PlayerForwardRetention);
            var sustainVelocity = Mathf.Max(
                PlayerMinimumCarry,
                desiredVelocityChange * PlayerMomentumCarry + forwardSpeed * 0.05f);
            sustainVelocity *= Mathf.Lerp(0.72f, 1.08f, Mathf.Clamp01(frontFactor * 0.75f + centerFactor * 0.25f));

            if (forwardSpeed < retainVelocity)
            {
                player.Body.AddForce(playerForward * (retainVelocity - forwardSpeed), ForceMode.VelocityChange);
            }

            player.Body.AddForce(playerForward * sustainVelocity, ForceMode.VelocityChange);

            var backwardLoss = Vector3.Dot(player.Body.linearVelocity, -contactNormal);

            if (backwardLoss > 0.1f)
            {
                player.Body.AddForce(contactNormal * Mathf.Min(backwardLoss * 0.55f, 2.2f), ForceMode.VelocityChange);
            }
        }
    }
}
