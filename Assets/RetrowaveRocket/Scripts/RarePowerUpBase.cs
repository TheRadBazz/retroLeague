using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public abstract class RarePowerUpBase : NetworkBehaviour, IRarePowerUp
    {
        public abstract RetrowaveRarePowerUpType RarePowerUpType { get; }

        public abstract bool ActivateServer(RetrowavePlayerController owner);

        protected static bool IsValidOwner(RetrowavePlayerController owner)
        {
            return owner != null
                   && owner.IsRuntimeActive
                   && owner.HasSimulationAuthority
                   && owner.IsArenaParticipant
                   && owner.Body != null;
        }
    }
}
