#pragma warning disable 0649

using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public sealed class GravityBombPowerUp : RarePowerUpBase
    {
        [SerializeField] private float _deployBehindDistance = 1.7f;
        [SerializeField] private float _fuseTime = 1.65f;
        [SerializeField] private float _explosionRadius = 9.5f;
        [SerializeField] private float _maxForce = 15f;
        [SerializeField] private float _upwardForceMultiplier = 0.35f;
        [SerializeField] private bool _affectBall = true;
        [SerializeField] private float _ballForceMultiplier = 0.45f;
        [SerializeField] private LayerMask _vehicleLayerMask = ~0;
        [SerializeField] private LayerMask _ballLayerMask = ~0;
        [SerializeField] private AudioClip _deployCue;

        public override RetrowaveRarePowerUpType RarePowerUpType => RetrowaveRarePowerUpType.GravityBomb;

        public override bool ActivateServer(RetrowavePlayerController owner)
        {
            if (!IsValidOwner(owner) || RetrowaveGameBootstrap.Instance == null)
            {
                return false;
            }

            var bombObject = RetrowaveGameBootstrap.Instance.CreateGravityBombDeviceInstance();

            if (bombObject == null)
            {
                return false;
            }

            var position = owner.transform.position - owner.transform.forward * Mathf.Max(0f, _deployBehindDistance);
            position.y = Mathf.Max(position.y, RetrowaveArenaConfig.GetSurfaceHeight(position.x, position.z) + 0.35f);
            bombObject.transform.SetPositionAndRotation(position, Quaternion.identity);
            RetrowaveGameBootstrap.Instance.MoveRuntimeInstanceToGameplayScene(bombObject);

            var networkObject = bombObject.GetComponent<NetworkObject>();
            var bomb = bombObject.GetComponent<GravityBombDevice>();

            if (networkObject == null || bomb == null)
            {
                Destroy(bombObject);
                return false;
            }

            networkObject.Spawn();
            bomb.InitializeServer(
                owner.OwnerClientId,
                owner.Team,
                _fuseTime,
                _explosionRadius,
                _maxForce,
                _upwardForceMultiplier,
                _vehicleLayerMask,
                _ballLayerMask,
                _affectBall,
                _ballForceMultiplier);
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
