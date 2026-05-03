using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    [DisallowMultipleComponent]
    public sealed class RetrowaveArenaObjectiveSystem : NetworkBehaviour
    {
        private const int MaxZoneHits = 80;
        private const int MaxCapturedPlayers = 32;
        private const int NeutralTeamValue = -1;

        private static readonly Collider[] ZoneHits = new Collider[MaxZoneHits];

        [SerializeField] private Vector2 _spawnDelayRange = new Vector2(32f, 52f);
        [SerializeField] private float _activeDuration = 22f;
        [SerializeField] private float _captureSeconds = 3.25f;
        [SerializeField] private float _radius = 6.2f;
        [SerializeField] private LayerMask _vehicleLayerMask = ~0;
        [SerializeField] private bool _spawnDuringWarmup = true;
        [SerializeField] private float _overchargeDuration = 7f;
        [SerializeField] private float _overchargeCooling = 30f;

        private readonly NetworkVariable<int> _objectiveTypeValue = new(
            (int)RetrowaveArenaObjectiveType.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _capturingTeamValue = new(
            NeutralTeamValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _captureProgress = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _objectivePosition = new(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly RetrowavePlayerController[] _bluePlayers = new RetrowavePlayerController[MaxCapturedPlayers];
        private readonly RetrowavePlayerController[] _pinkPlayers = new RetrowavePlayerController[MaxCapturedPlayers];

        private GameObject _visualRoot;
        private Transform _discTransform;
        private Transform _pulseTransform;
        private MeshRenderer _discRenderer;
        private MeshRenderer _pulseRenderer;
        private Light _objectiveLight;
        private TextMeshPro _label;
        private Material _discMaterial;
        private Material _pulseMaterial;
        private float _nextObjectiveAt;
        private float _activeEndsAt;
        private float _nextHoldStyleAwardAt;
        private float _pulseStartedAt = -10f;
        private float _pulseDuration = 0.58f;
        private int _lastProgressPulseBucket;
        private bool _offlineMode;
        private bool HasSimulationAuthority => IsServer || _offlineMode;

        public RetrowaveArenaObjectiveType ActiveObjectiveType => ActiveType;
        public float CaptureProgressNormalized => Mathf.Clamp01(_captureProgress.Value);
        public int CapturingTeamValue => _capturingTeamValue.Value;
        public Vector3 ObjectivePosition => _objectivePosition.Value;

        private RetrowaveArenaObjectiveType ActiveType => (RetrowaveArenaObjectiveType)_objectiveTypeValue.Value;

        private void Awake()
        {
            EnsureVisuals();
            RefreshVisuals();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _objectiveTypeValue.OnValueChanged += HandleObjectiveChanged;
            _capturingTeamValue.OnValueChanged += HandleObjectiveChanged;
            _captureProgress.OnValueChanged += HandleObjectiveChanged;
            _objectivePosition.OnValueChanged += HandleObjectivePositionChanged;
            RefreshVisuals();

            if (IsServer)
            {
                ScheduleNextObjectiveServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            _objectiveTypeValue.OnValueChanged -= HandleObjectiveChanged;
            _capturingTeamValue.OnValueChanged -= HandleObjectiveChanged;
            _captureProgress.OnValueChanged -= HandleObjectiveChanged;
            _objectivePosition.OnValueChanged -= HandleObjectivePositionChanged;
            base.OnNetworkDespawn();
        }

        public void EnableOfflineMode()
        {
            _offlineMode = true;
            ScheduleNextObjectiveServer();
            RefreshVisuals();
        }

        public void ConfigureOfflineFreeplay(
            float minDelaySeconds = 10f,
            float maxDelaySeconds = 18f,
            float activeDurationSeconds = 22f,
            float captureSeconds = 3.25f,
            bool spawnImmediately = true)
        {
            _offlineMode = true;
            _spawnDelayRange = new Vector2(
                Mathf.Max(3f, Mathf.Min(minDelaySeconds, maxDelaySeconds)),
                Mathf.Max(minDelaySeconds, maxDelaySeconds));
            _activeDuration = Mathf.Max(6f, activeDurationSeconds);
            _captureSeconds = Mathf.Max(0.5f, captureSeconds);
            _nextObjectiveAt = spawnImmediately ? Time.time + 2f : Time.time + Random.Range(_spawnDelayRange.x, _spawnDelayRange.y);
            RefreshVisuals();
        }

        private void FixedUpdate()
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager != null && (!CanRunDuringPhase(matchManager) || matchManager.IsGameplayLocked))
            {
                if (ActiveType != RetrowaveArenaObjectiveType.None)
                {
                    DeactivateObjectiveServer(scheduleNext: true);
                }

                return;
            }

            if (ActiveType == RetrowaveArenaObjectiveType.None)
            {
                if (Time.time >= _nextObjectiveAt)
                {
                    ActivateObjectiveServer();
                }

                return;
            }

            TickActiveObjectiveServer();
        }

        private void LateUpdate()
        {
            UpdatePulseVisual();

            if (_label == null || !_label.gameObject.activeInHierarchy)
            {
                return;
            }

            var camera = Camera.main;

            if (camera != null)
            {
                _label.transform.forward = camera.transform.forward;
            }
        }

        public void ResetForMatchStartServer()
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            DeactivateObjectiveServer(scheduleNext: true);
        }

        private void TickActiveObjectiveServer()
        {
            if (Time.time >= _activeEndsAt)
            {
                DeactivateObjectiveServer(scheduleNext: true);
                return;
            }

            CollectPlayersInZone(out var blueCount, out var pinkCount);

            if (blueCount > 0 && pinkCount > 0)
            {
                DecayCaptureServer(0.28f);
                return;
            }

            if (blueCount == 0 && pinkCount == 0)
            {
                DecayCaptureServer(0.5f);
                return;
            }

            var capturingTeam = blueCount > 0 ? RetrowaveTeam.Blue : RetrowaveTeam.Pink;
            var capturingTeamValue = (int)capturingTeam;

            if (_capturingTeamValue.Value != NeutralTeamValue && _capturingTeamValue.Value != capturingTeamValue)
            {
                DecayCaptureServer(1.35f);

                if (_captureProgress.Value <= 0.01f)
                {
                    _capturingTeamValue.Value = capturingTeamValue;
                }

                return;
            }

            _capturingTeamValue.Value = capturingTeamValue;
            var playerCount = capturingTeam == RetrowaveTeam.Blue ? blueCount : pinkCount;
            var players = capturingTeam == RetrowaveTeam.Blue ? _bluePlayers : _pinkPlayers;
            var captureRate = 0f;

            for (var i = 0; i < playerCount; i++)
            {
                captureRate += players[i] != null ? players[i].ObjectiveCaptureMultiplier : 1f;
            }

            captureRate = Mathf.Max(0.65f, captureRate);
            _captureProgress.Value = Mathf.Clamp01(_captureProgress.Value + captureRate * Time.fixedDeltaTime / Mathf.Max(0.25f, _captureSeconds));
            AwardObjectiveHoldStyleServer(players, playerCount);

            if (_captureProgress.Value >= 1f)
            {
                ResolveCaptureServer(capturingTeam, players, playerCount);
            }
        }

        private void ResolveCaptureServer(RetrowaveTeam capturingTeam, RetrowavePlayerController[] players, int playerCount)
        {
            var activeType = ActiveType;
            var duration = activeType switch
            {
                RetrowaveArenaObjectiveType.MidfieldControlRing => _overchargeDuration * 0.84f,
                RetrowaveArenaObjectiveType.WallGate => _overchargeDuration * 0.72f,
                _ => _overchargeDuration,
            };
            var cooling = activeType switch
            {
                RetrowaveArenaObjectiveType.BoostOverchargeZone => _overchargeCooling * 1.12f,
                RetrowaveArenaObjectiveType.WallGate => _overchargeCooling * 0.78f,
                _ => _overchargeCooling,
            };

            for (var i = 0; i < playerCount; i++)
            {
                var player = players[i];

                if (player == null || !player.IsArenaParticipant || player.Team != capturingTeam)
                {
                    continue;
                }

                player.ApplyObjectiveOverchargeServer(duration, cooling);
                RetrowaveMatchManager.Instance?.RecordObjectiveCaptureServer(player.ControllingClientId);
                player.AwardStyleServer(RetrowaveStyleEvent.ObjectiveCapture, activeType == RetrowaveArenaObjectiveType.WallGate ? 1.2f : 1f);
            }

            if (_offlineMode)
            {
                var objectiveColor = RetrowaveArenaObjectiveCatalog.GetColor(activeType);
                var teamColor = capturingTeam == RetrowaveTeam.Blue ? RetrowaveStyle.BlueGlow : RetrowaveStyle.PinkGlow;
                StartCapturePulse(_objectivePosition.Value, Color.Lerp(objectiveColor, teamColor, 0.72f), playAudio: true);
            }
            else
            {
                ObjectiveCapturedClientRpc(_objectivePosition.Value, (int)activeType, (int)capturingTeam);
            }

            DeactivateObjectiveServer(scheduleNext: true);
        }

        private void AwardObjectiveHoldStyleServer(RetrowavePlayerController[] players, int playerCount)
        {
            if (Time.time < _nextHoldStyleAwardAt)
            {
                return;
            }

            _nextHoldStyleAwardAt = Time.time + 1f;

            for (var i = 0; i < playerCount; i++)
            {
                var player = players[i];

                if (player != null && player.IsArenaParticipant)
                {
                    player.AwardStyleServer(RetrowaveStyleEvent.ObjectiveHold);
                }
            }
        }

        private void DecayCaptureServer(float multiplier)
        {
            _captureProgress.Value = Mathf.Max(
                0f,
                _captureProgress.Value - Time.fixedDeltaTime * Mathf.Max(0f, multiplier) / Mathf.Max(0.25f, _captureSeconds));

            if (_captureProgress.Value <= 0.01f)
            {
                _capturingTeamValue.Value = NeutralTeamValue;
            }
        }

        private void ActivateObjectiveServer()
        {
            var type = ChooseObjectiveType();
            _objectivePosition.Value = ResolveObjectivePosition(type);
            _objectiveTypeValue.Value = (int)type;
            _capturingTeamValue.Value = NeutralTeamValue;
            _captureProgress.Value = 0f;
            _activeEndsAt = Time.time + Mathf.Max(4f, _activeDuration);
            _nextHoldStyleAwardAt = Time.time + 1f;
            _lastProgressPulseBucket = 0;
            RefreshVisuals();
        }

        private void DeactivateObjectiveServer(bool scheduleNext)
        {
            _objectiveTypeValue.Value = (int)RetrowaveArenaObjectiveType.None;
            _capturingTeamValue.Value = NeutralTeamValue;
            _captureProgress.Value = 0f;
            _activeEndsAt = 0f;
            _nextHoldStyleAwardAt = 0f;
            _lastProgressPulseBucket = 0;
            RefreshVisuals();

            if (scheduleNext)
            {
                ScheduleNextObjectiveServer();
            }
        }

        private void ScheduleNextObjectiveServer()
        {
            var min = Mathf.Min(_spawnDelayRange.x, _spawnDelayRange.y);
            var max = Mathf.Max(_spawnDelayRange.x, _spawnDelayRange.y);
            _nextObjectiveAt = Time.time + Random.Range(Mathf.Max(3f, min), Mathf.Max(3f, max));
        }

        private RetrowaveArenaObjectiveType ChooseObjectiveType()
        {
            return (RetrowaveArenaObjectiveType)Random.Range(
                (int)RetrowaveArenaObjectiveType.BoostOverchargeZone,
                (int)RetrowaveArenaObjectiveType.MidfieldControlRing + 1);
        }

        private Vector3 ResolveObjectivePosition(RetrowaveArenaObjectiveType type)
        {
            var halfWidth = RetrowaveArenaConfig.FlatHalfWidth;
            var halfLength = RetrowaveArenaConfig.FlatHalfLength;
            var position = type switch
            {
                RetrowaveArenaObjectiveType.WallGate => new Vector3(
                    (Random.value < 0.5f ? -1f : 1f) * halfWidth * 0.9f,
                    0f,
                    Random.Range(-halfLength * 0.28f, halfLength * 0.28f)),
                RetrowaveArenaObjectiveType.BoostOverchargeZone => new Vector3(
                    Random.Range(-halfWidth * 0.55f, halfWidth * 0.55f),
                    0f,
                    (Random.value < 0.5f ? -1f : 1f) * halfLength * 0.42f),
                _ => new Vector3(
                    Random.Range(-halfWidth * 0.18f, halfWidth * 0.18f),
                    0f,
                    Random.Range(-halfLength * 0.12f, halfLength * 0.12f)),
            };

            position.y = RetrowaveArenaConfig.GetSurfaceHeight(position.x, position.z) + 0.08f;
            return position;
        }

        private void CollectPlayersInZone(out int blueCount, out int pinkCount)
        {
            blueCount = 0;
            pinkCount = 0;

            var center = _objectivePosition.Value + Vector3.up * 1.4f;
            var hitCount = Physics.OverlapSphereNonAlloc(center, _radius, ZoneHits, _vehicleLayerMask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var player = ZoneHits[i] != null ? ZoneHits[i].GetComponentInParent<RetrowavePlayerController>() : null;

                if (player == null || !player.IsArenaParticipant)
                {
                    continue;
                }

                if (player.Team == RetrowaveTeam.Blue)
                {
                    TryAddUniquePlayer(_bluePlayers, ref blueCount, player);
                }
                else if (player.Team == RetrowaveTeam.Pink)
                {
                    TryAddUniquePlayer(_pinkPlayers, ref pinkCount, player);
                }
            }
        }

        private static void TryAddUniquePlayer(RetrowavePlayerController[] players, ref int count, RetrowavePlayerController player)
        {
            for (var i = 0; i < count; i++)
            {
                if (players[i] != null && players[i].OwnerClientId == player.OwnerClientId)
                {
                    return;
                }
            }

            if (count >= players.Length)
            {
                return;
            }

            players[count] = player;
            count++;
        }

        private bool CanRunDuringPhase(RetrowaveMatchManager matchManager)
        {
            if (_offlineMode && matchManager == null)
            {
                return true;
            }

            return matchManager.IsLiveMatch || (_spawnDuringWarmup && matchManager.IsWarmup);
        }

        private void HandleObjectiveChanged<T>(T _, T __)
        {
            RefreshVisuals();
        }

        private void HandleObjectivePositionChanged(Vector3 _, Vector3 __)
        {
            RefreshVisuals();
        }

        [ClientRpc]
        private void ObjectiveCapturedClientRpc(Vector3 position, int objectiveTypeValue, int capturingTeamValue)
        {
            var objectiveColor = RetrowaveArenaObjectiveCatalog.GetColor((RetrowaveArenaObjectiveType)objectiveTypeValue);
            var teamColor = capturingTeamValue switch
            {
                (int)RetrowaveTeam.Blue => RetrowaveStyle.BlueGlow,
                (int)RetrowaveTeam.Pink => RetrowaveStyle.PinkGlow,
                _ => objectiveColor,
            };
            StartCapturePulse(position, Color.Lerp(objectiveColor, teamColor, 0.72f), playAudio: true);
        }

        private void EnsureVisuals()
        {
            if (_visualRoot != null)
            {
                return;
            }

            _visualRoot = new GameObject("Arena Objective Visual");
            _visualRoot.transform.SetParent(transform, false);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Capture Disc";
            disc.transform.SetParent(_visualRoot.transform, false);
            disc.transform.localScale = new Vector3(_radius * 2f, 0.018f, _radius * 2f);
            _discTransform = disc.transform;
            _discRenderer = disc.GetComponent<MeshRenderer>();

            var collider = disc.GetComponent<Collider>();

            if (collider != null)
            {
                Destroy(collider);
            }

            _discMaterial = RetrowaveStyle.CreateTransparentLitMaterial(
                new Color(0.18f, 0.9f, 1f, 0.22f),
                new Color(0.16f, 0.92f, 1f, 1f) * 2.4f,
                0.82f,
                0f);
            _discRenderer.sharedMaterial = _discMaterial;

            var pulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulse.name = "Objective Capture Pulse";
            pulse.transform.SetParent(transform, false);
            pulse.transform.localScale = new Vector3(_radius * 2f, 0.012f, _radius * 2f);
            _pulseTransform = pulse.transform;
            _pulseRenderer = pulse.GetComponent<MeshRenderer>();

            var pulseCollider = pulse.GetComponent<Collider>();

            if (pulseCollider != null)
            {
                Destroy(pulseCollider);
            }

            _pulseMaterial = RetrowaveStyle.CreateTransparentLitMaterial(
                new Color(0.48f, 1f, 0.72f, 0f),
                new Color(0.48f, 1f, 0.72f, 1f) * 2.8f,
                0.74f,
                0f);
            _pulseRenderer.sharedMaterial = _pulseMaterial;
            _pulseRenderer.enabled = false;

            var lightObject = new GameObject("Objective Light");
            lightObject.transform.SetParent(_visualRoot.transform, false);
            lightObject.transform.localPosition = Vector3.up * 1.35f;
            _objectiveLight = lightObject.AddComponent<Light>();
            _objectiveLight.type = LightType.Point;
            _objectiveLight.range = _radius * 2.4f;
            _objectiveLight.intensity = 0f;

            var labelObject = new GameObject("Objective Label");
            labelObject.transform.SetParent(_visualRoot.transform, false);
            labelObject.transform.localPosition = Vector3.up * 2.4f;
            _label = labelObject.AddComponent<TextMeshPro>();
            _label.font = TMP_Settings.defaultFontAsset;
            _label.fontSize = 2.25f;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private void RefreshVisuals()
        {
            EnsureVisuals();

            var activeType = ActiveType;
            var isActive = activeType != RetrowaveArenaObjectiveType.None;

            if (_visualRoot != null)
            {
                _visualRoot.SetActive(isActive);
                _visualRoot.transform.position = _objectivePosition.Value;
            }

            if (!isActive)
            {
                return;
            }

            var objectiveColor = RetrowaveArenaObjectiveCatalog.GetColor(activeType);
            var teamColor = _capturingTeamValue.Value switch
            {
                (int)RetrowaveTeam.Blue => RetrowaveStyle.BlueGlow,
                (int)RetrowaveTeam.Pink => RetrowaveStyle.PinkGlow,
                _ => objectiveColor,
            };
            var progress = Mathf.Clamp01(_captureProgress.Value);
            var displayColor = Color.Lerp(objectiveColor, teamColor, 0.7f);
            var progressBucket = Mathf.FloorToInt(progress * 4f);

            if (progress <= 0.01f)
            {
                _lastProgressPulseBucket = 0;
            }
            else if (progressBucket > _lastProgressPulseBucket && progress < 0.98f)
            {
                _lastProgressPulseBucket = progressBucket;
                StartCapturePulse(_objectivePosition.Value, displayColor, playAudio: false);
            }

            if (_discTransform != null)
            {
                var pulse = 1f + Mathf.Sin(Time.time * 3.5f) * 0.025f;
                _discTransform.localScale = new Vector3(_radius * 2f * pulse, 0.018f, _radius * 2f * pulse);
            }

            if (_discMaterial != null)
            {
                SetMaterialColor(_discMaterial, "_BaseColor", new Color(displayColor.r, displayColor.g, displayColor.b, Mathf.Lerp(0.16f, 0.34f, progress)));
                SetMaterialColor(_discMaterial, "_Color", new Color(displayColor.r, displayColor.g, displayColor.b, Mathf.Lerp(0.16f, 0.34f, progress)));
                SetMaterialColor(_discMaterial, "_EmissionColor", displayColor * Mathf.Lerp(1.9f, 3.4f, progress));
            }

            if (_objectiveLight != null)
            {
                _objectiveLight.color = displayColor;
                _objectiveLight.intensity = Mathf.Lerp(2.4f, 7f, progress);
                _objectiveLight.range = _radius * Mathf.Lerp(1.55f, 2.35f, progress);
            }

            if (_label != null)
            {
                _label.color = Color.Lerp(displayColor, Color.white, 0.22f);
                var teamCode = _capturingTeamValue.Value switch
                {
                    (int)RetrowaveTeam.Blue => "BLUE",
                    (int)RetrowaveTeam.Pink => "PINK",
                    _ => "NEUTRAL",
                };
                _label.text = $"{RetrowaveArenaObjectiveCatalog.GetLabel(activeType).ToUpperInvariant()}  {teamCode}\nHOLD TO CAPTURE {Mathf.RoundToInt(progress * 100f)}%";
            }
        }

        private static void SetMaterialColor(Material material, string propertyName, Color color)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }

        private void StartCapturePulse(Vector3 position, Color color, bool playAudio)
        {
            EnsureVisuals();

            if (_pulseTransform == null || _pulseRenderer == null || _pulseMaterial == null)
            {
                return;
            }

            _pulseStartedAt = Time.time;
            _pulseTransform.position = position + Vector3.up * 0.055f;
            _pulseRenderer.enabled = true;
            SetMaterialColor(_pulseMaterial, "_BaseColor", new Color(color.r, color.g, color.b, 0.34f));
            SetMaterialColor(_pulseMaterial, "_Color", new Color(color.r, color.g, color.b, 0.34f));
            SetMaterialColor(_pulseMaterial, "_EmissionColor", color * 3.1f);

            if (playAudio)
            {
                RetrowaveArenaAudio.PlayObjectiveCapture(position);
            }
        }

        private void UpdatePulseVisual()
        {
            if (_pulseTransform == null || _pulseRenderer == null || _pulseMaterial == null || !_pulseRenderer.enabled)
            {
                return;
            }

            var t = Mathf.Clamp01((Time.time - _pulseStartedAt) / Mathf.Max(0.05f, _pulseDuration));

            if (t >= 1f)
            {
                _pulseRenderer.enabled = false;
                return;
            }

            var ease = 1f - Mathf.Pow(1f - t, 2f);
            var scale = Mathf.Lerp(_radius * 1.1f, _radius * 2.85f, ease);
            var alpha = Mathf.Lerp(0.34f, 0f, t);
            _pulseTransform.localScale = new Vector3(scale, 0.012f, scale);

            if (_pulseMaterial.HasProperty("_BaseColor"))
            {
                var baseColor = _pulseMaterial.GetColor("_BaseColor");
                baseColor.a = alpha;
                _pulseMaterial.SetColor("_BaseColor", baseColor);
            }

            if (_pulseMaterial.HasProperty("_Color"))
            {
                var baseColor = _pulseMaterial.GetColor("_Color");
                baseColor.a = alpha;
                _pulseMaterial.SetColor("_Color", baseColor);
            }
        }

        private void OnDrawGizmosSelected()
        {
            var position = Application.isPlaying ? _objectivePosition.Value : transform.position;
            Gizmos.color = RetrowaveArenaObjectiveCatalog.GetColor(ActiveType == RetrowaveArenaObjectiveType.None
                ? RetrowaveArenaObjectiveType.MidfieldControlRing
                : ActiveType);
            Gizmos.DrawWireSphere(position + Vector3.up * 1.4f, _radius);
        }
    }
}
