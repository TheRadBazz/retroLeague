#pragma warning disable 0649

using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public sealed class ChronoDomePowerUp : RarePowerUpBase
    {
        [SerializeField] private float _deployForwardDistance = 2.5f;
        [SerializeField] private float _radius = 10f;
        [SerializeField] private float _duration = 6f;
        [SerializeField] private float _enemyMovementMultiplier = 0.48f;
        [SerializeField] private float _enemySteeringMultiplier = 0.55f;
        [SerializeField] private bool _affectFriendlyPlayers;
        [SerializeField] private bool _hardFreeze;
        [SerializeField] private bool _affectBall = true;
        [SerializeField] private float _ballVelocityDampingPerTick = 0.055f;
        [SerializeField] private float _tickRate = 8f;
        [SerializeField] private AudioClip _deployCue;

        public override RetrowaveRarePowerUpType RarePowerUpType => RetrowaveRarePowerUpType.ChronoDome;

        public override bool ActivateServer(RetrowavePlayerController owner)
        {
            if (!IsValidOwner(owner) || RetrowaveGameBootstrap.Instance == null)
            {
                return false;
            }

            var domeObject = RetrowaveGameBootstrap.Instance.CreateChronoDomeFieldInstance();

            if (domeObject == null)
            {
                return false;
            }

            var position = owner.transform.position + owner.transform.forward * Mathf.Max(0f, _deployForwardDistance);
            position.y = RetrowaveArenaConfig.GetSurfaceHeight(position.x, position.z) + 0.25f;
            domeObject.transform.SetPositionAndRotation(position, Quaternion.identity);
            RetrowaveGameBootstrap.Instance.MoveRuntimeInstanceToGameplayScene(domeObject);

            var networkObject = domeObject.GetComponent<NetworkObject>();
            var field = domeObject.GetComponent<ChronoDomeField>();

            if (networkObject == null || field == null)
            {
                Destroy(domeObject);
                return false;
            }

            networkObject.Spawn();
            field.InitializeServer(
                owner.OwnerClientId,
                owner.Team,
                _radius,
                _duration * owner.StatusEffectDurationMultiplier,
                _enemyMovementMultiplier,
                _enemySteeringMultiplier,
                _affectFriendlyPlayers,
                _hardFreeze,
                _affectBall,
                _ballVelocityDampingPerTick,
                _tickRate);
            PlayDeployCueClientRpc(position);
            return true;
        }

        [ClientRpc]
        private void PlayDeployCueClientRpc(Vector3 position)
        {
            if (_deployCue != null)
            {
                AudioSource.PlayClipAtPoint(_deployCue, position, RetrowaveGameSettings.SfxVolume);
            }
        }
    }
}
