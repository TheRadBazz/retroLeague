using UnityEngine;

namespace RetrowaveRocket
{
    public static class RetrowaveVehicleMovementCore
    {
        private static readonly Vector3[] GroundProbeLocalOffsets =
        {
            new Vector3(-0.48f, -0.08f, 0.74f),
            new Vector3(0.48f, -0.08f, 0.74f),
            new Vector3(-0.48f, -0.08f, -0.74f),
            new Vector3(0.48f, -0.08f, -0.74f),
        };

        public const float GroundedGraceSeconds = 0.14f;
        public const float JumpBoostCost = 10f;
        public const float GlideBoostDrain = 18f;
        public const float BoostForce = 34f;
        public const float BoostDrainRate = 28f;
        public const float BoostRechargeDelaySeconds = 1.25f;
        public const float MaxDriveSpeed = 29f;
        public const float MaxReverseSpeed = 15f;
        public const float MaxBoostSpeed = 38f;
        public const float BoostStartThreshold = 0.6f;

        private const float ProbeCastStart = 0.38f;
        private const float ProbeRayLength = 1.4f;
        private const float MinDriveableGroundNormalY = 0.52f;
        private const float MinSuspensionAlignment = 0.18f;
        private const float RideHeight = 0.58f;
        private const float SuspensionSpring = 104f;
        private const float SuspensionDamping = 14.5f;
        private const float GroundDriveAcceleration = 58f;
        private const float GroundReverseAcceleration = 39f;
        private const float GroundGrip = 24f;
        private const float GroundSteeringTorque = 14.5f;
        private const float GroundAlignTorque = 36f;
        private const float GroundAngularDamping = 4.6f;
        private const float GroundYawActiveDamping = 1.4f;
        private const float GroundYawReleaseDamping = 7.2f;
        private const float GroundDrag = 1.45f;
        private const float GroundStickForce = 13f;
        private const float JumpImpulse = 9.35f;
        private const float AirPitchTorque = 12.5f;
        private const float AirYawTorque = 11f;
        private const float AirRollTorque = 13.5f;
        private const float AirForwardThrust = 14f;
        private const float AirStrafeThrust = 10.5f;
        private const float AirHoverBurst = 3.4f;
        private const float AirBrakeDamping = 1.05f;
        private const float AirAngularDamping = 2.8f;
        private const float AirAutoLevelTorque = 6.5f;
        private const float AirYawStabilizeTorque = 2.6f;
        private const float AirLateralDamping = 0.55f;
        private const float AirGravityAssist = 4.5f;
        private const float GlideForwardAcceleration = 8.5f;
        private const float GlideSteerAcceleration = 5.5f;
        private const float GlideVerticalAssist = 9.5f;
        private const float GlideMaxFallSpeed = 5.2f;
        private const float GlideActivationMaxUpwardSpeed = 0.2f;
        private const float GroundVelocityTurnAssist = 5.2f;

        public static bool ApplyGroundProbes(
            Rigidbody body,
            Transform vehicleTransform,
            ref Vector3 groundNormal,
            ref bool isGrounded,
            ref float coyoteTimer,
            ref int groundProbeCount)
        {
            groundProbeCount = 0;
            var hitNormalSum = Vector3.zero;
            var transformUp = vehicleTransform.up;

            for (var i = 0; i < GroundProbeLocalOffsets.Length; i++)
            {
                var probeBase = vehicleTransform.TransformPoint(GroundProbeLocalOffsets[i]);
                var rayOrigin = probeBase + transformUp * ProbeCastStart;

                if (!Physics.Raycast(rayOrigin, -transformUp, out var hit, ProbeCastStart + ProbeRayLength, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody == body)
                {
                    continue;
                }

                if (hit.normal.y < MinDriveableGroundNormalY || Vector3.Dot(transformUp, hit.normal) < MinSuspensionAlignment)
                {
                    continue;
                }

                var distance = Mathf.Max(0f, hit.distance - ProbeCastStart);
                var compression = RideHeight - distance;

                if (compression <= -0.08f)
                {
                    continue;
                }

                var pointVelocity = body.GetPointVelocity(probeBase);
                var springVelocity = Vector3.Dot(pointVelocity, transformUp);
                var springForce = compression * SuspensionSpring - springVelocity * SuspensionDamping;
                body.AddForceAtPosition(transformUp * springForce, probeBase, ForceMode.Acceleration);

                hitNormalSum += hit.normal;
                groundProbeCount++;
            }

            if (groundProbeCount > 0)
            {
                var averagedNormal = (hitNormalSum / groundProbeCount).normalized;
                groundNormal = Vector3.Slerp(groundNormal, averagedNormal, 0.45f);
                isGrounded = true;
                coyoteTimer = GroundedGraceSeconds;
                return true;
            }

            isGrounded = false;
            groundNormal = Vector3.Slerp(groundNormal, Vector3.up, Time.fixedDeltaTime * 8f);
            coyoteTimer = Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);
            return false;
        }

