#pragma warning disable 0649

using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(RetrowavePlayerController))]
    public sealed class RarePowerUpInventory : NetworkBehaviour
    {
        [SerializeField] private bool _allowReplacement;

        private readonly NetworkVariable<int> _heldType = new(
            (int)RetrowaveRarePowerUpType.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private RarePowerUpBase[] _powerUps;
        private RetrowavePlayerController _player;

        public static event Action<RarePowerUpInventory, RetrowaveRarePowerUpType> HeldPowerUpChanged;

        public RetrowaveRarePowerUpType HeldType => (RetrowaveRarePowerUpType)_heldType.Value;
        public bool HasHeldPowerUp => HeldType != RetrowaveRarePowerUpType.None;
        public bool AllowReplacement => _allowReplacement;

        private void Awake()
        {
            _player = GetComponent<RetrowavePlayerController>();
            _powerUps = GetComponents<RarePowerUpBase>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _heldType.OnValueChanged += HandleHeldTypeChanged;
            HeldPowerUpChanged?.Invoke(this, HeldType);
        }

        public override void OnNetworkDespawn()
        {
            _heldType.OnValueChanged -= HandleHeldTypeChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsOwner || !IsSpawned || !HasHeldPowerUp || _player == null || !_player.IsArenaParticipant)
            {
                return;
            }

            if (RetrowaveGameBootstrap.IsGameplayInputBlocked())
            {
                return;
            }

            var activatePressed = false;
            var keyboard = Keyboard.current;

            if (keyboard != null)
            {
                activatePressed = RetrowaveInputBindings.WasPressedThisFrame(keyboard, RetrowaveBindingAction.ActivateRarePowerUp);
            }

            var gamepad = Gamepad.current;

            if (gamepad != null)
            {
                activatePressed |= gamepad.buttonNorth.wasPressedThisFrame;
            }

            if (activatePressed)
            {
                RequestActivateRarePowerUpServerRpc();
            }
        }

        public bool CanAcceptServer(RetrowaveRarePowerUpType type, bool allowReplacementOverride)
        {
            return IsServer
                   && type != RetrowaveRarePowerUpType.None
                   && (!HasHeldPowerUp || _allowReplacement || allowReplacementOverride);
        }

        public bool TryGrantServer(RetrowaveRarePowerUpType type, bool allowReplacementOverride)
        {
            if (!CanAcceptServer(type, allowReplacementOverride))
            {
                return false;
            }

            _heldType.Value = (int)type;
            return true;
        }

        public void ClearServer()
        {
            if (!IsServer)
            {
                return;
            }

            _heldType.Value = (int)RetrowaveRarePowerUpType.None;
        }

        [ServerRpc]
        private void RequestActivateRarePowerUpServerRpc()
        {
            if (!IsServer || _player == null || !_player.IsArenaParticipant)
            {
                return;
            }

            var heldType = HeldType;

            if (heldType == RetrowaveRarePowerUpType.None)
            {
                return;
            }

            var powerUp = ResolvePowerUp(heldType);

            if (powerUp == null || !powerUp.ActivateServer(_player))
            {
                return;
            }

            _heldType.Value = (int)RetrowaveRarePowerUpType.None;
        }

        private RarePowerUpBase ResolvePowerUp(RetrowaveRarePowerUpType type)
        {
            if (_powerUps == null || _powerUps.Length == 0)
            {
                _powerUps = GetComponents<RarePowerUpBase>();
            }

            for (var i = 0; i < _powerUps.Length; i++)
            {
                var powerUp = _powerUps[i];

                if (powerUp != null && powerUp.RarePowerUpType == type)
                {
                    return powerUp;
                }
            }

            return null;
        }

        private void HandleHeldTypeChanged(int _, int nextValue)
        {
            HeldPowerUpChanged?.Invoke(this, (RetrowaveRarePowerUpType)nextValue);
        }
    }
}
