using UnityEngine;

namespace RetrowaveRocket
{
    public static class ExplosionForceUtility
    {
        public static void ApplyRadialVelocityChange(
            Rigidbody body,
            Vector3 origin,
            float radius,
            float maxForce,
            float upwardMultiplier,
            float forceMultiplier = 1f)
        {
            if (body == null || radius <= 0f || maxForce <= 0f || forceMultiplier <= 0f)
            {
                return;
            }

            var offset = body.worldCenterOfMass - origin;
            var distance = offset.magnitude;

            if (distance > radius)
            {
                return;
            }

            var direction = distance > 0.001f ? offset / distance : Vector3.up;
            direction = (direction + Vector3.up * Mathf.Max(0f, upwardMultiplier)).normalized;
            var falloff = 1f - Mathf.Clamp01(distance / radius);
            body.AddForce(direction * (maxForce * falloff * forceMultiplier), ForceMode.VelocityChange);
        }
    }
}
