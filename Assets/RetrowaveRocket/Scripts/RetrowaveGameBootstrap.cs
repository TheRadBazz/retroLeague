using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RetrowaveRocket
{
    public sealed class RetrowaveGameBootstrap : MonoBehaviour
    {
        private sealed class RuntimePrefabHandler : INetworkPrefabInstanceHandler
        {
            private readonly Func<GameObject> _factory;

            public RuntimePrefabHandler(Func<GameObject> factory)
            {
                _factory = factory;
            }

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
            {
                var instance = _factory?.Invoke();

                if (instance == null)
                {
                    return null;
                }

                instance.transform.SetPositionAndRotation(position, rotation);
                return instance.GetComponent<NetworkObject>();
            }

            public void Destroy(NetworkObject networkObject)
            {
                if (networkObject != null)
                {
                    UnityEngine.Object.Destroy(networkObject.gameObject);
                }
            }
        }

        private enum PendingConnectionMode
        {
            None = 0,
            Host = 1,
            Client = 2,
        }

        public const string MainMenuSceneName = "MainMenu";
        public const string GameplaySceneName = "SampleScene";
        private const string SportCarResourcePath = "RetrowaveRocket/SportCar_5";
        private const string YughuesBallMaterialName = "M_YFMeM_49";
        private const string YughuesBallMaterialAssetPath = "Assets/YughuesFreeMetalMaterials/Materials/M_YFMeM_49.mat";
        private const string YughuesBallMaterialResourcePath = "RetrowaveRocket/M_YFMeM_49_Ball";
        private const float YughuesBallTextureTiling = 2.2f;
        private static readonly Color BallSurfaceTint = new Color(0.66f, 0.7f, 0.76f, 1f);
        private static readonly Color BallSurfaceEmission = new Color(0.9f, 0.9f, 0.9f, 1f);

        private static RetrowaveGameBootstrap _instance;
        private static readonly FieldInfo GlobalObjectIdHashField = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject _networkRuntimeRoot;
        private NetworkManager _networkManager;
        private UnityTransport _transport;
        private GameObject _playerPrefab;
        private GameObject _ballPrefab;
        private GameObject _powerUpPrefab;
        private GameObject _matchManagerPrefab;
        private string _address = "127.0.0.1";
        private string _preferredDisplayName = "Player";
        private ushort _port = 7777;
        private PendingConnectionMode _pendingConnectionMode;
        private bool _showPauseMenu;
        private bool _showScoreboard;
        private float _defaultFixedDeltaTime;
        private bool _goalCelebrationVisible;
        private float _goalCelebrationEndsAtRealtime;
        private RetrowaveTeam _goalCelebrationTeam;
        private string _goalCelebrationScorer = string.Empty;
        private int _goalCelebrationBlueScore;
        private int _goalCelebrationPinkScore;
        private GameObject _gameplayMenuRoot;
        private RectTransform _gameplayMenuPanelRect;
        private GameObject _gameplayMenuEventSystem;
        private TMP_FontAsset _gameplayMenuFont;
        private TMP_Text _gameplayMenuTitleText;
        private TMP_Text _gameplayMenuBodyText;
        private TMP_Text _gameplayMenuHintText;
        private TMP_Text _gameplayMenuFooterText;
        private Button _gameplayBlueButton;
        private Button _gameplayPinkButton;
        private Button _gameplaySpectateButton;
        private Button _gameplayResumeButton;
        private Button _gameplayStartButton;
        private Button _gameplayReturnButton;
        private TMP_Text _gameplayStartButtonLabel;
        private TMP_Text _gameplayReturnButtonLabel;
        private bool _gameplayMenuWasVisible;
        private bool _showHudInfoPanel = true;
        private GameObject _gameplayHudRoot;
        private GameObject _gameplayHudInfoRoot;
        private GameObject _gameplayHudInfoCollapsedRoot;
        private GameObject _gameplayHudScoreboardRoot;
        private GameObject _gameplayHudGoalRoot;
        private GameObject _gameplayHudCountdownRoot;
        private TMP_Text _hudScoreStateText;
        private TMP_Text _hudScoreClockText;
        private TMP_Text _hudBlueScoreText;
        private TMP_Text _hudPinkScoreText;
        private TMP_Text _hudInfoToggleText;
        private TMP_Text _hudInfoConnectionText;
        private TMP_Text _hudInfoPhaseText;
        private TMP_Text _hudInfoRoleText;
        private TMP_Text _hudInfoHintText;
        private TMP_Text _hudInfoCollapsedText;
        private TMP_Text _hudGaugeTitleText;
        private TMP_Text _hudSpeedValueText;
        private TMP_Text _hudSpeedLabelText;
        private TMP_Text _hudBoostValueText;
        private TMP_Text _hudGaugeStatusText;
        private Image _hudSpeedFillImage;
        private Image _hudBoostFillImage;
        private RectTransform _hudSpeedBarRect;
        private RectTransform _hudBoostBarRect;
        private RectTransform _hudSpeedMarkerRect;
        private RectTransform _hudBoostMarkerRect;
        private TMP_Text _hudScoreboardTitleText;
        private TMP_Text _hudScoreboardSummaryText;
        private TMP_Text _hudScoreboardBlueText;
        private TMP_Text _hudScoreboardPinkText;
        private TMP_Text _hudScoreboardSpectatorText;
        private TMP_Text _hudGoalHeadlineText;
        private TMP_Text _hudGoalScoreText;
        private TMP_Text _hudGoalDetailText;
        private TMP_Text _hudCountdownLabelText;
        private TMP_Text _hudCountdownValueText;
        private bool _gameplayHudSessionVisible;
        private bool _hudInfoIntroAutoHidePending;
        private float _hudInfoIntroHideAtRealtime;
        private GameObject _podiumPresentationRoot;
        private bool _podiumCameraActive;
        private bool _sessionShutdownInProgress;
        private float _serverSessionReconcileTimer;
        private RetrowaveMatchSettings _currentMatchSettings = RetrowaveMatchSettings.Default;

        public static RetrowaveGameBootstrap Instance => _instance;
        public GameObject PlayerPrefab => _playerPrefab;
        public GameObject BallPrefab => _ballPrefab;
        public GameObject PowerUpPrefab => _powerUpPrefab;
        public string DefaultAddress => _address;
        public string DefaultPort => _port.ToString();
        public string SuggestedHostAddress => ResolvePreferredAddress();
        public string PreferredDisplayName => _preferredDisplayName;
        public RetrowaveMatchSettings CurrentMatchSettings => _currentMatchSettings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var bootstrap = new GameObject("Retrowave Rocket Bootstrap");
            DontDestroyOnLoad(bootstrap);
            bootstrap.AddComponent<RetrowaveGameBootstrap>();
        }

        public static bool IsGameplayInputBlocked()
        {
            return _instance != null && _instance.ShouldBlockGameplayInput();
        }

        public static void RequestProcessShutdown()
        {
            _instance?.PrepareForProcessExit();
        }

        public static string NormalizeDisplayName(string value)
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            if (trimmed.Length == 0)
            {
                trimmed = Environment.UserName;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                trimmed = "Player";
            }

            return trimmed.Length > 24 ? trimmed[..24] : trimmed;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _defaultFixedDeltaTime = Time.fixedDeltaTime;
            _address = ResolvePreferredAddress();
            _preferredDisplayName = NormalizeDisplayName(Environment.UserName);

            EnsureNetworkRuntime();
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplyScenePresentation(SceneManager.GetActiveScene());
        }

        private void Update()
        {
            if (_networkManager == null || !IsGameplayScene(SceneManager.GetActiveScene()))
            {
                _showPauseMenu = false;
                _showScoreboard = false;
                ClearGoalCelebrationState();
                ClearPodiumPresentation();
                SetGameplayMenuVisible(false);
                if (_gameplayHudRoot != null)
                {
                    _gameplayHudRoot.SetActive(false);
                }
                ResetHudInfoIntroState();
                return;
            }

            if (!_networkManager.IsListening)
            {
                _showPauseMenu = false;
                _showScoreboard = false;
                ClearGoalCelebrationState();
                ClearPodiumPresentation();
                SetGameplayMenuVisible(false);
                if (_gameplayHudRoot != null)
                {
                    _gameplayHudRoot.SetActive(false);
                }
                ResetHudInfoIntroState();
                return;
            }

            var keyboard = Keyboard.current;

            if (keyboard != null)
            {
                HandleRoleSelectionHotkeys(keyboard);
                HandleSpectatorFollowHotkeys(keyboard);
                _showScoreboard = keyboard.tabKey.isPressed;

                if (keyboard.hKey.wasPressedThisFrame)
                {
                    _showHudInfoPanel = !_showHudInfoPanel;
                    _hudInfoIntroAutoHidePending = false;
                }

                if (keyboard.escapeKey.wasPressedThisFrame && !RequiresRoleSelection())
                {
                    _showPauseMenu = !_showPauseMenu;
                }
            }

            if (_goalCelebrationVisible && Time.unscaledTime >= _goalCelebrationEndsAtRealtime)
            {
                ClearGoalCelebrationState();
            }

            if (_networkManager.IsServer)
            {
                _serverSessionReconcileTimer -= Time.unscaledDeltaTime;

                if (_serverSessionReconcileTimer <= 0f)
                {
                    _serverSessionReconcileTimer = 0.25f;
                    ReconcileServerSessionState();
                }
            }

            RefreshGameplayMenuState();
            RefreshGameplayHudState();
        }

        private void HandleRoleSelectionHotkeys(Keyboard keyboard)
        {
            if (!RequiresRoleSelection())
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame || keyboard.bKey.wasPressedThisFrame)
            {
                TryRequestRoleSelection(RetrowaveLobbyRole.Blue);
                _showPauseMenu = false;
                return;
            }

            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame)
            {
                TryRequestRoleSelection(RetrowaveLobbyRole.Pink);
                _showPauseMenu = false;
                return;
            }

            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                TryRequestRoleSelection(RetrowaveLobbyRole.Spectator);
                _showPauseMenu = false;
            }
        }

        private void HandleSpectatorFollowHotkeys(Keyboard keyboard)
        {
            if (!CanCycleWarmupSpectatorTargets())
            {
                return;
            }

            if (keyboard.leftBracketKey.wasPressedThisFrame || keyboard.commaKey.wasPressedThisFrame)
            {
                RetrowaveCameraRig.CycleWarmupSpectatorTarget(-1, GetWarmupSpectatorTargets());
                return;
            }

            if (keyboard.rightBracketKey.wasPressedThisFrame || keyboard.periodKey.wasPressedThisFrame)
            {
                RetrowaveCameraRig.CycleWarmupSpectatorTarget(1, GetWarmupSpectatorTargets());
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                ForceShutdownSession(false, false);
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                ClearGoalCelebrationState();
                ClearPodiumPresentation();
                DestroyGameplayMenuOverlay();
                DestroyGameplayHudOverlay();
                _instance = null;
            }
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                ForceShutdownSession(false, false);
            }
        }

        private void OnApplicationQuit()
        {
            PrepareForProcessExit();
        }

        private void OnGUI()
        {
            if (_networkManager == null || !IsGameplayScene(SceneManager.GetActiveScene()))
            {
                return;
            }

            GUI.color = Color.white;

            if (!_networkManager.IsListening)
            {
                DrawGameplayFallbackMenu();
                return;
            }

            if ((_showPauseMenu || RequiresRoleSelection()) && _gameplayMenuRoot == null)
            {
                DrawPauseMenu();
            }
        }

        public bool BeginHostFromMenu(string displayName, string address, string portText, RetrowaveMatchSettings matchSettings, out string message)
        {
            _preferredDisplayName = NormalizeDisplayName(displayName);
            _currentMatchSettings = matchSettings;
            RetrowaveArenaConfig.ApplyMatchSettings(matchSettings);
            return BeginConnectionFromMenu(PendingConnectionMode.Host, address, portText, out message);
        }

        public bool BeginClientFromMenu(string displayName, string address, string portText, out string message)
        {
            _preferredDisplayName = NormalizeDisplayName(displayName);
            _currentMatchSettings = RetrowaveMatchSettings.Default;
            RetrowaveArenaConfig.ApplyMatchSettings(_currentMatchSettings);
            return BeginConnectionFromMenu(PendingConnectionMode.Client, address, portText, out message);
        }

        public void ReturnToMainMenu()
        {
            _showPauseMenu = false;
            _showScoreboard = false;
            ClearGoalCelebrationState();
            ShutdownSession();

            if (SceneManager.GetActiveScene().name != MainMenuSceneName)
            {
                SceneManager.LoadScene(MainMenuSceneName, LoadSceneMode.Single);
            }
        }

        public void BeginGoalCelebration(RetrowaveTeam scoringTeam, string scorerName, int blueScore, int pinkScore, float durationSeconds)
        {
            _goalCelebrationVisible = true;
            _goalCelebrationTeam = scoringTeam;
            _goalCelebrationScorer = string.IsNullOrWhiteSpace(scorerName)
                ? (scoringTeam == RetrowaveTeam.Blue ? "Blue Team" : "Pink Team")
                : scorerName;
            _goalCelebrationBlueScore = blueScore;
            _goalCelebrationPinkScore = pinkScore;
            _goalCelebrationEndsAtRealtime = Time.unscaledTime + Mathf.Max(0.25f, durationSeconds);
            SetLocalTimeScale(0.2f);
        }

        private void EnsureNetworkRuntime()
        {
            if (_networkManager != null)
            {
                return;
            }

            _networkRuntimeRoot = new GameObject("Retrowave Net Runtime");
            DontDestroyOnLoad(_networkRuntimeRoot);

            _networkManager = _networkRuntimeRoot.AddComponent<NetworkManager>();
            _transport = _networkRuntimeRoot.AddComponent<UnityTransport>();
            _networkManager.NetworkConfig ??= new NetworkConfig();
            _networkManager.NetworkConfig.NetworkTransport = _transport;
            _networkManager.NetworkConfig.EnableSceneManagement = false;
            _networkManager.NetworkConfig.ConnectionApproval = false;
            _networkManager.NetworkConfig.SpawnTimeout = 5f;
            _networkManager.NetworkConfig.TickRate = 60;
            _networkManager.NetworkConfig.EnsureNetworkVariableLengthSafety = true;

            if (_networkManager.NetworkConfig.Prefabs == null)
            {
                _networkManager.NetworkConfig.Prefabs = new NetworkPrefabs();
            }

            BuildRuntimePrefabs();
            RegisterNetworkPrefab(_playerPrefab);
            RegisterNetworkPrefab(_ballPrefab);
            RegisterNetworkPrefab(_powerUpPrefab);
            RegisterNetworkPrefab(_matchManagerPrefab);
            RegisterRuntimePrefabHandler(_playerPrefab, CreatePlayerInstance);
            RegisterRuntimePrefabHandler(_ballPrefab, CreateBallInstance);
            RegisterRuntimePrefabHandler(_powerUpPrefab, CreatePowerUpInstance);
            RegisterRuntimePrefabHandler(_matchManagerPrefab, CreateMatchManagerInstance);
            _networkManager.NetworkConfig.PlayerPrefab = null;

            _networkManager.OnServerStarted += HandleServerStarted;
            _networkManager.OnClientConnectedCallback += HandleNetworkClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleNetworkClientDisconnected;
        }

        private bool BeginConnectionFromMenu(PendingConnectionMode mode, string address, string portText, out string message)
        {
            if (_networkManager == null)
            {
                message = "Network bootstrap is not ready yet.";
                return false;
            }

            if (_networkManager.IsListening || _pendingConnectionMode != PendingConnectionMode.None)
            {
                message = "A connection is already active or starting.";
                return false;
            }

            if (!TryParsePort(portText, out var port))
            {
                message = "Port must be a number between 1 and 65535.";
                return false;
            }

            var trimmedAddress = string.IsNullOrWhiteSpace(address) ? string.Empty : address.Trim();
            var shouldResolveHostAddress = mode == PendingConnectionMode.Host && IsLoopbackAddress(trimmedAddress);
            _address = string.IsNullOrWhiteSpace(trimmedAddress) || shouldResolveHostAddress
                ? ResolvePreferredAddress()
                : trimmedAddress;
            _port = port;
            _pendingConnectionMode = mode;
            message = mode == PendingConnectionMode.Host
                ? $"Loading arena and starting host on {GetJoinAddressForDisplay()}:{_port}..."
                : $"Loading arena and joining {_address}:{_port}...";
            StartCoroutine(LoadGameplayAndConnect());
            return true;
        }

        private void BuildRuntimePrefabs()
        {
            if (_playerPrefab != null)
            {
                return;
            }

            _playerPrefab = CreatePlayerPrefab();
            _ballPrefab = CreateBallPrefab();
            _powerUpPrefab = CreatePowerUpPrefab();
            _matchManagerPrefab = CreateMatchManagerPrefab();
        }

        private void RegisterNetworkPrefab(GameObject prefab)
        {
            if (_networkManager.NetworkConfig.Prefabs.Contains(prefab))
            {
                return;
            }

            _networkManager.AddNetworkPrefab(prefab);
        }

        private void RegisterRuntimePrefabHandler(GameObject prefab, Func<GameObject> factory)
        {
            if (prefab == null || factory == null)
            {
                return;
            }

            _networkManager.PrefabHandler.AddHandler(prefab, new RuntimePrefabHandler(factory));
        }

        private void HandleServerStarted()
        {
            if (!_networkManager.IsServer)
            {
                return;
            }

            EnsureServerSessionStateForClient(NetworkManager.ServerClientId);

            foreach (var clientPair in _networkManager.ConnectedClients)
            {
                EnsureServerSessionStateForClient(clientPair.Key);
            }
        }

        private void HandleNetworkClientConnected(ulong clientId)
        {
            if (_networkManager == null || !_networkManager.IsServer)
            {
                return;
            }

            EnsureServerSessionStateForClient(clientId);
        }

        private void HandleNetworkClientDisconnected(ulong clientId)
        {
            if (_networkManager == null
                || _sessionShutdownInProgress
                || _networkManager.IsServer
                || clientId != _networkManager.LocalClientId)
            {
                return;
            }

            StartCoroutine(ReturnToMainMenuAfterRemoteShutdown());
        }

        private IEnumerator ReturnToMainMenuAfterRemoteShutdown()
        {
            yield return null;
            ReturnToMainMenu();
        }

        private void EnsureServerSessionStateForClient(ulong clientId)
        {
            if (_networkManager == null || !_networkManager.IsServer || !_networkManager.IsListening)
            {
                return;
            }

            EnsureServerMatchManagerExists();
            EnsureServerPlayerObjectExists(clientId);
        }

        private void ReconcileServerSessionState()
        {
            if (_networkManager == null || !_networkManager.IsServer || !_networkManager.IsListening)
            {
                return;
            }

            EnsureServerMatchManagerExists();

            foreach (var clientPair in _networkManager.ConnectedClients)
            {
                EnsureServerPlayerObjectExists(clientPair.Key);
            }
        }

        private void EnsureServerMatchManagerExists()
        {
            if (GetActiveMatchManager() != null)
            {
                return;
            }

            var matchManager = CreateMatchManagerInstance();
            matchManager.name = "Retrowave Match Manager";
            MoveRuntimeInstanceToGameplayScene(matchManager);
            matchManager.GetComponent<NetworkObject>().Spawn();
        }

        private void EnsureServerPlayerObjectExists(ulong clientId)
        {
            if (!_networkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null)
            {
                return;
            }

            var playerObject = CreatePlayerInstance();
            playerObject.name = $"Player {clientId}";
            MoveRuntimeInstanceToGameplayScene(playerObject);
            playerObject.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }

        public GameObject CreatePlayerInstance()
        {
            return CreatePlayerPrefab(false);
        }

        public GameObject CreateBallInstance()
        {
            return CreateBallPrefab(false);
        }

        public GameObject CreatePowerUpInstance()
        {
            return CreatePowerUpPrefab(false);
        }

        public GameObject CreateMatchManagerInstance()
        {
            return CreateMatchManagerPrefab(false);
        }

        public void MoveRuntimeInstanceToGameplayScene(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            var gameplayScene = SceneManager.GetSceneByName(GameplaySceneName);

            if (!gameplayScene.IsValid() || !gameplayScene.isLoaded)
            {
                gameplayScene = SceneManager.GetActiveScene();
            }

            if (!gameplayScene.IsValid() || !gameplayScene.isLoaded || instance.scene == gameplayScene)
            {
                return;
            }

            SceneManager.MoveGameObjectToScene(instance, gameplayScene);
        }

        private void DrawHud()
        {
            GUILayout.BeginArea(new Rect(18f, 14f, 420f, 190f), GUI.skin.box);

            var status = _networkManager.IsHost ? "Host" : (_networkManager.IsServer ? "Server" : "Client");
            GUILayout.Label($"{status} connected on {_address}:{_port}");
            GUILayout.Label("Esc: match menu    Tab: scoreboard");

            var matchManager = GetActiveMatchManager();

            if (matchManager != null)
            {
                var phaseLabel = matchManager.IsWarmup ? "Warmup / Practice" : "Live Match";
                GUILayout.Label($"{phaseLabel}    Blue {matchManager.BlueScore} : {matchManager.PinkScore} Pink");
                GUILayout.Label(matchManager.IsWarmup
                    ? $"Round timer preset: {FormatRoundDuration(matchManager.RoundDurationSeconds)} x {matchManager.RoundCount}    Max players: {matchManager.MaxPlayers}"
                    : $"Round {Mathf.Max(1, matchManager.CurrentRoundNumber)}/{matchManager.RoundCount}    {FormatRoundClock(matchManager.RoundTimeRemaining)} remaining");

                if (TryGetLocalLobbyEntry(out var entry))
                {
                    GUILayout.Label($"Role: {GetRoleLabel(entry)}");

                    if (!entry.HasSelectedRole)
                    {
                        GUILayout.Label("Choose a team or spectate to enter the lobby.");
                    }
                    else if (entry.QueuedForNextRound)
                    {
                        GUILayout.Label("Team change queued. You will enter on the next round.");
                    }
                    else if (entry.IsHost && matchManager.IsWarmup)
                    {
                        GUILayout.Label(matchManager.CanStartMatch
                            ? "Both teams are ready. Open Esc to start the match."
                            : "Warm up now. Match start unlocks once blue and pink both have a player.");
                    }
                }
            }
            else
            {
                GUILayout.Label("Waiting for match manager...");

                if (RetrowavePlayerController.LocalPlayer != null)
                {
                    GUILayout.Label($"Role: {GetRoleLabel(RetrowavePlayerController.LocalPlayer.LobbyRole, RetrowavePlayerController.LocalPlayer.HasSelectedRole)}");

                    if (!RetrowavePlayerController.LocalPlayer.HasSelectedRole)
                    {
                        GUILayout.Label("Choose a team or spectate to enter the lobby.");
                    }
                }
            }

            if (RetrowavePlayerController.LocalOwner != null)
            {
                var local = RetrowavePlayerController.LocalOwner;
                GUILayout.Label($"Driving: {local.Team}");
                GUILayout.Label($"Boost: {local.BoostNormalized * 100f:0}%");

                if (local.HasSpeedBoost)
                {
                    GUILayout.Label("Speed Burst Active");
                }
            }
            else
            {
                GUILayout.Label(RetrowaveCameraRig.GetSpectatorCameraLabel());

                if (CanCycleWarmupSpectatorTargets())
                {
                    GUILayout.Label("Warmup cam: [, ] or < > cycles player follow.");
                }
            }

            GUILayout.EndArea();
        }

        private void EnsureGameplayMenuOverlay()
        {
            if (_gameplayMenuRoot != null)
            {
                return;
            }

            EnsureGameplayEventSystem();
            _gameplayMenuFont = TMP_Settings.defaultFontAsset;

            _gameplayMenuRoot = new GameObject("Retrowave Gameplay Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _gameplayMenuRoot.transform.SetParent(transform, false);

            var canvas = _gameplayMenuRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var scaler = _gameplayMenuRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panel = CreateUiObject("Panel", _gameplayMenuRoot.transform, typeof(Image));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(700f, 560f);
            _gameplayMenuPanelRect = panelRect;

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.03f, 0.05f, 0.1f, 0.94f);

            var outline = panel.AddComponent<Outline>();
            outline.effectColor = new Color(0.09f, 0.75f, 1f, 0.8f);
            outline.effectDistance = new Vector2(2f, 2f);

            _gameplayMenuTitleText = CreateMenuText(panel.transform, "Title", 32, FontStyles.Bold, new Vector2(0f, 220f), new Vector2(580f, 48f), Color.white);
            _gameplayMenuBodyText = CreateMenuText(panel.transform, "Body", 18, FontStyles.Normal, new Vector2(0f, 172f), new Vector2(590f, 72f), new Color(0.88f, 0.92f, 0.97f, 1f));
            _gameplayMenuHintText = CreateMenuText(panel.transform, "Hint", 16, FontStyles.Normal, new Vector2(0f, 122f), new Vector2(590f, 44f), new Color(0.57f, 0.86f, 1f, 1f));
            _gameplayMenuFooterText = CreateMenuText(panel.transform, "Footer", 15, FontStyles.Normal, new Vector2(0f, -202f), new Vector2(590f, 52f), new Color(0.92f, 0.88f, 0.96f, 0.95f));

            _gameplayBlueButton = CreateMenuButton(panel.transform, "BlueButton", "Join Blue Team", new Vector2(0f, 48f), new Color(0.07f, 0.44f, 0.93f, 1f), () => SelectGameplayRole(RetrowaveLobbyRole.Blue));
            _gameplayPinkButton = CreateMenuButton(panel.transform, "PinkButton", "Join Pink Team", new Vector2(0f, -20f), RetrowaveStyle.PinkBase, () => SelectGameplayRole(RetrowaveLobbyRole.Pink));
            _gameplaySpectateButton = CreateMenuButton(panel.transform, "SpectateButton", "Spectate", new Vector2(0f, -88f), new Color(0.34f, 0.18f, 0.46f, 1f), () => SelectGameplayRole(RetrowaveLobbyRole.Spectator));
            _gameplayStartButton = CreateMenuButton(panel.transform, "StartButton", "Start Match", new Vector2(0f, -164f), new Color(0.14f, 0.64f, 0.42f, 1f), HandleGameplayStartMatch);
            _gameplayStartButtonLabel = _gameplayStartButton.GetComponentInChildren<TextMeshProUGUI>(true);
            _gameplayResumeButton = CreateMenuButton(panel.transform, "ResumeButton", "Resume", new Vector2(-132f, -242f), new Color(0.12f, 0.24f, 0.36f, 1f), HandleGameplayResume);
            _gameplayReturnButton = CreateMenuButton(panel.transform, "ReturnButton", "Return To Main Menu", new Vector2(132f, -242f), new Color(0.26f, 0.12f, 0.16f, 1f), ReturnToMainMenu);
            _gameplayReturnButtonLabel = _gameplayReturnButton.GetComponentInChildren<TextMeshProUGUI>(true);

            SetGameplayMenuVisible(false);
        }

        private void DestroyGameplayMenuOverlay()
        {
            if (_gameplayMenuRoot != null)
            {
                Destroy(_gameplayMenuRoot);
                _gameplayMenuRoot = null;
                _gameplayMenuPanelRect = null;
            }

            if (_gameplayMenuEventSystem != null)
            {
                Destroy(_gameplayMenuEventSystem);
                _gameplayMenuEventSystem = null;
            }

            _gameplayMenuTitleText = null;
            _gameplayMenuBodyText = null;
            _gameplayMenuHintText = null;
            _gameplayMenuFooterText = null;
            _gameplayBlueButton = null;
            _gameplayPinkButton = null;
            _gameplaySpectateButton = null;
            _gameplayResumeButton = null;
            _gameplayStartButton = null;
            _gameplayReturnButton = null;
            _gameplayStartButtonLabel = null;
            _gameplayReturnButtonLabel = null;
            _gameplayMenuWasVisible = false;
        }

        private void EnsureGameplayEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                _gameplayMenuEventSystem = new GameObject("Gameplay EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                _gameplayMenuEventSystem.transform.SetParent(transform, false);
                _gameplayMenuEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                return;
            }

            var legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            var inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();

            if (inputSystemModule == null)
            {
                inputSystemModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                inputSystemModule.AssignDefaultActions();
            }

            if (legacyModule != null)
            {
                Destroy(legacyModule);
            }
        }

        private void RefreshGameplayMenuState()
        {
            if (_gameplayMenuRoot == null)
            {
                return;
            }

            var sessionBootstrapPending = RequiresSessionBootstrap();
            var forceSelection = RequiresRoleSelection();
            var wasVisible = _gameplayMenuWasVisible;
            var matchManager = GetActiveMatchManager();
            var podiumActive = matchManager != null && matchManager.IsPodium;
            var matchComplete = matchManager != null && matchManager.IsMatchComplete;

            if (podiumActive)
            {
                _showPauseMenu = false;
            }

            var isVisible = _networkManager != null
                            && _networkManager.IsListening
                            && IsGameplayScene(SceneManager.GetActiveScene())
                            && !podiumActive
                            && (_showPauseMenu || forceSelection || sessionBootstrapPending || matchComplete);

            SetGameplayMenuVisible(isVisible);
            _gameplayMenuWasVisible = isVisible;

            if (!isVisible)
            {
                return;
            }

            if (sessionBootstrapPending)
            {
                _gameplayMenuTitleText.text = "Joining Server";
                _gameplayMenuBodyText.text = "Waiting for the host to finish syncing the lobby and your player object.";
                _gameplayMenuHintText.text = "This should resolve automatically once the server finishes session setup.";
            }
            else if (matchComplete && matchManager != null)
            {
                _gameplayMenuTitleText.text = "Final Score";
                _gameplayMenuBodyText.text = BuildFinalScoreSummary(matchManager);
                _gameplayMenuHintText.text = "Change teams for the next run, spectate, or leave the lobby.";
            }
            else
            {
                _gameplayMenuTitleText.text = forceSelection ? "Choose Your Role" : "Match Menu";
                _gameplayMenuBodyText.text = forceSelection
                    ? "Pick blue, pink, or spectator before jumping fully into the lobby."
                    : "Swap teams, spectate, or control the match flow from here.";
                _gameplayMenuHintText.text = forceSelection
                    ? "Keyboard also works: 1 / B = Blue, 2 / P = Pink, 3 / S = Spectator"
                    : "Esc closes this menu after you've chosen a role.";
            }

            var hostCanStart = false;
            var hostIsPresent = false;
            var localEntry = default(RetrowaveLobbyEntry);
            var hasLocalEntry = matchManager != null && TryGetLocalLobbyEntry(out localEntry);

            if (hasLocalEntry)
            {
                hostIsPresent = localEntry.IsHost;
                hostCanStart = localEntry.IsHost && matchManager.CanStartMatch;
            }

            _gameplayStartButton.gameObject.SetActive(hostIsPresent);
            _gameplayResumeButton.gameObject.SetActive(!forceSelection && !sessionBootstrapPending && !matchComplete);
            ApplyGameplayMenuLayout(matchComplete);

            if (_gameplayStartButtonLabel != null && matchManager != null)
            {
                _gameplayStartButtonLabel.text = matchComplete ? "Start New Game" : (matchManager.IsWarmup ? "Start Match" : "Restart Match");
            }

            if (_gameplayReturnButtonLabel != null)
            {
                _gameplayReturnButtonLabel.text = matchComplete && hostIsPresent ? "Exit Game" : "Return To Main Menu";
            }

            _gameplayStartButton.interactable = hostCanStart;
            var canSubmitRoleSelection = !sessionBootstrapPending && (RetrowavePlayerController.LocalPlayer != null || matchManager != null);
            _gameplayBlueButton.interactable = canSubmitRoleSelection;
            _gameplayPinkButton.interactable = canSubmitRoleSelection;
            _gameplaySpectateButton.interactable = canSubmitRoleSelection;

            if (sessionBootstrapPending)
            {
                _gameplayMenuFooterText.text = "Connected to the host. Waiting for the multiplayer session to finish initializing.";
            }
            else if (matchComplete && hostIsPresent)
            {
                _gameplayMenuFooterText.text = hostCanStart
                    ? "Start New Game begins a fresh kickoff. Exit Game closes the network session."
                    : "Need at least one blue and one pink player before starting again.";
            }
            else if (matchComplete)
            {
                _gameplayMenuFooterText.text = "The match has ended. Change teams for the next game or leave the lobby.";
            }
            else if (forceSelection)
            {
                _gameplayMenuFooterText.text = "Use the buttons above to enter the arena.";
            }
            else if (hasLocalEntry && localEntry.QueuedForNextRound)
            {
                _gameplayMenuFooterText.text = "Your new team selection is queued for the next round.";
            }
            else if (hostIsPresent && !hostCanStart && matchManager != null)
            {
                _gameplayMenuFooterText.text = "Host start unlocks once at least one player is on blue and pink.";
            }
            else
            {
                _gameplayMenuFooterText.text = matchManager != null && matchManager.IsWarmup && CanCycleWarmupSpectatorTargets()
                    ? "Warmup spectators can cycle player follow cams with [ and ]."
                    : "Team changes apply immediately for this client.";
            }

            if (!wasVisible)
            {
                var defaultButton = forceSelection ? _gameplayBlueButton : (_gameplayResumeButton.gameObject.activeSelf ? _gameplayResumeButton : _gameplayBlueButton);
                var eventSystem = FindFirstObjectByType<EventSystem>();

                if (eventSystem != null && defaultButton != null)
                {
                    eventSystem.SetSelectedGameObject(defaultButton.gameObject);
                }
            }
        }

        private void SetGameplayMenuVisible(bool isVisible)
        {
            if (_gameplayMenuRoot == null)
            {
                return;
            }

            _gameplayMenuRoot.SetActive(isVisible);
        }

        private void EnsureGameplayHudOverlay()
        {
            if (_gameplayHudRoot != null)
            {
                return;
            }

            _gameplayMenuFont = TMP_Settings.defaultFontAsset;

            _gameplayHudRoot = new GameObject("Retrowave Gameplay HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _gameplayHudRoot.transform.SetParent(transform, false);

            var canvas = _gameplayHudRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 160;

            var scaler = _gameplayHudRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var scoreStrip = CreateHudPanel(
                _gameplayHudRoot.transform,
                "ScoreStrip",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -26f),
                new Vector2(560f, 108f),
                new Color(0.03f, 0.06f, 0.12f, 0.9f),
                new Color(0.15f, 0.85f, 1f, 0.45f));

            _hudScoreStateText = CreateHudText(
                scoreStrip.transform,
                "State",
                "WARMUP",
                16f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -14f),
                new Vector2(360f, 26f),
                new Color(0.6f, 0.91f, 1f, 1f));

            _hudBlueScoreText = CreateHudText(
                scoreStrip.transform,
                "BlueScore",
                "0",
                40f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-176f, -6f),
                new Vector2(110f, 56f),
                RetrowaveStyle.BlueGlow);

            _hudScoreClockText = CreateHudText(
                scoreStrip.transform,
                "Clock",
                "0:00",
                30f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -6f),
                new Vector2(220f, 50f),
                Color.white);

            _hudPinkScoreText = CreateHudText(
                scoreStrip.transform,
                "PinkScore",
                "0",
                40f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(176f, -6f),
                new Vector2(110f, 56f),
                RetrowaveStyle.PinkBase);

            _gameplayHudInfoRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "InfoPanel",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -36f),
                new Vector2(540f, 312f),
                new Color(0.04f, 0.08f, 0.14f, 0.88f),
                new Color(0.11f, 0.74f, 0.95f, 0.36f));

            CreateHudText(
                _gameplayHudInfoRoot.transform,
                "InfoHeader",
                "MATCH STATUS",
                22f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -16f),
                new Vector2(270f, 34f),
                Color.white);

            _hudInfoToggleText = CreateHudText(
                _gameplayHudInfoRoot.transform,
                "InfoToggle",
                "H HIDE",
                16f,
                FontStyles.Bold,
                TextAlignmentOptions.Right,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -18f),
                new Vector2(120f, 24f),
                new Color(0.56f, 0.88f, 1f, 0.95f));

            _hudInfoConnectionText = CreateHudText(
                _gameplayHudInfoRoot.transform,
                "Connection",
                string.Empty,
                18f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -58f),
                new Vector2(482f, 36f),
                new Color(0.86f, 0.92f, 0.98f, 1f));

            _hudInfoPhaseText = CreateHudText(
                _gameplayHudInfoRoot.transform,
                "Phase",
                string.Empty,
                19f,
                FontStyles.Bold,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -104f),
                new Vector2(482f, 48f),
                Color.white);

            _hudInfoRoleText = CreateHudText(
                _gameplayHudInfoRoot.transform,
                "Role",
                string.Empty,
                18f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -164f),
                new Vector2(482f, 72f),
                new Color(0.82f, 0.89f, 1f, 1f));

            _hudInfoHintText = CreateHudText(
                _gameplayHudInfoRoot.transform,
                "Hint",
                string.Empty,
                16f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -250f),
                new Vector2(482f, 50f),
                new Color(0.58f, 0.86f, 1f, 0.95f));

            _gameplayHudInfoCollapsedRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "InfoCollapsed",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -36f),
                new Vector2(230f, 54f),
                new Color(0.03f, 0.07f, 0.13f, 0.82f),
                new Color(0.11f, 0.74f, 0.95f, 0.28f));

            _hudInfoCollapsedText = CreateHudText(
                _gameplayHudInfoCollapsedRoot.transform,
                "CollapsedText",
                "H show match info",
                16f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(180f, 28f),
                new Color(0.72f, 0.92f, 1f, 1f));

            var gaugesPanel = CreateHudPanel(
                _gameplayHudRoot.transform,
                "GaugesPanel",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-28f, 28f),
                new Vector2(430f, 196f),
                new Color(0.03f, 0.05f, 0.11f, 0.9f),
                new Color(0.96f, 0.34f, 0.74f, 0.28f));

            _hudGaugeTitleText = CreateHudText(
                gaugesPanel.transform,
                "GaugeTitle",
                "DRIVE",
                18f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -18f),
                new Vector2(210f, 28f),
                Color.white);

            _hudGaugeStatusText = CreateHudText(
                gaugesPanel.transform,
                "GaugeStatus",
                string.Empty,
                15f,
                FontStyles.Normal,
                TextAlignmentOptions.Right,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -18f),
                new Vector2(170f, 24f),
                new Color(0.57f, 0.86f, 1f, 0.95f));

            _hudSpeedValueText = CreateHudText(
                gaugesPanel.transform,
                "SpeedValue",
                "0.0",
                44f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -58f),
                new Vector2(180f, 52f),
                Color.white);

            _hudSpeedLabelText = CreateHudText(
                gaugesPanel.transform,
                "SpeedLabel",
                "SPEED",
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -102f),
                new Vector2(120f, 22f),
                new Color(0.62f, 0.9f, 1f, 0.88f));

            CreateHudBar(
                gaugesPanel.transform,
                "SpeedBar",
                new Vector2(24f, -130f),
                new Vector2(382f, 18f),
                new Color(0.08f, 0.12f, 0.18f, 0.95f),
                new Color(0.96f, 0.34f, 0.74f, 1f),
                out _hudSpeedFillImage,
                out _hudSpeedBarRect,
                out _hudSpeedMarkerRect);

            _hudBoostValueText = CreateHudText(
                gaugesPanel.transform,
                "BoostValue",
                "0%",
                26f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -146f),
                new Vector2(140f, 34f),
                new Color(1f, 0.9f, 0.96f, 1f));

            CreateHudText(
                gaugesPanel.transform,
                "BoostLabel",
                "BOOST",
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(148f, -148f),
                new Vector2(110f, 24f),
                new Color(0.62f, 0.9f, 1f, 0.88f));

            CreateHudBar(
                gaugesPanel.transform,
                "BoostBar",
                new Vector2(24f, -172f),
                new Vector2(382f, 14f),
                new Color(0.08f, 0.12f, 0.18f, 0.95f),
                new Color(0.11f, 0.87f, 1f, 1f),
                out _hudBoostFillImage,
                out _hudBoostBarRect,
                out _hudBoostMarkerRect);

            _gameplayHudScoreboardRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "Scoreboard",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -148f),
                new Vector2(1360f, 620f),
                new Color(0.02f, 0.04f, 0.09f, 0.95f),
                new Color(0.12f, 0.8f, 1f, 0.34f));

            _hudScoreboardTitleText = CreateHudText(
                _gameplayHudScoreboardRoot.transform,
                "Title",
                "Lobby Scoreboard",
                28f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -26f),
                new Vector2(700f, 36f),
                Color.white);

            _hudScoreboardSummaryText = CreateHudText(
                _gameplayHudScoreboardRoot.transform,
                "Summary",
                string.Empty,
                16f,
                FontStyles.Normal,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -64f),
                new Vector2(900f, 26f),
                new Color(0.63f, 0.88f, 1f, 0.94f));

            _hudScoreboardBlueText = CreateScoreboardSection(
                _gameplayHudScoreboardRoot.transform,
                "BlueSection",
                new Vector2(-430f, -118f),
                new Vector2(392f, 452f),
                "BLUE TEAM",
                RetrowaveStyle.BlueGlow);

            _hudScoreboardPinkText = CreateScoreboardSection(
                _gameplayHudScoreboardRoot.transform,
                "PinkSection",
                new Vector2(0f, -118f),
                new Vector2(392f, 452f),
                "PINK TEAM",
                RetrowaveStyle.PinkBase);

            _hudScoreboardSpectatorText = CreateScoreboardSection(
                _gameplayHudScoreboardRoot.transform,
                "SpectatorSection",
                new Vector2(430f, -118f),
                new Vector2(392f, 452f),
                "SPECTATORS",
                new Color(0.8f, 0.62f, 1f, 1f));

            _gameplayHudGoalRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "GoalBanner",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -126f),
                new Vector2(760f, 170f),
                new Color(0.03f, 0.03f, 0.08f, 0.95f),
                new Color(1f, 0.42f, 0.72f, 0.32f));

            _hudGoalHeadlineText = CreateHudText(
                _gameplayHudGoalRoot.transform,
                "GoalHeadline",
                string.Empty,
                34f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -26f),
                new Vector2(620f, 44f),
                Color.white);

            _hudGoalScoreText = CreateHudText(
                _gameplayHudGoalRoot.transform,
                "GoalScore",
                string.Empty,
                26f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -4f),
                new Vector2(520f, 36f),
                Color.white);

            _hudGoalDetailText = CreateHudText(
                _gameplayHudGoalRoot.transform,
                "GoalDetail",
                string.Empty,
                18f,
                FontStyles.Normal,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 22f),
                new Vector2(620f, 46f),
                new Color(0.93f, 0.95f, 1f, 0.96f));

            _gameplayHudCountdownRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "KickoffCountdown",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 68f),
                new Vector2(470f, 270f),
                new Color(0.02f, 0.04f, 0.09f, 0.92f),
                new Color(0.96f, 0.34f, 0.74f, 0.4f));

            _hudCountdownLabelText = CreateHudText(
                _gameplayHudCountdownRoot.transform,
                "Label",
                string.Empty,
                24f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -34f),
                new Vector2(380f, 36f),
                new Color(0.66f, 0.91f, 1f, 1f));

            _hudCountdownValueText = CreateHudText(
                _gameplayHudCountdownRoot.transform,
                "Value",
                string.Empty,
                112f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -20f),
                new Vector2(380f, 132f),
                Color.white);

            _gameplayHudInfoCollapsedRoot.SetActive(false);
            _gameplayHudScoreboardRoot.SetActive(false);
            _gameplayHudGoalRoot.SetActive(false);
            _gameplayHudCountdownRoot.SetActive(false);
        }

        private void DestroyGameplayHudOverlay()
        {
            if (_gameplayHudRoot != null)
            {
                Destroy(_gameplayHudRoot);
                _gameplayHudRoot = null;
            }

            _gameplayHudInfoRoot = null;
            _gameplayHudInfoCollapsedRoot = null;
            _gameplayHudScoreboardRoot = null;
            _gameplayHudGoalRoot = null;
            _gameplayHudCountdownRoot = null;
            _hudScoreStateText = null;
            _hudScoreClockText = null;
            _hudBlueScoreText = null;
            _hudPinkScoreText = null;
            _hudInfoToggleText = null;
            _hudInfoConnectionText = null;
            _hudInfoPhaseText = null;
            _hudInfoRoleText = null;
            _hudInfoHintText = null;
            _hudInfoCollapsedText = null;
            _hudGaugeTitleText = null;
            _hudSpeedValueText = null;
            _hudSpeedLabelText = null;
            _hudBoostValueText = null;
            _hudGaugeStatusText = null;
            _hudSpeedFillImage = null;
            _hudBoostFillImage = null;
            _hudSpeedBarRect = null;
            _hudBoostBarRect = null;
            _hudSpeedMarkerRect = null;
            _hudBoostMarkerRect = null;
            _hudScoreboardTitleText = null;
            _hudScoreboardSummaryText = null;
            _hudScoreboardBlueText = null;
            _hudScoreboardPinkText = null;
            _hudScoreboardSpectatorText = null;
            _hudGoalHeadlineText = null;
            _hudGoalScoreText = null;
            _hudGoalDetailText = null;
            _hudCountdownLabelText = null;
            _hudCountdownValueText = null;
        }

        private void RefreshGameplayHudState()
        {
            if (_gameplayHudRoot == null)
            {
                return;
            }

            var isVisible = _networkManager != null
                            && _networkManager.IsListening
                            && IsGameplayScene(SceneManager.GetActiveScene());
            _gameplayHudRoot.SetActive(isVisible);

            if (!isVisible)
            {
                ResetHudInfoIntroState();
                return;
            }

            if (!_gameplayHudSessionVisible)
            {
                _gameplayHudSessionVisible = true;
                _showHudInfoPanel = true;
                _hudInfoIntroAutoHidePending = true;
                _hudInfoIntroHideAtRealtime = Time.unscaledTime + 5f;
            }

            if (_hudInfoIntroAutoHidePending && Time.unscaledTime >= _hudInfoIntroHideAtRealtime)
            {
                _hudInfoIntroAutoHidePending = false;
                _showHudInfoPanel = false;
            }

            var matchManager = GetActiveMatchManager();
            var localPlayer = RetrowavePlayerController.LocalOwner;
            var localAddress = _networkManager.IsClient && !_networkManager.IsHost ? _address : GetJoinAddressForDisplay();
            var status = _networkManager.IsHost ? "HOST" : (_networkManager.IsServer ? "SERVER" : "CLIENT");

            if (matchManager != null && matchManager.IsPodium)
            {
                EnsurePodiumPresentation(matchManager);
            }
            else
            {
                ClearPodiumPresentation();
            }

            if (_gameplayHudInfoRoot != null)
            {
                _gameplayHudInfoRoot.SetActive(_showHudInfoPanel);
            }

            if (_gameplayHudInfoCollapsedRoot != null)
            {
                _gameplayHudInfoCollapsedRoot.SetActive(!_showHudInfoPanel);
            }

            if (_hudInfoConnectionText != null)
            {
                _hudInfoConnectionText.text = $"{status}  {localAddress}:{_port}";
            }

            if (_hudInfoToggleText != null)
            {
                _hudInfoToggleText.text = "H HIDE";
            }

            if (_hudInfoCollapsedText != null)
            {
                _hudInfoCollapsedText.text = "H show match info";
            }

            if (matchManager != null)
            {
                if (_hudScoreStateText != null)
                {
                    _hudScoreStateText.text = matchManager.Phase switch
                    {
                        RetrowaveMatchPhase.Warmup => "WARMUP LOBBY",
                        RetrowaveMatchPhase.Podium => matchManager.HasPodiumWinner ? "WINNERS PODIUM" : "DRAW SHOWCASE",
                        RetrowaveMatchPhase.MatchComplete => "FINAL SCORE",
                        RetrowaveMatchPhase.Countdown => "KICKOFF COUNTDOWN",
                        _ => $"ROUND {Mathf.Max(1, matchManager.CurrentRoundNumber)}/{matchManager.RoundCount}",
                    };
                }

                if (_hudScoreClockText != null)
                {
                    _hudScoreClockText.text = matchManager.Phase switch
                    {
                        RetrowaveMatchPhase.Warmup => $"{FormatRoundDuration(matchManager.RoundDurationSeconds)} x {matchManager.RoundCount}",
                        RetrowaveMatchPhase.Podium => matchManager.HasPodiumWinner
                            ? $"{GetTeamLabel(matchManager.PodiumWinnerTeam).ToUpperInvariant()} WINS"
                            : "SHOWCASE",
                        RetrowaveMatchPhase.MatchComplete => "COMPLETE",
                        RetrowaveMatchPhase.Countdown => Mathf.CeilToInt(matchManager.CountdownTimeRemaining).ToString(),
                        _ => FormatRoundClock(matchManager.RoundTimeRemaining),
                    };
                }

                if (_hudBlueScoreText != null)
                {
                    _hudBlueScoreText.text = matchManager.BlueScore.ToString();
                }

                if (_hudPinkScoreText != null)
                {
                    _hudPinkScoreText.text = matchManager.PinkScore.ToString();
                }

                if (_hudInfoPhaseText != null)
                {
                    _hudInfoPhaseText.text = matchManager.Phase switch
                    {
                        RetrowaveMatchPhase.Warmup => $"Warmup live. {FormatRoundDuration(matchManager.RoundDurationSeconds)} x {matchManager.RoundCount} rounds = {FormatRoundDuration(matchManager.RoundDurationSeconds * matchManager.RoundCount)} match. Max players {matchManager.MaxPlayers}.",
                        RetrowaveMatchPhase.Podium => matchManager.HasPodiumWinner
                            ? $"{GetTeamLabel(matchManager.PodiumWinnerTeam)} podium ceremony. Final menu opens in {Mathf.CeilToInt(matchManager.PodiumTimeRemaining)}."
                            : $"Draw showcase. Final menu opens in {Mathf.CeilToInt(matchManager.PodiumTimeRemaining)}.",
                        RetrowaveMatchPhase.MatchComplete => $"Match complete. Final score: Blue {matchManager.BlueScore} - {matchManager.PinkScore} Pink.",
                        RetrowaveMatchPhase.Countdown => $"Kickoff in {Mathf.CeilToInt(matchManager.CountdownTimeRemaining)}. Cars are locked until play resumes.",
                        _ => $"Live match. {FormatRoundClock(matchManager.RoundTimeRemaining)} left in round {Mathf.Max(1, matchManager.CurrentRoundNumber)}/{matchManager.RoundCount}.",
                    };
                }
            }
            else
            {
                if (_hudScoreStateText != null)
                {
                    _hudScoreStateText.text = "SYNCING MATCH";
                }

                if (_hudScoreClockText != null)
                {
                    _hudScoreClockText.text = "--:--";
                }

                if (_hudBlueScoreText != null)
                {
                    _hudBlueScoreText.text = "-";
                }

                if (_hudPinkScoreText != null)
                {
                    _hudPinkScoreText.text = "-";
                }

                if (_hudInfoPhaseText != null)
                {
                    _hudInfoPhaseText.text = "Waiting for the match manager to finish syncing.";
                }
            }

            if (_hudInfoRoleText != null)
            {
                if (TryGetLocalLobbyEntry(out var entry))
                {
                    var roleLine = $"Role: {GetRoleLabel(entry)}";

                    if (!entry.HasSelectedRole)
                    {
                        roleLine += "\nChoose blue, pink, or spectator to enter the lobby.";
                    }
                    else if (entry.QueuedForNextRound)
                    {
                        roleLine += "\nYour team switch is queued for the next round.";
                    }
                    else if (entry.IsHost && matchManager != null && matchManager.IsWarmup)
                    {
                        roleLine += matchManager.CanStartMatch
                            ? "\nBoth teams are ready. Open Esc to start."
                            : "\nMatch start unlocks once blue and pink both have a player.";
                    }

                    _hudInfoRoleText.text = roleLine;
                }
                else
                {
                    _hudInfoRoleText.text = localPlayer != null
                        ? $"Driving: {localPlayer.Team}"
                        : RetrowaveCameraRig.GetSpectatorCameraLabel();
                }
            }

            if (_hudInfoHintText != null)
            {
                var hint = "Esc match menu  •  Tab scoreboard  •  H hide panel";

                if (CanCycleWarmupSpectatorTargets())
                {
                    hint += "\nWarmup spectator cam: [ / ] cycles player follow.";
                }

                _hudInfoHintText.text = hint;
            }

            if (_hudGaugeTitleText != null)
            {
                _hudGaugeTitleText.text = localPlayer != null ? $"{localPlayer.Team.ToString().ToUpperInvariant()} DRIVER" : "SPECTATOR CAM";
            }

            if (localPlayer != null)
            {
                if (_hudSpeedValueText != null)
                {
                    _hudSpeedValueText.text = $"{localPlayer.CurrentSpeed:0.0}";
                }

                if (_hudSpeedLabelText != null)
                {
                    _hudSpeedLabelText.text = "SPEED";
                }

                if (_hudBoostValueText != null)
                {
                    _hudBoostValueText.text = $"{localPlayer.BoostAmount:0}%";
                }

                if (_hudGaugeStatusText != null)
                {
                    _hudGaugeStatusText.text = localPlayer.HasSpeedBoost
                        ? "Speed burst"
                        : (localPlayer.IsGroundedForHud ? "Grounded" : "Airborne");
                }

                if (_hudSpeedFillImage != null)
                {
                    _hudSpeedFillImage.fillAmount = localPlayer.SpeedNormalized;
                }

                if (_hudBoostFillImage != null)
                {
                    _hudBoostFillImage.fillAmount = localPlayer.BoostNormalized;
                }

                UpdateHudBarMarker(_hudSpeedBarRect, _hudSpeedMarkerRect, localPlayer.SpeedNormalized);
                UpdateHudBarMarker(_hudBoostBarRect, _hudBoostMarkerRect, localPlayer.BoostNormalized);
            }
            else
            {
                if (_hudSpeedValueText != null)
                {
                    _hudSpeedValueText.text = "--";
                }

                if (_hudSpeedLabelText != null)
                {
                    _hudSpeedLabelText.text = "CAM";
                }

                if (_hudBoostValueText != null)
                {
                    _hudBoostValueText.text = "--";
                }

                if (_hudGaugeStatusText != null)
                {
                    _hudGaugeStatusText.text = RetrowaveCameraRig.GetSpectatorCameraLabel();
                }

                if (_hudSpeedFillImage != null)
                {
                    _hudSpeedFillImage.fillAmount = 0f;
                }

                if (_hudBoostFillImage != null)
                {
                    _hudBoostFillImage.fillAmount = 0f;
                }

                UpdateHudBarMarker(_hudSpeedBarRect, _hudSpeedMarkerRect, 0f);
                UpdateHudBarMarker(_hudBoostBarRect, _hudBoostMarkerRect, 0f);
            }

            if (_gameplayHudScoreboardRoot != null)
            {
                _gameplayHudScoreboardRoot.SetActive(_showScoreboard && matchManager != null);
            }

            if (_showScoreboard && matchManager != null)
            {
                if (_hudScoreboardTitleText != null)
                {
                    _hudScoreboardTitleText.text = matchManager.IsMatchComplete
                        ? $"Final Score  •  Blue {matchManager.BlueScore} : {matchManager.PinkScore} Pink"
                        : (matchManager.IsPodium
                            ? (matchManager.HasPodiumWinner
                                ? $"{GetTeamLabel(matchManager.PodiumWinnerTeam)} Podium  •  Blue {matchManager.BlueScore} : {matchManager.PinkScore} Pink"
                                : $"Draw Showcase  •  Blue {matchManager.BlueScore} : {matchManager.PinkScore} Pink")
                        : (matchManager.IsWarmup
                            ? "Lobby Scoreboard"
                            : $"Live Scoreboard  •  Blue {matchManager.BlueScore} : {matchManager.PinkScore} Pink"));
                }

                if (_hudScoreboardSummaryText != null)
                {
                    _hudScoreboardSummaryText.text = matchManager.Phase switch
                    {
                        RetrowaveMatchPhase.Warmup => $"Warmup open  •  {FormatRoundDuration(matchManager.RoundDurationSeconds)} x {matchManager.RoundCount} rounds = {FormatRoundDuration(matchManager.RoundDurationSeconds * matchManager.RoundCount)}  •  Max players {matchManager.MaxPlayers}",
                        RetrowaveMatchPhase.Podium => $"Ceremony running  •  Final options unlock in {Mathf.CeilToInt(matchManager.PodiumTimeRemaining)}s",
                        RetrowaveMatchPhase.MatchComplete => "Match concluded  •  Change teams or wait for the host to start a new game",
                        RetrowaveMatchPhase.Countdown => $"Round {Mathf.Max(1, matchManager.CurrentRoundNumber)}/{matchManager.RoundCount} kickoff in {Mathf.CeilToInt(matchManager.CountdownTimeRemaining)}",
                        _ => $"Round {Mathf.Max(1, matchManager.CurrentRoundNumber)}/{matchManager.RoundCount}  •  {FormatRoundClock(matchManager.RoundTimeRemaining)} remaining  •  Hold Tab to view",
                    };
                }

                if (_hudScoreboardBlueText != null)
                {
                    _hudScoreboardBlueText.text = BuildScoreboardSectionText(RetrowaveLobbyRole.Blue, matchManager);
                }

                if (_hudScoreboardPinkText != null)
                {
                    _hudScoreboardPinkText.text = BuildScoreboardSectionText(RetrowaveLobbyRole.Pink, matchManager);
                }

                if (_hudScoreboardSpectatorText != null)
                {
                    _hudScoreboardSpectatorText.text = BuildScoreboardSectionText(RetrowaveLobbyRole.Spectator, matchManager, includeUnselected: true);
                }
            }

            if (_gameplayHudGoalRoot != null)
            {
                _gameplayHudGoalRoot.SetActive(_goalCelebrationVisible);
            }

            if (_goalCelebrationVisible)
            {
                var teamColor = _goalCelebrationTeam == RetrowaveTeam.Blue
                    ? RetrowaveStyle.BlueGlow
                    : RetrowaveStyle.PinkBase;
                var teamLabel = _goalCelebrationTeam == RetrowaveTeam.Blue ? "BLUE TEAM" : "PINK TEAM";

                if (_hudGoalHeadlineText != null)
                {
                    _hudGoalHeadlineText.text = $"{teamLabel} SCORES";
                    _hudGoalHeadlineText.color = teamColor;
                }

                if (_hudGoalScoreText != null)
                {
                    _hudGoalScoreText.text = $"Blue {_goalCelebrationBlueScore}  -  {_goalCelebrationPinkScore} Pink";
                }

                if (_hudGoalDetailText != null)
                {
                    _hudGoalDetailText.text = $"{_goalCelebrationScorer} found the finish. Resetting field...";
                }
            }

            if (_gameplayHudCountdownRoot != null)
            {
                _gameplayHudCountdownRoot.SetActive(matchManager != null && matchManager.IsCountdown && !_goalCelebrationVisible);
            }

            if (matchManager != null && matchManager.IsCountdown)
            {
                var countdownValue = Mathf.CeilToInt(matchManager.CountdownTimeRemaining);

                if (_hudCountdownLabelText != null)
                {
                    _hudCountdownLabelText.text = Mathf.Max(1, matchManager.CurrentRoundNumber) <= 1
                        ? "MATCH STARTS IN"
                        : $"ROUND {Mathf.Max(1, matchManager.CurrentRoundNumber)}/{matchManager.RoundCount} STARTS IN";
                }

                if (_hudCountdownValueText != null)
                {
                    _hudCountdownValueText.text = countdownValue <= 0 ? "GO" : countdownValue.ToString();
                }
            }
        }

        private string BuildScoreboardSectionText(RetrowaveLobbyRole role, RetrowaveMatchManager matchManager, bool includeUnselected = false)
        {
            var entries = new List<RetrowaveLobbyEntry>();

            for (var i = 0; i < matchManager.LobbyEntries.Count; i++)
            {
                var entry = matchManager.LobbyEntries[i];

                if (!includeUnselected && !entry.HasSelectedRole)
                {
                    continue;
                }

                if (includeUnselected)
                {
                    if (entry.HasSelectedRole && entry.Role != role)
                    {
                        continue;
                    }
                }
                else if (entry.Role != role)
                {
                    continue;
                }

                entries.Add(entry);
            }

            entries.Sort(static (left, right) =>
            {
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
            });

            if (entries.Count == 0)
            {
                return "No players in this section yet.";
            }

            var builder = new StringBuilder(entries.Count * 56);
            builder.Append("<mspace=0.56em>");
            builder.AppendLine("PLAYER                  G  A  PING");
            builder.AppendLine("-----------------------------------");

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var playerLabel = entry.HasSelectedRole ? entry.DisplayName.ToString() : $"{entry.DisplayName} (choosing)";

                if (entry.IsHost)
                {
                    playerLabel += " [Host]";
                }

                if (entry.QueuedForNextRound)
                {
                    playerLabel += " [Next]";
                }

                builder.Append(FormatScoreboardName(playerLabel).PadRight(22));
                builder.Append(entry.Goals.ToString().PadLeft(2));
                builder.Append(" ");
                builder.Append(entry.Assists.ToString().PadLeft(2));
                builder.Append(" ");
                builder.Append($"{entry.PingMs}ms".PadLeft(6));
                builder.AppendLine();
            }

            builder.Append("</mspace>");
            return builder.ToString();
        }

        private static string BuildFinalScoreSummary(RetrowaveMatchManager matchManager)
        {
            var winner = matchManager.BlueScore == matchManager.PinkScore
                ? "Draw"
                : (matchManager.BlueScore > matchManager.PinkScore ? "Blue wins" : "Pink wins");

            return $"{winner}\nBlue {matchManager.BlueScore}  -  {matchManager.PinkScore} Pink\nRounds played: {matchManager.RoundCount}\nTeams are unlocked for the next match.";
        }

        private void ResetHudInfoIntroState()
        {
            _gameplayHudSessionVisible = false;
            _hudInfoIntroAutoHidePending = false;
            _hudInfoIntroHideAtRealtime = 0f;
        }

        private static string FormatScoreboardName(string playerLabel)
        {
            var trimmed = string.IsNullOrWhiteSpace(playerLabel) ? "Player" : playerLabel.Trim();
            return trimmed.Length <= 21 ? trimmed : $"{trimmed[..20]}~";
        }

        private GameObject CreateHudPanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color backgroundColor,
            Color outlineColor)
        {
            var panel = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image));
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = panel.GetComponent<Image>();
            image.color = backgroundColor;

            var outline = panel.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(2f, 2f);
            return panel;
        }

        private TMP_Text CreateHudText(
            Transform parent,
            string name,
            string textValue,
            float fontSize,
            FontStyles fontStyle,
            TextAlignmentOptions alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            var textObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;

            if (anchorMin == anchorMax)
            {
                rect.sizeDelta = size;
            }
            else
            {
                rect.offsetMin = new Vector2(anchoredPosition.x, anchoredPosition.y);
                rect.offsetMax = new Vector2(size.x, size.y);
            }

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _gameplayMenuFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.text = textValue;
            return text;
        }

        private void CreateHudBar(
            Transform parent,
            string name,
            Vector2 anchoredPosition,
            Vector2 size,
            Color backgroundColor,
            Color fillColor,
            out Image fillImage,
            out RectTransform barRect,
            out RectTransform markerRect)
        {
            var background = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image));
            barRect = background.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 1f);
            barRect.anchoredPosition = anchoredPosition;
            barRect.sizeDelta = size;
            background.GetComponent<Image>().color = backgroundColor;

            var fill = CreateUiObject("Fill", background.transform, typeof(RectTransform), typeof(Image));
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            fillImage = fill.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
            fillImage.fillAmount = 0f;

            var marker = CreateUiObject("Marker", background.transform, typeof(RectTransform), typeof(Image));
            markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = new Vector2(0f, 0.5f);
            markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.anchoredPosition = new Vector2(0f, 0f);
            markerRect.sizeDelta = new Vector2(14f, size.y + 10f);

            var markerImage = marker.GetComponent<Image>();
            markerImage.color = Color.Lerp(fillColor, Color.white, 0.28f);
        }

        private TMP_Text CreateScoreboardSection(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, string title, Color titleColor)
        {
            var section = CreateHudPanel(
                parent,
                name,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                anchoredPosition,
                size,
                new Color(0.04f, 0.08f, 0.14f, 0.92f),
                new Color(titleColor.r, titleColor.g, titleColor.b, 0.22f));

            CreateHudText(
                section.transform,
                "Header",
                title,
                20f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(18f, -16f),
                new Vector2(300f, 28f),
                titleColor);

            var body = CreateHudText(
                section.transform,
                "Body",
                string.Empty,
                15f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(18f, -58f),
                new Vector2(356f, 360f),
                new Color(0.92f, 0.95f, 1f, 0.98f));
            body.lineSpacing = 8f;
            body.textWrappingMode = TextWrappingModes.NoWrap;
            return body;
        }

        private static void UpdateHudBarMarker(RectTransform barRect, RectTransform markerRect, float normalized)
        {
            if (barRect == null || markerRect == null)
            {
                return;
            }

            var width = Mathf.Max(0f, barRect.rect.width);
            var xPosition = Mathf.Clamp01(normalized) * width;
            markerRect.anchoredPosition = new Vector2(xPosition, 0f);
        }

        private void EnsurePodiumPresentation(RetrowaveMatchManager matchManager)
        {
            if (_podiumPresentationRoot == null)
            {
                BuildPodiumPresentation(matchManager);
            }

            if (!_podiumCameraActive)
            {
                RetrowaveCameraRig.ShowPodium();
                _podiumCameraActive = true;
            }
        }

        private void BuildPodiumPresentation(RetrowaveMatchManager matchManager)
        {
            ClearPodiumPresentation();

            _podiumPresentationRoot = new GameObject("Retrowave Winners Podium");
            _podiumPresentationRoot.transform.SetParent(transform, false);

            var hasWinner = matchManager != null && matchManager.HasPodiumWinner;
            var team = hasWinner ? matchManager.PodiumWinnerTeam : RetrowaveTeam.Blue;
            var glowColor = hasWinner ? RetrowaveStyle.GetTeamGlow(team) : new Color(0.82f, 0.64f, 1f, 1f);
            var baseColor = hasWinner ? RetrowaveStyle.GetTeamBase(team) : new Color(0.24f, 0.16f, 0.4f, 1f);
            var podiumMaterial = RetrowaveStyle.CreateLitMaterial(baseColor, glowColor * 2.2f, 0.86f, 0.04f);
            var trimMaterial = RetrowaveStyle.CreateUnlitMaterial(Color.Lerp(glowColor, Color.white, 0.18f));

            var stage = CreatePodiumPrimitive(
                "Podium Stage",
                RetrowavePodiumLayout.Center + new Vector3(0f, 0.08f, 2.4f),
                new Vector3(24f, 0.16f, 15f),
                RetrowaveStyle.CreateLitMaterial(new Color(0.03f, 0.05f, 0.11f), glowColor * 0.8f, 0.82f, 0.02f));
            stage.transform.SetParent(_podiumPresentationRoot.transform, true);

            for (var rank = 0; rank < 3; rank++)
            {
                var platform = CreatePodiumPrimitive(
                    $"Rank {rank + 1} Platform",
                    RetrowavePodiumLayout.GetPlatformPosition(rank),
                    RetrowavePodiumLayout.GetPlatformScale(rank),
                    podiumMaterial);
                platform.transform.SetParent(_podiumPresentationRoot.transform, true);

                var trim = CreatePodiumPrimitive(
                    $"Rank {rank + 1} Neon Trim",
                    RetrowavePodiumLayout.GetPlatformPosition(rank) + new Vector3(0f, RetrowavePodiumLayout.GetPlatformScale(rank).y * 0.5f + 0.04f, -2.08f),
                    new Vector3(RetrowavePodiumLayout.GetPlatformScale(rank).x + 0.18f, 0.08f, 0.12f),
                    trimMaterial);
                trim.transform.SetParent(_podiumPresentationRoot.transform, true);

                CreatePodiumText(
                    _podiumPresentationRoot.transform,
                    $"Rank {rank + 1} Placement Label",
                    RetrowavePodiumLayout.GetPlacementLabel(rank),
                    RetrowavePodiumLayout.GetPlacementLabelPosition(rank),
                    1.18f,
                    Color.Lerp(glowColor, Color.white, 0.32f));
            }

            CreatePodiumText(
                _podiumPresentationRoot.transform,
                "Winning Team Lineup Label",
                hasWinner ? "WINNING TEAM LINEUP" : "SHOWCASE LINEUP",
                RetrowavePodiumLayout.LineupLabelPosition,
                0.78f,
                Color.Lerp(glowColor, Color.white, 0.22f));

            var titleObject = new GameObject("Podium Title", typeof(TextMeshPro));
            titleObject.transform.SetParent(_podiumPresentationRoot.transform, false);
            titleObject.transform.position = RetrowavePodiumLayout.Center + new Vector3(0f, 6.2f, 5.9f);
            titleObject.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            var titleText = titleObject.GetComponent<TextMeshPro>();
            titleText.font = TMP_Settings.defaultFontAsset;
            titleText.fontSize = 2.2f;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = hasWinner ? $"{GetTeamLabel(team).ToUpperInvariant()} WINS" : "DRAW SHOWCASE";
            titleText.color = Color.Lerp(glowColor, Color.white, 0.18f);

            var lightObject = new GameObject("Podium Glow");
            lightObject.transform.SetParent(_podiumPresentationRoot.transform, false);
            lightObject.transform.position = RetrowavePodiumLayout.Center + new Vector3(0f, 5.5f, -1f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = glowColor;
            light.range = 34f;
            light.intensity = 9f;
        }

        private static GameObject CreatePodiumPrimitive(string name, Vector3 position, Vector3 scale, Material material)
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.name = name;
            primitive.transform.SetPositionAndRotation(position, Quaternion.identity);
            primitive.transform.localScale = scale;

            if (primitive.TryGetComponent<Collider>(out var collider))
            {
                collider.enabled = false;
            }

            if (primitive.TryGetComponent<MeshRenderer>(out var renderer))
            {
                renderer.sharedMaterial = material;
            }

            return primitive;
        }

        private static TextMeshPro CreatePodiumText(Transform parent, string name, string value, Vector3 position, float fontSize, Color color)
        {
            var textObject = new GameObject(name, typeof(TextMeshPro));
            textObject.transform.SetParent(parent, false);
            textObject.transform.SetPositionAndRotation(position, RetrowavePodiumLayout.LabelRotation);

            var text = textObject.GetComponent<TextMeshPro>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.text = value;
            text.color = color;
            text.outlineColor = new Color(0f, 0f, 0f, 0.78f);
            text.outlineWidth = 0.18f;
            return text;
        }

        private void ClearPodiumPresentation()
        {
            _podiumCameraActive = false;

            if (_podiumPresentationRoot == null)
            {
                return;
            }

            Destroy(_podiumPresentationRoot);
            _podiumPresentationRoot = null;
        }

        private void SelectGameplayRole(RetrowaveLobbyRole role)
        {
            TryRequestRoleSelection(role);
            _showPauseMenu = false;
        }

        private void HandleGameplayStartMatch()
        {
            var matchManager = GetActiveMatchManager();

            if (matchManager == null)
            {
                return;
            }

            matchManager.RequestStartMatch();
            _showPauseMenu = false;
        }

        private void HandleGameplayResume()
        {
            _showPauseMenu = false;
        }

        private GameObject CreateUiObject(string name, Transform parent, params System.Type[] components)
        {
            var gameObject = new GameObject(name, components);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private void ApplyGameplayMenuLayout(bool matchComplete)
        {
            if (_gameplayMenuPanelRect == null)
            {
                return;
            }

            if (matchComplete)
            {
                _gameplayMenuPanelRect.sizeDelta = new Vector2(760f, 660f);
                SetMenuTextLayout(_gameplayMenuTitleText, new Vector2(0f, 284f), new Vector2(640f, 42f), 34f);
                SetMenuTextLayout(_gameplayMenuBodyText, new Vector2(0f, 211f), new Vector2(650f, 96f), 18f);
                SetMenuTextLayout(_gameplayMenuHintText, new Vector2(0f, 139f), new Vector2(650f, 42f), 16f);
                SetMenuTextLayout(_gameplayMenuFooterText, new Vector2(0f, -214f), new Vector2(650f, 42f), 15f);
                SetMenuButtonLayout(_gameplayBlueButton, new Vector2(0f, 72f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplayPinkButton, new Vector2(0f, 0f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplaySpectateButton, new Vector2(0f, -72f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplayStartButton, new Vector2(0f, -148f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplayResumeButton, new Vector2(0f, -286f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplayReturnButton, new Vector2(0f, -286f), new Vector2(380f, 52f));
                return;
            }

            _gameplayMenuPanelRect.sizeDelta = new Vector2(700f, 560f);
            SetMenuTextLayout(_gameplayMenuTitleText, new Vector2(0f, 224f), new Vector2(580f, 42f), 32f);
            SetMenuTextLayout(_gameplayMenuBodyText, new Vector2(0f, 164f), new Vector2(590f, 68f), 18f);
            SetMenuTextLayout(_gameplayMenuHintText, new Vector2(0f, 106f), new Vector2(590f, 44f), 16f);
            SetMenuTextLayout(_gameplayMenuFooterText, new Vector2(0f, -202f), new Vector2(590f, 52f), 15f);
            SetMenuButtonLayout(_gameplayBlueButton, new Vector2(0f, 48f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplayPinkButton, new Vector2(0f, -20f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplaySpectateButton, new Vector2(0f, -88f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplayStartButton, new Vector2(0f, -164f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplayResumeButton, new Vector2(-132f, -242f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplayReturnButton, new Vector2(132f, -242f), new Vector2(320f, 52f));
        }

        private static void SetMenuTextLayout(TMP_Text text, Vector2 anchoredPosition, Vector2 size, float fontSize)
        {
            if (text == null)
            {
                return;
            }

            text.rectTransform.anchoredPosition = anchoredPosition;
            text.rectTransform.sizeDelta = size;
            text.fontSize = fontSize;
        }

        private static void SetMenuButtonLayout(Button button, Vector2 anchoredPosition, Vector2 size)
        {
            if (button == null)
            {
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private TMP_Text CreateMenuText(Transform parent, string name, float fontSize, FontStyles fontStyle, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var textObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _gameplayMenuFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            text.text = string.Empty;
            return text;
        }

        private Button CreateMenuButton(Transform parent, string name, string label, Vector2 anchoredPosition, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(320f, 52f);

            var image = buttonObject.GetComponent<Image>();
            image.color = color;

            var outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
            outline.effectDistance = new Vector2(1f, 1f);

            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.16f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.18f, 0.2f, 0.24f, 0.78f);
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var labelObject = CreateUiObject("Label", buttonObject.transform, typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var labelText = labelObject.GetComponent<TextMeshProUGUI>();
            labelText.font = _gameplayMenuFont;
            labelText.text = label;
            labelText.fontSize = 18f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private void DrawGoalCelebrationIndicator()
        {
            var boxWidth = Mathf.Min(760f, Screen.width - 48f);
            var boxHeight = 190f;
            var boxRect = new Rect(Screen.width * 0.5f - boxWidth * 0.5f, 56f, boxWidth, boxHeight);
            var teamColor = _goalCelebrationTeam == RetrowaveTeam.Blue
                ? RetrowaveStyle.BlueGlow
                : RetrowaveStyle.PinkBase;
            var teamLabel = _goalCelebrationTeam == RetrowaveTeam.Blue ? "Blue Team" : "Pink Team";
            var headline = $"{teamLabel.ToUpperInvariant()} SCORES!";
            var scorerLine = _goalCelebrationScorer == teamLabel
                ? $"{teamLabel} scored the goal"
                : $"{_goalCelebrationScorer} scored for {teamLabel}";
            var scoreLine = $"Blue {_goalCelebrationBlueScore}  -  {_goalCelebrationPinkScore} Pink";

            var previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.03f, 0.08f, 0.96f);
            GUI.Box(boxRect, string.Empty);
            GUI.color = previousColor;

            var headlineStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                normal = { textColor = teamColor },
            };

            var scoreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            var detailStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                normal = { textColor = new Color(0.95f, 0.95f, 1f, 0.92f) },
            };

            GUI.Label(new Rect(boxRect.x + 18f, boxRect.y + 18f, boxRect.width - 36f, 52f), headline, headlineStyle);
            GUI.Label(new Rect(boxRect.x + 18f, boxRect.y + 78f, boxRect.width - 36f, 42f), scoreLine, scoreStyle);
            GUI.Label(new Rect(boxRect.x + 18f, boxRect.y + 124f, boxRect.width - 36f, 28f), scorerLine, detailStyle);
            GUI.Label(new Rect(boxRect.x + 18f, boxRect.y + 148f, boxRect.width - 36f, 24f), "Resetting field...", detailStyle);
        }

        private void DrawPauseMenu()
        {
            var forceSelection = RequiresRoleSelection();
            var matchManager = GetActiveMatchManager();
            var title = forceSelection ? "Choose Your Role" : "Match Menu";

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 210f, 70f, 420f, 340f), GUI.skin.window);
            GUILayout.Label(title);

            if (forceSelection)
            {
                GUILayout.Label("Pick blue, pink, or spectator before jumping fully into the lobby.");
                GUILayout.Label("Shortcuts: 1 or B = Blue, 2 or P = Pink, 3 or S = Spectator");
            }
            else
            {
                GUILayout.Label("Swap teams, spectate, or control the match flow from here.");
            }

            GUILayout.Space(10f);

            DrawRoleButton("Join Blue Team", RetrowaveLobbyRole.Blue);
            DrawRoleButton("Join Pink Team", RetrowaveLobbyRole.Pink);
            DrawRoleButton("Spectate", RetrowaveLobbyRole.Spectator);

            if (matchManager != null && TryGetLocalLobbyEntry(out var entry) && entry.IsHost)
            {
                GUILayout.Space(14f);
                var startLabel = matchManager.IsWarmup ? "Start Match" : "Restart Match";
                GUI.enabled = matchManager.CanStartMatch;

                if (GUILayout.Button(startLabel, GUILayout.Height(34f)))
                {
                    matchManager.RequestStartMatch();
                    _showPauseMenu = false;
                }

                GUI.enabled = true;

                if (!matchManager.CanStartMatch)
                {
                    GUILayout.Label("Host start unlocks once at least one player is on blue and pink.");
                }
            }

            GUILayout.FlexibleSpace();

            if (!forceSelection && GUILayout.Button("Resume", GUILayout.Height(30f)))
            {
                _showPauseMenu = false;
            }

            if (GUILayout.Button("Return To Main Menu", GUILayout.Height(30f)))
            {
                ReturnToMainMenu();
            }

            GUILayout.EndArea();
        }

        private void DrawRoleButton(string label, RetrowaveLobbyRole role)
        {
            if (!GUILayout.Button(label, GUILayout.Height(32f)))
            {
                return;
            }

            TryRequestRoleSelection(role);
            _showPauseMenu = false;
        }

        private void DrawScoreboard()
        {
            var matchManager = GetActiveMatchManager();

            if (matchManager == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 330f, 18f, 660f, 420f), GUI.skin.box);
            GUILayout.Label(matchManager.IsWarmup
                ? "Lobby Scoreboard - Warmup"
                : $"Live Scoreboard - Blue {matchManager.BlueScore} : {matchManager.PinkScore} Pink");

            DrawScoreboardSection("Blue Team", RetrowaveLobbyRole.Blue, matchManager);
            DrawScoreboardSection("Pink Team", RetrowaveLobbyRole.Pink, matchManager);
            DrawScoreboardSection("Spectators", RetrowaveLobbyRole.Spectator, matchManager, includeUnselected: true);

            GUILayout.EndArea();
        }

        private void DrawScoreboardSection(string title, RetrowaveLobbyRole role, RetrowaveMatchManager matchManager, bool includeUnselected = false)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Player", GUILayout.Width(220f));
            GUILayout.Label("Goals", GUILayout.Width(70f));
            GUILayout.Label("Assists", GUILayout.Width(70f));
            GUILayout.Label("Ping", GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            var entries = new List<RetrowaveLobbyEntry>();

            for (var i = 0; i < matchManager.LobbyEntries.Count; i++)
            {
                var entry = matchManager.LobbyEntries[i];

                if (!includeUnselected && !entry.HasSelectedRole)
                {
                    continue;
                }

                if (includeUnselected)
                {
                    if (entry.HasSelectedRole && entry.Role != role)
                    {
                        continue;
                    }
                }
                else if (entry.Role != role)
                {
                    continue;
                }

                entries.Add(entry);
            }

            entries.Sort(static (left, right) =>
            {
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
            });

            if (entries.Count == 0)
            {
                GUILayout.Label("No players in this section.");
                return;
            }

            foreach (var entry in entries)
            {
                var playerLabel = entry.HasSelectedRole ? entry.DisplayName.ToString() : $"{entry.DisplayName} (choosing)";

                if (entry.IsHost)
                {
                    playerLabel += " [Host]";
                }

                if (entry.QueuedForNextRound)
                {
                    playerLabel += " [Next Round]";
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(playerLabel, GUILayout.Width(220f));
                GUILayout.Label(entry.Goals.ToString(), GUILayout.Width(70f));
                GUILayout.Label(entry.Assists.ToString(), GUILayout.Width(70f));
                GUILayout.Label($"{entry.PingMs} ms", GUILayout.Width(70f));
                GUILayout.EndHorizontal();
            }
        }

        private void DrawGameplayFallbackMenu()
        {
            GUILayout.BeginArea(new Rect(18f, 14f, 380f, 120f), GUI.skin.box);
            GUILayout.Label("Arena loaded, but no multiplayer session is active.");
            GUILayout.Label("Use the main menu to host or join a server.");

            if (GUILayout.Button("Return To Main Menu", GUILayout.Height(32f)))
            {
                ReturnToMainMenu();
            }

            GUILayout.EndArea();
        }

        private bool TryGetLocalLobbyEntry(out RetrowaveLobbyEntry entry)
        {
            var matchManager = GetActiveMatchManager();

            if (matchManager != null && _networkManager != null && _networkManager.IsListening)
            {
                return matchManager.TryGetLobbyEntry(_networkManager.LocalClientId, out entry);
            }

            entry = default;
            return false;
        }

        private bool RequiresSessionBootstrap()
        {
            return _networkManager != null
                   && _networkManager.IsListening
                   && !_networkManager.IsServer
                   && (GetActiveMatchManager() == null || RetrowavePlayerController.LocalPlayer == null);
        }

        private bool CanCycleWarmupSpectatorTargets()
        {
            var matchManager = GetActiveMatchManager();

            if (matchManager == null || !matchManager.IsWarmup)
            {
                return false;
            }

            if (!TryGetLocalLobbyEntry(out var entry))
            {
                return false;
            }

            return entry.HasSelectedRole
                   && entry.Role == RetrowaveLobbyRole.Spectator
                   && RetrowavePlayerController.LocalOwner == null
                   && GetWarmupSpectatorTargets().Count > 0;
        }

        private List<RetrowavePlayerController> GetWarmupSpectatorTargets()
        {
            var targets = new List<RetrowavePlayerController>();

            if (_networkManager?.SpawnManager == null)
            {
                return targets;
            }

            foreach (var networkObject in _networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null || !networkObject.TryGetComponent<RetrowavePlayerController>(out var player))
                {
                    continue;
                }

                if (!player.IsArenaParticipant)
                {
                    continue;
                }

                targets.Add(player);
            }

            targets.Sort(static (left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));
            return targets;
        }

        private bool RequiresRoleSelection()
        {
            if (RetrowavePlayerController.LocalPlayer != null)
            {
                return !RetrowavePlayerController.LocalPlayer.HasSelectedRole;
            }

            return TryGetLocalLobbyEntry(out var entry) && !entry.HasSelectedRole;
        }

        private bool ShouldBlockGameplayInput()
        {
            var matchManager = GetActiveMatchManager();
            var gameplayLocked = _goalCelebrationVisible
                                 || (matchManager != null && matchManager.IsGameplayLocked);

            return _networkManager != null
                   && _networkManager.IsListening
                   && IsGameplayScene(SceneManager.GetActiveScene())
                   && (_showPauseMenu || RequiresRoleSelection() || gameplayLocked);
        }

        private static string GetTeamLabel(RetrowaveTeam team)
        {
            return team == RetrowaveTeam.Blue ? "Blue Team" : "Pink Team";
        }

        private static string GetRoleLabel(RetrowaveLobbyEntry entry)
        {
            if (!entry.HasSelectedRole)
            {
                return "Unassigned";
            }

            var label = GetRoleLabel(entry.Role, true);
            return entry.QueuedForNextRound ? $"{label} (next round)" : label;
        }

        private static string GetRoleLabel(RetrowaveLobbyRole role, bool hasSelectedRole)
        {
            if (!hasSelectedRole)
            {
                return "Unassigned";
            }

            return role switch
            {
                RetrowaveLobbyRole.Blue => "Blue Team",
                RetrowaveLobbyRole.Pink => "Pink Team",
                _ => "Spectator",
            };
        }

        private static string FormatRoundDuration(int seconds)
        {
            var duration = TimeSpan.FromSeconds(Mathf.Max(0, seconds));
            return duration.TotalMinutes >= 1d && duration.Seconds == 0
                ? $"{duration.Minutes:0}m"
                : duration.ToString(@"m\:ss");
        }

        private static string FormatRoundClock(float secondsRemaining)
        {
            return TimeSpan.FromSeconds(Mathf.Max(0f, secondsRemaining)).ToString(@"m\:ss");
        }

        private RetrowaveMatchManager GetActiveMatchManager()
        {
            if (RetrowaveMatchManager.Instance != null)
            {
                return RetrowaveMatchManager.Instance;
            }

            if (_networkManager?.SpawnManager == null)
            {
                return null;
            }

            foreach (var networkObject in _networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject != null && networkObject.TryGetComponent<RetrowaveMatchManager>(out var matchManager))
                {
                    return matchManager;
                }
            }

            return null;
        }

        private bool TryRequestRoleSelection(RetrowaveLobbyRole role)
        {
            if (RetrowavePlayerController.LocalPlayer != null)
            {
                RetrowavePlayerController.LocalPlayer.RequestRoleSelection(role);
                return true;
            }

            var matchManager = GetActiveMatchManager();

            if (matchManager != null)
            {
                matchManager.RequestRoleSelection(role);
                return true;
            }

            return false;
        }

        private void ShutdownSession()
        {
            ForceShutdownSession(true, true);
        }

        private bool TryParsePort(string portText, out ushort port)
        {
            if (ushort.TryParse(portText, out port) && port > 0)
            {
                return true;
            }

            port = 0;
            return false;
        }

        private IEnumerator LoadGameplayAndConnect()
        {
            _showPauseMenu = false;
            _showScoreboard = false;

            if (!IsGameplayScene(SceneManager.GetActiveScene()))
            {
                SceneManager.LoadScene(GameplaySceneName, LoadSceneMode.Single);
            }

            while (!IsGameplayScene(SceneManager.GetActiveScene()))
            {
                yield return null;
            }

            yield return null;
            ApplyScenePresentation(SceneManager.GetActiveScene());

            var started = false;

            switch (_pendingConnectionMode)
            {
                case PendingConnectionMode.Host:
                    _transport.SetConnectionData(GetJoinAddressForDisplay(), _port, "0.0.0.0");
                    started = _networkManager.StartHost();
                    break;
                case PendingConnectionMode.Client:
                    _transport.SetConnectionData(_address, _port);
                    started = _networkManager.StartClient();
                    break;
            }

            _pendingConnectionMode = PendingConnectionMode.None;

            if (!started)
            {
                ForceShutdownSession(true, false);
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _showPauseMenu = false;
            _showScoreboard = false;
            ClearGoalCelebrationState();
            ClearPodiumPresentation();
            ApplyScenePresentation(scene);
        }

        private string GetJoinAddressForDisplay()
        {
            return IsLoopbackAddress(_address) ? ResolvePreferredAddress() : _address;
        }

        private static string ResolvePreferredAddress()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up
                        || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    var properties = networkInterface.GetIPProperties();

                    foreach (var unicastAddress in properties.UnicastAddresses)
                    {
                        var ip = unicastAddress.Address;

                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                        {
                            continue;
                        }

                        var address = ip.ToString();

                        if (address.StartsWith("169.254."))
                        {
                            continue;
                        }

                        if (IsPrivateIpv4(address))
                        {
                            return address;
                        }
                    }
                }

                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());

                foreach (var ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                    {
                        continue;
                    }

                    var address = ip.ToString();

                    if (address.StartsWith("169.254."))
                    {
                        continue;
                    }

                    if (IsPrivateIpv4(address))
                    {
                        return address;
                    }
                }

                foreach (var ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                    {
                        continue;
                    }

                    var address = ip.ToString();

                    if (!address.StartsWith("169.254."))
                    {
                        return address;
                    }
                }
            }
            catch
            {
                // Fall back to loopback when the local LAN address can't be resolved.
            }

            return "127.0.0.1";
        }

        private static bool IsPrivateIpv4(string address)
        {
            var octets = address.Split('.');

            if (octets.Length != 4
                || !byte.TryParse(octets[0], out var first)
                || !byte.TryParse(octets[1], out var second))
            {
                return false;
            }

            return first == 10
                   || (first == 172 && second >= 16 && second <= 31)
                   || (first == 192 && second == 168);
        }

        private static bool IsLoopbackAddress(string address)
        {
            return string.IsNullOrWhiteSpace(address)
                   || address == "127.0.0.1"
                   || address.Equals("localhost", System.StringComparison.OrdinalIgnoreCase);
        }

        private void PrepareForProcessExit()
        {
            StopAllCoroutines();
            ForceShutdownSession(false, false);
        }

        private void ForceShutdownSession(bool rebuildRuntime, bool stopCoroutines)
        {
            if (_sessionShutdownInProgress)
            {
                return;
            }

            _sessionShutdownInProgress = true;

            try
            {
                if (stopCoroutines)
                {
                    StopAllCoroutines();
                }

                _pendingConnectionMode = PendingConnectionMode.None;
                _showPauseMenu = false;
                _showScoreboard = false;
                ClearGoalCelebrationState();
                ClearPodiumPresentation();
                TearDownNetworkRuntime();
                RetrowavePlayerController.ClearLocalOwner();
                RetrowaveCameraRig.ShowOverview();

                if (rebuildRuntime)
                {
                    EnsureNetworkRuntime();
                }
            }
            finally
            {
                _sessionShutdownInProgress = false;
            }
        }

        private void TearDownNetworkRuntime()
        {
            if (_networkManager != null)
            {
                _networkManager.OnServerStarted -= HandleServerStarted;
                _networkManager.OnClientConnectedCallback -= HandleNetworkClientConnected;
                _networkManager.OnClientDisconnectCallback -= HandleNetworkClientDisconnected;

                if (_networkManager.IsListening)
                {
                    _networkManager.Shutdown();
                }
            }

            if (_networkRuntimeRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_networkRuntimeRoot);
                }
                else
                {
                    DestroyImmediate(_networkRuntimeRoot);
                }
            }

            _networkRuntimeRoot = null;
            _networkManager = null;
            _transport = null;
        }

        private void ApplyScenePresentation(Scene scene)
        {
            if (IsGameplayScene(scene))
            {
                RetrowaveArenaBuilder.EnsureBuilt();
                RetrowaveArenaBuilder.SetActive(true);
                RetrowaveCameraRig.EnsureCamera();
                RetrowaveCameraRig.ShowOverview();
                EnsureGameplayMenuOverlay();
                EnsureGameplayHudOverlay();
                return;
            }

            RetrowaveArenaBuilder.SetActive(false);
            ClearPodiumPresentation();
            DestroyGameplayMenuOverlay();
            DestroyGameplayHudOverlay();
        }

        private void ClearGoalCelebrationState()
        {
            if (!_goalCelebrationVisible && Mathf.Approximately(Time.timeScale, 1f))
            {
                return;
            }

            _goalCelebrationVisible = false;
            _goalCelebrationEndsAtRealtime = 0f;
            SetLocalTimeScale(1f);
        }

        private void SetLocalTimeScale(float scale)
        {
            var clampedScale = Mathf.Clamp(scale, 0.05f, 1f);
            Time.timeScale = clampedScale;
            Time.fixedDeltaTime = _defaultFixedDeltaTime * clampedScale;
        }

        private static bool IsGameplayScene(Scene scene)
        {
            return scene.name == GameplaySceneName;
        }

        private static GameObject CreatePlayerPrefab(bool isTemplate = true)
        {
            var prefab = new GameObject("RT Player Cube");
            prefab.name = "RT Player Cube";
            prefab.SetActive(false);

            var collider = prefab.AddComponent<CapsuleCollider>();
            var rigidbody = prefab.AddComponent<Rigidbody>();
            rigidbody.mass = 5.6f;
            rigidbody.linearDamping = 0.08f;
            rigidbody.angularDamping = 1.35f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.maxAngularVelocity = 12f;

            collider.direction = 2;
            collider.radius = 0.52f;
            collider.height = 1.65f;
            collider.center = new Vector3(0f, -0.12f, 0f);
            collider.material = new PhysicsMaterial("RT_PlayerPhysics")
            {
                bounciness = 0f,
                dynamicFriction = 0.02f,
                staticFriction = 0.01f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };

            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<NetworkTransform>();
            prefab.AddComponent<NetworkRigidbody>();
            prefab.AddComponent<RetrowavePlayerController>();

            if (!TryAttachSportCarVisual(prefab.transform))
            {
                CreateFallbackVehicleVisual(prefab.transform);
            }

            FinalizeRuntimePrefab(prefab, 0xA1000001u, isTemplate);
            return prefab;
        }

        private static GameObject CreateBallPrefab(bool isTemplate = true)
        {
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            prefab.name = "RT Ball";
            prefab.SetActive(false);
            prefab.transform.localScale = Vector3.one * 2.1f;

            var rigidbody = prefab.AddComponent<Rigidbody>();
            rigidbody.mass = 0.9f;
            rigidbody.linearDamping = 0.05f;
            rigidbody.angularDamping = 0.12f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var collider = prefab.GetComponent<SphereCollider>();
            var material = new PhysicsMaterial("RT_BallPhysics")
            {
                bounciness = 0.42f,
                dynamicFriction = 0.06f,
                staticFriction = 0.04f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
            collider.material = material;

            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<NetworkTransform>();
            prefab.AddComponent<NetworkRigidbody>();
            prefab.AddComponent<RetrowaveBall>();
            prefab.GetComponent<MeshRenderer>().sharedMaterial = CreateBallSurfaceMaterial();
            FinalizeRuntimePrefab(prefab, 0xA1000002u, isTemplate);
            return prefab;
        }

        private static Material CreateBallSurfaceMaterial()
        {
            var sourceMaterial = LoadYughuesBallMaterial();

            if (sourceMaterial != null)
            {
                var ballMaterial = new Material(sourceMaterial)
                {
                    name = $"{YughuesBallMaterialName} Runtime Ball",
                };

                ApplyBallMaterialGlow(ballMaterial);
                ApplyMaterialTextureScale(ballMaterial, new Vector2(YughuesBallTextureTiling, YughuesBallTextureTiling));
                return ballMaterial;
            }

            Debug.LogWarning($"RetrowaveGameBootstrap: {YughuesBallMaterialName} was not found. Using fallback glowing ball material.");
            return RetrowaveStyle.CreateLitMaterial(
                BallSurfaceTint,
                BallSurfaceEmission * 1.3f,
                0.95f,
                0.02f);
        }

        private static Material LoadYughuesBallMaterial()
        {
#if UNITY_EDITOR
            var editorMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(YughuesBallMaterialAssetPath);

            if (editorMaterial != null)
            {
                return editorMaterial;
            }
#endif

            return Resources.Load<Material>(YughuesBallMaterialResourcePath);
        }

        private static void ApplyBallMaterialGlow(Material material)
        {
            material.EnableKeyword("_EMISSION");
            SetMaterialColor(material, "_BaseColor", BallSurfaceTint);
            SetMaterialColor(material, "_Color", BallSurfaceTint);
            SetMaterialColor(material, "_EmissionColor", BallSurfaceEmission);
            SetMaterialFloat(material, "_Smoothness", 0.96f);
        }

        private static void ApplyMaterialTextureScale(Material material, Vector2 scale)
        {
            SetMaterialTextureScale(material, "_BaseMap", scale);
            SetMaterialTextureScale(material, "_MainTex", scale);
            SetMaterialTextureScale(material, "_BumpMap", scale);
            SetMaterialTextureScale(material, "_SpecGlossMap", scale);
        }

        private static void SetMaterialColor(Material material, string propertyName, Color color)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetMaterialTextureScale(Material material, string propertyName, Vector2 scale)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetTextureScale(propertyName, scale);
            }
        }

        private static GameObject CreatePowerUpPrefab(bool isTemplate = true)
        {
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "RT PowerUp";
            prefab.SetActive(false);
            prefab.transform.localScale = Vector3.one * 1.05f;

            var collider = prefab.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = Vector3.one * 1.7f;

            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<NetworkTransform>();
            prefab.AddComponent<RetrowavePowerUp>();
            prefab.GetComponent<MeshRenderer>().sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.18f, 0.14f, 0.28f),
                new Color(0.95f, 0.4f, 1f) * 2.5f,
                0.9f,
                0f);
            FinalizeRuntimePrefab(prefab, 0xA1000003u, isTemplate);
            return prefab;
        }

        private static GameObject CreateMatchManagerPrefab(bool isTemplate = true)
        {
            var prefab = new GameObject("RT Match Manager");
            prefab.SetActive(false);
            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<RetrowaveMatchManager>();
            FinalizeRuntimePrefab(prefab, 0xA1000004u, isTemplate);
            return prefab;
        }

        private static bool TryAttachSportCarVisual(Transform parent)
        {
            var sportCarPrefab = Resources.Load<GameObject>(SportCarResourcePath);

            if (sportCarPrefab == null)
            {
                Debug.LogWarning($"RetrowaveGameBootstrap: could not load SportsCar resource at Resources/{SportCarResourcePath}. Falling back to the generated cube body.");
                return false;
            }

            var visual = Instantiate(sportCarPrefab, parent, false);
            visual.name = "Body Visual";
            DisableChildColliders(visual);
            FitVisualToBounds(visual.transform, new Vector3(1.3f, 0.8f, 2.1f), new Vector3(0f, -0.25f, 0f));
            return true;
        }

        private static void CreateFallbackVehicleVisual(Transform parent)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Body Visual";
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            visual.transform.localScale = new Vector3(1.3f, 0.8f, 2.1f);

            var visualCollider = visual.GetComponent<BoxCollider>();

            if (visualCollider != null)
            {
                visualCollider.enabled = false;
            }

            var bodyRenderer = visual.GetComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                RetrowaveStyle.BlueBase,
                RetrowaveStyle.BlueGlow * 2f,
                0.86f,
                0.08f);

            CreateBoosterVisual(visual.transform, new Vector3(-0.35f, 0f, -0.85f));
            CreateBoosterVisual(visual.transform, new Vector3(0.35f, 0f, -0.85f));
        }

        private static void FitVisualToBounds(Transform visualRoot, Vector3 targetSize, Vector3 targetCenter)
        {
            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            var sourceSize = bounds.size;

            if (sourceSize.x <= 0.001f || sourceSize.y <= 0.001f || sourceSize.z <= 0.001f)
            {
                return;
            }

            var scaleFactor = Mathf.Min(
                targetSize.x / sourceSize.x,
                Mathf.Min(targetSize.y / sourceSize.y, targetSize.z / sourceSize.z));

            visualRoot.localScale = Vector3.one * scaleFactor;
            var scaledCenter = bounds.center * scaleFactor;
            visualRoot.localPosition = targetCenter - scaledCenter;
        }

        private static void DisableChildColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);

            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private static void CreateBoosterVisual(Transform parent, Vector3 localPosition)
        {
            var booster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            booster.name = "Booster";
            booster.transform.SetParent(parent, false);
            booster.transform.localPosition = localPosition;
            booster.transform.localScale = new Vector3(0.28f, 0.22f, 0.35f);
            booster.GetComponent<BoxCollider>().enabled = false;
            booster.GetComponent<MeshRenderer>().sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.08f, 0.08f, 0.12f),
                new Color(0.05f, 0.8f, 1f) * 1.6f,
                0.8f,
                0.04f);
        }

        private static void FinalizeRuntimePrefab(GameObject prefab, uint runtimeHash, bool isTemplate)
        {
            if (prefab == null)
            {
                return;
            }

            var networkObject = prefab.GetComponent<NetworkObject>();

            if (networkObject != null && GlobalObjectIdHashField != null)
            {
                GlobalObjectIdHashField.SetValue(networkObject, runtimeHash);
            }

            if (isTemplate)
            {
                prefab.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(prefab);
                prefab.SetActive(false);
            }
            else
            {
                prefab.SetActive(true);
            }
        }
    }
}
