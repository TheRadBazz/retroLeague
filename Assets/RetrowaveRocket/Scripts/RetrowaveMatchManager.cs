using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public enum RetrowaveMatchPhase
    {
        Warmup = 0,
        Live = 1,
    }

    public struct RetrowaveLobbyEntry : INetworkSerializable, IEquatable<RetrowaveLobbyEntry>
    {
        public ulong ClientId;
        public FixedString32Bytes DisplayName;
        public int RoleValue;
        public int ActiveRoleValue;
        public bool HasSelectedRole;
        public bool QueuedForNextRound;
        public bool IsHost;
        public int Goals;
        public int Assists;
        public int PingMs;

        public RetrowaveLobbyRole Role => (RetrowaveLobbyRole)RoleValue;
        public RetrowaveLobbyRole ActiveRole => (RetrowaveLobbyRole)ActiveRoleValue;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref RoleValue);
            serializer.SerializeValue(ref ActiveRoleValue);
            serializer.SerializeValue(ref HasSelectedRole);
            serializer.SerializeValue(ref QueuedForNextRound);
            serializer.SerializeValue(ref IsHost);
            serializer.SerializeValue(ref Goals);
            serializer.SerializeValue(ref Assists);
            serializer.SerializeValue(ref PingMs);
        }

        public bool Equals(RetrowaveLobbyEntry other)
        {
            return ClientId == other.ClientId
                   && DisplayName.Equals(other.DisplayName)
                   && RoleValue == other.RoleValue
                   && ActiveRoleValue == other.ActiveRoleValue
                   && HasSelectedRole == other.HasSelectedRole
                   && QueuedForNextRound == other.QueuedForNextRound
                   && IsHost == other.IsHost
                   && Goals == other.Goals
                   && Assists == other.Assists
                   && PingMs == other.PingMs;
        }
    }

    public sealed class RetrowaveMatchManager : NetworkBehaviour
    {
        private const float GoalCelebrationDuration = 2.35f;
        private const float GoalExplosionPlayerImpulse = 24f;
        private const float GoalExplosionBallImpulse = 19f;
        private const float GoalExplosionLift = 0.28f;
        private const float GoalExplosionDistance = 132f;

        private readonly NetworkVariable<int> _blueScore = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _orangeScore = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _phaseValue = new(
            (int)RetrowaveMatchPhase.Warmup,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _goalCelebrationActive = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _roundDurationSeconds = new(
            RetrowaveMatchSettings.Default.RoundDurationSeconds,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _maxPlayers = new(
            RetrowaveMatchSettings.Default.MaxPlayers,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _arenaSizePresetValue = new(
            (int)RetrowaveMatchSettings.Default.ArenaSizePreset,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _roundNumber = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _roundTimeRemaining = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkList<RetrowaveLobbyEntry> _lobbyEntries = new();
        private bool _worldSpawned;
        private bool _resetQueued;
        private float _pingRefreshTimer;
        private float _goalCelebrationTimer;
        private ulong _lastTouchClientId = ulong.MaxValue;
        private RetrowaveTeam _lastTouchTeam;
        private double _lastTouchTime;
        private ulong _previousTouchClientId = ulong.MaxValue;
        private RetrowaveTeam _previousTouchTeam;
        private double _previousTouchTime;

        public static RetrowaveMatchManager Instance { get; private set; }

        public int BlueScore => _blueScore.Value;
        public int OrangeScore => _orangeScore.Value;
        public RetrowaveMatchPhase Phase => (RetrowaveMatchPhase)_phaseValue.Value;
        public bool IsWarmup => Phase == RetrowaveMatchPhase.Warmup;
        public bool IsLiveMatch => Phase == RetrowaveMatchPhase.Live;
        public bool IsGoalCelebrationActive => _goalCelebrationActive.Value;
        public NetworkList<RetrowaveLobbyEntry> LobbyEntries => _lobbyEntries;
        public int RoundDurationSeconds => _roundDurationSeconds.Value;
        public int MaxPlayers => _maxPlayers.Value;
        public RetrowaveArenaSizePreset ArenaSizePreset => (RetrowaveArenaSizePreset)_arenaSizePresetValue.Value;
        public int CurrentRoundNumber => _roundNumber.Value;
        public float RoundTimeRemaining => _roundTimeRemaining.Value;
        public bool CanStartMatch => GetDesiredTeamCount(RetrowaveTeam.Blue) > 0 && GetDesiredTeamCount(RetrowaveTeam.Orange) > 0;

        public override void OnDestroy()
        {
            _lobbyEntries?.Dispose();
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            _pingRefreshTimer -= Time.deltaTime;

            if (_pingRefreshTimer <= 0f)
            {
                _pingRefreshTimer = 0.5f;
                RefreshPingValues();
            }

            if (_goalCelebrationActive.Value)
            {
                _goalCelebrationTimer -= Time.unscaledDeltaTime;

                if (_goalCelebrationTimer <= 0f)
                {
                    FinishGoalCelebration();
                }
            }

            if (IsLiveMatch && !_goalCelebrationActive.Value)
            {
                _roundTimeRemaining.Value = Mathf.Max(0f, _roundTimeRemaining.Value - Time.deltaTime);

                if (_roundTimeRemaining.Value <= 0f)
                {
                    AdvanceToNextRound();
                }
            }
        }

        private void FixedUpdate()
        {
            if (!IsServer || !_resetQueued)
            {
                return;
            }

            _resetQueued = false;
            ResetRound();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            _roundDurationSeconds.OnValueChanged += HandleArenaSettingsChanged;
            _maxPlayers.OnValueChanged += HandleArenaSettingsChanged;
            _arenaSizePresetValue.OnValueChanged += HandleArenaSettingsChanged;

            ApplyArenaSettingsLocally();

            if (!IsServer)
            {
                return;
            }

            var hostSettings = RetrowaveGameBootstrap.Instance != null
                ? RetrowaveGameBootstrap.Instance.CurrentMatchSettings
                : RetrowaveMatchSettings.Default;
            ApplyHostSettings(hostSettings);

            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

            SpawnWorldActors();
            EnsureLobbyEntry(NetworkManager.ServerClientId);

            foreach (var clientPair in NetworkManager.Singleton.ConnectedClients)
            {
                EnsureLobbyEntry(clientPair.Key);
            }

            RecalculateSpawnSlots();
        }

        public override void OnNetworkDespawn()
        {
            _roundDurationSeconds.OnValueChanged -= HandleArenaSettingsChanged;
            _maxPlayers.OnValueChanged -= HandleArenaSettingsChanged;
            _arenaSizePresetValue.OnValueChanged -= HandleArenaSettingsChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        private void HandleArenaSettingsChanged(int _, int __)
        {
            ApplyArenaSettingsLocally();
        }

        public void HandleGoal(RetrowaveTeam defendedGoal)
        {
            if (!IsServer || _goalCelebrationActive.Value)
            {
                return;
            }

            if (IsWarmup)
            {
                _resetQueued = true;
                ClearTouchHistory();
                return;
            }

            var scoringTeam = defendedGoal == RetrowaveTeam.Blue ? RetrowaveTeam.Orange : RetrowaveTeam.Blue;

            if (scoringTeam == RetrowaveTeam.Blue)
            {
                _blueScore.Value++;
            }
            else
            {
                _orangeScore.Value++;
            }

            var scorerClientId = ResolveScorerClientId(scoringTeam);
            var scorerName = GetScorerAnnouncementName(scorerClientId, scoringTeam);
            AwardGoalStats(scoringTeam, scorerClientId);
            StartGoalCelebration(defendedGoal, scoringTeam, scorerName);
        }

        public void RegisterBallTouch(RetrowavePlayerController player)
        {
            if (!IsServer || player == null)
            {
                return;
            }

            var now = Time.timeAsDouble;
            var clientId = player.ControllingClientId;

            if (_lastTouchClientId == clientId && now - _lastTouchTime < 0.15d)
            {
                return;
            }

            _previousTouchClientId = _lastTouchClientId;
            _previousTouchTeam = _lastTouchTeam;
            _previousTouchTime = _lastTouchTime;

            _lastTouchClientId = clientId;
            _lastTouchTeam = player.Team;
            _lastTouchTime = now;
        }

        public void RequestRoleSelection(RetrowaveLobbyRole role)
        {
            if (!IsClient)
            {
                return;
            }

            SubmitRoleSelectionServerRpc((int)role);
        }

        public void RequestStartMatch()
        {
            if (!IsClient)
            {
                return;
            }

            RequestStartMatchServerRpc();
        }

        public void HandlePlayerObjectSpawned(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            EnsureLobbyEntry(clientId);
            RecalculateSpawnSlots();
        }

        public void HandlePlayerDisplayName(ulong clientId, string displayName)
        {
            if (!IsServer)
            {
                return;
            }

            EnsureLobbyEntry(clientId);

            var index = GetLobbyEntryIndex(clientId);

            if (index < 0)
            {
                return;
            }

            var entry = _lobbyEntries[index];
            entry.DisplayName = new FixedString32Bytes(RetrowaveGameBootstrap.NormalizeDisplayName(displayName));
            _lobbyEntries[index] = entry;
        }

        public void HandlePlayerRoleSelection(ulong clientId, RetrowaveLobbyRole role)
        {
            if (!IsServer)
            {
                return;
            }

            EnsureLobbyEntry(clientId);

            var index = GetLobbyEntryIndex(clientId);

            if (index < 0)
            {
                return;
            }

            var entry = _lobbyEntries[index];
            var requestedRole = NormalizeRole(role);

            if (requestedRole != RetrowaveLobbyRole.Spectator && !CanAssignDesiredTeam(clientId, entry, requestedRole))
            {
                return;
            }

            entry.RoleValue = (int)requestedRole;
            entry.HasSelectedRole = true;

            if (requestedRole == RetrowaveLobbyRole.Spectator)
            {
                entry.ActiveRoleValue = (int)RetrowaveLobbyRole.Spectator;
                entry.QueuedForNextRound = false;
            }
            else if (IsLiveMatch)
            {
                entry.QueuedForNextRound = entry.ActiveRole != requestedRole;

                if (!IsTeamRole(entry.ActiveRole))
                {
                    entry.ActiveRoleValue = (int)RetrowaveLobbyRole.Spectator;
                }
            }
            else
            {
                entry.ActiveRoleValue = (int)requestedRole;
                entry.QueuedForNextRound = false;
            }

            _lobbyEntries[index] = entry;
            RecalculateSpawnSlots();
        }

        public bool TryGetLobbyEntry(ulong clientId, out RetrowaveLobbyEntry entry)
        {
            var index = GetLobbyEntryIndex(clientId);

            if (index >= 0)
            {
                entry = _lobbyEntries[index];
                return true;
            }

            entry = default;
            return false;
        }

        public int GetDesiredTeamCount(RetrowaveTeam team)
        {
            return CountTeams(team, useActiveRole: false);
        }

        public int GetActiveTeamCount(RetrowaveTeam team)
        {
            return CountTeams(team, useActiveRole: true);
        }

        private int CountTeams(RetrowaveTeam team, bool useActiveRole)
        {
            var count = 0;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                if (TryGetAssignedTeam(_lobbyEntries[i], useActiveRole, out var assignedTeam) && assignedTeam == team)
                {
                    count++;
                }
            }

            return count;
        }

        private bool CanAssignDesiredTeam(ulong clientId, RetrowaveLobbyEntry currentEntry, RetrowaveLobbyRole requestedRole)
        {
            var requestedCount = 0;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];

                if (entry.ClientId == clientId)
                {
                    entry = currentEntry;
                    entry.RoleValue = (int)requestedRole;
                    entry.HasSelectedRole = true;
                }

                if (TryGetAssignedTeam(entry, useActiveRole: false, out _))
                {
                    requestedCount++;
                }
            }

            return requestedCount <= MaxPlayers;
        }

        private static RetrowaveLobbyRole NormalizeRole(RetrowaveLobbyRole role)
        {
            return (RetrowaveLobbyRole)Mathf.Clamp((int)role, (int)RetrowaveLobbyRole.Spectator, (int)RetrowaveLobbyRole.Orange);
        }

        private static bool IsTeamRole(RetrowaveLobbyRole role)
        {
            return role == RetrowaveLobbyRole.Blue || role == RetrowaveLobbyRole.Orange;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitRoleSelectionServerRpc(int roleValue, RpcParams rpcParams = default)
        {
            var clientId = rpcParams.Receive.SenderClientId;
            var role = (RetrowaveLobbyRole)Mathf.Clamp(roleValue, (int)RetrowaveLobbyRole.Spectator, (int)RetrowaveLobbyRole.Orange);
            HandlePlayerRoleSelection(clientId, role);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestStartMatchServerRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId || !CanStartMatch)
            {
                return;
            }

            StartMatch();
        }

        private void HandleClientConnected(ulong clientId)
        {
            EnsureLobbyEntry(NetworkManager.ServerClientId);
            EnsureLobbyEntry(clientId);
            RecalculateSpawnSlots();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            RemoveLobbyEntry(clientId);
            RecalculateSpawnSlots();
        }

        private void EnsureLobbyEntry(ulong clientId)
        {
            if (GetLobbyEntryIndex(clientId) >= 0)
            {
                return;
            }

            var label = clientId == NetworkManager.ServerClientId ? "Host" : $"Player {clientId}";
            _lobbyEntries.Add(new RetrowaveLobbyEntry
            {
                ClientId = clientId,
                DisplayName = new FixedString32Bytes(label),
                RoleValue = (int)RetrowaveLobbyRole.Spectator,
                ActiveRoleValue = (int)RetrowaveLobbyRole.Spectator,
                HasSelectedRole = false,
                QueuedForNextRound = false,
                IsHost = clientId == NetworkManager.ServerClientId,
                Goals = 0,
                Assists = 0,
                PingMs = 0,
            });
        }

        private void RemoveLobbyEntry(ulong clientId)
        {
            var index = GetLobbyEntryIndex(clientId);

            if (index >= 0)
            {
                _lobbyEntries.RemoveAt(index);
            }
        }

        private void ApplyRoleForClient(ulong clientId, RetrowaveLobbyRole role)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                return;
            }

            if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent<RetrowavePlayerController>(out var player))
            {
                return;
            }

            if (role == RetrowaveLobbyRole.Spectator)
            {
                player.SetSpectatorStateServer(true);
            }
        }

        private void StartMatch()
        {
            CancelGoalCelebration();
            _phaseValue.Value = (int)RetrowaveMatchPhase.Live;
            _blueScore.Value = 0;
            _orangeScore.Value = 0;
            _roundNumber.Value = 1;
            _roundTimeRemaining.Value = RoundDurationSeconds;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];
                entry.Goals = 0;
                entry.Assists = 0;
                entry.ActiveRoleValue = entry.HasSelectedRole ? entry.RoleValue : (int)RetrowaveLobbyRole.Spectator;
                entry.QueuedForNextRound = false;
                _lobbyEntries[i] = entry;
            }

            _resetQueued = true;
            ClearTouchHistory();
        }

        private void AdvanceToNextRound()
        {
            CancelGoalCelebration();
            _roundNumber.Value = Mathf.Max(1, _roundNumber.Value + 1);
            _roundTimeRemaining.Value = RoundDurationSeconds;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];
                entry.ActiveRoleValue = entry.HasSelectedRole ? entry.RoleValue : (int)RetrowaveLobbyRole.Spectator;
                entry.QueuedForNextRound = false;
                _lobbyEntries[i] = entry;
            }

            ResetRound();
        }

        private void ApplyHostSettings(RetrowaveMatchSettings settings)
        {
            _roundDurationSeconds.Value = settings.RoundDurationSeconds;
            _maxPlayers.Value = settings.MaxPlayers;
            _arenaSizePresetValue.Value = (int)settings.ArenaSizePreset;
            ApplyArenaSettingsLocally();
        }

        private void ApplyArenaSettingsLocally()
        {
            var settings = new RetrowaveMatchSettings(
                _roundDurationSeconds.Value,
                _maxPlayers.Value,
                (RetrowaveArenaSizePreset)_arenaSizePresetValue.Value);

            RetrowaveArenaConfig.ApplyMatchSettings(settings);
            RetrowaveArenaBuilder.EnsureBuilt();
        }

        private void SpawnWorldActors()
        {
            if (_worldSpawned)
            {
                return;
            }

            _worldSpawned = true;

            var ball = RetrowaveGameBootstrap.Instance.CreateBallInstance();
            ball.name = "Match Ball";
            RetrowaveGameBootstrap.Instance.MoveRuntimeInstanceToGameplayScene(ball);
            ball.GetComponent<NetworkObject>().Spawn();
            ball.GetComponent<RetrowaveBall>().ResetBall();

            for (var i = 0; i < RetrowaveArenaConfig.PowerUpPositions.Length; i++)
            {
                var powerUpObject = RetrowaveGameBootstrap.Instance.CreatePowerUpInstance();
                powerUpObject.transform.SetPositionAndRotation(
                    RetrowaveArenaConfig.PowerUpPositions[i],
                    Quaternion.Euler(45f, i * 12f, 45f));

                powerUpObject.name = $"PowerUp {i}";
                RetrowaveGameBootstrap.Instance.MoveRuntimeInstanceToGameplayScene(powerUpObject);
                powerUpObject.GetComponent<NetworkObject>().Spawn();
                powerUpObject.GetComponent<RetrowavePowerUp>().InitializeServer(
                    i % 2 == 0 ? RetrowavePowerUpType.BoostRefill : RetrowavePowerUpType.SpeedBurst);
            }
        }

        private void ResetRound()
        {
            CancelGoalCelebration();
            RecalculateSpawnSlots();

            if (RetrowaveBall.Instance != null)
            {
                RetrowaveBall.Instance.ResetBall();
            }

            ClearTouchHistory();
        }

        private void StartGoalCelebration(RetrowaveTeam defendedGoal, RetrowaveTeam scoringTeam, string scorerName)
        {
            _goalCelebrationActive.Value = true;
            _goalCelebrationTimer = GoalCelebrationDuration;
            BlastActorsFromGoal(defendedGoal);
            BeginGoalCelebrationRpc((int)scoringTeam, scorerName, _blueScore.Value, _orangeScore.Value, GoalCelebrationDuration);
        }

        private void FinishGoalCelebration()
        {
            _goalCelebrationTimer = 0f;
            _goalCelebrationActive.Value = false;
            ResetRound();
        }

        private void CancelGoalCelebration()
        {
            _goalCelebrationTimer = 0f;

            if (_goalCelebrationActive.Value)
            {
                _goalCelebrationActive.Value = false;
            }
        }

        private void RecalculateSpawnSlots()
        {
            if (!IsServer || NetworkManager.Singleton == null)
            {
                return;
            }

            var blueSlot = 0;
            var orangeSlot = 0;

            foreach (var clientId in GetSortedConnectedClientIds())
            {
                var client = NetworkManager.Singleton.ConnectedClients[clientId];

                if (client.PlayerObject == null)
                {
                    continue;
                }

                if (!client.PlayerObject.TryGetComponent<RetrowavePlayerController>(out var player))
                {
                    continue;
                }

                if (TryGetAssignedTeam(clientId, useActiveRole: true, out var team))
                {
                    var spawnIndex = team == RetrowaveTeam.Blue ? blueSlot++ : orangeSlot++;
                    player.ConfigureServer(team, spawnIndex);
                    continue;
                }

                player.SetSpectatorStateServer(TryGetLobbyEntry(clientId, out var entry) && entry.HasSelectedRole);
            }
        }

        private List<ulong> GetSortedConnectedClientIds()
        {
            var clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClients.Count);

            foreach (var pair in NetworkManager.Singleton.ConnectedClients)
            {
                clientIds.Add(pair.Key);
            }

            clientIds.Sort();
            return clientIds;
        }

        private void RefreshPingValues()
        {
            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];
                entry.PingMs = (int)transport.GetCurrentRtt(entry.ClientId);
                _lobbyEntries[i] = entry;
            }
        }

        private void AwardGoalStats(RetrowaveTeam scoringTeam, ulong scorerClientId)
        {
            if (scorerClientId != ulong.MaxValue)
            {
                IncrementGoals(scorerClientId);
            }

            var assistIsValid = _previousTouchClientId != ulong.MaxValue
                                && _previousTouchClientId != scorerClientId
                                && _previousTouchTeam == scoringTeam
                                && _lastTouchTime - _previousTouchTime <= 8d;

            if (assistIsValid)
            {
                IncrementAssists(_previousTouchClientId);
            }

            ClearTouchHistory();
        }

        private ulong ResolveScorerClientId(RetrowaveTeam scoringTeam)
        {
            return _lastTouchClientId != ulong.MaxValue && _lastTouchTeam == scoringTeam
                ? _lastTouchClientId
                : ulong.MaxValue;
        }

        private string GetScorerAnnouncementName(ulong scorerClientId, RetrowaveTeam scoringTeam)
        {
            if (scorerClientId != ulong.MaxValue && TryGetLobbyEntry(scorerClientId, out var scorerEntry))
            {
                return scorerEntry.DisplayName.ToString();
            }

            return scoringTeam == RetrowaveTeam.Blue ? "Blue Team" : "Orange Team";
        }

        private void BlastActorsFromGoal(RetrowaveTeam defendedGoal)
        {
            var direction = defendedGoal == RetrowaveTeam.Blue ? -1f : 1f;
            var blastOrigin = new Vector3(
                0f,
                RetrowaveArenaConfig.GoalHeight * 0.45f,
                direction * (RetrowaveArenaConfig.FlatHalfLength + RetrowaveArenaConfig.GoalDepth * 0.9f));

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent<RetrowavePlayerController>(out var player) || player.Body == null)
                {
                    continue;
                }

                ApplyGoalExplosion(player.Body, blastOrigin, GoalExplosionPlayerImpulse);
            }

            if (RetrowaveBall.Instance != null && RetrowaveBall.Instance.Body != null)
            {
                ApplyGoalExplosion(RetrowaveBall.Instance.Body, blastOrigin, GoalExplosionBallImpulse);
            }
        }

        private void ApplyGoalExplosion(Rigidbody body, Vector3 blastOrigin, float impulse)
        {
            var offset = body.worldCenterOfMass - blastOrigin;

            if (offset.sqrMagnitude < 0.001f)
            {
                offset = Vector3.forward;
            }

            var distance = offset.magnitude;
            var direction = (offset.normalized + Vector3.up * GoalExplosionLift).normalized;
            var falloff = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(distance / GoalExplosionDistance));
            body.AddForce(direction * (impulse * falloff), ForceMode.VelocityChange);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BeginGoalCelebrationRpc(int scoringTeamValue, string scorerName, int blueScore, int orangeScore, float durationSeconds)
        {
            RetrowaveGameBootstrap.Instance?.BeginGoalCelebration(
                (RetrowaveTeam)scoringTeamValue,
                scorerName,
                blueScore,
                orangeScore,
                durationSeconds);
        }

        private void IncrementGoals(ulong clientId)
        {
            var index = GetLobbyEntryIndex(clientId);

            if (index < 0)
            {
                return;
            }

            var entry = _lobbyEntries[index];
            entry.Goals++;
            _lobbyEntries[index] = entry;
        }

        private void IncrementAssists(ulong clientId)
        {
            var index = GetLobbyEntryIndex(clientId);

            if (index < 0)
            {
                return;
            }

            var entry = _lobbyEntries[index];
            entry.Assists++;
            _lobbyEntries[index] = entry;
        }

        private void ClearTouchHistory()
        {
            _lastTouchClientId = ulong.MaxValue;
            _lastTouchTeam = RetrowaveTeam.Blue;
            _lastTouchTime = 0d;
            _previousTouchClientId = ulong.MaxValue;
            _previousTouchTeam = RetrowaveTeam.Blue;
            _previousTouchTime = 0d;
        }

        private bool TryGetAssignedTeam(ulong clientId, bool useActiveRole, out RetrowaveTeam team)
        {
            if (!TryGetLobbyEntry(clientId, out var entry))
            {
                team = RetrowaveTeam.Blue;
                return false;
            }

            return TryGetAssignedTeam(entry, useActiveRole, out team);
        }

        private static bool TryGetAssignedTeam(RetrowaveLobbyEntry entry, bool useActiveRole, out RetrowaveTeam team)
        {
            var role = useActiveRole ? entry.ActiveRole : entry.Role;

            switch (role)
            {
                case RetrowaveLobbyRole.Blue:
                    team = RetrowaveTeam.Blue;
                    return true;
                case RetrowaveLobbyRole.Orange:
                    team = RetrowaveTeam.Orange;
                    return true;
                default:
                    team = RetrowaveTeam.Blue;
                    return false;
            }
        }

        private int GetLobbyEntryIndex(ulong clientId)
        {
            if (_lobbyEntries == null)
            {
                return -1;
            }

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                if (_lobbyEntries[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
