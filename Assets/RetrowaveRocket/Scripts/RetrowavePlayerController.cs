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
        private const float RideHeight = 0.68f;
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
        private Canvas _nameTagCanvas;
        private TextMeshProUGUI _nameTagText;
        private GameObject _nameTagPowerUpIconRoot;
        private Image _nameTagPowerUpIconImage;
        private TextMeshProUGUI _nameTagPowerUpIconText;
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

        public static RetrowavePlayerController LocalOwner { get; private set; }
        public static RetrowavePlayerController LocalPlayer { get; private set; }

        public RetrowaveTeam Team => (RetrowaveTeam)_teamValue.Value;
        public RetrowaveLobbyRole LobbyRole => (RetrowaveLobbyRole)_lobbyRoleValue.Value;
        public bool HasSelectedRole => _hasSelectedRole.Value;
        public bool IsArenaParticipant => HasSelectedRole && LobbyRole != RetrowaveLobbyRole.Spectator;
        public float BoostNormalized => Mathf.Clamp01(_boostAmount.Value / RetrowaveArenaConfig.MaxBoost);
        public float BoostAmount => Mathf.Clamp(_boostAmount.Value, 0f, RetrowaveArenaConfig.MaxBoost);
        public bool HasSpeedBoost => _speedBoostTimer.Value > 0.05f;
        public float CurrentSpeed => _rigidbody != null ? _rigidbody.linearVelocity.magnitude : 0f;
        public float MaxHudSpeed => MaxBoostSpeed * RetrowaveArenaConfig.SpeedBurstMultiplier;
        public float SpeedNormalized => Mathf.Clamp01(CurrentSpeed / Mathf.Max(0.01f, MaxHudSpeed));
        public bool IsGroundedForHud => _isGrounded;
        public Rigidbody Body => _rigidbody;
        public ulong ControllingClientId => OwnerClientId;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _vehicleRenderers = GetComponentsInChildren<MeshRenderer>(true);
            _boostLight = GetComponentInChildren<Light>(true);
            _statusEffects = GetComponent<VehicleStatusEffects>();
            _rarePowerUpInventory = GetComponent<RarePowerUpInventory>();
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
                    SubmitInputServerRpc(outbound);
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
                return;
            }

            SimulateMovement(_latestInput);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _teamValue.OnValueChanged += HandleTeamChanged;
            _lobbyRoleValue.OnValueChanged += HandleLobbyRoleChanged;
            _hasSelectedRole.OnValueChanged += HandleSelectedRoleChanged;
            _podiumPresentationHidden.OnValueChanged += HandlePodiumPresentationChanged;

            if (!IsServer)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }

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

            base.OnNetworkDespawn();
        }

        [ServerRpc]
        private void SubmitInputServerRpc(RetrowavePlayerInputState input)
        {
            _latestInput = input;
        }

        [ServerRpc]
        private void SubmitRoleSelectionServerRpc(int roleValue)
        {
            var clampedRole = (RetrowaveLobbyRole)Mathf.Clamp(roleValue, (int)RetrowaveLobbyRole.Spectator, (int)RetrowaveLobbyRole.Pink);
            RetrowaveMatchManager.Instance?.HandlePlayerRoleSelection(OwnerClientId, clampedRole);
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

            _lobbyRoleValue.Value = team == RetrowaveTeam.Blue ? (int)RetrowaveLobbyRole.Blue : (int)RetrowaveLobbyRole.Pink;
            _hasSelectedRole.Value = true;
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
            _statusEffects?.ClearServer();
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
            _statusEffects?.ClearServer();
        }

        public void SetSpectatorStateServer(bool hasSelectedRole)
        {
            if (!IsServer)
            {
                return;
            }

            _lobbyRoleValue.Value = (int)RetrowaveLobbyRole.Spectator;
            _hasSelectedRole.Value = hasSelectedRole;
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
            _statusEffects?.ClearServer();
        }

        public void RequestRoleSelection(RetrowaveLobbyRole role)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            SubmitRoleSelectionServerRpc((int)role);
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
            _statusEffects?.ClearServer();
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
                _boostAmount.Value = Mathf.Clamp(
                    _boostAmount.Value + RetrowaveArenaConfig.PassiveBoostRegen * Time.fixedDeltaTime,
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

            if (treatedAsGrounded)
            {
                SimulateGrounded(controlInput, speedMultiplier);
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

            var isBoosting = controlInput.Boost && !_boostRequiresRelease && _boostAmount.Value > BoostStartThreshold;

            if (isBoosting)
            {
                ApplyBoost(treatedAsGrounded);
                SpendBoost(BoostDrainRate * Time.fixedDeltaTime);

                if (_boostAmount.Value <= BoostStartThreshold)
                {
                    DepleteBoost();
                    isBoosting = false;
                    _boostRequiresRelease = true;
                }
            }

            _boostFx.Value = isBoosting || isGliding;

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

        private void SimulateGrounded(RetrowavePlayerInputState input, float speedMultiplier)
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
            _rigidbody.AddForce(-surfaceRight * lateralSpeed * GroundGrip, ForceMode.Acceleration);
            _rigidbody.AddForce(-planarVelocity * GroundDrag, ForceMode.Acceleration);
            _rigidbody.AddForce(-_groundNormal * GroundStickForce, ForceMode.Acceleration);

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

            _rigidbody.AddForce(forceDirection * BoostForce, ForceMode.Acceleration);
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
        }

        private void UpdateBoostVisuals()
        {
            if (_boostLight == null)
            {
                return;
            }

            var targetIntensity = _boostFx.Value ? 11f : 0f;
            _boostLight.intensity = Mathf.Lerp(_boostLight.intensity, targetIntensity, Time.deltaTime * 11f);
            _boostLight.range = _boostFx.Value ? 11f : 5f;
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
            canvasRect.sizeDelta = new Vector2(268f, 48f);
            canvasRect.localScale = Vector3.one * 0.01f;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = new Vector2(-42f, 0f);

            _nameTagText = textObject.GetComponent<TextMeshProUGUI>();
            _nameTagText.font = TMP_Settings.defaultFontAsset;
            _nameTagText.fontSize = 28f;
            _nameTagText.fontStyle = FontStyles.Bold;
            _nameTagText.alignment = TextAlignmentOptions.Center;
            _nameTagText.textWrappingMode = TextWrappingModes.NoWrap;
            _nameTagText.text = string.Empty;

            _nameTagPowerUpIconRoot = new GameObject("PowerUp Icon", typeof(RectTransform), typeof(Image));
            _nameTagPowerUpIconRoot.transform.SetParent(canvasObject.transform, false);

            var iconRect = _nameTagPowerUpIconRoot.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(1f, 0.5f);
            iconRect.anchorMax = new Vector2(1f, 0.5f);
            iconRect.pivot = new Vector2(1f, 0.5f);
            iconRect.anchoredPosition = new Vector2(-4f, 0f);
            iconRect.sizeDelta = new Vector2(34f, 34f);

            _nameTagPowerUpIconImage = _nameTagPowerUpIconRoot.GetComponent<Image>();
            _nameTagPowerUpIconImage.color = new Color(0.1f, 1f, 0.32f, 0.9f);

            var iconTextObject = new GameObject("Icon Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            iconTextObject.transform.SetParent(_nameTagPowerUpIconRoot.transform, false);

            var iconTextRect = iconTextObject.GetComponent<RectTransform>();
            iconTextRect.anchorMin = Vector2.zero;
            iconTextRect.anchorMax = Vector2.one;
            iconTextRect.offsetMin = Vector2.zero;
            iconTextRect.offsetMax = Vector2.zero;

            _nameTagPowerUpIconText = iconTextObject.GetComponent<TextMeshProUGUI>();
            _nameTagPowerUpIconText.font = TMP_Settings.defaultFontAsset;
            _nameTagPowerUpIconText.fontSize = 23f;
            _nameTagPowerUpIconText.fontStyle = FontStyles.Bold;
            _nameTagPowerUpIconText.alignment = TextAlignmentOptions.Center;
            _nameTagPowerUpIconText.textWrappingMode = TextWrappingModes.NoWrap;
            _nameTagPowerUpIconText.text = string.Empty;
            _nameTagPowerUpIconText.color = Color.white;
            _nameTagPowerUpIconRoot.SetActive(false);
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

            RefreshNameTagPowerUpIcon();

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

            if (_nameTagPowerUpIconText != null)
            {
                _nameTagPowerUpIconText.text = GetRarePowerUpIconLabel(heldType);
            }
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

        public static string GetRarePowerUpIconLabel(RetrowaveRarePowerUpType type)
        {
            return type switch
            {
                RetrowaveRarePowerUpType.NeonSnareTrail => "N",
                RetrowaveRarePowerUpType.GravityBomb => "G",
                RetrowaveRarePowerUpType.ChronoDome => "C",
                _ => string.Empty,
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
