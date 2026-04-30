using UnityEngine;

namespace RetrowaveRocket
{
    public interface IStatusEffectReceiver
    {
        bool ApplyStunServer(float duration, Vector3 spinTorque, ulong sourceId, float immunitySeconds);
        void ApplySlowServer(float duration, float movementMultiplier, float steeringMultiplier, ulong sourceId);
    }
}
