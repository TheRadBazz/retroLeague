#pragma warning disable 0649

using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public sealed class NeonSnareTrailPowerUp : RarePowerUpBase
    {
        [SerializeField] private float _activeDuration = 6f;
        [SerializeField] private float _segmentSpawnDistance = 1.35f;
        [SerializeField] private Vector3 _segmentColliderSize = new(1.85f, 1.4f, 1.35f);
        [SerializeField] private float _segmentLifetime = 5f;
        [SerializeField] private float _stunDuration = 1.35f;
        [SerializeField] private float _stunImmunitySeconds = 1.5f;
        [SerializeField] private float _spinTorque = 11f;
        [SerializeField] private bool _affectEnemiesOnly = true;
        [SerializeField] private bool _affectOwner;
        [SerializeField] private float _trailVisualWidth = 0.72f;
        [SerializeField] private AudioClip _activateCue;

        private RetrowavePlayerController _player;
        private Coroutine _activeRoutine;
        private uint _activationSerial;

        public override RetrowaveRarePowerUpType RarePowerUpType => RetrowaveRarePowerUpType.NeonSnareTrail;

        private void Awake()
        {
            _player = GetComponent<RetrowavePlayerController>();
        }

        public override bool ActivateServer(RetrowavePlayerController owner)
        {
            if (!IsValidOwner(owner) || _activeRoutine != null)
            {
                return false;
            }

            _player = owner;
            _activationSerial++;
            _activeRoutine = StartCoroutine(TrailRoutine(owner, _activationSerial));
            StartTrailVisualClientRpc(new NetworkObjectReference(owner.NetworkObject), _activeDuration);
            return true;
        }

        private IEnumerator TrailRoutine(RetrowavePlayerController owner, uint activationSerial)
        {
            var endsAt = Time.time + Mathf.Max(0.25f, _activeDuration);
            var lastSegmentPosition = owner.transform.position;
            var sourceId = ((ulong)owner.OwnerClientId << 32) ^ activationSerial;

            SpawnSegment(owner, lastSegmentPosition, owner.transform.rotation, sourceId, _segmentSpawnDistance);

            while (Time.time < endsAt && owner != null && owner.IsSpawned && owner.IsArenaParticipant)
            {
                var currentPosition = owner.transform.position;
                var delta = currentPosition - lastSegmentPosition;
                delta.y = 0f;
                var distance = delta.magnitude;

                if (distance >= _segmentSpawnDistance)
                {
                    var rotation = delta.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(delta.normalized, Vector3.up)
                        : owner.transform.rotation;
                    SpawnSegment(owner, Vector3.Lerp(lastSegmentPosition, currentPosition, 0.5f), rotation, sourceId, distance);
                    lastSegmentPosition = currentPosition;
                }

                yield return new WaitForFixedUpdate();
            }

            _activeRoutine = null;
        }

        private void SpawnSegment(RetrowavePlayerController owner, Vector3 position, Quaternion rotation, ulong sourceId, float length)
        {
            if (RetrowaveGameBootstrap.Instance == null)
            {
                return;
            }

            var segmentObject = RetrowaveGameBootstrap.Instance.CreateNeonTrailSegmentInstance();

            if (segmentObject == null)
            {
                return;
            }

            var spawnPosition = position;
            spawnPosition.y = Mathf.Max(spawnPosition.y, RetrowaveArenaConfig.GetSurfaceHeight(spawnPosition.x, spawnPosition.z) + 0.25f);
            segmentObject.transform.SetPositionAndRotation(spawnPosition, rotation);
            RetrowaveGameBootstrap.Instance.MoveRuntimeInstanceToGameplayScene(segmentObject);

            var networkObject = segmentObject.GetComponent<NetworkObject>();
            var segment = segmentObject.GetComponent<NeonTrailSegment>();

            if (networkObject == null || segment == null)
            {
                Destroy(segmentObject);
                return;
            }

            networkObject.Spawn();
            var size = _segmentColliderSize;
            size.z = Mathf.Max(size.z, length + _segmentColliderSize.z);
            segment.InitializeServer(
                owner.OwnerClientId,
                owner.Team,
                sourceId,
                size,
                _segmentLifetime,
                _stunDuration,
                _stunImmunitySeconds,
                _spinTorque,
                _affectEnemiesOnly,
                _affectOwner);
        }

        [ClientRpc]
        private void StartTrailVisualClientRpc(NetworkObjectReference ownerReference, float duration)
        {
            if (!ownerReference.TryGet(out var ownerObject) || ownerObject == null)
            {
                return;
            }

            StartCoroutine(TrailVisualRoutine(ownerObject.transform, duration));

            if (_activateCue != null)
            {
                AudioSource.PlayClipAtPoint(_activateCue, ownerObject.transform.position, RetrowaveGameSettings.SfxVolume);
            }
        }

        private IEnumerator TrailVisualRoutine(Transform owner, float duration)
        {
            if (owner == null)
            {
                yield break;
            }

            var trailObject = new GameObject("Neon Snare Trail Visual");
            trailObject.transform.SetParent(owner, false);
            trailObject.transform.localPosition = new Vector3(0f, -0.65f, -0.95f);
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = Mathf.Max(0.2f, _segmentLifetime);
            trail.minVertexDistance = 0.12f;
            trail.widthMultiplier = Mathf.Max(0.05f, _trailVisualWidth);
            trail.numCornerVertices = 4;
            trail.numCapVertices = 4;
            trail.material = RetrowaveStyle.CreateTransparentUnlitMaterial(new Color(0.08f, 1f, 0.25f, 0.92f));
            trail.startColor = new Color(0.08f, 1f, 0.25f, 0.95f);
            trail.endColor = new Color(0.08f, 1f, 0.25f, 0f);
            yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
            trail.emitting = false;
            Destroy(trailObject, trail.time);
        }
    }
}
