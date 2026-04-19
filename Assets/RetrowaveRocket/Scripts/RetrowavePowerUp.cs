using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public sealed class RetrowavePowerUp : NetworkBehaviour
    {
        private readonly NetworkVariable<int> _powerUpType = new(
            (int)RetrowavePowerUpType.BoostRefill,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isAvailable = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private MeshRenderer _renderer;
        private Collider _trigger;
        private Light _glow;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _trigger = GetComponent<Collider>();

            var glowObject = new GameObject("PowerUp Glow");
            glowObject.transform.SetParent(transform, false);
            _glow = glowObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.range = 8f;
            _glow.intensity = 7f;
        }

        private void Update()
        {
            if (!IsSpawned || IsServer)
            {
                transform.Rotate(Vector3.up, 100f * Time.deltaTime, Space.Self);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _powerUpType.OnValueChanged += HandleValueChanged;
            _isAvailable.OnValueChanged += HandleAvailabilityChanged;
            RefreshVisuals();
        }

        public override void OnNetworkDespawn()
        {
            _powerUpType.OnValueChanged -= HandleValueChanged;
            _isAvailable.OnValueChanged -= HandleAvailabilityChanged;
            base.OnNetworkDespawn();
        }

        public void InitializeServer(RetrowavePowerUpType type)
        {
            if (!IsServer)
            {
                return;
            }

            _powerUpType.Value = (int)type;
            _isAvailable.Value = true;
            RefreshVisuals();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || !_isAvailable.Value)
            {
                return;
            }

            if (!other.TryGetComponent<RetrowavePlayerController>(out var player))
            {
                return;
            }

            player.ApplyPowerUp((RetrowavePowerUpType)_powerUpType.Value);
            _isAvailable.Value = false;
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(RetrowaveArenaConfig.PowerUpRespawnSeconds);
            _powerUpType.Value = Random.value > 0.5f ? (int)RetrowavePowerUpType.BoostRefill : (int)RetrowavePowerUpType.SpeedBurst;
            _isAvailable.Value = true;
        }

        private void HandleValueChanged(int _, int __)
        {
            RefreshVisuals();
        }

        private void HandleAvailabilityChanged(bool _, bool __)
        {
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            if (_renderer == null || _trigger == null || _glow == null)
            {
                return;
            }

            _renderer.enabled = _isAvailable.Value;
            _trigger.enabled = _isAvailable.Value;
            _glow.enabled = _isAvailable.Value;

            var type = (RetrowavePowerUpType)_powerUpType.Value;
            var color = type == RetrowavePowerUpType.BoostRefill
                ? new Color(0.08f, 0.94f, 1f)
                : new Color(1f, 0.32f, 0.7f);

            _renderer.sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                color * 0.35f,
                color * 2.4f,
                0.92f,
                0f);
            _glow.color = color;
        }
    }
}
