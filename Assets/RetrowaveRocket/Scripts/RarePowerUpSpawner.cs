#pragma warning disable 0649

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public sealed class RarePowerUpSpawner : NetworkBehaviour
    {
        private static readonly RetrowaveRarePowerUpType[] DefaultTypes =
        {
            RetrowaveRarePowerUpType.NeonSnareTrail,
            RetrowaveRarePowerUpType.GravityBomb,
            RetrowaveRarePowerUpType.ChronoDome,
        };

        [SerializeField] private List<RarePowerUpSpawnPoint> _spawnPoints = new();
        [SerializeField] private RetrowaveRarePowerUpType[] _possibleTypes = DefaultTypes;
        [SerializeField] private Vector2 _spawnDelayRange = new(45f, 75f);
        [SerializeField] private bool _requireCapture = true;
        [SerializeField] private bool _allowReplacement;
        [SerializeField] private float _captureSeconds = 5f;
        [SerializeField] private float _beaconRadius = 3f;
        [SerializeField] private bool _spawnDuringWarmup = true;
        [SerializeField] private AudioClip _globalSpawnCue;

        private readonly List<RarePowerUpPickupBeacon> _activeBeacons = new();
        private float _spawnTimer;

        public static event Action<RetrowaveRarePowerUpType, Vector3> RareBeaconSpawned;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                ScheduleNextSpawn();
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            CleanupActiveBeacons();

            if (_activeBeacons.Count > 0)
            {
                return;
            }

            if (!CanSpawnNow())
            {
                return;
            }

            _spawnTimer -= Time.deltaTime;

            if (_spawnTimer <= 0f)
            {
                SpawnBeaconWave();
            }
        }

        public void HandleBeaconResolvedServer(RarePowerUpPickupBeacon beacon)
        {
            if (!IsServer)
            {
                return;
            }

            _activeBeacons.Remove(beacon);
            CleanupActiveBeacons();

            if (_activeBeacons.Count == 0)
            {
                ScheduleNextSpawn();
            }
        }

        public void ResetForMatchStartServer()
        {
            if (!IsServer)
            {
                return;
            }

            for (var i = _activeBeacons.Count - 1; i >= 0; i--)
            {
                var activeBeacon = _activeBeacons[i];

                if (activeBeacon != null
                    && activeBeacon.NetworkObject != null
                    && activeBeacon.NetworkObject.IsSpawned)
                {
                    activeBeacon.NetworkObject.Despawn(true);
                }
            }

            _activeBeacons.Clear();
            ScheduleNextSpawn();
        }

        private bool CanSpawnNow()
        {
            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager == null)
            {
                return true;
            }

            if (matchManager.IsGameplayLocked)
            {
                return false;
            }

            return matchManager.IsLiveMatch || (_spawnDuringWarmup && matchManager.IsWarmup);
        }

        private void SpawnBeaconWave()
        {
            if (RetrowaveGameBootstrap.Instance == null)
            {
                ScheduleNextSpawn();
                return;
            }

            var positions = ResolveSpawnPositions();
            var spawnedCount = 0;

            for (var i = 0; i < positions.Length; i++)
            {
                if (TrySpawnBeacon(ChooseType(), positions[i]))
                {
                    spawnedCount++;
                }
            }

            if (spawnedCount == 0)
            {
                ScheduleNextSpawn();
            }
        }

        private bool TrySpawnBeacon(RetrowaveRarePowerUpType type, Vector3 position)
        {
            var beaconObject = RetrowaveGameBootstrap.Instance.CreateRarePowerUpPickupBeaconInstance();

            if (beaconObject == null)
            {
                return false;
            }

            beaconObject.name = $"Rare Beacon {type}";
            beaconObject.transform.SetPositionAndRotation(position, Quaternion.identity);
            RetrowaveGameBootstrap.Instance.MoveRuntimeInstanceToGameplayScene(beaconObject);

            var networkObject = beaconObject.GetComponent<NetworkObject>();
            var beacon = beaconObject.GetComponent<RarePowerUpPickupBeacon>();

            if (networkObject == null || beacon == null)
            {
                Destroy(beaconObject);
                return false;
            }

            networkObject.Spawn();
            beacon.InitializeServer(this, type, _requireCapture, _captureSeconds, _allowReplacement, _beaconRadius);
            _activeBeacons.Add(beacon);
            AnnounceSpawnClientRpc((int)type, position);
            return true;
        }

        private RetrowaveRarePowerUpType ChooseType()
        {
            var choices = _possibleTypes != null && _possibleTypes.Length > 0 ? _possibleTypes : DefaultTypes;
            var type = choices[UnityEngine.Random.Range(0, choices.Length)];
            return type == RetrowaveRarePowerUpType.None ? RetrowaveRarePowerUpType.NeonSnareTrail : type;
        }

        private Vector3[] ResolveSpawnPositions()
        {
            CleanupSpawnPoints();

            return new[]
            {
                ResolveEndSpawnPosition(1f),
                ResolveEndSpawnPosition(-1f),
            };
        }

        private Vector3 ResolveEndSpawnPosition(float endSign)
        {
            if (TryResolveSpawnPointForEnd(endSign, out var spawnPointPosition))
            {
                return spawnPointPosition;
            }

            return ResolveFallbackEndSpawnPosition(endSign);
        }

        private bool TryResolveSpawnPointForEnd(float endSign, out Vector3 position)
        {
            var source = _spawnPoints.Count > 0 ? _spawnPoints : RarePowerUpSpawnPoint.Active;
            var candidates = 0;

            for (var i = 0; i < source.Count; i++)
            {
                var spawnPoint = source[i];

                if (spawnPoint != null && spawnPoint.transform.position.z * endSign >= 0f)
                {
                    candidates++;
                }
            }

            if (candidates <= 0)
            {
                position = default;
                return false;
            }

            var selected = UnityEngine.Random.Range(0, candidates);

            for (var i = 0; i < source.Count; i++)
            {
                var spawnPoint = source[i];

                if (spawnPoint == null || spawnPoint.transform.position.z * endSign < 0f)
                {
                    continue;
                }

                if (selected == 0)
                {
                    position = spawnPoint.transform.position;
                    return true;
                }

                selected--;
            }

            position = default;
            return false;
        }

        private static Vector3 ResolveFallbackEndSpawnPosition(float endSign)
        {
            var x = UnityEngine.Random.Range(-RetrowaveArenaConfig.FlatHalfWidth * 0.58f, RetrowaveArenaConfig.FlatHalfWidth * 0.58f);
            var z = Mathf.Sign(endSign) * RetrowaveArenaConfig.FlatHalfLength * 0.58f;
            var y = RetrowaveArenaConfig.GetSurfaceSpawnHeight(x, z) + 1.4f;
            return new Vector3(x, y, z);
        }

        private void CleanupActiveBeacons()
        {
            for (var i = _activeBeacons.Count - 1; i >= 0; i--)
            {
                var beacon = _activeBeacons[i];

                if (beacon == null || !beacon.IsSpawned)
                {
                    _activeBeacons.RemoveAt(i);
                }
            }
        }

        private void CleanupSpawnPoints()
        {
            for (var i = _spawnPoints.Count - 1; i >= 0; i--)
            {
                if (_spawnPoints[i] == null)
                {
                    _spawnPoints.RemoveAt(i);
                }
            }
        }

        private void ScheduleNextSpawn()
        {
            var min = Mathf.Max(1f, Mathf.Min(_spawnDelayRange.x, _spawnDelayRange.y));
            var max = Mathf.Max(min, Mathf.Max(_spawnDelayRange.x, _spawnDelayRange.y));
            _spawnTimer = UnityEngine.Random.Range(min, max);
        }

        [ClientRpc]
        private void AnnounceSpawnClientRpc(int typeValue, Vector3 position)
        {
            var type = (RetrowaveRarePowerUpType)typeValue;
            RareBeaconSpawned?.Invoke(type, position);

            if (_globalSpawnCue != null)
            {
                RetrowaveArenaAudio.PlayRarePowerCue(_globalSpawnCue, position, 0.92f);
            }
        }
    }
}