        public static void SimulateGrounded(
            Rigidbody body,
            Transform vehicleTransform,
            Vector3 groundNormal,
            RetrowavePlayerInputState input,
            float speedMultiplier,
            float gripMultiplier)
        {
            var surfaceForward = Vector3.ProjectOnPlane(vehicleTransform.forward, groundNormal);
            var surfaceRight = Vector3.ProjectOnPlane(vehicleTransform.right, groundNormal);

            if (surfaceForward.sqrMagnitude < 0.001f)
            {
                surfaceForward = Vector3.ProjectOnPlane(Vector3.forward, groundNormal);
            }

            if (surfaceRight.sqrMagnitude < 0.001f)
            {
                surfaceRight = Vector3.ProjectOnPlane(Vector3.right, groundNormal);
            }

            surfaceForward.Normalize();
            surfaceRight.Normalize();

            var planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, groundNormal);
            var forwardSpeed = Vector3.Dot(planarVelocity, surfaceForward);
            var lateralSpeed = Vector3.Dot(planarVelocity, surfaceRight);
            var desiredSpeed = input.Throttle >= 0f
                ? input.Throttle * MaxDriveSpeed * speedMultiplier
                : input.Throttle * MaxReverseSpeed;
            var accel = input.Throttle >= 0f ? GroundDriveAcceleration : GroundReverseAcceleration;
            var forwardDelta = desiredSpeed - forwardSpeed;
            var maxStep = accel * Time.fixedDeltaTime;
            forwardDelta = Mathf.Clamp(forwardDelta, -maxStep, maxStep);
            var lateralCorrection = Mathf.Clamp(-lateralSpeed, -GroundVelocityTurnAssist, GroundVelocityTurnAssist);

            body.AddForce(surfaceForward * forwardDelta, ForceMode.VelocityChange);
            body.AddForce(surfaceRight * lateralCorrection, ForceMode.VelocityChange);
            body.AddForce(-surfaceRight * lateralSpeed * GroundGrip * gripMultiplier, ForceMode.Acceleration);
            body.AddForce(-planarVelocity * GroundDrag * Mathf.Lerp(1f, gripMultiplier, 0.45f), ForceMode.Acceleration);
            body.AddForce(-groundNormal * GroundStickForce, ForceMode.Acceleration);

            if (Mathf.Abs(input.Steer) > 0.02f)
            {
                var directionScale = forwardSpeed >= -0.4f ? 1f : -0.75f;
                var steerScale = 0.25f + Mathf.Clamp01(Mathf.Abs(forwardSpeed) / MaxDriveSpeed);
                body.AddTorque(groundNormal * (input.Steer * GroundSteeringTorque * steerScale * directionScale), ForceMode.Acceleration);
            }

            var alignAxis = Vector3.Cross(vehicleTransform.up, groundNormal);
            body.AddTorque(alignAxis * GroundAlignTorque, ForceMode.Acceleration);

            var localAngularVelocity = vehicleTransform.InverseTransformDirection(body.angularVelocity);
            body.AddTorque(vehicleTransform.right * (-localAngularVelocity.x * GroundAngularDamping), ForceMode.Acceleration);
            body.AddTorque(vehicleTransform.forward * (-localAngularVelocity.z * GroundAngularDamping), ForceMode.Acceleration);

            var groundYawVelocity = Vector3.Dot(body.angularVelocity, groundNormal);
            var yawDamping = Mathf.Abs(input.Steer) > 0.02f ? GroundYawActiveDamping : GroundYawReleaseDamping;
            body.AddTorque(groundNormal * (-groundYawVelocity * yawDamping), ForceMode.Acceleration);

            if (input.Brake)
            {
                body.AddForce(-planarVelocity * 2.6f, ForceMode.Acceleration);
            }
        }

