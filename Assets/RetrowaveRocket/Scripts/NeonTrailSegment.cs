using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class NeonTrailSegment : NetworkBehaviour
    {
        private readonly NetworkVariable<ulong> _ownerClientId = new(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _ownerTeam = new(
            (int)RetrowaveTeam.Blue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _segmentSize = new(
            Vector3.one,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _lifetime = new(
            5f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private BoxCollider _trigger;
        private MeshRenderer _renderer;
        private Material _material;
        private Light _glow;
        private ulong _sourceId;
        private float _stunDuration;
        private float _stunImmunitySeconds;
        private float _spinTorque;
        private bool _affectEnemiesOnly;
        private bool _affectOwner;
        private float _spawnedAt;
        private bool _offlineMode;
        private bool HasSimulationAuthority => IsServer || _offlineMode;

        private void Awake()
        {
            _trigger = GetComponent<BoxCollider>();
            _trigger.isTrigger = true;
            EnsureVisual();
            RefreshSize();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _segmentSize.OnValueChanged += HandleSizeChanged;
            _spawnedAt = Time.time;
            RefreshSize();
        }

        public override void OnNetworkDespawn()
        {
            _segmentSize.OnValueChanged -= HandleSizeChanged;
            base.OnNetworkDespawn();
        }

        public void EnableOfflineMode()
        {
            _offlineMode = true;
            RefreshSize();
        }

        private void Update()
        {
            if (_material == null)
            {
                return;
            }

            var progress = _lifetime.Value <= 0f ? 1f : Mathf.Clamp01((Time.time - _spawnedAt) / _lifetime.Value);
            var alpha = 1f - progress;
            _material.SetColor("_BaseColor", new Color(0.03f, 0.85f, 0.18f, alpha * 0.62f));
            _material.SetColor("_EmissionColor", new Color(0.08f, 1f, 0.25f, 1f) * Mathf.Lerp(0.45f, 4.4f, alpha));

            if (_glow != null)
            {
                _glow.intensity = Mathf.Lerp(0f, 4.8f, alpha);
                _glow.range = Mathf.Lerp(1.4f, 5.6f, alpha);
            }
        }

        public void InitializeServer(
            ulong ownerClientId,
            RetrowaveTeam ownerTeam,
            ulong sourceId,
            Vector3 segmentSize,
            float lifetime,
            float stunDuration,
            float stunImmunitySeconds,
            float spinTorque,
            bool affectEnemiesOnly,
            bool affectOwner)
        {
            if (!IsServer)
            {
                return;
            }

            _ownerClientId.Value = ownerClientId;
            _ownerTeam.Value = (int)ownerTeam;
            _sourceId = sourceId;
            _segmentSize.Value = new Vector3(
                Mathf.Max(0.1f, segmentSize.x),
                Mathf.Max(0.1f, segmentSize.y),
                Mathf.Max(0.1f, segmentSize.z));
            _lifetime.Value = Mathf.Max(0.1f, lifetime);
            _stunDuration = Mathf.Max(0.05f, stunDuration);
            _stunImmunitySeconds = Mathf.Max(0f, stunImmunitySeconds);
            _spinTorque = Mathf.Max(0f, spinTorque);
            _affectEnemiesOnly = affectEnemiesOnly;
            _affectOwner = affectOwner;
            RefreshSize();
            StartCoroutine(ExpireRoutine(_lifetime.Value));
        }

        public void InitializeOffline(
            ulong ownerClientId,
            RetrowaveTeam ownerTeam,
            ulong sourceId,
            Vector3 segmentSize,
            float lifetime,
            float stunDuration,
            float stunImmunitySeconds,
            float spinTorque,
            bool affectEnemiesOnly,
            bool affectOwner)
        {
            _offlineMode = true;
            _ownerClientId.Value = ownerClientId;
            _ownerTeam.Value = (int)ownerTeam;
            _sourceId = sourceId;
            _segmentSize.Value = new Vector3(
                Mathf.Max(0.1f, segmentSize.x),
                Mathf.Max(0.1f, segmentSize.y),
                Mathf.Max(0.1f, segmentSize.z));
            _lifetime.Value = Mathf.Max(0.1f, lifetime);
            _stunDuration = Mathf.Max(0.05f, stunDuration);
            _stunImmunitySeconds = Mathf.Max(0f, stunImmunitySeconds);
            _spinTorque = Mathf.Max(0f, spinTorque);
            _affectEnemiesOnly = affectEnemiesOnly;
            _affectOwner = affectOwner;
            _spawnedAt = Time.time;
            RefreshSize();
            StartCoroutine(ExpireRoutine(_lifetime.Value));
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!HasSimulationAuthority || !TryResolvePlayer(other, out var player) || !CanAffect(player))
            {
                return;
            }

            if (!player.TryGetComponent<VehicleStatusEffects>(out var statusEffects))
            {
                return;
            }

            var spinDirection = Vector3.up * (Random.value > 0.5f ? 1f : -1f);
            statusEffects.ApplyStunServer(
                _stunDuration,
                spinDirection * _spinTorque,
                _sourceId,
                _stunImmunitySeconds);
            RetrowaveMatchManager.Instance?.RecordPowerUpHitServer(_ownerClientId.Value, player.ControllingClientId);
        }

        private bool CanAffect(RetrowavePlayerController player)
        {
            if (player == null || !player.IsArenaParticipant)
            {
                return false;
            }

            if (!_affectOwner && player.OwnerClientId == _ownerClientId.Value)
            {
                return false;
            }

            return !_affectEnemiesOnly || player.Team != (RetrowaveTeam)_ownerTeam.Value;
        }

        private static bool TryResolvePlayer(Collider collider, out RetrowavePlayerController player)
        {
            player = null;

            if (collider == null)
            {
                return false;
            }

            if (collider.TryGetComponent(out player))
            {
                return true;
            }

            player = collider.GetComponentInParent<RetrowavePlayerController>();
            return player != null;
        }

        private IEnumerator ExpireRoutine(float lifetime)
        {
            yield return new WaitForSeconds(lifetime);

            if (_offlineMode)
            {
                Destroy(gameObject);
            }
            else if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }

        private void HandleSizeChanged(Vector3 _, Vector3 __)
        {
            RefreshSize();
        }

        private void RefreshSize()
        {
            if (_trigger != null)
            {
                _trigger.size = _segmentSize.Value;
            }

            if (_renderer != null)
            {
                _renderer.transform.localScale = _segmentSize.Value;
            }
        }

        private void EnsureVisual()
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Neon Trail Segment Visual";
            visual.transform.SetParent(transform, false);
            Destroy(visual.GetComponent<Collider>());
            _renderer = visual.GetComponent<MeshRenderer>();
            _material = RetrowaveStyle.CreateTransparentLitMaterial(
                new Color(0.03f, 0.85f, 0.18f, 0.62f),
                new Color(0.08f, 1f, 0.25f, 1f) * 4.4f,
                0.88f,
                0f);
            _renderer.sharedMaterial = _material;

            var glowObject = new GameObject("Neon Trail Glow");
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.localPosition = Vector3.up * 0.2f;
            _glow = glowObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = new Color(0.08f, 1f, 0.25f, 1f);
            _glow.range = 5.6f;
            _glow.intensity = 4.8f;
        }
    }
}
