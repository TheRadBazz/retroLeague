#pragma warning disable 0649

using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(SphereCollider))]
    public sealed class GravityBombDevice : NetworkBehaviour
    {
        private const int MaxExplosionHits = 96;
        private static readonly Collider[] ExplosionHits = new Collider[MaxExplosionHits];
        private static readonly HashSet<RetrowavePlayerController> AffectedVehicles = new();

        private readonly NetworkVariable<float> _fuseTime = new(
            1.5f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _radius = new(
            9f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _detonated = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [SerializeField] private AudioClip _detonateCue;

        private SphereCollider _trigger;
        private MeshRenderer _coreRenderer;
        private MeshRenderer _ringRenderer;
        private float _maxForce;
        private float _upwardForceMultiplier;
        private LayerMask _vehicleLayerMask;
        private LayerMask _ballLayerMask;
        private bool _affectBall;
        private float _ballForceMultiplier;
        private float _spawnedAt;

        private void Awake()
        {
            _trigger = GetComponent<SphereCollider>();
            _trigger.isTrigger = true;
            _trigger.enabled = false;
            EnsureVisuals();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _radius.OnValueChanged += HandleRadiusChanged;
            _detonated.OnValueChanged += HandleDetonatedChanged;
            _spawnedAt = Time.time;
            RefreshRadius();
        }

        public override void OnNetworkDespawn()
        {
            _radius.OnValueChanged -= HandleRadiusChanged;
            _detonated.OnValueChanged -= HandleDetonatedChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            var fuseProgress = _fuseTime.Value <= 0f ? 1f : Mathf.Clamp01((Time.time - _spawnedAt) / _fuseTime.Value);
            var pulse = 1f + Mathf.Sin(Time.time * Mathf.Lerp(4f, 15f, fuseProgress)) * Mathf.Lerp(0.05f, 0.2f, fuseProgress);

            if (_coreRenderer != null)
            {
                _coreRenderer.transform.localScale = Vector3.one * pulse;
            }

            if (_ringRenderer != null)
            {
                _ringRenderer.transform.localScale = new Vector3(_radius.Value * 2f, 0.035f, _radius.Value * 2f);
            }

            RefreshVisualAlpha(fuseProgress);
        }

        public void InitializeServer(
            ulong ownerClientId,
            RetrowaveTeam ownerTeam,
            float fuseTime,
            float radius,
            float maxForce,
            float upwardForceMultiplier,
            LayerMask vehicleLayerMask,
            LayerMask ballLayerMask,
            bool affectBall,
            float ballForceMultiplier)
        {
            if (!IsServer)
            {
                return;
            }

            _fuseTime.Value = Mathf.Max(0.05f, fuseTime);
            _radius.Value = Mathf.Max(0.5f, radius);
            _maxForce = Mathf.Max(0f, maxForce);
            _upwardForceMultiplier = Mathf.Max(0f, upwardForceMultiplier);
            _vehicleLayerMask = vehicleLayerMask;
            _ballLayerMask = ballLayerMask;
            _affectBall = affectBall;
            _ballForceMultiplier = Mathf.Clamp01(ballForceMultiplier);
            RefreshRadius();
            StartCoroutine(FuseRoutine(_fuseTime.Value));
        }

        private IEnumerator FuseRoutine(float fuseTime)
        {
            yield return new WaitForSeconds(fuseTime);

            if (IsServer)
            {
                DetonateServer();
            }
        }

        private void DetonateServer()
        {
            if (_detonated.Value)
            {
                return;
            }

            _detonated.Value = true;
            var hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                _radius.Value,
                ExplosionHits,
                _vehicleLayerMask,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var player = ResolvePlayer(ExplosionHits[i]);

                if (player == null || player.Body == null || !AffectedVehicles.Add(player))
                {
                    continue;
                }

                ExplosionForceUtility.ApplyRadialVelocityChange(
                    player.Body,
                    transform.position,
                    _radius.Value,
                    _maxForce,
                    _upwardForceMultiplier);
            }

            AffectedVehicles.Clear();

            if (_affectBall
                && RetrowaveBall.Instance != null
                && RetrowaveBall.Instance.Body != null
                && (_ballLayerMask.value & (1 << RetrowaveBall.Instance.gameObject.layer)) != 0)
            {
                var ballOffset = RetrowaveBall.Instance.Body.worldCenterOfMass - transform.position;

                if (ballOffset.sqrMagnitude <= _radius.Value * _radius.Value)
                {
                    ExplosionForceUtility.ApplyRadialVelocityChange(
                        RetrowaveBall.Instance.Body,
                        transform.position,
                        _radius.Value,
                        _maxForce,
                        _upwardForceMultiplier,
                        _ballForceMultiplier);
                }
            }

            DetonateClientRpc();
            StartCoroutine(DespawnAfterDelay());
        }

        [ClientRpc]
        private void DetonateClientRpc()
        {
            RetrowaveArenaAudio.PlayRarePowerCue(_detonateCue, transform.position, 1.12f);
        }

        private IEnumerator DespawnAfterDelay()
        {
            yield return new WaitForSeconds(0.25f);

            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
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

        private void HandleDetonatedChanged(bool _, bool detonated)
        {
            if (_coreRenderer != null)
            {
                _coreRenderer.enabled = !detonated;
            }
        }

        private void RefreshRadius()
        {
            if (_trigger != null)
            {
                _trigger.radius = _radius.Value;
            }
        }

        private void EnsureVisuals()
        {
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Gravity Bomb Core";
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.85f;
            Destroy(core.GetComponent<Collider>());
            _coreRenderer = core.GetComponent<MeshRenderer>();
            _coreRenderer.sharedMaterial = RetrowaveStyle.CreateTransparentLitMaterial(
                new Color(1f, 0.22f, 0.04f, 0.34f),
                new Color(1f, 0.35f, 0.08f) * 2.1f,
                0.9f,
                0f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Gravity Bomb Warning Ring";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            Destroy(ring.GetComponent<Collider>());
            _ringRenderer = ring.GetComponent<MeshRenderer>();
            _ringRenderer.sharedMaterial = RetrowaveStyle.CreateTransparentLitMaterial(
                new Color(1f, 0.18f, 0.03f, 0.18f),
                new Color(1f, 0.28f, 0.04f) * 1.45f,
                0.72f,
                0f);
        }

        private void RefreshVisualAlpha(float fuseProgress)
        {
            if (_coreRenderer != null && _coreRenderer.sharedMaterial != null)
            {
                var alpha = Mathf.Lerp(0.22f, 0.48f, fuseProgress);
                _coreRenderer.sharedMaterial.SetColor("_BaseColor", new Color(1f, 0.22f, 0.04f, alpha));
                _coreRenderer.sharedMaterial.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.08f, 1f) * Mathf.Lerp(1.4f, 2.8f, fuseProgress));
            }

            if (_ringRenderer != null && _ringRenderer.sharedMaterial != null)
            {
                var alpha = Mathf.Lerp(0.12f, 0.28f, fuseProgress);
                _ringRenderer.sharedMaterial.SetColor("_BaseColor", new Color(1f, 0.18f, 0.03f, alpha));
                _ringRenderer.sharedMaterial.SetColor("_EmissionColor", new Color(1f, 0.28f, 0.04f, 1f) * Mathf.Lerp(0.9f, 2.1f, fuseProgress));
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.34f, 0.08f, 0.45f);
            Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? _radius.Value : 9f);
        }
    }
}
