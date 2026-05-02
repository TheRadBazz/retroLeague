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
        Countdown = 1,
        Live = 2,
        Podium = 3,
        MatchComplete = 4,
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
        public int Saves;
        public int StyleScore;
        public int ObjectiveCaptures;
        public int PowerUpHits;
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
            serializer.SerializeValue(ref Saves);
            serializer.SerializeValue(ref StyleScore);
            serializer.SerializeValue(ref ObjectiveCaptures);
            serializer.SerializeValue(ref PowerUpHits);
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
                   && Saves == other.Saves
                   && StyleScore == other.StyleScore
                   && ObjectiveCaptures == other.ObjectiveCaptures
                   && PowerUpHits == other.PowerUpHits
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
        private const float KickoffCountdownSeconds = 5f;
        private const float PodiumSequenceSeconds = 24f;
        private const double SaveAwardCooldownSeconds = 3.25d;
        private const float SaveDefensiveZoneRatio = 0.56f;
        private const float SaveGoalwardSpeed = 3.25f;

        private readonly NetworkVariable<int> _blueScore = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _pinkScore = new(
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

        private readonly NetworkVariable<int> _roundCount = new(
            RetrowaveMatchSettings.Default.RoundCount,
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

        private readonly NetworkVariable<float> _countdownTimeRemaining = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _podiumTimeRemaining = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _podiumWinnerTeamValue = new(
            -1,
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
        private readonly Dictionary<ulong, double> _lastSaveAwardByClient = new();
        private readonly Dictionary<ulong, float> _styleTotalsByClient = new();
        private static readonly Vector2[] SpawnClearanceOffsets =
        {
            Vector2.zero,
            new Vector2(4.5f, 0f),
            new Vector2(-4.5f, 0f),
            new Vector2(0f, 4.5f),
            new Vector2(0f, -4.5f),
            new Vector2(4.5f, 4.5f),
            new Vector2(-4.5f, 4.5f),
            new Vector2(4.5f, -4.5f),
            new Vector2(-4.5f, -4.5f),
            new Vector2(9f, 0f),
            new Vector2(-9f, 0f),
            new Vector2(0f, 9f),
            new Vector2(0f, -9f),
        };

        public static RetrowaveMatchManager Instance { get; private set; }

        public int BlueScore => _blueScore.Value;
        public int PinkScore => _pinkScore.Value;
        public RetrowaveMatchPhase Phase => (RetrowaveMatchPhase)_phaseValue.Value;
        public bool IsWarmup => Phase == RetrowaveMatchPhase.Warmup;
        public bool IsCountdown => Phase == RetrowaveMatchPhase.Countdown;
        public bool IsLiveMatch => Phase == RetrowaveMatchPhase.Live;
        public bool IsPodium => Phase == RetrowaveMatchPhase.Podium;
        public bool IsMatchComplete => Phase == RetrowaveMatchPhase.MatchComplete;
        public bool IsGameplayLocked => IsCountdown || IsPodium || IsMatchComplete || IsGoalCelebrationActive;
        public bool IsGoalCelebrationActive => _goalCelebrationActive.Value;
        public NetworkList<RetrowaveLobbyEntry> LobbyEntries => _lobbyEntries;
        public int RoundDurationSeconds => _roundDurationSeconds.Value;
        public int RoundCount => _roundCount.Value;
        public int MaxPlayers => _maxPlayers.Value;
        public RetrowaveArenaSizePreset ArenaSizePreset => (RetrowaveArenaSizePreset)_arenaSizePresetValue.Value;
        public int CurrentRoundNumber => _roundNumber.Value;
        public float RoundTimeRemaining => _roundTimeRemaining.Value;
        public float CountdownTimeRemaining => _countdownTimeRemaining.Value;
        public float PodiumTimeRemaining => _podiumTimeRemaining.Value;
        public float PodiumSequenceDuration => PodiumSequenceSeconds;
        public bool HasPodiumWinner => _podiumWinnerTeamValue.Value >= 0;
        public RetrowaveTeam PodiumWinnerTeam => _podiumWinnerTeamValue.Value == (int)RetrowaveTeam.Pink ? RetrowaveTeam.Pink : RetrowaveTeam.Blue;
        public bool CanStartMatch => GetDesiredTeamCount(RetrowaveTeam.Blue) > 0 && GetDesiredTeamCount(RetrowaveTeam.Pink) > 0;

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

            if (IsCountdown && !_goalCelebrationActive.Value)
            {
                _countdownTimeRemaining.Value = Mathf.Max(0f, _countdownTimeRemaining.Value - Time.deltaTime);

                if (_countdownTimeRemaining.Value <= 0f)
                {
                    BeginLivePlay();
                }
            }

            if (IsPodium)
            {
                _podiumTimeRemaining.Value = Mathf.Max(0f, _podiumTimeRemaining.Value - Time.deltaTime);

                if (_podiumTimeRemaining.Value <= 0f)
                {
                    CompleteMatch();
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
            _roundCount.OnValueChanged += HandleArenaSettingsChanged;
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
            _roundCount.OnValueChanged -= HandleArenaSettingsChanged;
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

            if (!IsLiveMatch)
            {
                if (IsWarmup)
                {
                    _resetQueued = true;
                    ClearTouchHistory();
                }

                return;
            }

            var scoringTeam = defendedGoal == RetrowaveTeam.Blue ? RetrowaveTeam.Pink : RetrowaveTeam.Blue;

            if (scoringTeam == RetrowaveTeam.Blue)
            {
                _blueScore.Value++;
            }
            else
            {
                _pinkScore.Value++;
            }

            var scorerClientId = ResolveScorerClientId(scoringTeam);
            var assistClientId = ResolveAssistClientId(scoringTeam, scorerClientId);
            var scorerName = GetScorerAnnouncementName(scorerClientId, scoringTeam);
            var assistName = GetAssistAnnouncementName(assistClientId);
            AwardGoalStats(scorerClientId, assistClientId);
            StartGoalCelebration(defendedGoal, scoringTeam, scorerName, assistName);
        }

        public RetrowaveBallTouchResult RegisterBallTouch(RetrowavePlayerController player)
        {
            if (!IsServer || player == null)
            {
                return RetrowaveBallTouchResult.Ignored;
            }

            var now = Time.timeAsDouble;
            var clientId = player.ControllingClientId;

            if (_lastTouchClientId == clientId && now - _lastTouchTime < 0.15d)
            {
                return RetrowaveBallTouchResult.Ignored;
            }

            TryAwardSaveServer(player, now);

            var isTeamCombo = _lastTouchClientId != ulong.MaxValue
                              && _lastTouchClientId != clientId
                              && _lastTouchTeam == player.Team
                              && now - _lastTouchTime <= 2d;
            var previousClientId = _lastTouchClientId;
            _previousTouchClientId = _lastTouchClientId;
            _previousTouchTeam = _lastTouchTeam;
            _previousTouchTime = _lastTouchTime;

            _lastTouchClientId = clientId;
            _lastTouchTeam = player.Team;
            _lastTouchTime = now;

            if (isTeamCombo)
            {
                player.AwardStyleServer(RetrowaveStyleEvent.TeamCombo);
                AwardStyleToPlayerServer(previousClientId, RetrowaveStyleEvent.Pass);
            }

            return new RetrowaveBallTouchResult(true, isTeamCombo, previousClientId, isTeamCombo ? 1.16f : 1f);
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
            RefreshActorSlotsForCurrentPhase();
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
            else if (IsLiveMatch || IsCountdown || IsPodium)
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
            RefreshActorSlotsForCurrentPhase();
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
            return (RetrowaveLobbyRole)Mathf.Clamp((int)role, (int)RetrowaveLobbyRole.Spectator, (int)RetrowaveLobbyRole.Pink);
        }

        private static bool IsTeamRole(RetrowaveLobbyRole role)
        {
            return role == RetrowaveLobbyRole.Blue || role == RetrowaveLobbyRole.Pink;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitRoleSelectionServerRpc(int roleValue, RpcParams rpcParams = default)
        {
            var clientId = rpcParams.Receive.SenderClientId;
            var role = (RetrowaveLobbyRole)Mathf.Clamp(roleValue, (int)RetrowaveLobbyRole.Spectator, (int)RetrowaveLobbyRole.Pink);
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
            RefreshActorSlotsForCurrentPhase();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            RemoveLobbyEntry(clientId);
            RefreshActorSlotsForCurrentPhase();
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
                Saves = 0,
                StyleScore = 0,
                ObjectiveCaptures = 0,
                PowerUpHits = 0,
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

            _lastSaveAwardByClient.Remove(clientId);
            _styleTotalsByClient.Remove(clientId);
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
            ClearPowerUpsForMatchStart();
            _blueScore.Value = 0;
            _pinkScore.Value = 0;
            _roundNumber.Value = 1;
            _roundTimeRemaining.Value = RoundDurationSeconds;
            _countdownTimeRemaining.Value = 0f;
            _podiumTimeRemaining.Value = 0f;
            _podiumWinnerTeamValue.Value = -1;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];
                entry.Goals = 0;
                entry.Assists = 0;
                entry.Saves = 0;
                entry.StyleScore = 0;
                entry.ObjectiveCaptures = 0;
                entry.PowerUpHits = 0;
                entry.ActiveRoleValue = entry.HasSelectedRole ? entry.RoleValue : (int)RetrowaveLobbyRole.Spectator;
                entry.QueuedForNextRound = false;
                _lobbyEntries[i] = entry;
            }

            _lastSaveAwardByClient.Clear();
            _styleTotalsByClient.Clear();
            ClearTouchHistory();
            BeginKickoffCountdown();
            ShowPreMatchLineupRpc(_roundNumber.Value, RoundCount, KickoffCountdownSeconds);
        }

        private void ClearPowerUpsForMatchStart()
        {
            if (!IsServer || NetworkManager.Singleton == null)
            {
                return;
            }

            var objectsToDespawn = new List<NetworkObject>();

            foreach (var networkObject in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null)
                {
                    continue;
                }

                if (networkObject.TryGetComponent<RetrowavePlayerController>(out var player))
                {
                    player.ClearPowerUpsForMatchStartServer();
                }

                if (networkObject.TryGetComponent<RarePowerUpPickupBeacon>(out _)
                    || networkObject.TryGetComponent<NeonTrailSegment>(out _)
                    || networkObject.TryGetComponent<GravityBombDevice>(out _)
                    || networkObject.TryGetComponent<ChronoDomeField>(out _))
                {
                    objectsToDespawn.Add(networkObject);
                }
            }

            for (var i = 0; i < objectsToDespawn.Count; i++)
            {
                var networkObject = objectsToDespawn[i];

                if (networkObject != null && networkObject.IsSpawned)
                {
                    networkObject.Despawn(true);
                }
            }

            if (TryGetComponent<RarePowerUpSpawner>(out var rareSpawner))
            {
                rareSpawner.ResetForMatchStartServer();
            }

            if (TryGetComponent<RetrowaveArenaObjectiveSystem>(out var arenaObjectives))
            {
                arenaObjectives.ResetForMatchStartServer();
            }
        }

        private void AdvanceToNextRound()
        {
            CancelGoalCelebration();
            ShowRoundStatCardsRpc(_roundNumber.Value, _blueScore.Value, _pinkScore.Value, 5.75f);

            if (_roundNumber.Value >= RoundCount)
            {
                BeginPodiumSequence();
                return;
            }

            _roundNumber.Value = Mathf.Max(1, _roundNumber.Value + 1);
            _roundTimeRemaining.Value = RoundDurationSeconds;
            _countdownTimeRemaining.Value = 0f;
            _podiumTimeRemaining.Value = 0f;
            _podiumWinnerTeamValue.Value = -1;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];
                entry.ActiveRoleValue = entry.HasSelectedRole ? entry.RoleValue : (int)RetrowaveLobbyRole.Spectator;
                entry.QueuedForNextRound = false;
                _lobbyEntries[i] = entry;
            }

            BeginKickoffCountdown();
        }

        private void BeginPodiumSequence()
        {
            CancelGoalCelebration();
            _phaseValue.Value = (int)RetrowaveMatchPhase.Podium;
            _roundTimeRemaining.Value = 0f;
            _countdownTimeRemaining.Value = 0f;
            _podiumTimeRemaining.Value = PodiumSequenceSeconds;

            var hasWinner = TryResolvePodiumWinner(out var winningTeam);
            _podiumWinnerTeamValue.Value = hasWinner ? (int)winningTeam : -1;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];
                entry.ActiveRoleValue = entry.HasSelectedRole ? entry.RoleValue : (int)RetrowaveLobbyRole.Spectator;
                entry.QueuedForNextRound = false;
                _lobbyEntries[i] = entry;
            }

            PositionActorsForPodium(hasWinner, winningTeam);

            if (TryResolveMvp(hasWinner, winningTeam, out var mvpEntry, out var mvpScore))
            {
                ShowMvpMomentRpc(mvpEntry.ClientId, mvpScore, 9f);
            }

            if (RetrowaveBall.Instance != null)
            {
                RetrowaveBall.Instance.ResetBall();
            }

            ClearTouchHistory();
        }

        private void CompleteMatch()
        {
            _phaseValue.Value = (int)RetrowaveMatchPhase.MatchComplete;
            _roundTimeRemaining.Value = 0f;
            _countdownTimeRemaining.Value = 0f;
            _podiumTimeRemaining.Value = 0f;

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];
                entry.ActiveRoleValue = entry.HasSelectedRole ? entry.RoleValue : (int)RetrowaveLobbyRole.Spectator;
                entry.QueuedForNextRound = false;
                _lobbyEntries[i] = entry;
            }

            ResetRound();
        }

        private void BeginKickoffCountdown()
        {
            _phaseValue.Value = (int)RetrowaveMatchPhase.Countdown;
            _countdownTimeRemaining.Value = KickoffCountdownSeconds;
            _podiumTimeRemaining.Value = 0f;
            _podiumWinnerTeamValue.Value = -1;
            ResetRound();
        }

        private void BeginLivePlay()
        {
            _countdownTimeRemaining.Value = 0f;
            _podiumTimeRemaining.Value = 0f;
            _phaseValue.Value = (int)RetrowaveMatchPhase.Live;
            ClearTouchHistory();
        }

        private void ApplyHostSettings(RetrowaveMatchSettings settings)
        {
            _roundDurationSeconds.Value = settings.RoundDurationSeconds;
            _roundCount.Value = settings.RoundCount;
            _maxPlayers.Value = settings.MaxPlayers;
            _arenaSizePresetValue.Value = (int)settings.ArenaSizePreset;
            ApplyArenaSettingsLocally();
        }

        private void ApplyArenaSettingsLocally()
        {
            var settings = new RetrowaveMatchSettings(
                _roundDurationSeconds.Value,
                _roundCount.Value,
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
            ResetActorsForKickoff();
        }

        private void ResetActorsForKickoff()
        {
            RecalculateSpawnSlots();

            if (RetrowaveBall.Instance != null)
            {
                RetrowaveBall.Instance.ResetBall();
            }

            ClearTouchHistory();
        }

        private bool TryResolvePodiumWinner(out RetrowaveTeam winningTeam)
        {
            if (BlueScore > PinkScore)
            {
                winningTeam = RetrowaveTeam.Blue;
                return true;
            }

            if (PinkScore > BlueScore)
            {
                winningTeam = RetrowaveTeam.Pink;
                return true;
            }

            winningTeam = RetrowaveTeam.Blue;
            return false;
        }

        private void PositionActorsForPodium(bool hasWinner, RetrowaveTeam winningTeam)
        {
            if (!IsServer || NetworkManager.Singleton == null)
            {
                return;
            }

            var podiumEntries = BuildPodiumEntries(hasWinner, winningTeam);
            var podiumRanks = new Dictionary<ulong, int>(podiumEntries.Count);

            for (var i = 0; i < podiumEntries.Count; i++)
            {
                podiumRanks[podiumEntries[i].ClientId] = i;
            }

            foreach (var clientId in GetSortedConnectedClientIds())
            {
                var client = NetworkManager.Singleton.ConnectedClients[clientId];

                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent<RetrowavePlayerController>(out var player))
                {
                    continue;
                }

                if (podiumRanks.TryGetValue(clientId, out var rank))
                {
                    player.SetPodiumPresentationServer(
                        RetrowavePodiumLayout.GetVehiclePosition(rank, podiumEntries.Count),
                        RetrowavePodiumLayout.VehicleRotation,
                        true);
                    continue;
                }

                var hiddenPosition = RetrowaveArenaConfig.GetSpectatorStagingPoint(clientId) + Vector3.up * 18f;
                player.SetPodiumPresentationServer(hiddenPosition, Quaternion.identity, false);
            }
        }

        private List<RetrowaveLobbyEntry> BuildPodiumEntries(bool hasWinner, RetrowaveTeam winningTeam)
        {
            var entries = new List<RetrowaveLobbyEntry>();

            for (var i = 0; i < _lobbyEntries.Count; i++)
            {
                var entry = _lobbyEntries[i];

                if (!TryGetAssignedTeam(entry, useActiveRole: true, out var team))
                {
                    continue;
                }

                if (hasWinner && team != winningTeam)
                {
                    continue;
                }

                entries.Add(entry);
            }

            entries.Sort(ComparePodiumEntries);
            return entries;
        }

        private bool TryResolveMvp(bool hasWinner, RetrowaveTeam winningTeam, out RetrowaveLobbyEntry mvpEntry, out int mvpScore)
        {
            var entries = BuildPodiumEntries(hasWinner, winningTeam);

            if (entries.Count == 0 && hasWinner)
            {
                entries = BuildPodiumEntries(hasWinner: false, RetrowaveTeam.Blue);
            }

            if (entries.Count == 0)
            {
                mvpEntry = default;
                mvpScore = 0;
                return false;
            }

            entries.Sort(ComparePodiumEntries);
            mvpEntry = entries[0];
            mvpScore = CalculateMvpScore(mvpEntry);
            return true;
        }

        private static int ComparePodiumEntries(RetrowaveLobbyEntry left, RetrowaveLobbyEntry right)
        {
            var mvpCompare = CalculateMvpScore(right).CompareTo(CalculateMvpScore(left));

            if (mvpCompare != 0)
            {
                return mvpCompare;
            }

            var goalCompare = right.Goals.CompareTo(left.Goals);

            if (goalCompare != 0)
            {
                return goalCompare;
            }

            var assistCompare = right.Assists.CompareTo(left.Assists);

            if (assistCompare != 0)
            {
                return assistCompare;
            }

            return left.ClientId.CompareTo(right.ClientId);
        }

        private static int CalculateMvpScore(RetrowaveLobbyEntry entry)
        {
            return entry.Goals * 100
                   + entry.Assists * 60
                   + entry.Saves * 70
                   + entry.ObjectiveCaptures * 45
                   + entry.PowerUpHits * 35
                   + entry.StyleScore;
        }

        private void StartGoalCelebration(RetrowaveTeam defendedGoal, RetrowaveTeam scoringTeam, string scorerName, string assistName)
        {
            _goalCelebrationActive.Value = true;
            _goalCelebrationTimer = GoalCelebrationDuration;
            ResetActorsForKickoff();
            BeginGoalCelebrationRpc((int)scoringTeam, scorerName, assistName, _blueScore.Value, _pinkScore.Value, GoalCelebrationDuration);
        }

        private void FinishGoalCelebration()
        {
            _goalCelebrationTimer = 0f;
            _goalCelebrationActive.Value = false;
            BeginKickoffCountdown();
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
            var pinkSlot = 0;
            var blueCount = GetActiveTeamCount(RetrowaveTeam.Blue);
            var pinkCount = GetActiveTeamCount(RetrowaveTeam.Pink);
            var reservedSpawnPositions = new List<Vector3>(blueCount + pinkCount);

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
                    var spawnIndex = team == RetrowaveTeam.Blue ? blueSlot++ : pinkSlot++;
                    var teamCount = team == RetrowaveTeam.Blue ? blueCount : pinkCount;
                    var preferredSpawn = RetrowaveArenaConfig.GetSpawnPoint(team, spawnIndex, teamCount);
                    var resolvedSpawn = ResolveClearSpawnPoint(preferredSpawn, team, reservedSpawnPositions);
                    reservedSpawnPositions.Add(resolvedSpawn);
                    player.ConfigureServer(team, resolvedSpawn);
                    continue;
                }

                player.SetSpectatorStateServer(TryGetLobbyEntry(clientId, out var entry) && entry.HasSelectedRole);
            }
        }

        private static Vector3 ResolveClearSpawnPoint(Vector3 preferredSpawn, RetrowaveTeam team, List<Vector3> reservedSpawnPositions)
        {
            var baseSpawn = RetrowaveArenaConfig.ClampToPlayableSpawn(preferredSpawn, team);

            for (var i = 0; i < SpawnClearanceOffsets.Length; i++)
            {
                var offset = SpawnClearanceOffsets[i];
                var candidate = RetrowaveArenaConfig.ClampToPlayableSpawn(
                    baseSpawn + new Vector3(offset.x, 0f, offset.y),
                    team);

                if (IsSpawnClear(candidate, reservedSpawnPositions))
                {
                    return candidate;
                }
            }

            return baseSpawn;
        }

        private static bool IsSpawnClear(Vector3 candidate, List<Vector3> reservedSpawnPositions)
        {
            const float minSpawnSpacing = 3.8f;
            var minSpacingSquared = minSpawnSpacing * minSpawnSpacing;

            for (var i = 0; i < reservedSpawnPositions.Count; i++)
            {
                var offset = candidate - reservedSpawnPositions[i];
                offset.y = 0f;

                if (offset.sqrMagnitude < minSpacingSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private void RefreshActorSlotsForCurrentPhase()
        {
            if (IsPodium)
            {
                PositionActorsForPodium(HasPodiumWinner, PodiumWinnerTeam);
                return;
            }

            RecalculateSpawnSlots();
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

        private void AwardGoalStats(ulong scorerClientId, ulong assistClientId)
        {
            if (scorerClientId != ulong.MaxValue)
            {
                IncrementGoals(scorerClientId);
            }

            if (assistClientId != ulong.MaxValue)
            {
                IncrementAssists(assistClientId);
            }

            ClearTouchHistory();
        }

        private ulong ResolveScorerClientId(RetrowaveTeam scoringTeam)
        {
            return _lastTouchClientId != ulong.MaxValue && _lastTouchTeam == scoringTeam
                ? _lastTouchClientId
                : ulong.MaxValue;
        }

        private ulong ResolveAssistClientId(RetrowaveTeam scoringTeam, ulong scorerClientId)
        {
            var assistIsValid = _previousTouchClientId != ulong.MaxValue
                                && _previousTouchClientId != scorerClientId
                                && _previousTouchTeam == scoringTeam
                                && _lastTouchTime - _previousTouchTime <= 8d;

            return assistIsValid ? _previousTouchClientId : ulong.MaxValue;
        }

        private string GetScorerAnnouncementName(ulong scorerClientId, RetrowaveTeam scoringTeam)
        {
            if (scorerClientId != ulong.MaxValue && TryGetLobbyEntry(scorerClientId, out var scorerEntry))
            {
                return scorerEntry.DisplayName.ToString();
            }

            return scoringTeam == RetrowaveTeam.Blue ? "Blue Team" : "Pink Team";
        }

        private string GetAssistAnnouncementName(ulong assistClientId)
        {
            return assistClientId != ulong.MaxValue && TryGetLobbyEntry(assistClientId, out var assistEntry)
                ? assistEntry.DisplayName.ToString()
                : string.Empty;
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
        private void BeginGoalCelebrationRpc(int scoringTeamValue, string scorerName, string assistName, int blueScore, int pinkScore, float durationSeconds)
        {
            RetrowaveArenaAudio.PlayGoalCelebration(Vector3.zero);
            RetrowaveGameBootstrap.Instance?.BeginGoalCelebration(
                (RetrowaveTeam)scoringTeamValue,
                scorerName,
                assistName,
                blueScore,
                pinkScore,
                durationSeconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowPreMatchLineupRpc(int roundNumber, int roundCount, float durationSeconds)
        {
            RetrowaveGameBootstrap.Instance?.ShowPreMatchLineup(roundNumber, roundCount, durationSeconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowRoundStatCardsRpc(int completedRound, int blueScore, int pinkScore, float durationSeconds)
        {
            RetrowaveGameBootstrap.Instance?.ShowRoundStatCards(completedRound, blueScore, pinkScore, durationSeconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowMvpMomentRpc(ulong mvpClientId, int mvpScore, float durationSeconds)
        {
            RetrowaveGameBootstrap.Instance?.ShowMvpMoment(mvpClientId, mvpScore, durationSeconds);
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

        public void RecordStyleServer(ulong clientId, float points)
        {
            if (!IsServer || clientId == ulong.MaxValue || points <= 0f)
            {
                return;
            }

            var index = GetLobbyEntryIndex(clientId);

            if (index < 0)
            {
                return;
            }

            _styleTotalsByClient.TryGetValue(clientId, out var currentTotal);
            currentTotal += points;
            _styleTotalsByClient[clientId] = currentTotal;

            var entry = _lobbyEntries[index];
            var nextScore = Mathf.FloorToInt(currentTotal);

            if (entry.StyleScore != nextScore)
            {
                entry.StyleScore = nextScore;
                _lobbyEntries[index] = entry;
            }
        }

        public void RecordObjectiveCaptureServer(ulong clientId)
        {
            if (!IsServer || clientId == ulong.MaxValue)
            {
                return;
            }

            var index = GetLobbyEntryIndex(clientId);

            if (index < 0)
            {
                return;
            }

            var entry = _lobbyEntries[index];
            entry.ObjectiveCaptures++;
            _lobbyEntries[index] = entry;
        }

        public void RecordPowerUpHitServer(ulong sourceClientId, ulong targetClientId = ulong.MaxValue)
        {
            if (!IsServer || sourceClientId == ulong.MaxValue || sourceClientId == targetClientId)
            {
                return;
            }

            var index = GetLobbyEntryIndex(sourceClientId);

            if (index < 0)
            {
                return;
            }

            var entry = _lobbyEntries[index];
            entry.PowerUpHits++;
            _lobbyEntries[index] = entry;
        }

        private void IncrementSaves(ulong clientId)
        {
            var index = GetLobbyEntryIndex(clientId);

            if (index < 0)
            {
                return;
            }

            var entry = _lobbyEntries[index];
            entry.Saves++;
            _lobbyEntries[index] = entry;
        }

        private void TryAwardSaveServer(RetrowavePlayerController player, double now)
        {
            if (!IsLiveMatch || player == null || !player.IsArenaParticipant || RetrowaveBall.Instance == null || RetrowaveBall.Instance.Body == null)
            {
                return;
            }

            var ballBody = RetrowaveBall.Instance.Body;
            var ballPosition = ballBody.worldCenterOfMass;
            var ballVelocity = ballBody.linearVelocity;
            var defendingBlueGoal = player.Team == RetrowaveTeam.Blue;
            var defensiveZone = defendingBlueGoal
                ? ballPosition.z <= -RetrowaveArenaConfig.FlatHalfLength * SaveDefensiveZoneRatio
                : ballPosition.z >= RetrowaveArenaConfig.FlatHalfLength * SaveDefensiveZoneRatio;
            var goalwardSpeed = defendingBlueGoal ? -ballVelocity.z : ballVelocity.z;
            var inGoalLane = Mathf.Abs(ballPosition.x) <= RetrowaveArenaConfig.GoalHalfWidth + 5f;

            if (!defensiveZone || !inGoalLane || goalwardSpeed < SaveGoalwardSpeed)
            {
                return;
            }

            var clientId = player.ControllingClientId;

            if (_lastSaveAwardByClient.TryGetValue(clientId, out var lastAwardTime)
                && now - lastAwardTime < SaveAwardCooldownSeconds)
            {
                return;
            }

            _lastSaveAwardByClient[clientId] = now;
            IncrementSaves(clientId);
            player.AwardStyleServer(RetrowaveStyleEvent.PowerPlay, 0.65f);
        }

        private void AwardStyleToPlayerServer(ulong clientId, RetrowaveStyleEvent styleEvent)
        {
            if (clientId == ulong.MaxValue || NetworkManager.Singleton == null)
            {
                return;
            }

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)
                || client.PlayerObject == null
                || !client.PlayerObject.TryGetComponent<RetrowavePlayerController>(out var player))
            {
                return;
            }

            player.AwardStyleServer(styleEvent);
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
                case RetrowaveLobbyRole.Pink:
                    team = RetrowaveTeam.Pink;
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
