#pragma warning disable 0649

using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [RequireComponent(typeof(SphereCollider))]
    public sealed class RarePowerUpPickupBeacon : NetworkBehaviour
    {
        private const float GroundRingCenterOffset = 0.045f;
        private static readonly List<RarePowerUpPickupBeacon> ActiveBeacons = new();

        private readonly NetworkVariable<int> _heldType = new(
            (int)RetrowaveRarePowerUpType.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _active = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _capturingClientId = new(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _captureProgress = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _syncedRadius = new(
            3f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _requiresCapture = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<RetrowavePlayerController> _playersInside = new();

        [SerializeField] private bool _requireCapture = true;
        [SerializeField] private bool _allowReplacement;
        [SerializeField] private float _captureSeconds = 5f;
        [SerializeField] private float _radius = 3f;
        [SerializeField] private AudioClip _spawnCue;
        [SerializeField] private AudioClip _collectedCue;

        private SphereCollider _trigger;
        private MeshRenderer _orbRenderer;
        private MeshRenderer _ringRenderer;
        private Light _glow;
        private TextMeshPro _label;
        private Material _orbMaterial;
        private Material _ringMaterial;
        private RarePowerUpSpawner _spawner;
        private RetrowavePlayerController _capturingPlayer;
        private float _captureTimer;

        public static event Action<RarePowerUpPickupBeacon, bool, RetrowaveRarePowerUpType> BeaconStateChanged;

        public RetrowaveRarePowerUpType HeldType => (RetrowaveRarePowerUpType)_heldType.Value;
        public bool IsActive => _active.Value;
        public float CaptureProgress => _captureProgress.Value;
        public ulong CapturingClientId => _capturingClientId.Value;
        public float Radius => _syncedRadius.Value;
        public bool RequiresCapture => _requiresCapture.Value;
        public static IReadOnlyList<RarePowerUpPickupBeacon> Active => ActiveBeacons;

        private void Awake()
        {
            _trigger = GetComponent<SphereCollider>();
            _trigger.isTrigger = true;
            EnsureVisuals();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _heldType.OnValueChanged += HandleStateChanged;
            _active.OnValueChanged += HandleStateChanged;
            _captureProgress.OnValueChanged += HandleCaptureChanged;
            _syncedRadius.OnValueChanged += HandleCaptureChanged;
            _requiresCapture.OnValueChanged += HandleStateChanged;
            RegisterActiveBeacon();
            RefreshVisuals();
            BeaconStateChanged?.Invoke(this, IsActive, HeldType);

            if (IsActive && _spawnCue != null)
            {
                AudioSource.PlayClipAtPoint(_spawnCue, transform.position, RetrowaveGameSettings.SfxVolume);
            }
        }

        public override void OnNetworkDespawn()
        {
            _heldType.OnValueChanged -= HandleStateChanged;
            _active.OnValueChanged -= HandleStateChanged;
            _captureProgress.OnValueChanged -= HandleCaptureChanged;
            _syncedRadius.OnValueChanged -= HandleCaptureChanged;
            _requiresCapture.OnValueChanged -= HandleStateChanged;
            ActiveBeacons.Remove(this);
            BeaconStateChanged?.Invoke(this, false, HeldType);
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            transform.Rotate(Vector3.up, 48f * Time.deltaTime, Space.World);
            var pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.08f;

            if (_orbRenderer != null)
            {
                _orbRenderer.transform.localScale = Vector3.one * pulse;
            }

            if (_ringRenderer != null)
            {
                var radius = Mathf.Max(0.5f, Radius);
                var progress = RequiresCapture ? Mathf.Max(0.08f, CaptureProgress) : 1f;
                _ringRenderer.transform.localPosition = ResolveGroundRingLocalPosition();
                _ringRenderer.transform.localScale = new Vector3(radius * 2f * progress, 0.04f, radius * 2f * progress);
            }

            if (_label != null)
            {
                var camera = Camera.main;

                if (camera != null)
                {
                    _label.transform.forward = camera.transform.forward;
                }
            }
        }

        private void FixedUpdate()
        {
            if (!IsServer || !_active.Value)
            {
                return;
            }

            if (!RequiresCapture)
            {
                TryAwardFirstEligiblePlayer();
                return;
            }

            if (!IsEligible(_capturingPlayer))
            {
                SelectCapturingPlayer();
            }

            if (_capturingPlayer == null)
            {
                _captureTimer = 0f;
                _captureProgress.Value = 0f;
                _capturingClientId.Value = ulong.MaxValue;
                return;
            }

            _captureTimer += Time.fixedDeltaTime;
            _captureProgress.Value = Mathf.Clamp01(_captureTimer / Mathf.Max(0.01f, _captureSeconds));
            _capturingClientId.Value = _capturingPlayer.OwnerClientId;

            if (_captureTimer >= _captureSeconds)
            {
                TryAward(_capturingPlayer);
            }
        }

        public void InitializeServer(
            RarePowerUpSpawner spawner,
            RetrowaveRarePowerUpType type,
            bool requireCapture,
            float captureSeconds,
            bool allowReplacement,
            float radius)
        {
            if (!IsServer)
            {
                return;
            }

            _spawner = spawner;
            _heldType.Value = (int)type;
            _requireCapture = requireCapture;
            _captureSeconds = Mathf.Max(0.05f, captureSeconds);
            _allowReplacement = allowReplacement;
            _radius = Mathf.Max(0.5f, radius);
            _requiresCapture.Value = requireCapture;
            _syncedRadius.Value = _radius;
            _active.Value = true;
            _captureProgress.Value = requireCapture ? 0f : 1f;
            _capturingClientId.Value = ulong.MaxValue;
            _trigger.radius = _radius;
            RefreshVisuals();
            AnnounceSpawnClientRpc();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || !_active.Value || !TryResolvePlayer(other, out var player))
            {
                return;
            }

            if (!_playersInside.Contains(player))
            {
                _playersInside.Add(player);
            }

            if (!RequiresCapture)
            {
                TryAward(player);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer || !TryResolvePlayer(other, out var player))
            {
                return;
            }

            _playersInside.Remove(player);

            if (_capturingPlayer == player)
            {
                _capturingPlayer = null;
                _captureTimer = 0f;
                _captureProgress.Value = 0f;
                _capturingClientId.Value = ulong.MaxValue;
            }
        }

        private bool TryAwardFirstEligiblePlayer()
        {
            for (var i = 0; i < _playersInside.Count; i++)
            {
                if (IsEligible(_playersInside[i]) && TryAward(_playersInside[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void SelectCapturingPlayer()
        {
            _capturingPlayer = null;
            _captureTimer = 0f;

            for (var i = 0; i < _playersInside.Count; i++)
            {
                var candidate = _playersInside[i];

                if (!IsEligible(candidate))
                {
                    continue;
                }

                _capturingPlayer = candidate;
                return;
            }
        }

        private bool IsEligible(RetrowavePlayerController player)
        {
            if (player == null || !player.IsSpawned || !player.IsArenaParticipant)
            {
                return false;
            }

            if (!player.TryGetComponent<RarePowerUpInventory>(out var inventory))
            {
                return false;
            }

            return inventory.CanAcceptServer(HeldType, _allowReplacement);
        }

        private bool TryAward(RetrowavePlayerController player)
        {
            if (!IsEligible(player) || !player.TryGetComponent<RarePowerUpInventory>(out var inventory))
            {
                _capturingPlayer = null;
                _captureTimer = 0f;
                _captureProgress.Value = 0f;
                return false;
            }

            if (!inventory.TryGrantServer(HeldType, _allowReplacement))
            {
                return false;
            }

            _active.Value = false;
            _trigger.enabled = false;
            PlayCollectedClientRpc();
            _spawner?.HandleBeaconResolvedServer(this);
            NetworkObject.Despawn(true);
            return true;
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

        [ClientRpc]
        private void AnnounceSpawnClientRpc()
        {
            if (_spawnCue != null)
            {
                AudioSource.PlayClipAtPoint(_spawnCue, transform.position, RetrowaveGameSettings.SfxVolume);
            }
        }

        [ClientRpc]
        private void PlayCollectedClientRpc()
        {
            if (_collectedCue != null)
            {
                AudioSource.PlayClipAtPoint(_collectedCue, transform.position, RetrowaveGameSettings.SfxVolume);
            }
        }

        private void HandleStateChanged(int _, int __)
        {
            RegisterActiveBeacon();
            RefreshVisuals();
            BeaconStateChanged?.Invoke(this, IsActive, HeldType);
        }

        private void HandleStateChanged(bool _, bool __)
        {
            RegisterActiveBeacon();
            RefreshVisuals();
            BeaconStateChanged?.Invoke(this, IsActive, HeldType);
        }

        private void HandleCaptureChanged(float _, float __)
        {
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            var color = ResolveColor(HeldType);

            if (_trigger != null)
            {
                _radius = Mathf.Max(0.5f, Radius);
                _trigger.enabled = IsActive;
                _trigger.radius = _radius;
            }

            if (_orbRenderer != null)
            {
                _orbRenderer.enabled = IsActive;
            }

            if (_ringRenderer != null)
            {
                _ringRenderer.enabled = IsActive;
                _ringRenderer.transform.localPosition = ResolveGroundRingLocalPosition();
            }

            if (_glow != null)
            {
                _glow.enabled = IsActive;
                _glow.color = color;
            }

            if (_label != null)
            {
                _label.gameObject.SetActive(IsActive);
                _label.color = Color.Lerp(color, Color.white, 0.18f);
                _label.text = $"{ResolveLabel(HeldType).ToUpperInvariant()}\n{(RequiresCapture ? $"CAPTURE {Mathf.RoundToInt(CaptureProgress * 100f)}%" : "PICKUP READY")}";
            }

            if (_orbMaterial != null)
            {
                _orbMaterial.SetColor("_BaseColor", color * 0.35f);
                _orbMaterial.SetColor("_EmissionColor", color * 2.8f);
            }

            if (_ringMaterial != null)
            {
                _ringMaterial.SetColor("_BaseColor", color * 0.22f);
                _ringMaterial.SetColor("_EmissionColor", color * 2.1f);
            }
        }

        private void EnsureVisuals()
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Rare Beacon Orb";
            orb.transform.SetParent(transform, false);
            orb.transform.localScale = Vector3.one * 1.2f;
            Destroy(orb.GetComponent<Collider>());
            _orbRenderer = orb.GetComponent<MeshRenderer>();

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Rare Beacon Capture Ring";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = ResolveGroundRingLocalPosition();
            ring.transform.localScale = new Vector3(_radius * 2f, 0.04f, _radius * 2f);
            Destroy(ring.GetComponent<Collider>());
            _ringRenderer = ring.GetComponent<MeshRenderer>();

            _orbMaterial = RetrowaveStyle.CreateLitMaterial(new Color(0.08f, 0.4f, 0.16f), new Color(0.1f, 1f, 0.36f) * 2.8f, 0.88f, 0f);
            _ringMaterial = RetrowaveStyle.CreateLitMaterial(new Color(0.03f, 0.3f, 0.08f), new Color(0.1f, 1f, 0.36f) * 2.1f, 0.88f, 0f);
            _orbRenderer.sharedMaterial = _orbMaterial;
            _ringRenderer.sharedMaterial = _ringMaterial;

            var glowObject = new GameObject("Rare Beacon Glow");
            glowObject.transform.SetParent(transform, false);
            _glow = glowObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.range = 13f;
            _glow.intensity = 9f;

            var labelObject = new GameObject("Rare Beacon Label");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = Vector3.up * 2.15f;
            _label = labelObject.AddComponent<TextMeshPro>();
            _label.font = TMP_Settings.defaultFontAsset;
            _label.fontSize = 1.65f;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private void RegisterActiveBeacon()
        {
            if (IsActive)
            {
                if (!ActiveBeacons.Contains(this))
                {
                    ActiveBeacons.Add(this);
                }
            }
            else
            {
                ActiveBeacons.Remove(this);
            }
        }

        private Vector3 ResolveGroundRingLocalPosition()
        {
            var position = transform.position;
            var groundY = RetrowaveArenaConfig.GetSurfaceHeight(position.x, position.z) + GroundRingCenterOffset;
            return new Vector3(0f, groundY - position.y, 0f);
        }

        private static Color ResolveColor(RetrowaveRarePowerUpType type)
        {
            return type switch
            {
                RetrowaveRarePowerUpType.NeonSnareTrail => new Color(0.08f, 1f, 0.28f, 1f),
                RetrowaveRarePowerUpType.GravityBomb => new Color(1f, 0.38f, 0.1f, 1f),
                RetrowaveRarePowerUpType.ChronoDome => new Color(0.32f, 0.72f, 1f, 1f),
                _ => Color.white,
            };
        }

        private static string ResolveLabel(RetrowaveRarePowerUpType type)
        {
            return type switch
            {
                RetrowaveRarePowerUpType.NeonSnareTrail => "Neon snare",
                RetrowaveRarePowerUpType.GravityBomb => "Gravity bomb",
                RetrowaveRarePowerUpType.ChronoDome => "Chrono dome",
                _ => "Rare power",
            };
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.12f, 1f, 0.45f, 0.55f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.5f, _radius));
        }
    }
}
