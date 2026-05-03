using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RetrowaveBall : NetworkBehaviour
    {
        private Rigidbody _rigidbody;
        private MeshRenderer _renderer;
        private Light _glow;
        private RetrowaveBallStateController _stateController;
        private bool _offlineMode;

        public static RetrowaveBall Instance { get; private set; }
        public Rigidbody Body => _rigidbody;
        public bool IsOfflineMode => _offlineMode;
        private bool HasSimulationAuthority => IsServer || _offlineMode;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _renderer = GetComponent<MeshRenderer>();
            _stateController = GetComponent<RetrowaveBallStateController>();

            var glowObject = new GameObject("Ball Glow");
            glowObject.transform.SetParent(transform, false);
            _glow = glowObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.range = 10f;
            _glow.color = Color.white;
            _glow.intensity = 5f;
        }

        private void Update()
        {
            if (_glow == null || _rigidbody == null)
            {
                return;
            }

            var stateBoost = _stateController != null && _stateController.State != RetrowaveBallState.Normal ? 3.2f : 0f;
            _glow.intensity = 4f + stateBoost + _rigidbody.linearVelocity.magnitude * 0.35f;
        }

        private void FixedUpdate()
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            RetrowaveBallSimulationCore.ClampVelocity(_rigidbody);

            if (RetrowaveBallSimulationCore.ShouldReset(transform.position))
            {
                ResetBall();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            var player = collision.rigidbody != null
                ? collision.rigidbody.GetComponent<RetrowavePlayerController>()
                : collision.collider.GetComponentInParent<RetrowavePlayerController>();

            if (player != null)
            {
                var touchResult = RetrowaveMatchManager.Instance != null
                    ? RetrowaveMatchManager.Instance.RegisterBallTouch(player)
                    : new RetrowaveBallTouchResult(true, false, ulong.MaxValue, 1f);
                ApplyPlayerHit(player, collision, touchResult);
            }
        }

        public void EnableOfflineMode()
        {
            _offlineMode = true;
            Instance = this;

            var networkTransform = GetComponent<NetworkTransform>();

            if (networkTransform != null)
            {
                networkTransform.enabled = false;
            }

            var networkRigidbody = GetComponent<NetworkRigidbody>();

            if (networkRigidbody != null)
            {
                networkRigidbody.enabled = false;
            }

            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;

            if (!IsServer)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        public void ResetBall()
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            RetrowaveBallSimulationCore.ResetBall(_rigidbody, transform, _stateController);
        }

        private void ApplyPlayerHit(RetrowavePlayerController player, Collision collision, RetrowaveBallTouchResult touchResult)
        {
            var result = RetrowaveBallSimulationCore.ApplyPlayerHit(_rigidbody, transform, _stateController, player, collision, touchResult);

            if (!result.HasValue)
            {
                return;
            }

            if (_offlineMode)
            {
                RetrowaveArenaAudio.PlayImpact(result.Value.ContactPoint, result.Value.ImpactIntensity);
            }
            else
            {
                PlayBallImpactClientRpc(result.Value.ContactPoint, result.Value.ImpactIntensity);
            }
        }

        [ClientRpc]
        private void PlayBallImpactClientRpc(Vector3 contactPoint, float intensity)
        {
            RetrowaveArenaAudio.PlayImpact(contactPoint, intensity);
        }
    }
}