        public static void SimulateAirborne(Rigidbody body, Transform vehicleTransform, RetrowavePlayerInputState input)
        {
            body.AddTorque(vehicleTransform.right * (-input.Throttle * AirPitchTorque), ForceMode.Acceleration);
            body.AddTorque(vehicleTransform.up * (input.Steer * AirYawTorque), ForceMode.Acceleration);
            body.AddTorque(vehicleTransform.forward * (-input.Roll * AirRollTorque), ForceMode.Acceleration);

            var airThrust = vehicleTransform.forward * (input.Throttle * AirForwardThrust);
            airThrust += vehicleTransform.right * (input.Steer * AirStrafeThrust);
            body.AddForce(airThrust, ForceMode.Acceleration);

            var localAngularVelocity = vehicleTransform.InverseTransformDirection(body.angularVelocity);
            body.AddTorque(vehicleTransform.right * (-localAngularVelocity.x * AirAngularDamping), ForceMode.Acceleration);
            body.AddTorque(vehicleTransform.forward * (-localAngularVelocity.z * AirAngularDamping), ForceMode.Acceleration);
            body.AddTorque(vehicleTransform.up * (-localAngularVelocity.y * AirYawStabilizeTorque), ForceMode.Acceleration);

            var levelAxis = Vector3.Cross(vehicleTransform.up, Vector3.up);
            var controlIntent = Mathf.Abs(input.Throttle) + Mathf.Abs(input.Steer) + Mathf.Abs(input.Roll);
            var levelBlend = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(controlIntent));
            body.AddTorque(levelAxis * (AirAutoLevelTorque * levelBlend), ForceMode.Acceleration);

            var sidewaysVelocity = Vector3.Project(body.linearVelocity, vehicleTransform.right);
            body.AddForce(-sidewaysVelocity * AirLateralDamping, ForceMode.Acceleration);
            body.AddForce(Vector3.down * AirGravityAssist, ForceMode.Acceleration);

            if (input.Brake)
            {
                body.AddForce(-body.linearVelocity * AirBrakeDamping, ForceMode.Acceleration);
            }
        }

        public static bool TryApplyJump(
            Rigidbody body,
            Transform vehicleTransform,
            RetrowavePlayerInputState input,
            bool treatedAsGrounded,
            float boostAmount,
            ref bool isGrounded,
            ref int groundProbeCount,
            ref float coyoteTimer)
        {
            if (!input.JumpPressed || boostAmount < JumpBoostCost)
            {
                return false;
            }

            if (treatedAsGrounded)
            {
                body.AddForce(vehicleTransform.up * JumpImpulse, ForceMode.VelocityChange);
                coyoteTimer = 0f;
                isGrounded = false;
                groundProbeCount = 0;
                return true;
            }

            body.AddForce(vehicleTransform.up * AirHoverBurst, ForceMode.VelocityChange);
            return true;
        }

        public static bool CanApplyGlide(
            RetrowavePlayerInputState input,
            bool glideRequiresRelease,
            float boostAmount,
            float verticalVelocity)
        {
            return input.JumpHeld
                   && !glideRequiresRelease
                   && boostAmount > BoostStartThreshold
                   && verticalVelocity <= GlideActivationMaxUpwardSpeed;
        }

        public static void ApplyGlideForces(Rigidbody body, Transform vehicleTransform, RetrowavePlayerInputState input)
        {
            var verticalVelocity = Vector3.Dot(body.linearVelocity, Vector3.up);
            var glideForward = Vector3.ProjectOnPlane(vehicleTransform.forward, Vector3.up);

            if (glideForward.sqrMagnitude > 0.001f)
            {
                body.AddForce(glideForward.normalized * GlideForwardAcceleration, ForceMode.Acceleration);
            }

            var glideRight = Vector3.ProjectOnPlane(vehicleTransform.right, Vector3.up);

            if (glideRight.sqrMagnitude > 0.001f && Mathf.Abs(input.Steer) > 0.02f)
            {
                body.AddForce(glideRight.normalized * (input.Steer * GlideSteerAcceleration), ForceMode.Acceleration);
            }

            if (verticalVelocity < -GlideMaxFallSpeed)
            {
                body.AddForce(Vector3.up * (-GlideMaxFallSpeed - verticalVelocity), ForceMode.VelocityChange);
            }

            if (verticalVelocity <= 0f)
            {
                body.AddForce(Vector3.up * GlideVerticalAssist, ForceMode.Acceleration);
            }
        }

        public static void ApplyBoostForce(
            Rigidbody body,
            Transform vehicleTransform,
            Vector3 groundNormal,
            bool treatedAsGrounded,
            float forceMultiplier)
        {
            var forceDirection = treatedAsGrounded
                ? Vector3.ProjectOnPlane(vehicleTransform.forward, groundNormal).normalized
                : vehicleTransform.forward;

            if (forceDirection.sqrMagnitude < 0.001f)
            {
                forceDirection = vehicleTransform.forward;
            }

            body.AddForce(forceDirection * (BoostForce * forceMultiplier), ForceMode.Acceleration);
        }

        public static void ClampVelocity(Rigidbody body, float maxVelocity)
        {
            if (body.linearVelocity.sqrMagnitude > maxVelocity * maxVelocity)
            {
                body.linearVelocity = body.linearVelocity.normalized * maxVelocity;
            }
        }
    }
}
