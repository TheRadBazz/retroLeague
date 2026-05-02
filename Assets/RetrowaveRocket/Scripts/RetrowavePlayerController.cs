using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RetrowaveRocket
{
    public struct RetrowavePlayerInputState : INetworkSerializable, IEquatable<RetrowavePlayerInputState>
    {
        public float Throttle;
        public float Steer;
        public float Roll;
        public bool Boost;
        public bool Brake;
        public bool JumpPressed;
        public bool JumpHeld;
        public bool ResetPressed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Throttle);
            serializer.SerializeValue(ref Steer);
            serializer.SerializeValue(ref Roll);
            serializer.SerializeValue(ref Boost);
            serializer.SerializeValue(ref Brake);
            serializer.SerializeValue(ref JumpPressed);
            serializer.SerializeValue(ref JumpHeld);
            serializer.SerializeValue(ref ResetPressed);
        }

        public bool Equals(RetrowavePlayerInputState other)
        {
            return Mathf.Approximately(Throttle, other.Throttle)
                   && Mathf.Approximately(Steer, other.Steer)
                   && Mathf.Approximately(Roll, other.Roll)
                   && Boost == other.Boost
                   && Brake == other.Brake
                   && JumpPressed == other.JumpPressed
                   && JumpHeld == other.JumpHeld
                   && ResetPressed == other.ResetPressed;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RetrowavePlayerController : NetworkBehaviour
    {
        private static readonly Vector3[] GroundProbeLocalOffsets =
        {
            new Vector3(-0.48f, -0.08f, 0.74f),
            new Vector3(0.48f, -0.08f, 0.74f),
            new Vector3(-0.48f, -0.08f, -0.74f),
            new Vector3(0.48f, -0.08f, -0.74f),
        };

        private const float ProbeCastStart = 0.38f;
        private const float ProbeRayLength = 1.4f;
        private const float MinDriveableGroundNormalY = 0.52f;
        private const float MinSuspensionAlignment = 0.18f;
        private const float RideHeight = 0.58f;
        private const float SuspensionSpring = 104f;
        private const float SuspensionDamping = 14.5f;
        private const float GroundDriveAcceleration = 58f;
        private const float GroundReverseAcceleration = 39f;
        private const float GroundGrip = 24f;
        private const float GroundSteeringTorque = 14.5f;
        private const float GroundAlignTorque = 36f;
        private const float GroundAngularDamping = 4.6f;
        private const float GroundYawActiveDamping = 1.4f;
        private const float GroundYawReleaseDamping = 7.2f;
        private const float GroundDrag = 1.45f;
        private const float GroundStickForce = 13f;
        private const float JumpImpulse = 9.35f;
        private const float JumpBoostCost = 10f;
        private const float AirPitchTorque = 12.5f;
        private const float AirYawTorque = 11f;
        private const float AirRollTorque = 13.5f;
        private const float AirForwardThrust = 14f;
        private const float AirStrafeThrust = 10.5f;
        private const float AirHoverBurst = 3.4f;
        private const float AirBrakeDamping = 1.05f;
        private const float AirAngularDamping = 2.8f;
        private const float AirAutoLevelTorque = 6.5f;
        private const float AirYawStabilizeTorque = 2.6f;
        private const float AirLateralDamping = 0.55f;
        private const float AirGravityAssist = 4.5f;
        private const float GlideBoostDrain = 18f;
        private const float GlideForwardAcceleration = 8.5f;
        private const float GlideSteerAcceleration = 5.5f;
        private const float GlideVerticalAssist = 9.5f;
        private const float GlideMaxFallSpeed = 5.2f;
        private const float GlideActivationMaxUpwardSpeed = 0.2f;
        private const float GroundVelocityTurnAssist = 5.2f;
        private const float BoostForce = 34f;
        private const float BoostDrainRate = 28f;
        private const float BoostRechargeDelaySeconds = 1.25f;
        private const float MaxDriveSpeed = 29f;
        private const float MaxReverseSpeed = 15f;
        private const float MaxBoostSpeed = 38f;
        private const float GroundedGraceSeconds = 0.14f;
        private const float BoostStartThreshold = 0.6f;
        private const float ClientSpeedSmoothTime = 0.14f;
        private const float ClientSpeedMaxChangeRate = 90f;
        private const float ClientVelocityProbeBlendRate = 6.5f;
        private const float ClientVelocityDisplayBlendRate = 10f;
        private const float ObservedVelocityTeleportDistanceSqr = 256f;
        private const float ObservedVelocityMinDisplacementSqr = 0.0004f;
        private const float CleanLandingMinAirTime = 0.65f;
        private const float CleanLandingMinAlignment = 0.72f;
        private const float AerialTrickMinAirTime = 0.42f;
        private const float AerialTrickCooldownSeconds = 1.35f;
        private const float FlipTrickSpinThreshold = 5.8f;

        private readonly NetworkVariable<int> _teamValue = new(
            (int)RetrowaveTeam.Blue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _lobbyRoleValue = new(
            (int)RetrowaveLobbyRole.Spectator,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _hasSelectedRole = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _boostAmount = new(
            RetrowaveArenaConfig.StartingBoost,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _speedBoostTimer = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _boostFx = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _replicatedSpeed = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _engineAudioThrottle = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _engineAudioBoosting = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _utilityRoleValue = new(
            (int)RetrowaveUtilityRole.Striker,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _hasSelectedUtilityRole = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _podiumPresentationHidden = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Rigidbody _rigidbody;
        private Collider[] _colliders;
        private MeshRenderer[] _vehicleRenderers;
        private Light _boostLight;
        private VehicleStatusEffects _statusEffects;
        private RarePowerUpInventory _rarePowerUpInventory;
        private VehicleOverdriveSystem _overdrive;
        private VehicleStyleMeter _styleMeter;
        private Canvas _nameTagCanvas;
        private TextMeshProUGUI _nameTagText;
        private GameObject _nameTagRoleIconRoot;
        private Image _nameTagRoleIconImage;
        private TextMeshProUGUI _nameTagStatusText;
        private GameObject _nameTagPowerUpIconRoot;
        private Image _nameTagPowerUpIconImage;
        private string _appliedDisplayName = string.Empty;
        private Material _appliedBodyMaterial;
        private RetrowavePlayerInputState _latestInput;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private bool _jumpQueued;
        private bool _resetQueued;
        private float _cachedThrottle;
        private float _cachedSteer;
        private float _cachedRoll;
        private bool _cachedBoost;
        private bool _cachedBrake;
        private bool _cachedJumpHeld;
        private bool _boostRequiresRelease;
        private bool _glideRequiresRelease;
        private float _boostRechargeDelayTimer;
        private bool _isGrounded;
        private Vector3 _groundNormal = Vector3.up;
        private float _coyoteTimer;
        private int _groundProbeCount;
        private bool _styleWasGrounded = true;
        private float _styleAirborneTime;
        private float _styleAirborneSpin;
        private float _stylePeakHeight;
        private float _styleMinVerticalVelocity;
        private float _lastAerialManeuverStyleAt;
        private float _lastFlipStyleAt;
        private Vector3 _observedVelocity;
        private Vector3 _smoothedVelocity;
        private Vector3 _lastObservedPosition;
        private float _smoothedSpeed;
        private float _speedSmoothVelocity;
        private bool _hasObservedPosition;
        private bool _serverJumpQueued;
        private bool _serverResetQueued;

        public static RetrowavePlayerController LocalOwner { get; private set; }
        public static RetrowavePlayerController LocalPlayer { get; private set; }

        public RetrowaveTeam Team => (RetrowaveTeam)_teamValue.Value;
        public RetrowaveLobbyRole LobbyRole => (RetrowaveLobbyRole)_lobbyRoleValue.Value;
        public bool HasSelectedRole => _hasSelectedRole.Value;
        public bool IsArenaParticipant => HasSelectedRole && LobbyRole != RetrowaveLobbyRole.Spectator;
        public float BoostNormalized => Mathf.Clamp01(_boostAmount.Value / RetrowaveArenaConfig.MaxBoost);
        public float BoostAmount => Mathf.Clamp(_boostAmount.Value, 0f, RetrowaveArenaConfig.MaxBoost);
        public bool HasSpeedBoost => _speedBoostTimer.Value > 0.05f;
        public Vector3 CurrentVelocity => ResolveCurrentVelocity();
        public float CurrentSpeed => ResolveCurrentSpeed();
        public float MaxHudSpeed => MaxBoostSpeed * RetrowaveArenaConfig.SpeedBurstMultiplier;
        public float SpeedNormalized => Mathf.Clamp01(CurrentSpeed / Mathf.Max(0.01f, MaxHudSpeed));
        public bool IsGroundedForHud => _isGrounded;
        public Rigidbody Body => _rigidbody;
        public ulong ControllingClientId => OwnerClientId;
        public bool BoostFxActive => _boostFx.Value;
        public float EngineAudioThrottle => IsOwner ? _cachedThrottle : _engineAudioThrottle.Value;
        public bool EngineAudioBoosting => _engineAudioBoosting.Value;
        public RetrowaveUtilityRole UtilityRole => (RetrowaveUtilityRole)_utilityRoleValue.Value;
        public bool HasSelectedUtilityRole => _hasSelectedUtilityRole.Value;
        public float HeatNormalized => _overdrive != null ? _overdrive.HeatNormalized : 0f;
        public bool IsOverheated => _overdrive != null && _overdrive.IsOverheated;
        public bool IsOvercharged => _overdrive != null && _overdrive.IsOvercharged;
        public bool IsStunned => _statusEffects != null && _statusEffects.IsStunned;
        public bool IsSlowed => _statusEffects != null && (_statusEffects.MovementMultiplier < 0.98f || _statusEffects.SteeringMultiplier < 0.98f);
        public float StyleNormalized => _styleMeter != null ? _styleMeter.StyleNormalized : 0f;
        public float ObjectiveCaptureMultiplier => _styleMeter != null ? _styleMeter.CaptureSpeedMultiplier : 1f;
        public int LastStyleAwardSerial => _styleMeter != null ? _styleMeter.LastAwardSerial : 0;
        public float LastStyleAwardPoints => _styleMeter != null ? _styleMeter.LastAwardPoints : 0f;
        public RetrowaveStyleEvent LastStyleAwardEvent => _styleMeter != null ? _styleMeter.LastAwardEvent : RetrowaveStyleEvent.ControlledTouch;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _vehicleRenderers = GetComponentsInChildren<MeshRenderer>(true);
            _boostLight = GetComponentInChildren<Light>(true);
            _statusEffects = GetComponent<VehicleStatusEffects>();
            _rarePowerUpInventory = GetComponent<RarePowerUpInventory>();
            _overdrive = GetComponent<VehicleOverdriveSystem>();
            _styleMeter = GetComponent<VehicleStyleMeter>();
            EnsureNameTag();

            if (_boostLight == null)
            {
                var glow = new GameObject("Boost Glow");
                glow.transform.SetParent(transform, false);
                glow.transform.localPosition = new Vector3(0f, 0.2f, -1f);
                _boostLight = glow.AddComponent<Light>();
                _boostLight.type = LightType.Point;
                _boostLight.range = 7f;
            }

            _boostLight.intensity = 0f;
            _rigidbody.centerOfMass = new Vector3(0f, -0.38f, 0f);
            _rigidbody.maxAngularVelocity = 16f;
            ApplyTeamVisuals(Team);
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner)
            {
                CacheLocalInput();
            }

            UpdateObservedMotion();
            RefreshPresentationState();
            UpdateBoostVisuals();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (!IsArenaParticipant)
            {
                if (IsServer)
                {
                    _boostFx.Value = false;
                    SetReplicatedSpeedServer(0f);
                }

                return;
            }

            if (IsOwner)
            {
                var outbound = new RetrowavePlayerInputState
                {
                    Throttle = _cachedThrottle,
                    Steer = _cachedSteer,
                    Roll = _cachedRoll,
                    Boost = _cachedBoost,
                    Brake = _cachedBrake,
                    JumpPressed = _jumpQueued,
                    JumpHeld = _cachedJumpHeld,
                    ResetPressed = _resetQueued,
                };

                if (IsServer)
                {
                    _latestInput = outbound;
                }
                else
                {
                    var continuousInput = outbound;
                    continuousInput.JumpPressed = false;
                    continuousInput.ResetPressed = false;
                    SubmitInputServerRpc(continuousInput);

                    if (outbound.JumpPressed)
                    {
                        SubmitJumpPressedServerRpc();
                    }

                    if (outbound.ResetPressed)
                    {
                        SubmitResetPressedServerRpc();
                    }
                }

                _jumpQueued = false;
                _resetQueued = false;
            }

            if (!IsServer)
            {
                return;
            }

            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager != null && matchManager.IsGameplayLocked)
            {
                _latestInput = default;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _boostFx.Value = false;
                SetReplicatedSpeedServer(0f);
                SetEngineAudioStateServer(0f, false);
                return;
            }

            var simulationInput = _latestInput;

            if (_serverJumpQueued)
            {
                simulationInput.JumpPressed = true;
                _serverJumpQueued = false;
            }

            if (_serverResetQueued)
            {
                simulationInput.ResetPressed = true;
                _serverResetQueued = false;
            }

            SimulateMovement(simulationInput);
            PublishServerMotionState();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _teamValue.OnValueChanged += HandleTeamChanged;
            _lobbyRoleValue.OnValueChanged += HandleLobbyRoleChanged;
            _hasSelectedRole.OnValueChanged += HandleSelectedRoleChanged;
            _utilityRoleValue.OnValueChanged += HandleUtilityRoleChanged;
            _podiumPresentationHidden.OnValueChanged += HandlePodiumPresentationChanged;

            if (!IsServer)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }

            ResetObservedMotion();
            ApplyTeamVisuals(Team);
            RefreshPresentationState();

            if (IsOwner)
            {
                LocalPlayer = this;
                SubmitDisplayName();
            }

            if (IsServer)
            {
                RetrowaveMatchManager.Instance?.HandlePlayerObjectSpawned(OwnerClientId);
            }
        }

        public override void OnNetworkDespawn()
        {
            _teamValue.OnValueChanged -= HandleTeamChanged;
            _lobbyRoleValue.OnValueChanged -= HandleLobbyRoleChanged;
            _hasSelectedRole.OnValueChanged -= HandleSelectedRoleChanged;
            _utilityRoleValue.OnValueChanged -= HandleUtilityRoleChanged;
            _podiumPresentationHidden.OnValueChanged -= HandlePodiumPresentationChanged;

            if (LocalOwner == this)
            {
                LocalOwner = null;
                RetrowaveCameraRig.ShowOverview();
            }

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }

            _observedVelocity = Vector3.zero;
            _smoothedVelocity = Vector3.zero;
            _smoothedSpeed = 0f;
            _speedSmoothVelocity = 0f;
            _hasObservedPosition = false;

            base.OnNetworkDespawn();
        }

        private Vector3 ResolveCurrentVelocity()
        {
            if (_rigidbody == null)
            {
                return _smoothedVelocity;
            }

            if (IsServer || !_rigidbody.isKinematic || _rigidbody.linearVelocity.sqrMagnitude > 0.01f)
            {
                return _rigidbody.linearVelocity;
            }

            if (_smoothedVelocity.sqrMagnitude > 0.01f)
            {
                return _smoothedVelocity;
            }

            return transform.forward * ResolveCurrentSpeed();
        }

        private float ResolveCurrentSpeed()
        {
            if (_rigidbody == null)
            {
                return _smoothedSpeed;
            }

            if (IsServer || !_rigidbody.isKinematic || _rigidbody.linearVelocity.sqrMagnitude > 0.01f)
            {
                return _rigidbody.linearVelocity.magnitude;
            }

            return _smoothedSpeed;
        }

        private void UpdateObservedMotion()
        {
            var currentPosition = transform.position;

            if (!_hasObservedPosition)
            {
                _lastObservedPosition = currentPosition;
                _observedVelocity = _rigidbody != null ? _rigidbody.linearVelocity : Vector3.zero;
                _smoothedVelocity = _observedVelocity;
                _smoothedSpeed = _observedVelocity.magnitude;
                _hasObservedPosition = true;
                return;
            }

            var deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            var displacement = currentPosition - _lastObservedPosition;
            _lastObservedPosition = currentPosition;

            if (displacement.sqrMagnitude > ObservedVelocityTeleportDistanceSqr)
            {
                _observedVelocity = Vector3.zero;
                _smoothedVelocity = Vector3.zero;
                _smoothedSpeed = 0f;
                _speedSmoothVelocity = 0f;
                return;
            }

            if (IsServer && _rigidbody != null)
            {
                _observedVelocity = _rigidbody.linearVelocity;
                _smoothedVelocity = _observedVelocity;
                _smoothedSpeed = _observedVelocity.magnitude;
                return;
            }

            if (displacement.sqrMagnitude > ObservedVelocityMinDisplacementSqr)
            {
                var rawVelocity = displacement / deltaTime;
                var probeBlend = 1f - Mathf.Exp(-deltaTime * ClientVelocityProbeBlendRate);
                _observedVelocity = Vector3.Lerp(_observedVelocity, rawVelocity, probeBlend);
            }

            var targetSpeed = Mathf.Max(0f, _replicatedSpeed.Value);
            _smoothedSpeed = Mathf.SmoothDamp(
                _smoothedSpeed,
                targetSpeed,
                ref _speedSmoothVelocity,
                ClientSpeedSmoothTime,
                ClientSpeedMaxChangeRate,
                deltaTime);

            if (_smoothedSpeed < 0.04f && targetSpeed < 0.04f)
            {
                _smoothedSpeed = 0f;
                _speedSmoothVelocity = 0f;
            }

            var direction = _observedVelocity.sqrMagnitude > 0.25f
                ? _observedVelocity.normalized
                : _smoothedVelocity.sqrMagnitude > 0.25f
                    ? _smoothedVelocity.normalized
                    : transform.forward;
            var targetVelocity = direction * _smoothedSpeed;
            var displayBlend = 1f - Mathf.Exp(-deltaTime * ClientVelocityDisplayBlendRate);
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, targetVelocity, displayBlend);
        }

        private void ResetObservedMotion()
        {
            _lastObservedPosition = transform.position;
            _observedVelocity = Vector3.zero;
            _smoothedVelocity = Vector3.zero;
            _smoothedSpeed = 0f;
            _speedSmoothVelocity = 0f;
            _hasObservedPosition = true;
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void SubmitInputServerRpc(RetrowavePlayerInputState input)
        {
            input.JumpPressed = false;
            input.ResetPressed = false;
            _latestInput = input;
        }

        [ServerRpc]
        private void SubmitJumpPressedServerRpc()
        {
            _serverJumpQueued = true;
        }

        [ServerRpc]
        private void SubmitResetPressedServerRpc()
        {
            _serverResetQueued = true;
        }

        [ServerRpc]
        private void SubmitRoleSelectionServerRpc(int roleValue)
        {
            var clampedRole = (RetrowaveLobbyRole)Mathf.Clamp(roleValue, (int)RetrowaveLobbyRole.Spectator, (int)RetrowaveLobbyRole.Pink);
            RetrowaveMatchManager.Instance?.HandlePlayerRoleSelection(OwnerClientId, clampedRole);
        }

        [ServerRpc]
        private void SubmitUtilityRoleSelectionServerRpc(int roleValue)
        {
            var clampedRole = (RetrowaveUtilityRole)Mathf.Clamp(
                roleValue,
                (int)RetrowaveUtilityRole.Striker,
                (int)RetrowaveUtilityRole.Disruptor);
            SetUtilityRoleServer(clampedRole, true);
        }

        [ServerRpc]
        private void SubmitDisplayNameServerRpc(string displayName)
        {
            RetrowaveMatchManager.Instance?.HandlePlayerDisplayName(OwnerClientId, displayName);
        }

        public void ConfigureServer(RetrowaveTeam team, int spawnIndex, int teamPlayerCount)
        {
            ConfigureServer(team, RetrowaveArenaConfig.GetSpawnPoint(team, spawnIndex, teamPlayerCount));
        }

        public void ConfigureServer(RetrowaveTeam team, Vector3 spawnPosition)
        {
            if (!IsServer)
            {
                return;
            }

            var wasArenaParticipant = _hasSelectedRole.Value && _lobbyRoleValue.Value != (int)RetrowaveLobbyRole.Spectator;
            var previousTeam = (RetrowaveTeam)_teamValue.Value;
            _lobbyRoleValue.Value = team == RetrowaveTeam.Blue ? (int)RetrowaveLobbyRole.Blue : (int)RetrowaveLobbyRole.Pink;
            _hasSelectedRole.Value = true;
            _hasSelectedUtilityRole.Value = wasArenaParticipant && previousTeam == team && _hasSelectedUtilityRole.Value;
            _teamValue.Value = (int)team;
            _spawnPosition = RetrowaveArenaConfig.ClampToPlayableSpawn(spawnPosition, team);
            _spawnRotation = RetrowaveArenaConfig.GetSpawnRotation(team);
            ResetToSpawn();
        }

        public void ResetToSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _spawnPosition = RetrowaveArenaConfig.ClampToPlayableSpawn(_spawnPosition, Team);
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _rigidbody.position = _spawnPosition;
            _rigidbody.rotation = _spawnRotation;
            Physics.SyncTransforms();
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _podiumPresentationHidden.Value = false;
            _boostAmount.Value = RetrowaveArenaConfig.StartingBoost;
            _speedBoostTimer.Value = 0f;
            _groundNormal = Vector3.up;
            _isGrounded = false;
            _groundProbeCount = 0;
            _coyoteTimer = 0f;
            _boostRequiresRelease = false;
            _glideRequiresRelease = false;
            _boostRechargeDelayTimer = 0f;
            _serverJumpQueued = false;
            _serverResetQueued = false;
            SetReplicatedSpeedServer(0f);
            ResetAerialStyleTracking();
            SetEngineAudioStateServer(0f, false);
            _statusEffects?.ClearServer();
            _overdrive?.ClearServer();
            _styleMeter?.ClearServer();
        }

        public void SetPodiumPresentationServer(Vector3 position, Quaternion rotation, bool isVisible)
        {
            if (!IsServer)
            {
                return;
            }

            _podiumPresentationHidden.Value = !isVisible;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            transform.SetPositionAndRotation(position, rotation);
            _boostAmount.Value = RetrowaveArenaConfig.StartingBoost;
            _speedBoostTimer.Value = 0f;
            _latestInput = default;
            _boostFx.Value = false;
            _boostRequiresRelease = false;
            _glideRequiresRelease = false;
            _boostRechargeDelayTimer = 0f;
            _serverJumpQueued = false;
            _serverResetQueued = false;
            SetReplicatedSpeedServer(0f);
            ResetAerialStyleTracking();
            SetEngineAudioStateServer(0f, false);
            _statusEffects?.ClearServer();
            _overdrive?.ClearServer();
            _styleMeter?.ClearServer();
        }

        public void SetSpectatorStateServer(bool hasSelectedRole)
        {
            if (!IsServer)
            {
                return;
            }

            _lobbyRoleValue.Value = (int)RetrowaveLobbyRole.Spectator;
            _hasSelectedRole.Value = hasSelectedRole;
            _hasSelectedUtilityRole.Value = false;
            _podiumPresentationHidden.Value = false;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            transform.SetPositionAndRotation(RetrowaveArenaConfig.GetSpectatorStagingPoint(OwnerClientId), Quaternion.identity);
            _boostAmount.Value = RetrowaveArenaConfig.StartingBoost;
            _speedBoostTimer.Value = 0f;
            _groundNormal = Vector3.up;
            _isGrounded = false;
            _groundProbeCount = 0;
            _coyoteTimer = 0f;
            _latestInput = default;
            _boostFx.Value = false;
            _boostRequiresRelease = false;
            _glideRequiresRelease = false;
            _boostRechargeDelayTimer = 0f;
            _serverJumpQueued = false;
            _serverResetQueued = false;
            SetReplicatedSpeedServer(0f);
            ResetAerialStyleTracking();
            SetEngineAudioStateServer(0f, false);
            _statusEffects?.ClearServer();
            _overdrive?.ClearServer();
            _styleMeter?.ClearServer();
        }

        public void RequestRoleSelection(RetrowaveLobbyRole role)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            SubmitRoleSelectionServerRpc((int)role);
        }

        public void RequestUtilityRoleSelection(RetrowaveUtilityRole role)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            SubmitUtilityRoleSelectionServerRpc((int)role);
        }

        public void SetUtilityRoleServer(RetrowaveUtilityRole role, bool selected)
        {
            if (!IsServer)
            {
                return;
            }

            _utilityRoleValue.Value = (int)role;
            _hasSelectedUtilityRole.Value = selected;
        }

        public void ApplyPowerUp(RetrowavePowerUpType type)
        {
            if (!IsServer)
            {
                return;
            }

            switch (type)
            {
                case RetrowavePowerUpType.BoostRefill:
                    _boostAmount.Value = RetrowaveArenaConfig.MaxBoost;
                    _boostRechargeDelayTimer = 0f;
                    break;
                case RetrowavePowerUpType.SpeedBurst:
                    _speedBoostTimer.Value = RetrowaveArenaConfig.SpeedBurstDuration;
                    _rigidbody.AddForce(transform.forward * 9f, ForceMode.VelocityChange);
                    break;
            }
        }

        public void ClearPowerUpsForMatchStartServer()
        {
            if (!IsServer)
            {
                return;
            }

            _speedBoostTimer.Value = 0f;
            _boostFx.Value = false;
            _boostRequiresRelease = false;
            _glideRequiresRelease = false;
            _serverJumpQueued = false;
            _serverResetQueued = false;
            SetReplicatedSpeedServer(0f);
            ResetAerialStyleTracking();
            SetEngineAudioStateServer(0f, false);
            _statusEffects?.ClearServer();
            _overdrive?.ClearServer();
            _styleMeter?.ClearServer();
            _rarePowerUpInventory ??= GetComponent<RarePowerUpInventory>();
            _rarePowerUpInventory?.ClearServer();
        }

        public static void ClearLocalOwner()
        {
            LocalOwner = null;
            LocalPlayer = null;
        }

        private void CacheLocalInput()
        {
            if (RetrowaveGameBootstrap.IsGameplayInputBlocked() || !IsArenaParticipant)
            {
                _cachedThrottle = 0f;
                _cachedSteer = 0f;
                _cachedRoll = 0f;
                _cachedBoost = false;
                _cachedBrake = false;
                _cachedJumpHeld = false;
                _jumpQueued = false;
                _resetQueued = false;
                _boostRequiresRelease = false;
                _glideRequiresRelease = false;
                return;
            }

            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            var keyboardThrottle = 0f;
            var keyboardSteer = 0f;
            var keyboardRoll = 0f;
            var keyboardBoost = false;
            var keyboardBrake = false;
            var keyboardJumpHeld = false;

            if (keyboard != null)
            {
                keyboardThrottle += RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.DriveForward) ? 1f : 0f;
                keyboardThrottle -= RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.DriveReverse) ? 1f : 0f;
                keyboardSteer += RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.SteerRight) ? 1f : 0f;
                keyboardSteer -= RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.SteerLeft) ? 1f : 0f;
                keyboardRoll += RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.AirRollRight) ? 1f : 0f;
                keyboardRoll -= RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.AirRollLeft) ? 1f : 0f;
                keyboardBoost = RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.Boost);
                keyboardBrake = RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.Brake);
                keyboardJumpHeld = RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.Jump);

                if (RetrowaveInputBindings.WasPressedThisFrame(keyboard, RetrowaveBindingAction.Jump))
                {
                    _jumpQueued = true;
                }

                if (RetrowaveInputBindings.WasPressedThisFrame(keyboard, RetrowaveBindingAction.ResetCar))
                {
                    _resetQueued = true;
                }
            }

            _cachedThrottle = keyboardThrottle;
            _cachedSteer = keyboardSteer;
            _cachedRoll = keyboardRoll;
            _cachedBoost = keyboardBoost;
            _cachedBrake = keyboardBrake;
            _cachedJumpHeld = keyboardJumpHeld;

            if (gamepad != null)
            {
                var leftStick = gamepad.leftStick.ReadValue();
                _cachedThrottle = Mathf.Abs(leftStick.y) > Mathf.Abs(_cachedThrottle) ? leftStick.y : _cachedThrottle;
                _cachedSteer = Mathf.Abs(leftStick.x) > Mathf.Abs(_cachedSteer) ? leftStick.x : _cachedSteer;

                var shoulderRoll = 0f;
                shoulderRoll += gamepad.rightShoulder.isPressed ? 1f : 0f;
                shoulderRoll -= gamepad.leftShoulder.isPressed ? 1f : 0f;
                _cachedRoll = Mathf.Abs(shoulderRoll) > Mathf.Abs(_cachedRoll) ? shoulderRoll : _cachedRoll;
                _cachedBoost |= gamepad.rightTrigger.ReadValue() > 0.35f;
                _cachedBrake |= gamepad.leftTrigger.ReadValue() > 0.35f;
                _cachedJumpHeld |= gamepad.buttonSouth.isPressed;

                if (gamepad.buttonSouth.wasPressedThisFrame)
                {
                    _jumpQueued = true;
                }

                if (gamepad.startButton.wasPressedThisFrame)
                {
                    _resetQueued = true;
                }
            }

            if (!_cachedBoost)
            {
                _boostRequiresRelease = false;
            }
            else if (_boostRequiresRelease || _boostAmount.Value <= BoostStartThreshold)
            {
                _cachedBoost = false;
                _boostRequiresRelease = true;
            }
        }

        private void SimulateMovement(RetrowavePlayerInputState input)
        {
            ApplyGroundProbes();
            var speedMultiplier = _speedBoostTimer.Value > 0f ? RetrowaveArenaConfig.SpeedBurstMultiplier : 1f;
            var controlInput = _statusEffects != null ? _statusEffects.ModifyInput(input) : input;

            if (_speedBoostTimer.Value > 0f)
            {
                _speedBoostTimer.Value = Mathf.Max(0f, _speedBoostTimer.Value - Time.fixedDeltaTime);
            }

            if (_boostRechargeDelayTimer > 0f)
            {
                _boostRechargeDelayTimer = Mathf.Max(0f, _boostRechargeDelayTimer - Time.fixedDeltaTime);
            }
            else
            {
                var rechargeMultiplier = _overdrive != null ? _overdrive.RechargeMultiplier : 1f;
                _boostAmount.Value = Mathf.Clamp(
                    _boostAmount.Value + RetrowaveArenaConfig.PassiveBoostRegen * rechargeMultiplier * Time.fixedDeltaTime,
                    0f,
                    RetrowaveArenaConfig.MaxBoost);
            }

            if (!controlInput.Boost)
            {
                _boostRequiresRelease = false;
            }

            var treatedAsGrounded = _isGrounded || _coyoteTimer > 0f;

            if (treatedAsGrounded || !controlInput.JumpHeld)
            {
                _glideRequiresRelease = false;
            }

            if (_statusEffects != null)
            {
                speedMultiplier = _statusEffects.ModifyMaxSpeedMultiplier(speedMultiplier);
            }

            speedMultiplier *= RetrowaveUtilityRoleCatalog.GetMaxSpeedMultiplier(UtilityRole);

            if (_overdrive != null)
            {
                speedMultiplier *= _overdrive.MaxSpeedMultiplier;
            }

            if (_styleMeter != null)
            {
                speedMultiplier *= Mathf.Lerp(1f, 1.018f, _styleMeter.StyleNormalized);
            }

            if (treatedAsGrounded && _groundNormal.y < 0.78f)
            {
                AwardStyleServer(RetrowaveStyleEvent.WallRide, Time.fixedDeltaTime);
            }

            UpdateAerialStyleTracking(controlInput, treatedAsGrounded);

            var gripMultiplier = RetrowaveUtilityRoleCatalog.GetGroundGripMultiplier(UtilityRole)
                                 * (_overdrive != null ? _overdrive.GroundGripMultiplier : 1f);

            if (treatedAsGrounded)
            {
                SimulateGrounded(controlInput, speedMultiplier, gripMultiplier);
            }
            else
            {
                SimulateAirborne(controlInput);
            }

            HandleJump(controlInput, treatedAsGrounded);
            var isGliding = !treatedAsGrounded && TryApplyGlide(controlInput);

            if (controlInput.Boost && _boostAmount.Value <= BoostStartThreshold)
            {
                _boostRequiresRelease = true;
            }

            var canBoost = _overdrive == null || _overdrive.CanBoost;

            if (controlInput.Boost && !canBoost)
            {
                _boostRequiresRelease = true;
            }

            var isBoosting = controlInput.Boost && !_boostRequiresRelease && canBoost && _boostAmount.Value > BoostStartThreshold;

            if (isBoosting)
            {
                ApplyBoost(treatedAsGrounded);
                var drainMultiplier = RetrowaveUtilityRoleCatalog.GetBoostDrainMultiplier(UtilityRole)
                                      * (_overdrive != null ? _overdrive.BoostDrainMultiplier : 1f)
                                      * (_styleMeter != null ? _styleMeter.BoostEfficiencyMultiplier : 1f);
                SpendBoost(BoostDrainRate * drainMultiplier * Time.fixedDeltaTime);

                if (_boostAmount.Value <= BoostStartThreshold)
                {
                    DepleteBoost();
                    isBoosting = false;
                    _boostRequiresRelease = true;
                }
            }

            _overdrive?.TickServer(isBoosting, isGliding, treatedAsGrounded);
            _boostFx.Value = isBoosting || isGliding;
            SetEngineAudioStateServer(controlInput.Throttle, isBoosting);

            if (input.ResetPressed || !RetrowaveArenaConfig.IsWithinArenaRecoveryBounds(transform.position))
            {
                ResetToSpawn();
                return;
            }

            var maxVelocity = isBoosting ? MaxBoostSpeed * speedMultiplier : MaxDriveSpeed * speedMultiplier;

            if (_rigidbody.linearVelocity.sqrMagnitude > maxVelocity * maxVelocity)
            {
                _rigidbody.linearVelocity = _rigidbody.linearVelocity.normalized * maxVelocity;
            }
        }

        private bool ApplyGroundProbes()
        {
            _groundProbeCount = 0;
            var hitNormalSum = Vector3.zero;
            var transformUp = transform.up;

            for (var i = 0; i < GroundProbeLocalOffsets.Length; i++)
            {
                var probeBase = transform.TransformPoint(GroundProbeLocalOffsets[i]);
                var rayOrigin = probeBase + transformUp * ProbeCastStart;

                if (!Physics.Raycast(rayOrigin, -transformUp, out var hit, ProbeCastStart + ProbeRayLength, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody == _rigidbody)
                {
                    continue;
                }

                if (hit.normal.y < MinDriveableGroundNormalY || Vector3.Dot(transformUp, hit.normal) < MinSuspensionAlignment)
                {
                    continue;
                }

                var distance = Mathf.Max(0f, hit.distance - ProbeCastStart);
                var compression = RideHeight - distance;

                if (compression <= -0.08f)
                {
                    continue;
                }

                var pointVelocity = _rigidbody.GetPointVelocity(probeBase);
                var springVelocity = Vector3.Dot(pointVelocity, transformUp);
                var springForce = compression * SuspensionSpring - springVelocity * SuspensionDamping;
                _rigidbody.AddForceAtPosition(transformUp * springForce, probeBase, ForceMode.Acceleration);

                hitNormalSum += hit.normal;
                _groundProbeCount++;
            }

            if (_groundProbeCount > 0)
            {
                var averagedNormal = (hitNormalSum / _groundProbeCount).normalized;
                _groundNormal = Vector3.Slerp(_groundNormal, averagedNormal, 0.45f);
                _isGrounded = true;
                _coyoteTimer = GroundedGraceSeconds;
                return true;
            }

            _isGrounded = false;
            _groundNormal = Vector3.Slerp(_groundNormal, Vector3.up, Time.fixedDeltaTime * 8f);
            _coyoteTimer = Mathf.Max(0f, _coyoteTimer - Time.fixedDeltaTime);
            return false;
        }

        private void UpdateAerialStyleTracking(RetrowavePlayerInputState input, bool treatedAsGrounded)
        {
            if (treatedAsGrounded)
            {
                if (!_styleWasGrounded)
                {
                    EvaluateLandingStyleServer();
                }

                ResetAerialStyleTracking();
                return;
            }

            if (_styleWasGrounded)
            {
                _styleAirborneTime = 0f;
                _styleAirborneSpin = 0f;
                _stylePeakHeight = transform.position.y;
                _styleMinVerticalVelocity = 0f;
            }

            _styleWasGrounded = false;
            _styleAirborneTime += Time.fixedDeltaTime;
            _stylePeakHeight = Mathf.Max(_stylePeakHeight, transform.position.y);
            _styleMinVerticalVelocity = Mathf.Min(_styleMinVerticalVelocity, _rigidbody.linearVelocity.y);

            var localAngularVelocity = transform.InverseTransformDirection(_rigidbody.angularVelocity);
            var pitchSpin = Mathf.Abs(localAngularVelocity.x);
            var yawSpin = Mathf.Abs(localAngularVelocity.y);
            var rollSpin = Mathf.Abs(localAngularVelocity.z);
            var trickRate = pitchSpin + rollSpin + yawSpin * 0.55f;
            _styleAirborneSpin += trickRate * Time.fixedDeltaTime;

            if (_styleAirborneTime < AerialTrickMinAirTime)
            {
                return;
            }

            var airControl = Mathf.Abs(input.Throttle) + Mathf.Abs(input.Steer) + Mathf.Abs(input.Roll);

            if (airControl > 0.85f
                && trickRate > 5.2f
                && _rigidbody.linearVelocity.magnitude > 7f
                && Time.time >= _lastAerialManeuverStyleAt + AerialTrickCooldownSeconds)
            {
                var multiplier = Mathf.Clamp(trickRate / 8f, 0.65f, 1.4f);
                AwardStyleServer(RetrowaveStyleEvent.AerialManeuver, multiplier);
                _lastAerialManeuverStyleAt = Time.time;
            }

            var flipSpin = Mathf.Max(pitchSpin, rollSpin);

            if (_styleAirborneSpin >= FlipTrickSpinThreshold
                && flipSpin > 5.6f
                && Time.time >= _lastFlipStyleAt + AerialTrickCooldownSeconds)
            {
                var multiplier = Mathf.Clamp(_styleAirborneSpin / 8f, 0.75f, 1.55f);
                AwardStyleServer(RetrowaveStyleEvent.FlipTrick, multiplier);
                _styleAirborneSpin = Mathf.Max(0f, _styleAirborneSpin - FlipTrickSpinThreshold);
                _lastFlipStyleAt = Time.time;
            }
        }

        private void EvaluateLandingStyleServer()
        {
            if (_styleAirborneTime < CleanLandingMinAirTime)
            {
                return;
            }

            var alignment = Vector3.Dot(transform.up, _groundNormal);
            var angularSpeed = _rigidbody.angularVelocity.magnitude;
            var planarSpeed = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, _groundNormal).magnitude;

            if (alignment < CleanLandingMinAlignment || angularSpeed > 8.4f || planarSpeed < 3.5f)
            {
                return;
            }

            var heightDrop = Mathf.Max(0f, _stylePeakHeight - transform.position.y);
            var fallBonus = Mathf.InverseLerp(1f, 9f, heightDrop);
            var fallSpeedBonus = Mathf.InverseLerp(3f, 12f, -_styleMinVerticalVelocity);
            var spinBonus = Mathf.InverseLerp(3f, 13f, _styleAirborneSpin);
            var airTimeBonus = Mathf.InverseLerp(0.65f, 2.4f, _styleAirborneTime);
            var multiplier = Mathf.Clamp(0.72f + fallBonus * 0.28f + fallSpeedBonus * 0.22f + spinBonus * 0.28f + airTimeBonus * 0.28f, 0.72f, 1.65f);
            AwardStyleServer(RetrowaveStyleEvent.CleanLanding, multiplier);
        }

        private void ResetAerialStyleTracking()
        {
            _styleWasGrounded = true;
            _styleAirborneTime = 0f;
            _styleAirborneSpin = 0f;
            _stylePeakHeight = transform.position.y;
            _styleMinVerticalVelocity = 0f;
        }

        private void SimulateGrounded(RetrowavePlayerInputState input, float speedMultiplier, float gripMultiplier)
        {
            var surfaceForward = Vector3.ProjectOnPlane(transform.forward, _groundNormal);
            var surfaceRight = Vector3.ProjectOnPlane(transform.right, _groundNormal);

            if (surfaceForward.sqrMagnitude < 0.001f)
            {
                surfaceForward = Vector3.ProjectOnPlane(Vector3.forward, _groundNormal);
            }

            if (surfaceRight.sqrMagnitude < 0.001f)
            {
                surfaceRight = Vector3.ProjectOnPlane(Vector3.right, _groundNormal);
            }

            surfaceForward.Normalize();
            surfaceRight.Normalize();

            var planarVelocity = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, _groundNormal);
            var forwardSpeed = Vector3.Dot(planarVelocity, surfaceForward);
            var lateralSpeed = Vector3.Dot(planarVelocity, surfaceRight);
            var desiredSpeed = input.Throttle >= 0f
                ? input.Throttle * MaxDriveSpeed * speedMultiplier
                : input.Throttle * MaxReverseSpeed;
            var accel = input.Throttle >= 0f ? GroundDriveAcceleration : GroundReverseAcceleration;
            var forwardDelta = desiredSpeed - forwardSpeed;
            var maxStep = accel * Time.fixedDeltaTime;
            forwardDelta = Mathf.Clamp(forwardDelta, -maxStep, maxStep);
            var lateralCorrection = Mathf.Clamp(-lateralSpeed, -GroundVelocityTurnAssist, GroundVelocityTurnAssist);

            _rigidbody.AddForce(surfaceForward * forwardDelta, ForceMode.VelocityChange);
            _rigidbody.AddForce(surfaceRight * lateralCorrection, ForceMode.VelocityChange);
            _rigidbody.AddForce(-surfaceRight * lateralSpeed * GroundGrip * gripMultiplier, ForceMode.Acceleration);
            _rigidbody.AddForce(-planarVelocity * GroundDrag * Mathf.Lerp(1f, gripMultiplier, 0.45f), ForceMode.Acceleration);
            _rigidbody.AddForce(-_groundNormal * GroundStickForce, ForceMode.Acceleration);

            if (Mathf.Abs(input.Steer) > 0.45f && Mathf.Abs(lateralSpeed) > 3.8f && Mathf.Abs(forwardSpeed) > 8f)
            {
                AwardStyleServer(RetrowaveStyleEvent.Drift, Time.fixedDeltaTime);
            }

            if (Mathf.Abs(input.Steer) > 0.02f)
            {
                var directionScale = forwardSpeed >= -0.4f ? 1f : -0.75f;
                var steerScale = 0.25f + Mathf.Clamp01(Mathf.Abs(forwardSpeed) / MaxDriveSpeed);
                _rigidbody.AddTorque(_groundNormal * (input.Steer * GroundSteeringTorque * steerScale * directionScale), ForceMode.Acceleration);
            }

            var alignAxis = Vector3.Cross(transform.up, _groundNormal);
            _rigidbody.AddTorque(alignAxis * GroundAlignTorque, ForceMode.Acceleration);

            var localAngularVelocity = transform.InverseTransformDirection(_rigidbody.angularVelocity);
            _rigidbody.AddTorque(transform.right * (-localAngularVelocity.x * GroundAngularDamping), ForceMode.Acceleration);
            _rigidbody.AddTorque(transform.forward * (-localAngularVelocity.z * GroundAngularDamping), ForceMode.Acceleration);

            var groundYawVelocity = Vector3.Dot(_rigidbody.angularVelocity, _groundNormal);
            var yawDamping = Mathf.Abs(input.Steer) > 0.02f ? GroundYawActiveDamping : GroundYawReleaseDamping;
            _rigidbody.AddTorque(_groundNormal * (-groundYawVelocity * yawDamping), ForceMode.Acceleration);

            if (input.Brake)
            {
                _rigidbody.AddForce(-planarVelocity * 2.6f, ForceMode.Acceleration);
            }
        }

        private void SimulateAirborne(RetrowavePlayerInputState input)
        {
            _rigidbody.AddTorque(transform.right * (-input.Throttle * AirPitchTorque), ForceMode.Acceleration);
            _rigidbody.AddTorque(transform.up * (input.Steer * AirYawTorque), ForceMode.Acceleration);
            _rigidbody.AddTorque(transform.forward * (-input.Roll * AirRollTorque), ForceMode.Acceleration);

            var airThrust = transform.forward * (input.Throttle * AirForwardThrust);
            airThrust += transform.right * (input.Steer * AirStrafeThrust);
            _rigidbody.AddForce(airThrust, ForceMode.Acceleration);

            var localAngularVelocity = transform.InverseTransformDirection(_rigidbody.angularVelocity);
            _rigidbody.AddTorque(transform.right * (-localAngularVelocity.x * AirAngularDamping), ForceMode.Acceleration);
            _rigidbody.AddTorque(transform.forward * (-localAngularVelocity.z * AirAngularDamping), ForceMode.Acceleration);
            _rigidbody.AddTorque(transform.up * (-localAngularVelocity.y * AirYawStabilizeTorque), ForceMode.Acceleration);

            var levelAxis = Vector3.Cross(transform.up, Vector3.up);
            var controlIntent = Mathf.Abs(input.Throttle) + Mathf.Abs(input.Steer) + Mathf.Abs(input.Roll);
            var levelBlend = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(controlIntent));
            _rigidbody.AddTorque(levelAxis * (AirAutoLevelTorque * levelBlend), ForceMode.Acceleration);

            var sidewaysVelocity = Vector3.Project(_rigidbody.linearVelocity, transform.right);
            _rigidbody.AddForce(-sidewaysVelocity * AirLateralDamping, ForceMode.Acceleration);
            _rigidbody.AddForce(Vector3.down * AirGravityAssist, ForceMode.Acceleration);

            if (input.Brake)
            {
                _rigidbody.AddForce(-_rigidbody.linearVelocity * AirBrakeDamping, ForceMode.Acceleration);
            }
        }

        private void HandleJump(RetrowavePlayerInputState input, bool treatedAsGrounded)
        {
            if (!input.JumpPressed)
            {
                return;
            }

            if (_boostAmount.Value < JumpBoostCost)
            {
                return;
            }

            SpendBoost(JumpBoostCost);
            _overdrive?.RegisterJumpServer();

            if (treatedAsGrounded)
            {
                _rigidbody.AddForce(transform.up * JumpImpulse, ForceMode.VelocityChange);
                _coyoteTimer = 0f;
                _isGrounded = false;
                _groundProbeCount = 0;
                return;
            }

            _rigidbody.AddForce(transform.up * AirHoverBurst, ForceMode.VelocityChange);
        }

        private bool TryApplyGlide(RetrowavePlayerInputState input)
        {
            if (!input.JumpHeld || _glideRequiresRelease)
            {
                return false;
            }

            if (_boostAmount.Value <= BoostStartThreshold)
            {
                _glideRequiresRelease = true;
                return false;
            }

            var verticalVelocity = Vector3.Dot(_rigidbody.linearVelocity, Vector3.up);

            if (verticalVelocity > GlideActivationMaxUpwardSpeed)
            {
                return false;
            }

            var glideForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

            if (glideForward.sqrMagnitude > 0.001f)
            {
                _rigidbody.AddForce(glideForward.normalized * GlideForwardAcceleration, ForceMode.Acceleration);
            }

            var glideRight = Vector3.ProjectOnPlane(transform.right, Vector3.up);

            if (glideRight.sqrMagnitude > 0.001f && Mathf.Abs(input.Steer) > 0.02f)
            {
                _rigidbody.AddForce(glideRight.normalized * (input.Steer * GlideSteerAcceleration), ForceMode.Acceleration);
            }

            if (verticalVelocity < -GlideMaxFallSpeed)
            {
                _rigidbody.AddForce(Vector3.up * (-GlideMaxFallSpeed - verticalVelocity), ForceMode.VelocityChange);
            }

            if (verticalVelocity <= 0f)
            {
                _rigidbody.AddForce(Vector3.up * GlideVerticalAssist, ForceMode.Acceleration);
            }

            SpendBoost(GlideBoostDrain * Time.fixedDeltaTime);

            if (_boostAmount.Value <= BoostStartThreshold)
            {
                DepleteBoost();
                _glideRequiresRelease = true;
            }

            return true;
        }

        private void ApplyBoost(bool treatedAsGrounded)
        {
            var forceDirection = treatedAsGrounded
                ? Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized
                : transform.forward;

            if (forceDirection.sqrMagnitude < 0.001f)
            {
                forceDirection = transform.forward;
            }

            var forceMultiplier = (_overdrive != null ? _overdrive.BoostForceMultiplier : 1f)
                                  * RetrowaveUtilityRoleCatalog.GetMaxSpeedMultiplier(UtilityRole);
            _rigidbody.AddForce(forceDirection * (BoostForce * forceMultiplier), ForceMode.Acceleration);
        }

        private void SpendBoost(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            var previousBoost = _boostAmount.Value;
            _boostAmount.Value = Mathf.Max(0f, _boostAmount.Value - amount);

            if (previousBoost > 0f && _boostAmount.Value <= 0.01f)
            {
                _boostAmount.Value = 0f;
                _boostRechargeDelayTimer = BoostRechargeDelaySeconds;
            }
        }

        private void DepleteBoost()
        {
            _boostAmount.Value = 0f;
            _boostRechargeDelayTimer = BoostRechargeDelaySeconds;
        }

        public float GetBallHitPowerMultiplier(Vector3 ballPosition)
        {
            var multiplier = RetrowaveUtilityRoleCatalog.GetBallHitMultiplier(UtilityRole);

            if (UtilityRole == RetrowaveUtilityRole.Defender)
            {
                var defendingOwnHalf = Team == RetrowaveTeam.Blue ? ballPosition.z < 0f : ballPosition.z > 0f;
                multiplier *= defendingOwnHalf ? 1.08f : 0.98f;
            }

            if (_overdrive != null)
            {
                multiplier *= Mathf.Lerp(1f, 1.055f, _overdrive.HeatNormalized);
            }

            if (_styleMeter != null)
            {
                multiplier *= Mathf.Lerp(1f, 1.045f, _styleMeter.StyleNormalized);
            }

            return multiplier;
        }

        public float StatusEffectDurationMultiplier => RetrowaveUtilityRoleCatalog.GetStatusDurationMultiplier(UtilityRole);

        public void AwardStyleServer(RetrowaveStyleEvent styleEvent, float multiplier = 1f)
        {
            if (!IsServer)
            {
                return;
            }

            _styleMeter ??= GetComponent<VehicleStyleMeter>();
            _styleMeter?.AwardServer(styleEvent, multiplier * RetrowaveUtilityRoleCatalog.GetStyleGainMultiplier(UtilityRole));
        }

        public void ApplyObjectiveOverchargeServer(float durationSeconds, float coolingAmount)
        {
            if (!IsServer)
            {
                return;
            }

            _overdrive ??= GetComponent<VehicleOverdriveSystem>();
            _overdrive?.ApplyOverchargeServer(durationSeconds, coolingAmount);
        }

        private void SetEngineAudioStateServer(float throttle, bool isBoosting)
        {
            if (!IsServer)
            {
                return;
            }

            _engineAudioThrottle.Value = Mathf.Clamp(throttle, -1f, 1f);
            _engineAudioBoosting.Value = isBoosting;
        }

        private void PublishServerMotionState()
        {
            if (!IsServer || _rigidbody == null)
            {
                return;
            }

            SetReplicatedSpeedServer(_rigidbody.linearVelocity.magnitude);
        }

        private void SetReplicatedSpeedServer(float speed)
        {
            if (!IsServer)
            {
                return;
            }

            var clampedSpeed = Mathf.Clamp(speed, 0f, MaxHudSpeed);

            if (Mathf.Abs(_replicatedSpeed.Value - clampedSpeed) > 0.04f || clampedSpeed < 0.04f)
            {
                _replicatedSpeed.Value = clampedSpeed;
            }
        }

        private void HandleTeamChanged(int _, int nextValue)
        {
            ApplyTeamVisuals((RetrowaveTeam)nextValue);
            RefreshPresentationState();
        }

        private void HandleLobbyRoleChanged(int _, int __)
        {
            RefreshPresentationState();
        }

        private void HandleSelectedRoleChanged(bool _, bool __)
        {
            RefreshPresentationState();
        }

        private void HandleUtilityRoleChanged(int _, int __)
        {
            RefreshNameTagRoleIcon();
        }

        private void HandlePodiumPresentationChanged(bool _, bool __)
        {
            RefreshPresentationState();
        }

        private void RefreshPresentationState()
        {
            var shouldShowVehicle = IsArenaParticipant && !_podiumPresentationHidden.Value;

            foreach (var renderer in _vehicleRenderers)
            {
                renderer.enabled = shouldShowVehicle;
            }

            foreach (var collider in _colliders)
            {
                collider.enabled = shouldShowVehicle;
            }

            if (_boostLight != null)
            {
                _boostLight.enabled = shouldShowVehicle;
            }

            if (_nameTagCanvas != null)
            {
                _nameTagCanvas.enabled = shouldShowVehicle;
            }

            if (!IsOwner)
            {
                return;
            }

            if (shouldShowVehicle)
            {
                var podiumActive = RetrowaveMatchManager.Instance != null && RetrowaveMatchManager.Instance.IsPodium;

                if (!podiumActive && (LocalOwner != this || !RetrowaveCameraRig.IsFollowing(this)))
                {
                    LocalOwner = this;
                    RetrowaveCameraRig.AttachTo(this);
                }

                return;
            }

            if (LocalOwner == this)
            {
                LocalOwner = null;
                RetrowaveCameraRig.ShowOverview();
            }
        }

        private void ApplyTeamVisuals(RetrowaveTeam team)
        {
            if (_appliedBodyMaterial != null)
            {
                Destroy(_appliedBodyMaterial);
                _appliedBodyMaterial = null;
            }

            for (var rendererIndex = 0; rendererIndex < _vehicleRenderers.Length; rendererIndex++)
            {
                var renderer = _vehicleRenderers[rendererIndex];
                var sharedMaterials = renderer.sharedMaterials;
                var changed = false;

                for (var materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    var material = sharedMaterials[materialIndex];

                    if (material == null || !material.name.StartsWith("Body", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _appliedBodyMaterial ??= CreateTeamBodyMaterial(material, team);
                    sharedMaterials[materialIndex] = _appliedBodyMaterial;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = sharedMaterials;
                }
            }

            if (_boostLight != null)
            {
                _boostLight.color = RetrowaveStyle.GetTeamGlow(team);
            }

            if (_nameTagText != null)
            {
                _nameTagText.color = Color.Lerp(RetrowaveStyle.GetTeamGlow(team), Color.white, 0.2f);
            }

            RefreshNameTagRoleIcon();
        }

        private void UpdateBoostVisuals()
        {
            if (_boostLight == null)
            {
                return;
            }

            var heat = HeatNormalized;
            var teamGlow = RetrowaveStyle.GetTeamGlow(Team);
            var heatColor = IsStunned
                ? Color.Lerp(new Color(1f, 0.08f, 0.16f, 1f), Color.white, Mathf.PingPong(Time.time * 6f, 0.3f))
                : IsSlowed
                    ? new Color(0.34f, 0.78f, 1f, 1f)
                    : IsOvercharged
                        ? new Color(1f, 0.86f, 0.24f, 1f)
                        : IsOverheated
                ? Color.Lerp(new Color(1f, 0.12f, 0.05f, 1f), Color.white, Mathf.PingPong(Time.time * 5f, 0.28f))
                : Color.Lerp(teamGlow, new Color(1f, 0.42f, 0.08f, 1f), heat);
            _boostLight.color = heatColor;

            var targetIntensity = _boostFx.Value ? 11f : Mathf.Lerp(0f, 3.2f, heat);
            targetIntensity = Mathf.Max(targetIntensity, IsStunned ? 7.5f : IsSlowed ? 4.8f : IsOvercharged ? 6.5f : 0f);
            _boostLight.intensity = Mathf.Lerp(_boostLight.intensity, targetIntensity, Time.deltaTime * 11f);
            _boostLight.range = _boostFx.Value ? 11f : Mathf.Lerp(5f, IsOvercharged ? 10.5f : 7.5f, heat);
        }

        private void LateUpdate()
        {
            RefreshNameTag();
        }

        private void SubmitDisplayName()
        {
            var displayName = RetrowaveGameBootstrap.Instance != null
                ? RetrowaveGameBootstrap.Instance.PreferredDisplayName
                : "Player";

            if (IsServer)
            {
                RetrowaveMatchManager.Instance?.HandlePlayerDisplayName(OwnerClientId, displayName);
                return;
            }

            SubmitDisplayNameServerRpc(displayName);
        }

        private void EnsureNameTag()
        {
            var canvasObject = new GameObject("Name Tag", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            canvasObject.transform.localRotation = Quaternion.identity;

            _nameTagCanvas = canvasObject.GetComponent<Canvas>();
            _nameTagCanvas.renderMode = RenderMode.WorldSpace;
            _nameTagCanvas.worldCamera = Camera.main;
            _nameTagCanvas.sortingOrder = 40;

            var canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            canvasScaler.dynamicPixelsPerUnit = 18f;

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(284f, 72f);
            canvasRect.localScale = Vector3.one * 0.01f;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(42f, 18f);
            textRect.offsetMax = new Vector2(-42f, 0f);

            _nameTagText = textObject.GetComponent<TextMeshProUGUI>();
            _nameTagText.font = TMP_Settings.defaultFontAsset;
            _nameTagText.fontSize = 28f;
            _nameTagText.fontStyle = FontStyles.Bold;
            _nameTagText.alignment = TextAlignmentOptions.Center;
            _nameTagText.textWrappingMode = TextWrappingModes.NoWrap;
            _nameTagText.text = string.Empty;

            _nameTagRoleIconRoot = new GameObject("Utility Role Icon", typeof(RectTransform), typeof(Image));
            _nameTagRoleIconRoot.transform.SetParent(canvasObject.transform, false);

            var roleIconRect = _nameTagRoleIconRoot.GetComponent<RectTransform>();
            roleIconRect.anchorMin = new Vector2(0f, 0.5f);
            roleIconRect.anchorMax = new Vector2(0f, 0.5f);
            roleIconRect.pivot = new Vector2(0f, 0.5f);
            roleIconRect.anchoredPosition = new Vector2(4f, 9f);
            roleIconRect.sizeDelta = new Vector2(24f, 24f);

            _nameTagRoleIconImage = _nameTagRoleIconRoot.GetComponent<Image>();

            var statusTextObject = new GameObject("Status Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            statusTextObject.transform.SetParent(canvasObject.transform, false);

            var statusTextRect = statusTextObject.GetComponent<RectTransform>();
            statusTextRect.anchorMin = new Vector2(0.5f, 0f);
            statusTextRect.anchorMax = new Vector2(0.5f, 0f);
            statusTextRect.pivot = new Vector2(0.5f, 0f);
            statusTextRect.anchoredPosition = new Vector2(0f, 2f);
            statusTextRect.sizeDelta = new Vector2(240f, 22f);

            _nameTagStatusText = statusTextObject.GetComponent<TextMeshProUGUI>();
            _nameTagStatusText.font = TMP_Settings.defaultFontAsset;
            _nameTagStatusText.fontSize = 16f;
            _nameTagStatusText.fontStyle = FontStyles.Bold;
            _nameTagStatusText.alignment = TextAlignmentOptions.Center;
            _nameTagStatusText.textWrappingMode = TextWrappingModes.NoWrap;
            _nameTagStatusText.color = Color.white;
            _nameTagStatusText.gameObject.SetActive(false);

            _nameTagPowerUpIconRoot = new GameObject("PowerUp Icon", typeof(RectTransform), typeof(Image));
            _nameTagPowerUpIconRoot.transform.SetParent(canvasObject.transform, false);

            var iconRect = _nameTagPowerUpIconRoot.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(1f, 0.5f);
            iconRect.anchorMax = new Vector2(1f, 0.5f);
            iconRect.pivot = new Vector2(1f, 0.5f);
            iconRect.anchoredPosition = new Vector2(-4f, 0f);
            iconRect.sizeDelta = new Vector2(24f, 24f);

            _nameTagPowerUpIconImage = _nameTagPowerUpIconRoot.GetComponent<Image>();
            _nameTagPowerUpIconImage.color = new Color(0.1f, 1f, 0.32f, 0.9f);

            _nameTagPowerUpIconRoot.SetActive(false);
            RefreshNameTagRoleIcon();
        }

        private void RefreshNameTag()
        {
            if (_nameTagCanvas == null || _nameTagText == null)
            {
                return;
            }

            if (RetrowaveMatchManager.Instance != null
                && RetrowaveMatchManager.Instance.TryGetLobbyEntry(OwnerClientId, out var entry))
            {
                var displayName = entry.DisplayName.ToString();

                if (!string.Equals(_appliedDisplayName, displayName, StringComparison.Ordinal))
                {
                    _appliedDisplayName = displayName;
                    _nameTagText.text = displayName;
                }
            }

            RefreshNameTagRoleIcon();
            RefreshNameTagPowerUpIcon();
            RefreshNameTagStatus();

            var camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            _nameTagCanvas.worldCamera = camera;
            _nameTagCanvas.transform.forward = camera.transform.forward;
        }

        private void RefreshNameTagPowerUpIcon()
        {
            if (_nameTagPowerUpIconRoot == null)
            {
                return;
            }

            _rarePowerUpInventory ??= GetComponent<RarePowerUpInventory>();
            var heldType = _rarePowerUpInventory != null
                ? _rarePowerUpInventory.HeldType
                : RetrowaveRarePowerUpType.None;

            if (heldType == RetrowaveRarePowerUpType.None)
            {
                _nameTagPowerUpIconRoot.SetActive(false);
                return;
            }

            _nameTagPowerUpIconRoot.SetActive(true);

            if (_nameTagPowerUpIconImage != null)
            {
                _nameTagPowerUpIconImage.color = GetRarePowerUpColor(heldType);
            }
        }

        private void RefreshNameTagRoleIcon()
        {
            if (_nameTagRoleIconRoot == null)
            {
                return;
            }

            var shouldShow = IsArenaParticipant && HasSelectedUtilityRole;
            _nameTagRoleIconRoot.SetActive(shouldShow);

            if (!shouldShow)
            {
                return;
            }

            var roleColor = RetrowaveUtilityRoleCatalog.GetColor(UtilityRole);
            var teamColor = RetrowaveStyle.GetTeamGlow(Team);
            var blendedColor = Color.Lerp(roleColor, teamColor, 0.28f);

            if (_nameTagRoleIconImage != null)
            {
                _nameTagRoleIconImage.color = blendedColor;
            }
        }

        private void RefreshNameTagStatus()
        {
            if (_nameTagStatusText == null)
            {
                return;
            }

            if (TryResolveStatusLabel(out var label, out var color))
            {
                _nameTagStatusText.gameObject.SetActive(true);
                _nameTagStatusText.text = label;
                _nameTagStatusText.color = Color.Lerp(color, Color.white, 0.18f);
                return;
            }

            _nameTagStatusText.gameObject.SetActive(false);
        }

        private bool TryResolveStatusLabel(out string label, out Color color)
        {
            if (IsStunned)
            {
                label = "STUNNED";
                color = new Color(1f, 0.08f, 0.18f, 1f);
                return true;
            }

            if (IsSlowed)
            {
                label = "SLOWED";
                color = new Color(0.34f, 0.78f, 1f, 1f);
                return true;
            }

            if (IsOvercharged)
            {
                label = "OVERDRIVE";
                color = new Color(1f, 0.86f, 0.24f, 1f);
                return true;
            }

            if (IsOverheated)
            {
                label = "OVERHEAT";
                color = new Color(1f, 0.22f, 0.06f, 1f);
                return true;
            }

            if (HeatNormalized > 0.72f)
            {
                label = "HOT";
                color = new Color(1f, 0.48f, 0.08f, 1f);
                return true;
            }

            label = string.Empty;
            color = Color.white;
            return false;
        }

        public static Color GetRarePowerUpColor(RetrowaveRarePowerUpType type)
        {
            return type switch
            {
                RetrowaveRarePowerUpType.NeonSnareTrail => new Color(0.08f, 1f, 0.28f, 0.92f),
                RetrowaveRarePowerUpType.GravityBomb => new Color(1f, 0.38f, 0.1f, 0.92f),
                RetrowaveRarePowerUpType.ChronoDome => new Color(0.32f, 0.72f, 1f, 0.92f),
                _ => new Color(0.72f, 0.92f, 1f, 0.92f),
            };
        }

        private static Material CreateTeamBodyMaterial(Material source, RetrowaveTeam team)
        {
            var bodyMaterial = source != null ? new Material(source) : RetrowaveStyle.CreateLitMaterial(Color.white, Color.black);

            if (bodyMaterial.HasProperty("_BaseColor"))
            {
                bodyMaterial.SetColor("_BaseColor", RetrowaveStyle.GetTeamBase(team));
            }

            if (bodyMaterial.HasProperty("_Color"))
            {
                bodyMaterial.SetColor("_Color", RetrowaveStyle.GetTeamBase(team));
            }

            if (bodyMaterial.HasProperty("_EmissionColor"))
            {
                bodyMaterial.EnableKeyword("_EMISSION");
                bodyMaterial.SetColor("_EmissionColor", RetrowaveStyle.GetTeamGlow(team) * 1.6f);
            }

            return bodyMaterial;
        }
    }
}
