#pragma warning disable 0649

using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(SphereCollider))]
    public sealed class ChronoDomeField : NetworkBehaviour
    {
        private const int MaxFieldHits = 96;
        private static readonly Collider[] FieldHits = new Collider[MaxFieldHits];

        private readonly NetworkVariable<float> _radius = new(
            10f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _duration = new(
            6f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [SerializeField] private AudioClip _expireCue;

        private SphereCollider _trigger;
        private MeshRenderer _renderer;
        private Material _material;
        private ulong _ownerClientId;
        private RetrowaveTeam _ownerTeam;
        private float _movementMultiplier;
        private float _steeringMultiplier;
        private bool _affectFriendlyPlayers;
        private bool _affectBall;
        private float _ballVelocityDampingPerTick;
        private float _tickInterval;
        private float _nextTickAt;
        private float _spawnedAt;
        private ulong _sourceId;

        private void Awake()
        {
            _trigger = GetComponent<SphereCollider>();
            _trigger.isTrigger = true;
            _trigger.enabled = false;
            EnsureVisual();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _radius.OnValueChanged += HandleRadiusChanged;
            _spawnedAt = Time.time;
            RefreshRadius();
        }

        public override void OnNetworkDespawn()
        {
            _radius.OnValueChanged -= HandleRadiusChanged;
            base.OnNetworkDespawn();
        }

        private void FixedUpdate()
        {
            if (!IsServer || Time.time < _nextTickAt)
            {
                return;
            }

            _nextTickAt = Time.time + _tickInterval;
            ApplyTickServer();
        }

        private void Update()
        {
            if (_material == null)
            {
                return;
            }

            var progress = _duration.Value <= 0f ? 1f : Mathf.Clamp01((Time.time - _spawnedAt) / _duration.Value);
            var alpha = Mathf.Lerp(0.16f, 0.035f, progress);
            _material.SetColor("_BaseColor", new Color(0.12f, 0.46f, 0.9f, alpha));
            _material.SetColor("_EmissionColor", new Color(0.22f, 0.78f, 1f, 1f) * Mathf.Lerp(2.15f, 0.55f, progress));
        }

        public void InitializeServer(
            ulong ownerClientId,
            RetrowaveTeam ownerTeam,
            float radius,
            float duration,
            float movementMultiplier,
            float steeringMultiplier,
            bool affectFriendlyPlayers,
            bool hardFreeze,
            bool affectBall,
            float ballVelocityDampingPerTick,
            float tickRate)
        {
            if (!IsServer)
            {
                return;
            }

            _ownerClientId = ownerClientId;
            _ownerTeam = ownerTeam;
            _radius.Value = Mathf.Max(0.5f, radius);
            _duration.Value = Mathf.Max(0.25f, duration);
            _movementMultiplier = hardFreeze ? 0f : Mathf.Clamp01(movementMultiplier);
            _steeringMultiplier = hardFreeze ? 0f : Mathf.Clamp01(steeringMultiplier);
            _affectFriendlyPlayers = affectFriendlyPlayers;
            _affectBall = affectBall;
            _ballVelocityDampingPerTick = Mathf.Clamp01(ballVelocityDampingPerTick);
            _tickInterval = 1f / Mathf.Max(1f, tickRate);
            _nextTickAt = Time.time;
            _sourceId = ((ulong)ownerClientId << 32) ^ (ulong)NetworkObjectId;
            RefreshRadius();
            StartCoroutine(ExpireRoutine(_duration.Value));
        }

        private void ApplyTickServer()
        {
            var hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                _radius.Value,
                FieldHits,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var player = ResolvePlayer(FieldHits[i]);

                if (player == null || !CanAffect(player))
                {
                    continue;
                }

                if (player.TryGetComponent<VehicleStatusEffects>(out var statusEffects))
                {
                    statusEffects.ApplySlowServer(
                        _tickInterval * 1.75f,
                        _movementMultiplier,
                        _steeringMultiplier,
                        _sourceId);
                }
            }

            if (_affectBall && RetrowaveBall.Instance != null && RetrowaveBall.Instance.Body != null)
            {
                var ballOffset = RetrowaveBall.Instance.Body.worldCenterOfMass - transform.position;

                if (ballOffset.sqrMagnitude <= _radius.Value * _radius.Value)
                {
                    RetrowaveBall.Instance.Body.linearVelocity *= 1f - _ballVelocityDampingPerTick;
                    RetrowaveBall.Instance.Body.angularVelocity *= 1f - _ballVelocityDampingPerTick;
                }
            }
        }

        private bool CanAffect(RetrowavePlayerController player)
        {
            if (player == null || !player.IsArenaParticipant || player.OwnerClientId == _ownerClientId)
            {
                return false;
            }

            return _affectFriendlyPlayers || player.Team != _ownerTeam;
        }

        private IEnumerator ExpireRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            ExpireClientRpc();

            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }

        [ClientRpc]
        private void ExpireClientRpc()
        {
            if (_expireCue != null)
            {
                AudioSource.PlayClipAtPoint(_expireCue, transform.position, RetrowaveGameSettings.SfxVolume);
            }
        }

        private static RetrowavePlayerController ResolvePlayer(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            return collider.GetComponentInParent<RetrowavePlayerController>()
                   ?? collider.GetComponent<RetrowavePlayerController>();
        }

        private void HandleRadiusChanged(float _, float __)
        {
            RefreshRadius();
        }

        private void RefreshRadius()
        {
            if (_trigger != null)
            {
                _trigger.radius = _radius.Value;
            }

            if (_renderer != null)
            {
                _renderer.transform.localScale = Vector3.one * (_radius.Value * 2f);
            }
        }

        private void EnsureVisual()
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Chrono Dome Visual";
            visual.transform.SetParent(transform, false);
            Destroy(visual.GetComponent<Collider>());
            _renderer = visual.GetComponent<MeshRenderer>();
            _material = RetrowaveStyle.CreateTransparentLitMaterial(
                new Color(0.12f, 0.46f, 0.9f, 0.14f),
                new Color(0.22f, 0.78f, 1f) * 2.15f,
                0.35f,
                0f);
            _renderer.sharedMaterial = _material;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.22f, 0.78f, 1f, 0.45f);
            Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? _radius.Value : 10f);
        }
    }
}
