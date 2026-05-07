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

        private enum GameplaySettingsTab
        {
            Game = 0,
            Controls = 1,
            Video = 2,
            KeyBindings = 3,
        }

        public const string MainMenuSceneName = "MainMenu";
        public const string GameplaySceneName = "SampleScene";
        public const string TestArenaSceneName = "TestArena";
        private const string SportCarResourcePath = "RetrowaveRocket/SportCar_5";
        private const string YughuesBallMaterialName = "M_YFMeM_49";
        private const string YughuesBallMaterialAssetPath = "Assets/YughuesFreeMetalMaterials/Materials/M_YFMeM_49.mat";
        private const string YughuesBallMaterialResourcePath = "RetrowaveRocket/M_YFMeM_49_Ball";
        private const float YughuesBallTextureTiling = 2.2f;
        private const uint NetworkSimulationTickRate = 60;
        private const int ClientInterpolationBufferTicks = 2;
        private const int LanPacketQueueSize = 512;
        private const float GameplayFixedDeltaTime = 1f / 60f;
        private const float PhysicsNetworkInterpolationSeconds = 0.05f;
        private const float RoleSelectionRequestTimeoutSeconds = 2f;
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
        private GameObject _rarePowerUpBeaconPrefab;
        private GameObject _neonTrailSegmentPrefab;
        private GameObject _gravityBombDevicePrefab;
        private GameObject _chronoDomeFieldPrefab;
        private GameObject _matchManagerPrefab;
        private string _address = "127.0.0.1";
        private string _preferredDisplayName = "Player";
        private ushort _port = 7777;
        private PendingConnectionMode _pendingConnectionMode;
        private bool _showPauseMenu;
        private bool _showScoreboard;
        private bool _roleSelectionRequestPending;
        private float _roleSelectionRequestExpiresAtRealtime;
        private float _defaultFixedDeltaTime;
        private bool _goalCelebrationVisible;
        private float _goalCelebrationEndsAtRealtime;
        private RetrowaveTeam _goalCelebrationTeam;
        private string _goalCelebrationScorer = string.Empty;
        private string _goalCelebrationAssist = string.Empty;
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
        private Button _gameplayStrikerButton;
        private Button _gameplayDefenderButton;
        private Button _gameplayRunnerButton;
        private Button _gameplayDisruptorButton;
        private Button _gameplayResumeButton;
        private Button _gameplayStartButton;
        private Button _gameplayReturnButton;
        private Button _gameplaySettingsButton;
        private TMP_Text _gameplayStartButtonLabel;
        private TMP_Text _gameplayReturnButtonLabel;
        private bool _gameplayMenuWasVisible;
        private GameObject _gameplaySettingsRoot;
        private RectTransform _gameplaySettingsPanelRect;
        private RectTransform _gameplaySettingsContentHost;
        private TMP_Text _gameplaySettingsStatusText;
        private Button _gameplaySettingsCloseButton;
        private readonly Dictionary<GameplaySettingsTab, Image> _gameplaySettingsTabImages = new();
        private readonly Dictionary<RetrowaveBindingAction, TMP_Text> _gameplaySettingsBindingValueTexts = new();
        private GameplaySettingsTab _gameplaySettingsTab = GameplaySettingsTab.Game;
        private RetrowaveBindingAction? _pendingGameplaySettingsBindingAction;
        private bool _showHudInfoPanel = true;
        private GameObject _gameplayHudRoot;
        private GameObject _gameplayHudInfoRoot;
        private GameObject _gameplayHudInfoCollapsedRoot;
        private GameObject _gameplayHudScoreboardRoot;
        private GameObject _gameplayHudGoalRoot;
        private GameObject _gameplayHudCountdownRoot;
        private GameObject _gameplayHudLineupRoot;
        private GameObject _gameplayHudRoundStatsRoot;
        private GameObject _gameplayHudMvpRoot;
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
        private GameObject _hudStyleNotificationRoot;
        private TMP_Text _hudStyleNotificationText;
        private TMP_Text _hudGaugeTitleText;
        private TMP_Text _hudSpeedValueText;
        private TMP_Text _hudSpeedLabelText;
        private TMP_Text _hudBoostValueText;
        private TMP_Text _hudHeatValueText;
        private TMP_Text _hudStyleValueText;
        private TMP_Text _hudGaugeStatusText;
        private GameObject _hudRarePowerUpRoot;
        private Image _hudRarePowerUpIconImage;
        private TMP_Text _hudRarePowerUpText;
        private GameObject _hudObjectivePointerRoot;
        private RectTransform _hudObjectivePointerArrowRect;
        private TMP_Text _hudObjectivePointerArrowText;
        private TMP_Text _hudObjectivePointerText;
        private GameObject _hudRareBeaconPointerRoot;
        private RectTransform _hudRareBeaconPointerArrowRect;
        private TMP_Text _hudRareBeaconPointerArrowText;
        private TMP_Text _hudRareBeaconPointerText;
        private Image _hudSpeedFillImage;
        private Image _hudBoostFillImage;
        private Image _hudHeatFillImage;
        private Image _hudStyleFillImage;
        private RectTransform _hudSpeedBarRect;
        private RectTransform _hudBoostBarRect;
        private RectTransform _hudHeatBarRect;
        private RectTransform _hudStyleBarRect;
        private RectTransform _hudSpeedMarkerRect;
        private RectTransform _hudBoostMarkerRect;
        private RectTransform _hudHeatMarkerRect;
        private RectTransform _hudStyleMarkerRect;
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
        private TMP_Text _hudLineupTitleText;
        private TMP_Text _hudLineupSummaryText;
        private TMP_Text _hudLineupBlueText;
        private TMP_Text _hudLineupPinkText;
        private TMP_Text _hudRoundStatsTitleText;
        private TMP_Text _hudRoundStatsSummaryText;
        private TMP_Text _hudRoundStatsBodyText;
        private TMP_Text _hudMvpTitleText;
        private TMP_Text _hudMvpNameText;
        private TMP_Text _hudMvpDetailText;
        private bool _gameplayHudSessionVisible;
        private bool _hudInfoIntroAutoHidePending;
        private float _hudInfoIntroHideAtRealtime;
        private int _observedStyleAwardSerial;
        private float _styleNotificationHideAtRealtime;
        private int _lineupRoundNumber;
        private int _lineupRoundCount;
        private float _lineupVisibleUntilRealtime;
        private int _roundStatsRoundNumber;
        private int _roundStatsBlueScore;
        private int _roundStatsPinkScore;
        private float _roundStatsVisibleUntilRealtime;
        private ulong _mvpClientId = ulong.MaxValue;
        private int _mvpScore;
        private float _mvpVisibleUntilRealtime;
        private GameObject _podiumPresentationRoot;
        private bool _podiumCameraActive;
        private bool _sessionShutdownInProgress;
        private float _serverSessionReconcileTimer;
        private RetrowaveMatchSettings _currentMatchSettings = RetrowaveMatchSettings.Default;

        public static RetrowaveGameBootstrap Instance => _instance;
        public GameObject PlayerPrefab => _playerPrefab;
        public GameObject BallPrefab => _ballPrefab;
        public GameObject PowerUpPrefab => _powerUpPrefab;
        public GameObject RarePowerUpBeaconPrefab => _rarePowerUpBeaconPrefab;
        public GameObject NeonTrailSegmentPrefab => _neonTrailSegmentPrefab;
        public GameObject GravityBombDevicePrefab => _gravityBombDevicePrefab;
        public GameObject ChronoDomeFieldPrefab => _chronoDomeFieldPrefab;
        public string DefaultAddress => _address;
        public string DefaultPort => _port.ToString();
        public string SuggestedHostAddress => ResolvePreferredAddress();
        public string PreferredDisplayName => _preferredDisplayName;
        public RetrowaveMatchSettings CurrentMatchSettings => _currentMatchSettings;

        public static RetrowaveGameBootstrap EnsureInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var bootstrap = new GameObject("Retrowave Rocket Bootstrap");
            DontDestroyOnLoad(bootstrap);
            return bootstrap.AddComponent<RetrowaveGameBootstrap>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureInstance();
        }

        public static bool IsGameplayInputBlocked()
        {
            if (_instance != null && _instance.ShouldBlockGameplayInput())
            {
                return true;
            }

            return RetrowaveTestArenaManager.Instance != null && RetrowaveTestArenaManager.Instance.IsLocalSettingsVisible;
        }

        public void EnsureRuntimeReady()
        {
            EnsureNetworkRuntime();
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
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplyScenePresentation(SceneManager.GetActiveScene());
        }

        private void Update()
        {
            var activeScene = SceneManager.GetActiveScene();
            var isGameplayScene = IsGameplayScene(activeScene);
            var isTestArenaScene = IsTestArenaScene(activeScene);

            if (!isGameplayScene && !isTestArenaScene)
            {
                _showPauseMenu = false;
                _showScoreboard = false;
                ClearPendingRoleSelectionRequest();
                ClearGoalCelebrationState();
                ClearPodiumPresentation();
                SetGameplayMenuVisible(false);
                CloseGameplaySettingsOverlay();
                if (_gameplayHudRoot != null)
                {
                    _gameplayHudRoot.SetActive(false);
                }
                ResetHudInfoIntroState();
                return;
            }

            if (isTestArenaScene)
            {
                _showPauseMenu = false;
                _showScoreboard = false;
                ClearPendingRoleSelectionRequest();
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

            if (_networkManager == null || !_networkManager.IsListening)
            {
                _showPauseMenu = false;
                _showScoreboard = false;
                ClearPendingRoleSelectionRequest();
                ClearGoalCelebrationState();
                ClearPodiumPresentation();
                SetGameplayMenuVisible(false);
                CloseGameplaySettingsOverlay();
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
                if (IsGameplaySettingsVisible())
                {
                    HandleGameplaySettingsKeyboard(keyboard);
                    _showScoreboard = false;
                }
                else
                {
                    HandleRoleSelectionHotkeys(keyboard);
                    HandleSpectatorFollowHotkeys(keyboard);
                    _showScoreboard = RetrowaveInputBindings.IsPressed(keyboard, RetrowaveBindingAction.Scoreboard);

                    if (RetrowaveInputBindings.WasPressedThisFrame(keyboard, RetrowaveBindingAction.ToggleMatchInfo))
                    {
                        _showHudInfoPanel = !_showHudInfoPanel;
                        _hudInfoIntroAutoHidePending = false;
                    }

                    if (RetrowaveInputBindings.WasPressedThisFrame(keyboard, RetrowaveBindingAction.Pause) && !RequiresRoleSelection())
                    {
                        _showPauseMenu = !_showPauseMenu;
                    }
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

            if (RequiresUtilityRoleSelection())
            {
                if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                {
                    TryRequestUtilityRoleSelection(RetrowaveUtilityRole.Striker);
                    _showPauseMenu = false;
                    return;
                }

                if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                {
                    TryRequestUtilityRoleSelection(RetrowaveUtilityRole.Defender);
                    _showPauseMenu = false;
                    return;
                }

                if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
                {
                    TryRequestUtilityRoleSelection(RetrowaveUtilityRole.Runner);
                    _showPauseMenu = false;
                    return;
                }

                if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame)
                {
                    TryRequestUtilityRoleSelection(RetrowaveUtilityRole.Disruptor);
                    _showPauseMenu = false;
                }

                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame || keyboard.bKey.wasPressedThisFrame)
            {
                TryRequestRoleSelection(RetrowaveLobbyRole.Blue);
                _showPauseMenu = true;
                return;
            }

            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame)
            {
                TryRequestRoleSelection(RetrowaveLobbyRole.Pink);
                _showPauseMenu = true;
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
            if (!CanCycleSpectatorTargets())
            {
                return;
            }

            if (keyboard.leftBracketKey.wasPressedThisFrame || keyboard.commaKey.wasPressedThisFrame)
            {
                RetrowaveCameraRig.CycleSpectatorTarget(-1, GetSpectatorTargets());
                return;
            }

            if (keyboard.rightBracketKey.wasPressedThisFrame || keyboard.periodKey.wasPressedThisFrame)
            {
                RetrowaveCameraRig.CycleSpectatorTarget(1, GetSpectatorTargets());
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
                DestroyGameplaySettingsOverlay();
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

        public bool BeginTestArenaFromMenu(string displayName, out string message)
        {
            if (_pendingConnectionMode != PendingConnectionMode.None)
            {
                message = "A connection is already starting.";
                return false;
            }

            _preferredDisplayName = NormalizeDisplayName(displayName);
            _currentMatchSettings = RetrowaveMatchSettings.Default;
            RetrowaveArenaConfig.ApplyMatchSettings(_currentMatchSettings);
            ShutdownSession();
            message = "Loading the single-player Testing Arena...";
            StartCoroutine(LoadTestArena());
            return true;
        }

        public void OpenSharedSettingsOverlay()
        {
            OpenGameplaySettingsOverlay();
        }

        public void CloseSharedSettingsOverlay()
        {
            CloseGameplaySettingsOverlay();
        }

        public bool IsSharedSettingsOverlayVisible()
        {
            return IsGameplaySettingsVisible();
        }

        public void ReturnToMainMenu()
        {
            var returningFromTestArena = IsTestArenaScene(SceneManager.GetActiveScene());
            _showPauseMenu = false;
            _showScoreboard = false;
            ClearPendingRoleSelectionRequest();
            ClearGoalCelebrationState();
            CloseGameplaySettingsOverlay();

            if (returningFromTestArena)
            {
                RetrowaveTestArenaManager.TearDownForSceneExit();
            }

            ShutdownSession();
            DestroyGameplayMenuOverlay();
            DestroyGameplaySettingsOverlay();
            DestroyGameplayHudOverlay();

            if (SceneManager.GetActiveScene().name != MainMenuSceneName)
            {
                SceneManager.LoadScene(MainMenuSceneName, LoadSceneMode.Single);
            }

            StartCoroutine(FinalizeMainMenuReturn());
        }

        private IEnumerator FinalizeMainMenuReturn()
        {
            while (SceneManager.GetActiveScene().name != MainMenuSceneName)
            {
                yield return null;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;

            yield return null;

            RetrowaveMainMenuController.EnsureEventSystemIsInputSystemCompatible();
        }

        public void BeginGoalCelebration(RetrowaveTeam scoringTeam, string scorerName, string assistName, int blueScore, int pinkScore, float durationSeconds)
        {
            _goalCelebrationVisible = true;
            _goalCelebrationTeam = scoringTeam;
            _goalCelebrationScorer = string.IsNullOrWhiteSpace(scorerName)
                ? (scoringTeam == RetrowaveTeam.Blue ? "Blue Team" : "Pink Team")
                : scorerName;
            _goalCelebrationAssist = string.IsNullOrWhiteSpace(assistName) ? string.Empty : assistName;
            _goalCelebrationBlueScore = blueScore;
            _goalCelebrationPinkScore = pinkScore;
            _goalCelebrationEndsAtRealtime = Time.unscaledTime + Mathf.Max(0.25f, durationSeconds);
            SetLocalTimeScale(0.2f);
        }

        public void ShowPreMatchLineup(int roundNumber, int roundCount, float durationSeconds)
        {
            if (_gameplayHudRoot == null && IsGameplayScene(SceneManager.GetActiveScene()))
            {
                EnsureGameplayHudOverlay();
            }

            _lineupRoundNumber = Mathf.Max(1, roundNumber);
            _lineupRoundCount = Mathf.Max(1, roundCount);
            _lineupVisibleUntilRealtime = Time.unscaledTime + Mathf.Max(0.25f, durationSeconds);
        }

        public void ShowRoundStatCards(int completedRound, int blueScore, int pinkScore, float durationSeconds)
        {
            if (_gameplayHudRoot == null && IsGameplayScene(SceneManager.GetActiveScene()))
            {
                EnsureGameplayHudOverlay();
            }

            _roundStatsRoundNumber = Mathf.Max(1, completedRound);
            _roundStatsBlueScore = Mathf.Max(0, blueScore);
            _roundStatsPinkScore = Mathf.Max(0, pinkScore);
            _roundStatsVisibleUntilRealtime = Time.unscaledTime + Mathf.Max(0.25f, durationSeconds);
        }

        public void ShowMvpMoment(ulong mvpClientId, int mvpScore, float durationSeconds)
        {
            if (_gameplayHudRoot == null && IsGameplayScene(SceneManager.GetActiveScene()))
            {
                EnsureGameplayHudOverlay();
            }

            _mvpClientId = mvpClientId;
            _mvpScore = Mathf.Max(0, mvpScore);
            _mvpVisibleUntilRealtime = Time.unscaledTime + Mathf.Max(0.25f, durationSeconds);
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
            _transport.MaxPacketQueueSize = LanPacketQueueSize;
            _networkManager.NetworkConfig ??= new NetworkConfig();
            _networkManager.NetworkConfig.NetworkTransport = _transport;
            _networkManager.NetworkConfig.EnableSceneManagement = false;
            _networkManager.NetworkConfig.ConnectionApproval = false;
            _networkManager.NetworkConfig.SpawnTimeout = 5f;
            _networkManager.NetworkConfig.TickRate = NetworkSimulationTickRate;
            _networkManager.NetworkConfig.EnableTimeResync = true;
            _networkManager.NetworkConfig.TimeResyncInterval = 10;
            _networkManager.NetworkConfig.EnsureNetworkVariableLengthSafety = true;
            NetworkTransform.InterpolationBufferTickOffset = ClientInterpolationBufferTicks;

            if (_networkManager.NetworkConfig.Prefabs == null)
            {
                _networkManager.NetworkConfig.Prefabs = new NetworkPrefabs();
            }

            BuildRuntimePrefabs();
            RegisterNetworkPrefab(_playerPrefab);
            RegisterNetworkPrefab(_ballPrefab);
            RegisterNetworkPrefab(_powerUpPrefab);
            RegisterNetworkPrefab(_rarePowerUpBeaconPrefab);
            RegisterNetworkPrefab(_neonTrailSegmentPrefab);
            RegisterNetworkPrefab(_gravityBombDevicePrefab);
            RegisterNetworkPrefab(_chronoDomeFieldPrefab);
            RegisterNetworkPrefab(_matchManagerPrefab);
            RegisterRuntimePrefabHandler(_playerPrefab, CreatePlayerInstance);
            RegisterRuntimePrefabHandler(_ballPrefab, CreateBallInstance);
            RegisterRuntimePrefabHandler(_powerUpPrefab, CreatePowerUpInstance);
            RegisterRuntimePrefabHandler(_rarePowerUpBeaconPrefab, CreateRarePowerUpPickupBeaconInstance);
            RegisterRuntimePrefabHandler(_neonTrailSegmentPrefab, CreateNeonTrailSegmentInstance);
            RegisterRuntimePrefabHandler(_gravityBombDevicePrefab, CreateGravityBombDeviceInstance);
            RegisterRuntimePrefabHandler(_chronoDomeFieldPrefab, CreateChronoDomeFieldInstance);
            RegisterRuntimePrefabHandler(_matchManagerPrefab, CreateMatchManagerInstance);
            _networkManager.NetworkConfig.PlayerPrefab = null;

            _networkManager.OnServerStarted += HandleServerStarted;
            _networkManager.OnClientConnectedCallback += HandleNetworkClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleNetworkClientDisconnected;
        }

        private bool BeginConnectionFromMenu(PendingConnectionMode mode, string address, string portText, out string message)
        {
            EnsureNetworkRuntime();

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
            _rarePowerUpBeaconPrefab = CreateRarePowerUpPickupBeaconPrefab();
            _neonTrailSegmentPrefab = CreateNeonTrailSegmentPrefab();
            _gravityBombDevicePrefab = CreateGravityBombDevicePrefab();
            _chronoDomeFieldPrefab = CreateChronoDomeFieldPrefab();
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
            return CreatePlayerPrefab(false, true);
        }

        public GameObject CreateBallInstance()
        {
            return CreateBallPrefab(false, true);
        }

        public GameObject CreateOfflinePlayerInstance()
        {
            return CreatePlayerPrefab(false, false);
        }

        public GameObject CreateOfflineBallInstance()
        {
            return CreateBallPrefab(false, false);
        }

        public GameObject CreatePowerUpInstance()
        {
            return CreatePowerUpPrefab(false, true);
        }

        public GameObject CreateOfflinePowerUpInstance()
        {
            return CreatePowerUpPrefab(false, false);
        }

        public GameObject CreateRarePowerUpPickupBeaconInstance()
        {
            return CreateRarePowerUpPickupBeaconPrefab(false, true);
        }

        public GameObject CreateOfflineRarePowerUpPickupBeaconInstance()
        {
            return CreateRarePowerUpPickupBeaconPrefab(false, false);
        }

        public GameObject CreateNeonTrailSegmentInstance()
        {
            return CreateNeonTrailSegmentPrefab(false, true);
        }

        public GameObject CreateOfflineNeonTrailSegmentInstance()
        {
            return CreateNeonTrailSegmentPrefab(false, false);
        }

        public GameObject CreateGravityBombDeviceInstance()
        {
            return CreateGravityBombDevicePrefab(false, true);
        }

        public GameObject CreateOfflineGravityBombDeviceInstance()
        {
            return CreateGravityBombDevicePrefab(false, false);
        }

        public GameObject CreateChronoDomeFieldInstance()
        {
            return CreateChronoDomeFieldPrefab(false, true);
        }

        public GameObject CreateOfflineChronoDomeFieldInstance()
        {
            return CreateChronoDomeFieldPrefab(false, false);
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

                if (CanCycleSpectatorTargets())
                {
                    GUILayout.Label("Spectator cam: [, ] or < > cycles player follow.");
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
            _gameplayStrikerButton = CreateMenuButton(panel.transform, "StrikerButton", "Striker", new Vector2(-172f, 28f), RetrowaveUtilityRoleCatalog.GetColor(RetrowaveUtilityRole.Striker), () => SelectUtilityRole(RetrowaveUtilityRole.Striker));
            _gameplayDefenderButton = CreateMenuButton(panel.transform, "DefenderButton", "Defender", new Vector2(172f, 28f), RetrowaveUtilityRoleCatalog.GetColor(RetrowaveUtilityRole.Defender), () => SelectUtilityRole(RetrowaveUtilityRole.Defender));
            _gameplayRunnerButton = CreateMenuButton(panel.transform, "RunnerButton", "Runner", new Vector2(-172f, -50f), RetrowaveUtilityRoleCatalog.GetColor(RetrowaveUtilityRole.Runner), () => SelectUtilityRole(RetrowaveUtilityRole.Runner));
            _gameplayDisruptorButton = CreateMenuButton(panel.transform, "DisruptorButton", "Disruptor", new Vector2(172f, -50f), RetrowaveUtilityRoleCatalog.GetColor(RetrowaveUtilityRole.Disruptor), () => SelectUtilityRole(RetrowaveUtilityRole.Disruptor));
            _gameplayStartButton = CreateMenuButton(panel.transform, "StartButton", "Start Match", new Vector2(0f, -164f), new Color(0.14f, 0.64f, 0.42f, 1f), HandleGameplayStartMatch);
            _gameplayStartButtonLabel = _gameplayStartButton.GetComponentInChildren<TextMeshProUGUI>(true);
            _gameplayResumeButton = CreateMenuButton(panel.transform, "ResumeButton", "Resume", new Vector2(-132f, -242f), new Color(0.12f, 0.24f, 0.36f, 1f), HandleGameplayResume);
            _gameplayReturnButton = CreateMenuButton(panel.transform, "ReturnButton", "Return To Main Menu", new Vector2(132f, -242f), new Color(0.26f, 0.12f, 0.16f, 1f), ReturnToMainMenu);
            _gameplaySettingsButton = CreateMenuButton(panel.transform, "SettingsButton", "Settings", new Vector2(248f, 246f), new Color(0.17f, 0.24f, 0.38f, 1f), OpenGameplaySettingsOverlay);
            _gameplayReturnButtonLabel = _gameplayReturnButton.GetComponentInChildren<TextMeshProUGUI>(true);
            SetMenuButtonLayout(_gameplaySettingsButton, new Vector2(248f, 246f), new Vector2(150f, 38f));

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
            _gameplayStrikerButton = null;
            _gameplayDefenderButton = null;
            _gameplayRunnerButton = null;
            _gameplayDisruptorButton = null;
            _gameplayResumeButton = null;
            _gameplayStartButton = null;
            _gameplayReturnButton = null;
            _gameplaySettingsButton = null;
            _gameplayStartButtonLabel = null;
            _gameplayReturnButtonLabel = null;
            _gameplayMenuWasVisible = false;
            DestroyGameplaySettingsOverlay();
        }

        private void EnsureGameplayEventSystem()
        {
            var eventSystem = FindAnyObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                _gameplayMenuEventSystem = new GameObject("Gameplay EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                _gameplayMenuEventSystem.transform.SetParent(transform, false);
                eventSystem = _gameplayMenuEventSystem.GetComponent<EventSystem>();
            }

            eventSystem.sendNavigationEvents = true;
            var inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();

            if (inputSystemModule == null)
            {
                inputSystemModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            inputSystemModule.AssignDefaultActions();
            inputSystemModule.enabled = true;
            eventSystem.enabled = true;
            eventSystem.gameObject.SetActive(true);

            var legacyModule = eventSystem.GetComponent<StandaloneInputModule>();

            if (legacyModule != null)
            {
                legacyModule.enabled = false;
            }

        }

        private EventSystem GetInteractiveGameplayEventSystem()
        {
            var eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < eventSystems.Length; i++)
            {
                var eventSystem = eventSystems[i];

                if (eventSystem != null
                    && eventSystem.isActiveAndEnabled
                    && eventSystem.currentInputModule != null
                    && eventSystem.gameObject.activeInHierarchy)
                {
                    return eventSystem;
                }
            }

            return null;
        }

        private void RefreshGameplayMenuState()
        {
            if (_gameplayMenuRoot == null)
            {
                return;
            }

            var sessionBootstrapPending = RequiresSessionBootstrap();
            var forceSelection = RequiresRoleSelection();
            var choosingUtility = IsChoosingUtilityRole();
            var wasVisible = _gameplayMenuWasVisible;
            var matchManager = GetActiveMatchManager();
            var podiumActive = matchManager != null && matchManager.IsPodium;
            var matchComplete = matchManager != null && matchManager.IsMatchComplete;

            if (podiumActive)
            {
                _showPauseMenu = false;
                CloseGameplaySettingsOverlay();
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
                _gameplayMenuTitleText.text = choosingUtility ? "Choose Utility Role" : (forceSelection ? "Choose Your Role" : "Match Menu");
                _gameplayMenuBodyText.text = choosingUtility
                    ? "Pick a subtle vehicle identity for this team. Roles give small edges without replacing skill."
                    : (forceSelection
                        ? "Pick blue, pink, or spectator before jumping fully into the lobby."
                        : "Swap teams, spectate, or control the match flow from here.");
                _gameplayMenuHintText.text = choosingUtility
                    ? "1 Striker: stronger shots  •  2 Defender: clears/grip  •  3 Runner: boost efficiency  •  4 Disruptor: longer effects"
                    : (forceSelection
                        ? "Keyboard also works: 1 / B = Blue, 2 / P = Pink, 3 / S = Spectator"
                        : "Esc closes this menu after you've chosen a role.");
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

            _gameplayStartButton.gameObject.SetActive(hostIsPresent && !forceSelection);
            _gameplayBlueButton.gameObject.SetActive(!choosingUtility);
            _gameplayPinkButton.gameObject.SetActive(!choosingUtility);
            _gameplaySpectateButton.gameObject.SetActive(!choosingUtility);
            _gameplayStrikerButton.gameObject.SetActive(choosingUtility);
            _gameplayDefenderButton.gameObject.SetActive(choosingUtility);
            _gameplayRunnerButton.gameObject.SetActive(choosingUtility);
            _gameplayDisruptorButton.gameObject.SetActive(choosingUtility);
            _gameplayResumeButton.gameObject.SetActive(!forceSelection && !sessionBootstrapPending && !matchComplete);
            _gameplaySettingsButton.gameObject.SetActive(!sessionBootstrapPending);
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
            _gameplayStrikerButton.interactable = RetrowavePlayerController.LocalPlayer != null;
            _gameplayDefenderButton.interactable = RetrowavePlayerController.LocalPlayer != null;
            _gameplayRunnerButton.interactable = RetrowavePlayerController.LocalPlayer != null;
            _gameplayDisruptorButton.interactable = RetrowavePlayerController.LocalPlayer != null;

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
                _gameplayMenuFooterText.text = choosingUtility
                    ? "Your role icon appears beside your name tag and in your driver HUD."
                    : "Use the buttons above to enter the arena.";
            }
            else if (hasLocalEntry && localEntry.QueuedForNextRound)
            {
                _gameplayMenuFooterText.text = CanCycleSpectatorTargets()
                    ? "Your new team selection is queued for next round. Spectator cams stay available with [ and ]."
                    : "Your new team selection is queued for the next round.";
            }
            else if (hostIsPresent && !hostCanStart && matchManager != null)
            {
                _gameplayMenuFooterText.text = "Host start unlocks once at least one player is on blue and pink.";
            }
            else
            {
                _gameplayMenuFooterText.text = CanCycleSpectatorTargets()
                    ? "Spectators can cycle player follow cams with [ and ]."
                    : "Team changes apply immediately for this client.";
            }

            if (!wasVisible)
            {
                var defaultButton = choosingUtility
                    ? _gameplayStrikerButton
                    : (forceSelection ? _gameplayBlueButton : (_gameplayResumeButton.gameObject.activeSelf ? _gameplayResumeButton : _gameplayBlueButton));
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
                14f,
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

            _hudStyleNotificationRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "StyleNotification",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -148f),
                new Vector2(520f, 58f),
                new Color(0.02f, 0.05f, 0.08f, 0.78f),
                new Color(0.42f, 1f, 0.72f, 0.45f));

            _hudStyleNotificationText = CreateHudText(
                _hudStyleNotificationRoot.transform,
                "Text",
                string.Empty,
                23f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(480f, 40f),
                new Color(0.74f, 1f, 0.82f, 1f));
            _hudStyleNotificationRoot.SetActive(false);

            _hudObjectivePointerRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "ObjectivePointer",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(-176f, -218f),
                new Vector2(320f, 52f),
                new Color(0.02f, 0.06f, 0.1f, 0.82f),
                new Color(1f, 0.72f, 0.16f, 0.46f));
            _hudObjectivePointerArrowText = CreateHudText(
                _hudObjectivePointerRoot.transform,
                "Arrow",
                "▲",
                25f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(28f, 0f),
                new Vector2(32f, 32f),
                new Color(1f, 0.72f, 0.16f, 1f));
            _hudObjectivePointerArrowRect = _hudObjectivePointerArrowText.rectTransform;
            _hudObjectivePointerText = CreateHudText(
                _hudObjectivePointerRoot.transform,
                "Label",
                string.Empty,
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(54f, 0f),
                new Vector2(248f, 38f),
                Color.white);
            _hudObjectivePointerRoot.SetActive(false);

            _hudRareBeaconPointerRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "RareBeaconPointer",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(176f, -218f),
                new Vector2(320f, 52f),
                new Color(0.02f, 0.06f, 0.1f, 0.82f),
                new Color(0.42f, 1f, 0.72f, 0.46f));
            _hudRareBeaconPointerArrowText = CreateHudText(
                _hudRareBeaconPointerRoot.transform,
                "Arrow",
                "▲",
                25f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(28f, 0f),
                new Vector2(32f, 32f),
                new Color(0.42f, 1f, 0.72f, 1f));
            _hudRareBeaconPointerArrowRect = _hudRareBeaconPointerArrowText.rectTransform;
            _hudRareBeaconPointerText = CreateHudText(
                _hudRareBeaconPointerRoot.transform,
                "Label",
                string.Empty,
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(54f, 0f),
                new Vector2(248f, 38f),
                Color.white);
            _hudRareBeaconPointerRoot.SetActive(false);

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
                new Vector2(430f, 260f),
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

            _hudRarePowerUpRoot = CreateHudPanel(
                gaugesPanel.transform,
                "RarePowerUpIndicator",
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -46f),
                new Vector2(220f, 32f),
                new Color(0.03f, 0.08f, 0.12f, 0.86f),
                new Color(0.12f, 1f, 0.4f, 0.42f));

            var rareIcon = CreateUiObject("RareIcon", _hudRarePowerUpRoot.transform, typeof(RectTransform), typeof(Image));
            var rareIconRect = rareIcon.GetComponent<RectTransform>();
            rareIconRect.anchorMin = new Vector2(0f, 0.5f);
            rareIconRect.anchorMax = new Vector2(0f, 0.5f);
            rareIconRect.pivot = new Vector2(0f, 0.5f);
            rareIconRect.anchoredPosition = new Vector2(7f, 0f);
            rareIconRect.sizeDelta = new Vector2(22f, 22f);
            _hudRarePowerUpIconImage = rareIcon.GetComponent<Image>();

            _hudRarePowerUpText = CreateHudText(
                _hudRarePowerUpRoot.transform,
                "RareLabel",
                "ARMED",
                13f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(36f, 0f),
                new Vector2(176f, 24f),
                Color.white);
            _hudRarePowerUpRoot.SetActive(false);

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

            _hudHeatValueText = CreateHudText(
                gaugesPanel.transform,
                "HeatValue",
                "HEAT 0%",
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -192f),
                new Vector2(170f, 22f),
                new Color(1f, 0.64f, 0.32f, 0.95f));

            CreateHudBar(
                gaugesPanel.transform,
                "HeatBar",
                new Vector2(24f, -214f),
                new Vector2(382f, 10f),
                new Color(0.08f, 0.12f, 0.18f, 0.95f),
                new Color(1f, 0.38f, 0.08f, 1f),
                out _hudHeatFillImage,
                out _hudHeatBarRect,
                out _hudHeatMarkerRect);

            _hudStyleValueText = CreateHudText(
                gaugesPanel.transform,
                "StyleValue",
                "STYLE 0%",
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -226f),
                new Vector2(170f, 22f),
                new Color(0.52f, 1f, 0.72f, 0.95f));

            CreateHudBar(
                gaugesPanel.transform,
                "StyleBar",
                new Vector2(24f, -248f),
                new Vector2(382f, 10f),
                new Color(0.08f, 0.12f, 0.18f, 0.95f),
                new Color(0.14f, 1f, 0.48f, 1f),
                out _hudStyleFillImage,
                out _hudStyleBarRect,
                out _hudStyleMarkerRect);

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

            _gameplayHudLineupRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "PreMatchLineup",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -210f),
                new Vector2(920f, 250f),
                new Color(0.02f, 0.04f, 0.09f, 0.92f),
                new Color(0.12f, 0.8f, 1f, 0.34f));

            _hudLineupTitleText = CreateHudText(
                _gameplayHudLineupRoot.transform,
                "Title",
                "MATCH LINEUP",
                25f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -18f),
                new Vector2(560f, 34f),
                Color.white);

            _hudLineupSummaryText = CreateHudText(
                _gameplayHudLineupRoot.transform,
                "Summary",
                string.Empty,
                15f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -52f),
                new Vector2(620f, 24f),
                new Color(0.64f, 0.9f, 1f, 0.96f));

            _hudLineupBlueText = CreateLineupColumn(
                _gameplayHudLineupRoot.transform,
                "BlueLineup",
                new Vector2(-230f, -86f),
                "BLUE TEAM",
                RetrowaveStyle.BlueGlow);

            _hudLineupPinkText = CreateLineupColumn(
                _gameplayHudLineupRoot.transform,
                "PinkLineup",
                new Vector2(230f, -86f),
                "PINK TEAM",
                RetrowaveStyle.PinkBase);

            _gameplayHudRoundStatsRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "RoundStatCards",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 64f),
                new Vector2(860f, 232f),
                new Color(0.03f, 0.04f, 0.09f, 0.94f),
                new Color(0.42f, 1f, 0.72f, 0.32f));

            _hudRoundStatsTitleText = CreateHudText(
                _gameplayHudRoundStatsRoot.transform,
                "Title",
                "ROUND STATS",
                25f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -18f),
                new Vector2(620f, 34f),
                Color.white);

            _hudRoundStatsSummaryText = CreateHudText(
                _gameplayHudRoundStatsRoot.transform,
                "Summary",
                string.Empty,
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -54f),
                new Vector2(720f, 26f),
                new Color(0.68f, 0.92f, 1f, 0.96f));

            _hudRoundStatsBodyText = CreateHudText(
                _gameplayHudRoundStatsRoot.transform,
                "Body",
                string.Empty,
                16f,
                FontStyles.Bold,
                TextAlignmentOptions.TopLeft,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -88f),
                new Vector2(760f, 118f),
                new Color(0.92f, 0.98f, 1f, 0.98f));
            _hudRoundStatsBodyText.lineSpacing = 2f;
            _hudRoundStatsBodyText.textWrappingMode = TextWrappingModes.NoWrap;
            _hudRoundStatsBodyText.overflowMode = TextOverflowModes.Ellipsis;

            _gameplayHudMvpRoot = CreateHudPanel(
                _gameplayHudRoot.transform,
                "MvpMoment",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 238f),
                new Vector2(720f, 180f),
                new Color(0.03f, 0.03f, 0.08f, 0.95f),
                new Color(1f, 0.78f, 0.18f, 0.38f));

            _hudMvpTitleText = CreateHudText(
                _gameplayHudMvpRoot.transform,
                "Title",
                "MVP MOMENT",
                21f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -16f),
                new Vector2(520f, 30f),
                new Color(1f, 0.82f, 0.24f, 1f));

            _hudMvpNameText = CreateHudText(
                _gameplayHudMvpRoot.transform,
                "Name",
                string.Empty,
                31f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 8f),
                new Vector2(600f, 42f),
                Color.white);
            _hudMvpNameText.enableAutoSizing = true;
            _hudMvpNameText.fontSizeMin = 22f;
            _hudMvpNameText.fontSizeMax = 31f;
            _hudMvpNameText.textWrappingMode = TextWrappingModes.NoWrap;
            _hudMvpNameText.overflowMode = TextOverflowModes.Ellipsis;

            _hudMvpDetailText = CreateHudText(
                _gameplayHudMvpRoot.transform,
                "Detail",
                string.Empty,
                15f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -48f),
                new Vector2(640f, 58f),
                new Color(0.94f, 0.98f, 1f, 0.96f));
            _hudMvpDetailText.enableAutoSizing = true;
            _hudMvpDetailText.fontSizeMin = 11f;
            _hudMvpDetailText.fontSizeMax = 15f;
            _hudMvpDetailText.textWrappingMode = TextWrappingModes.Normal;
            _hudMvpDetailText.overflowMode = TextOverflowModes.Ellipsis;

            _gameplayHudInfoCollapsedRoot.SetActive(false);
            _gameplayHudScoreboardRoot.SetActive(false);
            _gameplayHudGoalRoot.SetActive(false);
            _gameplayHudCountdownRoot.SetActive(false);
            _gameplayHudLineupRoot.SetActive(false);
            _gameplayHudRoundStatsRoot.SetActive(false);
            _gameplayHudMvpRoot.SetActive(false);
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
            _gameplayHudLineupRoot = null;
            _gameplayHudRoundStatsRoot = null;
            _gameplayHudMvpRoot = null;
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
            _hudStyleNotificationRoot = null;
            _hudStyleNotificationText = null;
            _hudGaugeTitleText = null;
            _hudSpeedValueText = null;
            _hudSpeedLabelText = null;
            _hudBoostValueText = null;
            _hudHeatValueText = null;
            _hudStyleValueText = null;
            _hudGaugeStatusText = null;
            _hudRarePowerUpRoot = null;
            _hudRarePowerUpIconImage = null;
            _hudRarePowerUpText = null;
            _hudObjectivePointerRoot = null;
            _hudObjectivePointerArrowRect = null;
            _hudObjectivePointerArrowText = null;
            _hudObjectivePointerText = null;
            _hudRareBeaconPointerRoot = null;
            _hudRareBeaconPointerArrowRect = null;
            _hudRareBeaconPointerArrowText = null;
            _hudRareBeaconPointerText = null;
            _hudSpeedFillImage = null;
            _hudBoostFillImage = null;
            _hudHeatFillImage = null;
            _hudStyleFillImage = null;
            _hudSpeedBarRect = null;
            _hudBoostBarRect = null;
            _hudHeatBarRect = null;
            _hudStyleBarRect = null;
            _hudSpeedMarkerRect = null;
            _hudBoostMarkerRect = null;
            _hudHeatMarkerRect = null;
            _hudStyleMarkerRect = null;
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
            _hudLineupTitleText = null;
            _hudLineupSummaryText = null;
            _hudLineupBlueText = null;
            _hudLineupPinkText = null;
            _hudRoundStatsTitleText = null;
            _hudRoundStatsSummaryText = null;
            _hudRoundStatsBodyText = null;
            _hudMvpTitleText = null;
            _hudMvpNameText = null;
            _hudMvpDetailText = null;
            _observedStyleAwardSerial = 0;
            _styleNotificationHideAtRealtime = 0f;
            ClearMatchPresentationState();
        }

        private void RefreshGameplayHudState()
        {
            if (_gameplayHudRoot == null)
            {
                return;
            }

            var isVisible = _networkManager != null
                            && _networkManager.IsListening
                            && IsGameplayScene(SceneManager.GetActiveScene())
                            && RetrowaveGameSettings.ShowHud;
            _gameplayHudRoot.SetActive(isVisible);

            if (!isVisible)
            {
                ResetHudInfoIntroState();
                HideStyleNotification(resetObservedSerial: true);
                HideTargetPointers();
                HideMatchPresentationHud();
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
                    else if (localPlayer != null && localPlayer.IsArenaParticipant && !localPlayer.HasSelectedUtilityRole)
                    {
                        roleLine += "\nChoose a utility role to finish entering the arena.";
                    }
                    else if (localPlayer != null && localPlayer.IsArenaParticipant)
                    {
                        roleLine += $"\nUtility: {RetrowaveUtilityRoleCatalog.GetLabel(localPlayer.UtilityRole)}";
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

                if (CanCycleSpectatorTargets())
                {
                    hint += "\nSpectator cam: [ / ] cycles player follow.";
                }

                if (matchManager != null
                    && matchManager.TryGetComponent<RetrowaveArenaObjectiveSystem>(out var objectives)
                    && objectives.ActiveObjectiveType != RetrowaveArenaObjectiveType.None)
                {
                    hint += $"\nHold objective: {RetrowaveArenaObjectiveCatalog.GetLabel(objectives.ActiveObjectiveType)} {Mathf.RoundToInt(objectives.CaptureProgressNormalized * 100f)}%";
                }

                _hudInfoHintText.text = hint;
            }

            if (_hudGaugeTitleText != null)
            {
                _hudGaugeTitleText.text = localPlayer != null
                    ? $"{localPlayer.Team.ToString().ToUpperInvariant()} {RetrowaveUtilityRoleCatalog.GetLabel(localPlayer.UtilityRole).ToUpperInvariant()}"
                    : "SPECTATOR CAM";
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
                    var hasRarePowerUp = localPlayer.TryGetComponent<RarePowerUpInventory>(out var rareInventory)
                                         && rareInventory.HasHeldPowerUp;
                    var heldType = hasRarePowerUp ? rareInventory.HeldType : RetrowaveRarePowerUpType.None;
                    _hudGaugeStatusText.text = hasRarePowerUp
                        ? $"Armed: {GetRarePowerUpLabel(heldType)}"
                        : localPlayer.IsStunned
                            ? "STUNNED"
                            : localPlayer.IsSlowed
                            ? "SLOWED"
                            : localPlayer.IsOvercharged
                            ? "OVERDRIVE"
                            : localPlayer.IsOverheated
                            ? "Overheated"
                            : localPlayer.HasSpeedBoost
                            ? "Speed burst"
                            : (localPlayer.IsGroundedForHud ? "Grounded" : "Airborne");

                    RefreshHudRarePowerUpIndicator(heldType);
                }

                if (_hudSpeedFillImage != null)
                {
                    _hudSpeedFillImage.fillAmount = localPlayer.SpeedNormalized;
                }

                if (_hudBoostFillImage != null)
                {
                    _hudBoostFillImage.fillAmount = localPlayer.BoostNormalized;
                }

                if (_hudHeatValueText != null)
                {
                    _hudHeatValueText.text = localPlayer.IsOverheated
                        ? "HEAT OVER"
                        : $"HEAT {Mathf.RoundToInt(localPlayer.HeatNormalized * 100f)}%";
                }

                if (_hudStyleValueText != null)
                {
                    _hudStyleValueText.text = $"STYLE {Mathf.RoundToInt(localPlayer.StyleNormalized * 100f)}%";
                }

                if (_hudHeatFillImage != null)
                {
                    _hudHeatFillImage.fillAmount = localPlayer.HeatNormalized;
                    _hudHeatFillImage.color = localPlayer.IsOvercharged
                        ? Color.Lerp(new Color(1f, 0.86f, 0.24f, 1f), Color.white, Mathf.PingPong(Time.time * 3.5f, 0.18f))
                        : localPlayer.IsOverheated
                        ? Color.Lerp(new Color(1f, 0.08f, 0.04f, 1f), Color.white, Mathf.PingPong(Time.time * 4f, 0.25f))
                        : Color.Lerp(new Color(1f, 0.5f, 0.08f, 1f), new Color(1f, 0.1f, 0.04f, 1f), localPlayer.HeatNormalized);
                }

                if (_hudStyleFillImage != null)
                {
                    _hudStyleFillImage.fillAmount = localPlayer.StyleNormalized;
                    _hudStyleFillImage.color = Color.Lerp(new Color(0.14f, 1f, 0.48f, 1f), new Color(0.94f, 1f, 0.2f, 1f), localPlayer.StyleNormalized);
                }

                UpdateHudBarMarker(_hudSpeedBarRect, _hudSpeedMarkerRect, localPlayer.SpeedNormalized);
                UpdateHudBarMarker(_hudBoostBarRect, _hudBoostMarkerRect, localPlayer.BoostNormalized);
                UpdateHudBarMarker(_hudHeatBarRect, _hudHeatMarkerRect, localPlayer.HeatNormalized);
                UpdateHudBarMarker(_hudStyleBarRect, _hudStyleMarkerRect, localPlayer.StyleNormalized);
                RefreshStyleNotification(localPlayer);
                RefreshTargetPointers(localPlayer, matchManager);
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

                if (_hudHeatValueText != null)
                {
                    _hudHeatValueText.text = "HEAT --";
                }

                if (_hudStyleValueText != null)
                {
                    _hudStyleValueText.text = "STYLE --";
                }

                if (_hudGaugeStatusText != null)
                {
                    _hudGaugeStatusText.text = RetrowaveCameraRig.GetSpectatorCameraLabel();
                }

                RefreshHudRarePowerUpIndicator(RetrowaveRarePowerUpType.None);

                if (_hudSpeedFillImage != null)
                {
                    _hudSpeedFillImage.fillAmount = 0f;
                }

                if (_hudBoostFillImage != null)
                {
                    _hudBoostFillImage.fillAmount = 0f;
                }

                if (_hudHeatFillImage != null)
                {
                    _hudHeatFillImage.fillAmount = 0f;
                }

                if (_hudStyleFillImage != null)
                {
                    _hudStyleFillImage.fillAmount = 0f;
                }

                UpdateHudBarMarker(_hudSpeedBarRect, _hudSpeedMarkerRect, 0f);
                UpdateHudBarMarker(_hudBoostBarRect, _hudBoostMarkerRect, 0f);
                UpdateHudBarMarker(_hudHeatBarRect, _hudHeatMarkerRect, 0f);
                UpdateHudBarMarker(_hudStyleBarRect, _hudStyleMarkerRect, 0f);
                HideStyleNotification(resetObservedSerial: true);
                HideTargetPointers();
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
                    _hudGoalDetailText.text = string.IsNullOrWhiteSpace(_goalCelebrationAssist)
                        ? $"{_goalCelebrationScorer} scores. Setting up kickoff..."
                        : $"{_goalCelebrationScorer} scores. Assist: {_goalCelebrationAssist}. Setting up kickoff...";
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

            RefreshMatchPresentationHud(matchManager);
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

                var saveCompare = right.Saves.CompareTo(left.Saves);

                if (saveCompare != 0)
                {
                    return saveCompare;
                }

                var styleCompare = right.StyleScore.CompareTo(left.StyleScore);

                if (styleCompare != 0)
                {
                    return styleCompare;
                }

                return left.ClientId.CompareTo(right.ClientId);
            });

            if (entries.Count == 0)
            {
                return "No players in this section yet.";
            }

            var builder = new StringBuilder(entries.Count * 56);
            builder.Append("<mspace=0.56em>");
            builder.AppendLine("PLAYER                G A S STL OBJ PWR PING");
            builder.AppendLine("---------------------------------------------");

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

                builder.Append(FormatScoreboardName(playerLabel, 20).PadRight(20));
                builder.Append(entry.Goals.ToString().PadLeft(2));
                builder.Append(" ");
                builder.Append(entry.Assists.ToString().PadLeft(1));
                builder.Append(" ");
                builder.Append(entry.Saves.ToString().PadLeft(1));
                builder.Append(" ");
                builder.Append(FormatCompactStat(entry.StyleScore).PadLeft(3));
                builder.Append(" ");
                builder.Append(entry.ObjectiveCaptures.ToString().PadLeft(3));
                builder.Append(" ");
                builder.Append(entry.PowerUpHits.ToString().PadLeft(3));
                builder.Append(" ");
                builder.Append($"{entry.PingMs}ms".PadLeft(6));
                builder.AppendLine();
            }

            builder.Append("</mspace>");
            return builder.ToString();
        }

        private void RefreshMatchPresentationHud(RetrowaveMatchManager matchManager)
        {
            var now = Time.unscaledTime;
            var showLineup = matchManager != null
                             && !_goalCelebrationVisible
                             && now < _lineupVisibleUntilRealtime;
            var showRoundStats = matchManager != null
                                 && !_goalCelebrationVisible
                                 && now < _roundStatsVisibleUntilRealtime;
            var showMvp = matchManager != null
                          && !_goalCelebrationVisible
                          && now < _mvpVisibleUntilRealtime;

            if (_gameplayHudLineupRoot != null)
            {
                _gameplayHudLineupRoot.SetActive(showLineup);
            }

            if (_gameplayHudRoundStatsRoot != null)
            {
                _gameplayHudRoundStatsRoot.SetActive(showRoundStats);
            }

            if (_gameplayHudMvpRoot != null)
            {
                _gameplayHudMvpRoot.SetActive(showMvp);
            }

            if (showLineup)
            {
                RefreshLineupPanel(matchManager);
            }

            if (showRoundStats)
            {
                RefreshRoundStatsPanel(matchManager);
            }

            if (showMvp)
            {
                RefreshMvpPanel(matchManager);
            }
        }

        private void RefreshLineupPanel(RetrowaveMatchManager matchManager)
        {
            if (_hudLineupTitleText != null)
            {
                _hudLineupTitleText.text = "PRE-MATCH LINEUP";
            }

            if (_hudLineupSummaryText != null)
            {
                _hudLineupSummaryText.text = $"Round {_lineupRoundNumber}/{Mathf.Max(1, _lineupRoundCount)}  •  Blue {matchManager.BlueScore} - {matchManager.PinkScore} Pink";
            }

            if (_hudLineupBlueText != null)
            {
                _hudLineupBlueText.text = BuildLineupColumnText(RetrowaveLobbyRole.Blue, matchManager);
            }

            if (_hudLineupPinkText != null)
            {
                _hudLineupPinkText.text = BuildLineupColumnText(RetrowaveLobbyRole.Pink, matchManager);
            }
        }

        private void RefreshRoundStatsPanel(RetrowaveMatchManager matchManager)
        {
            if (_hudRoundStatsTitleText != null)
            {
                _hudRoundStatsTitleText.text = $"ROUND {_roundStatsRoundNumber} STAT CARDS";
            }

            if (_hudRoundStatsSummaryText != null)
            {
                _hudRoundStatsSummaryText.text = $"Blue {_roundStatsBlueScore}  -  {_roundStatsPinkScore} Pink";
            }

            if (_hudRoundStatsBodyText != null)
            {
                _hudRoundStatsBodyText.text = BuildRoundStatCardsText(matchManager);
            }
        }

        private void RefreshMvpPanel(RetrowaveMatchManager matchManager)
        {
            var entry = default(RetrowaveLobbyEntry);
            var hasEntry = matchManager != null && matchManager.TryGetLobbyEntry(_mvpClientId, out entry);
            var displayName = hasEntry ? FormatScoreboardName(entry.DisplayName.ToString(), 24) : "Match MVP";
            var detail = hasEntry
                ? $"{GetRoleLabel(entry)}  |  MVP {_mvpScore}\nG {entry.Goals}   A {entry.Assists}   S {entry.Saves}   STYLE {FormatCompactStat(entry.StyleScore)}   OBJ {entry.ObjectiveCaptures}   PWR {entry.PowerUpHits}"
                : $"MVP {_mvpScore}";

            if (_hudMvpTitleText != null)
            {
                _hudMvpTitleText.text = "MVP MOMENT";
            }

            if (_hudMvpNameText != null)
            {
                _hudMvpNameText.text = displayName;
                _hudMvpNameText.color = hasEntry && TryGetTeamFromEntry(entry, out var team)
                    ? RetrowaveStyle.GetTeamGlow(team)
                    : Color.white;
            }

            if (_hudMvpDetailText != null)
            {
                _hudMvpDetailText.text = detail;
            }
        }

        private string BuildLineupColumnText(RetrowaveLobbyRole role, RetrowaveMatchManager matchManager)
        {
            var entries = new List<RetrowaveLobbyEntry>();

            if (matchManager != null)
            {
                for (var i = 0; i < matchManager.LobbyEntries.Count; i++)
                {
                    var entry = matchManager.LobbyEntries[i];

                    if (!entry.HasSelectedRole || entry.ActiveRole != role)
                    {
                        continue;
                    }

                    entries.Add(entry);
                }
            }

            entries.Sort(static (left, right) => left.ClientId.CompareTo(right.ClientId));

            if (entries.Count == 0)
            {
                return "Waiting for drivers";
            }

            var builder = new StringBuilder(entries.Count * 34);

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                builder.Append(i + 1);
                builder.Append(". ");
                builder.Append(FormatScoreboardName(entry.DisplayName.ToString(), 28));

                if (entry.IsHost)
                {
                    builder.Append(" [Host]");
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private string BuildRoundStatCardsText(RetrowaveMatchManager matchManager)
        {
            if (matchManager == null || matchManager.LobbyEntries.Count == 0)
            {
                return "Stats are syncing...";
            }

            var builder = new StringBuilder(384);
            builder.Append("<mspace=0.54em>");
            builder.AppendLine(TopStatLine("GOALS", matchManager, static entry => entry.Goals));
            builder.AppendLine(TopStatLine("ASSISTS", matchManager, static entry => entry.Assists));
            builder.AppendLine(TopStatLine("SAVES", matchManager, static entry => entry.Saves));
            builder.AppendLine(TopStatLine("STYLE", matchManager, static entry => entry.StyleScore, compactValue: true));
            builder.AppendLine(TopStatLine("OBJECTIVES", matchManager, static entry => entry.ObjectiveCaptures));
            builder.AppendLine(TopStatLine("POWER HITS", matchManager, static entry => entry.PowerUpHits));
            builder.Append("</mspace>");
            return builder.ToString();
        }

        private static string TopStatLine(string label, RetrowaveMatchManager matchManager, Func<RetrowaveLobbyEntry, int> selector, bool compactValue = false)
        {
            var bestEntry = default(RetrowaveLobbyEntry);
            var bestValue = 0;
            var hasEntry = false;

            for (var i = 0; i < matchManager.LobbyEntries.Count; i++)
            {
                var entry = matchManager.LobbyEntries[i];

                if (!entry.HasSelectedRole || (entry.ActiveRole != RetrowaveLobbyRole.Blue && entry.ActiveRole != RetrowaveLobbyRole.Pink))
                {
                    continue;
                }

                var value = selector(entry);

                if (!hasEntry || value > bestValue || (value == bestValue && entry.ClientId < bestEntry.ClientId))
                {
                    bestEntry = entry;
                    bestValue = value;
                    hasEntry = true;
                }
            }

            var name = hasEntry && bestValue > 0 ? FormatScoreboardName(bestEntry.DisplayName.ToString(), 20) : "No leader yet";
            var valueText = compactValue ? FormatCompactStat(bestValue) : bestValue.ToString();
            return $"{label.PadRight(10)} {name.PadRight(20)} {valueText.PadLeft(5)}";
        }

        private static bool TryGetTeamFromEntry(RetrowaveLobbyEntry entry, out RetrowaveTeam team)
        {
            if (entry.ActiveRole == RetrowaveLobbyRole.Blue)
            {
                team = RetrowaveTeam.Blue;
                return true;
            }

            if (entry.ActiveRole == RetrowaveLobbyRole.Pink)
            {
                team = RetrowaveTeam.Pink;
                return true;
            }

            team = RetrowaveTeam.Blue;
            return false;
        }

        private void HideMatchPresentationHud()
        {
            if (_gameplayHudLineupRoot != null)
            {
                _gameplayHudLineupRoot.SetActive(false);
            }

            if (_gameplayHudRoundStatsRoot != null)
            {
                _gameplayHudRoundStatsRoot.SetActive(false);
            }

            if (_gameplayHudMvpRoot != null)
            {
                _gameplayHudMvpRoot.SetActive(false);
            }
        }

        private void ClearMatchPresentationState()
        {
            _lineupRoundNumber = 0;
            _lineupRoundCount = 0;
            _lineupVisibleUntilRealtime = 0f;
            _roundStatsRoundNumber = 0;
            _roundStatsBlueScore = 0;
            _roundStatsPinkScore = 0;
            _roundStatsVisibleUntilRealtime = 0f;
            _mvpClientId = ulong.MaxValue;
            _mvpScore = 0;
            _mvpVisibleUntilRealtime = 0f;
            HideMatchPresentationHud();
        }

        private static string FormatCompactStat(int value)
        {
            if (value >= 10000)
            {
                return $"{value / 1000f:0.#}k";
            }

            if (value >= 1000)
            {
                return $"{value / 1000f:0.0}k";
            }

            return Mathf.Max(0, value).ToString();
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

        private static string FormatScoreboardName(string playerLabel, int maxLength = 21)
        {
            var trimmed = string.IsNullOrWhiteSpace(playerLabel) ? "Player" : playerLabel.Trim();
            var clampedLength = Mathf.Max(4, maxLength);
            return trimmed.Length <= clampedLength ? trimmed : $"{trimmed[..(clampedLength - 1)]}~";
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

        private TMP_Text CreateLineupColumn(Transform parent, string name, Vector2 anchoredPosition, string title, Color titleColor)
        {
            var section = CreateHudPanel(
                parent,
                name,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                anchoredPosition,
                new Vector2(400f, 136f),
                new Color(0.04f, 0.08f, 0.14f, 0.88f),
                new Color(titleColor.r, titleColor.g, titleColor.b, 0.24f));

            CreateHudText(
                section.transform,
                "Header",
                title,
                18f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -12f),
                new Vector2(320f, 28f),
                titleColor);

            var body = CreateHudText(
                section.transform,
                "Body",
                string.Empty,
                15f,
                FontStyles.Bold,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -46f),
                new Vector2(356f, 78f),
                new Color(0.92f, 0.96f, 1f, 0.98f));
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
            if (TryRequestRoleSelection(role))
            {
                _showPauseMenu = role != RetrowaveLobbyRole.Spectator;

                if (role == RetrowaveLobbyRole.Spectator)
                {
                    SetGameplayMenuVisible(false);
                }
            }
        }

        private void SelectUtilityRole(RetrowaveUtilityRole role)
        {
            if (TryRequestUtilityRoleSelection(role))
            {
                _showPauseMenu = false;
                SetGameplayMenuVisible(false);
            }
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
                SetMenuButtonLayout(_gameplayStrikerButton, new Vector2(-196f, 38f), new Vector2(186f, 52f));
                SetMenuButtonLayout(_gameplayDefenderButton, new Vector2(196f, 38f), new Vector2(186f, 52f));
                SetMenuButtonLayout(_gameplayRunnerButton, new Vector2(-196f, -38f), new Vector2(186f, 52f));
                SetMenuButtonLayout(_gameplayDisruptorButton, new Vector2(196f, -38f), new Vector2(186f, 52f));
                SetMenuButtonLayout(_gameplayStartButton, new Vector2(0f, -148f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplayResumeButton, new Vector2(0f, -286f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplayReturnButton, new Vector2(0f, -286f), new Vector2(380f, 52f));
                SetMenuButtonLayout(_gameplaySettingsButton, new Vector2(290f, 294f), new Vector2(150f, 38f));
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
            SetMenuButtonLayout(_gameplayStrikerButton, new Vector2(-170f, 34f), new Vector2(154f, 52f));
            SetMenuButtonLayout(_gameplayDefenderButton, new Vector2(170f, 34f), new Vector2(154f, 52f));
            SetMenuButtonLayout(_gameplayRunnerButton, new Vector2(-170f, -42f), new Vector2(154f, 52f));
            SetMenuButtonLayout(_gameplayDisruptorButton, new Vector2(170f, -42f), new Vector2(154f, 52f));
            SetMenuButtonLayout(_gameplayStartButton, new Vector2(0f, -164f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplayResumeButton, new Vector2(-132f, -242f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplayReturnButton, new Vector2(132f, -242f), new Vector2(320f, 52f));
            SetMenuButtonLayout(_gameplaySettingsButton, new Vector2(250f, 244f), new Vector2(150f, 38f));
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

        private bool IsGameplaySettingsVisible()
        {
            return _gameplaySettingsRoot != null && _gameplaySettingsRoot.activeSelf;
        }

        private void HandleGameplaySettingsKeyboard(Keyboard keyboard)
        {
            if (_pendingGameplaySettingsBindingAction.HasValue)
            {
                if (RetrowaveInputBindings.TryGetPressedKeyThisFrame(keyboard, out var key))
                {
                    RetrowaveInputBindings.SetBinding(_pendingGameplaySettingsBindingAction.Value, key);
                    _pendingGameplaySettingsBindingAction = null;
                    RefreshGameplaySettingsBindingTexts();
                    RefreshGameplaySettingsStatus();
                }

                return;
            }

            if (RetrowaveInputBindings.WasPressedThisFrame(keyboard, RetrowaveBindingAction.Pause))
            {
                CloseGameplaySettingsOverlay();
            }
        }

        private void OpenGameplaySettingsOverlay()
        {
            EnsureGameplayEventSystem();
            EnsureGameplaySettingsOverlay();

            if (_gameplaySettingsRoot == null)
            {
                return;
            }

            _pendingGameplaySettingsBindingAction = null;
            _showPauseMenu = true;
            _gameplaySettingsRoot.SetActive(true);
            _gameplaySettingsRoot.transform.SetAsLastSibling();
            RebuildGameplaySettingsContent();
            RefreshGameplaySettingsStatus();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            var eventSystem = GetInteractiveGameplayEventSystem();

            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(_gameplaySettingsCloseButton != null
                    ? _gameplaySettingsCloseButton.gameObject
                    : _gameplaySettingsRoot);
            }
        }

        private void CloseGameplaySettingsOverlay()
        {
            _pendingGameplaySettingsBindingAction = null;

            if (_gameplaySettingsRoot != null)
            {
                _gameplaySettingsRoot.SetActive(false);
            }

            RefreshGameplaySettingsStatus();
        }

        private void DestroyGameplaySettingsOverlay()
        {
            if (_gameplaySettingsRoot != null)
            {
                Destroy(_gameplaySettingsRoot);
                _gameplaySettingsRoot = null;
            }

            _gameplaySettingsPanelRect = null;
            _gameplaySettingsContentHost = null;
            _gameplaySettingsStatusText = null;
            _gameplaySettingsCloseButton = null;
            _gameplaySettingsTabImages.Clear();
            _gameplaySettingsBindingValueTexts.Clear();
            _pendingGameplaySettingsBindingAction = null;
        }

        private void EnsureGameplaySettingsOverlay()
        {
            if (_gameplaySettingsRoot != null)
            {
                return;
            }

            EnsureGameplayEventSystem();
            _gameplayMenuFont = TMP_Settings.defaultFontAsset;

            _gameplaySettingsRoot = new GameObject("Retrowave Gameplay Settings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _gameplaySettingsRoot.transform.SetParent(transform, false);

            var canvas = _gameplaySettingsRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;

            var scaler = _gameplaySettingsRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var backdrop = CreateUiObject("Backdrop", _gameplaySettingsRoot.transform, typeof(RectTransform), typeof(Image), typeof(Button));
            var backdropRect = backdrop.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.58f);
            backdrop.GetComponent<Button>().onClick.AddListener(CloseGameplaySettingsOverlay);

            var panel = CreateUiObject("Panel", _gameplaySettingsRoot.transform, typeof(RectTransform), typeof(Image));
            _gameplaySettingsPanelRect = panel.GetComponent<RectTransform>();
            _gameplaySettingsPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _gameplaySettingsPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _gameplaySettingsPanelRect.pivot = new Vector2(0.5f, 0.5f);
            _gameplaySettingsPanelRect.sizeDelta = new Vector2(1240f, 760f);
            panel.GetComponent<Image>().color = new Color(0.04f, 0.07f, 0.13f, 0.97f);

            var outline = panel.AddComponent<Outline>();
            outline.effectColor = new Color(0.09f, 0.75f, 1f, 0.78f);
            outline.effectDistance = new Vector2(2f, 2f);

            var title = CreateSettingsText(panel.transform, "Title", "SETTINGS", 34f, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -32f), new Vector2(520f, 48f), new Vector2(0f, 1f));

            _gameplaySettingsStatusText = CreateSettingsText(panel.transform, "Status", string.Empty, 16f, FontStyles.Normal, TextAlignmentOptions.Left, new Color(0.58f, 0.86f, 1f, 1f));
            SetRect(_gameplaySettingsStatusText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -82f), new Vector2(760f, 30f), new Vector2(0f, 1f));

            _gameplaySettingsCloseButton = CreateSettingsButton(panel.transform, "CloseButton", "Back", new Color(0.14f, 0.22f, 0.38f, 0.96f), CloseGameplaySettingsOverlay);
            SetRect(_gameplaySettingsCloseButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -32f), new Vector2(150f, 42f), new Vector2(1f, 1f));

            CreateGameplaySettingsTabButton(panel.transform, GameplaySettingsTab.Game, "Game", new Vector2(-480f, 204f));
            CreateGameplaySettingsTabButton(panel.transform, GameplaySettingsTab.Controls, "Controls", new Vector2(-480f, 132f));
            CreateGameplaySettingsTabButton(panel.transform, GameplaySettingsTab.Video, "Video", new Vector2(-480f, 60f));
            CreateGameplaySettingsTabButton(panel.transform, GameplaySettingsTab.KeyBindings, "Key Bindings", new Vector2(-480f, -12f));

            var contentHost = CreateUiObject("ContentHost", panel.transform, typeof(RectTransform), typeof(Image));
            contentHost.transform.SetSiblingIndex(0);
            _gameplaySettingsContentHost = contentHost.GetComponent<RectTransform>();
            SetRect(_gameplaySettingsContentHost, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(150f, -48f), new Vector2(870f, 590f), new Vector2(0.5f, 0.5f));
            var contentHostImage = contentHost.GetComponent<Image>();
            contentHostImage.color = new Color(0.07f, 0.11f, 0.19f, 0.94f);
            contentHostImage.raycastTarget = false;

            _gameplaySettingsRoot.SetActive(false);
        }

        private void CreateGameplaySettingsTabButton(Transform parent, GameplaySettingsTab tab, string label, Vector2 anchoredPosition)
        {
            var button = CreateSettingsButton(parent, $"{tab}Tab", label, new Color(0.1f, 0.14f, 0.24f, 0.92f), () => SetGameplaySettingsTab(tab));
            SetRect(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(240f, 54f), new Vector2(0.5f, 0.5f));
            _gameplaySettingsTabImages[tab] = button.GetComponent<Image>();
        }

        private void SetGameplaySettingsTab(GameplaySettingsTab tab)
        {
            _gameplaySettingsTab = tab;
            _pendingGameplaySettingsBindingAction = null;
            RebuildGameplaySettingsContent();
            RefreshGameplaySettingsStatus();
        }

        private void RebuildGameplaySettingsContent()
        {
            if (_gameplaySettingsContentHost == null)
            {
                return;
            }

            foreach (var pair in _gameplaySettingsTabImages)
            {
                pair.Value.color = pair.Key == _gameplaySettingsTab
                    ? new Color(0.12f, 0.32f, 0.58f, 0.98f)
                    : new Color(0.1f, 0.14f, 0.24f, 0.92f);
            }

            ClearUiChildren(_gameplaySettingsContentHost);
            _gameplaySettingsBindingValueTexts.Clear();

            var content = CreateSettingsScrollContent(_gameplaySettingsContentHost);

            switch (_gameplaySettingsTab)
            {
                case GameplaySettingsTab.Controls:
                    BuildGameplayControlsSettings(content);
                    break;
                case GameplaySettingsTab.Video:
                    BuildGameplayVideoSettings(content);
                    break;
                case GameplaySettingsTab.KeyBindings:
                    BuildGameplayKeyBindingSettings(content);
                    break;
                default:
                    BuildGameplayGameSettings(content);
                    break;
            }

            Canvas.ForceUpdateCanvases();
            if (content is RectTransform contentRect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            }
        }

        private Transform CreateSettingsScrollContent(RectTransform parent)
        {
            var viewport = CreateUiObject("Viewport", parent, typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(18f, 18f);
            viewportRect.offsetMax = new Vector2(-32f, -18f);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.08f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = CreateUiObject("Content", viewport.transform, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 34);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbarObject = CreateUiObject("Scrollbar", parent, typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-18f, 18f);
            scrollbarRect.offsetMax = new Vector2(-8f, -18f);
            scrollbarObject.GetComponent<Image>().color = new Color(0.13f, 0.19f, 0.3f, 0.9f);

            var slidingArea = CreateUiObject("Sliding Area", scrollbarObject.transform, typeof(RectTransform));
            var slidingRect = slidingArea.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(1f, 1f);
            slidingRect.offsetMax = new Vector2(-1f, -1f);

            var handle = CreateUiObject("Handle", slidingArea.transform, typeof(RectTransform), typeof(Image));
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 1f);
            handleRect.anchorMax = new Vector2(1f, 1f);
            handleRect.pivot = new Vector2(0.5f, 1f);
            handleRect.sizeDelta = new Vector2(0f, 48f);
            handle.GetComponent<Image>().color = new Color(0.39f, 0.72f, 1f, 0.96f);

            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.value = 1f;

            var scrollRect = viewport.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 28f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            return content.transform;
        }

        private void BuildGameplayGameSettings(Transform content)
        {
            CreateSettingsSection(content, "Game");
            CreateSettingsToggleRow(content, "Show HUD", RetrowaveGameSettings.ShowHud, value => RetrowaveGameSettings.SetShowHud(value));
            CreateSettingsSliderRow(content, "Music Volume", RetrowaveGameSettings.MusicVolume, RetrowaveGameSettings.SetMusicVolume);

            if (IsTestArenaScene(SceneManager.GetActiveScene()))
            {
                CreateSettingsNote(content, "Esc closes this panel in Test Arena. Use the button below to leave the arena.");
                var returnButton = CreateSettingsButton(content, "ReturnToMainMenu", "Return To Main Menu", new Color(0.26f, 0.12f, 0.16f, 0.98f), () =>
                {
                    CloseGameplaySettingsOverlay();
                    ReturnToMainMenu();
                });
                returnButton.gameObject.GetComponent<LayoutElement>().preferredHeight = 46f;
                returnButton.gameObject.GetComponent<LayoutElement>().preferredWidth = 260f;
            }

            CreateSettingsNote(content, "These settings apply immediately and carry back to the main menu.");
        }

        private void BuildGameplayControlsSettings(Transform content)
        {
            CreateSettingsSection(content, "Controls");
            CreateSettingsToggleRow(content, "Invert Camera Look", RetrowaveGameSettings.InvertLook, value => RetrowaveGameSettings.SetInvertLook(value));
            CreateSettingsSliderRow(content, "Look Sensitivity X", RetrowaveGameSettings.LookSensitivityXNormalized, RetrowaveGameSettings.SetLookSensitivityX);
            CreateSettingsSliderRow(content, "Look Sensitivity Y", RetrowaveGameSettings.LookSensitivityYNormalized, RetrowaveGameSettings.SetLookSensitivityY);
            CreateSettingsNote(content, "Key rebinding has its own tab so it can use a larger scrollable list.");
        }

        private void BuildGameplayVideoSettings(Transform content)
        {
            CreateSettingsSection(content, "Video");
            CreateSettingsToggleRow(content, "Full Screen", RetrowaveGameSettings.Fullscreen, value => RetrowaveGameSettings.SetFullscreen(value));
            CreateSettingsToggleRow(content, "V-Sync", RetrowaveGameSettings.VSync, value => RetrowaveGameSettings.SetVSync(value));
            CreateSettingsToggleRow(content, "Ambient Occlusion", RetrowaveGameSettings.AmbientOcclusion, value => RetrowaveGameSettings.SetAmbientOcclusion(value));
            CreateSettingsToggleRow(content, "Motion Blur", RetrowaveGameSettings.MotionBlur, value => RetrowaveGameSettings.SetMotionBlur(value));
            CreateSettingsChoiceRow(content, "Shadow Quality", new[] { "Off", "Low", "High" }, (int)RetrowaveGameSettings.ShadowQuality, index => RetrowaveGameSettings.SetShadowQuality((RetrowaveShadowQuality)index));
            CreateSettingsChoiceRow(content, "Texture Quality", new[] { "Low", "Med", "High" }, (int)RetrowaveGameSettings.TextureQuality, index => RetrowaveGameSettings.SetTextureQuality((RetrowaveTextureQuality)index));
            CreateSettingsChoiceRow(content, "VFX Density", new[] { "Low", "Med", "High" }, (int)RetrowaveGameSettings.VfxDensity, index => RetrowaveGameSettings.SetVfxDensity((RetrowaveVfxDensity)index));
            CreateSettingsChoiceRow(content, "Camera Effects", new[] { "Clean", "Retro", "Cinema", "Neon" }, (int)RetrowaveGameSettings.CameraEffectPreset, index => RetrowaveGameSettings.SetCameraEffectPreset((RetrowaveCameraEffectPreset)index));
        }

        private void BuildGameplayKeyBindingSettings(Transform content)
        {
            CreateSettingsSection(content, "Key Bindings");
            CreateSettingsNote(content, "Click a binding, then press the key you want. Changes save immediately.");

            var definitions = RetrowaveInputBindings.AllDefinitions;

            foreach (RetrowaveBindingCategory category in Enum.GetValues(typeof(RetrowaveBindingCategory)))
            {
                CreateSettingsSection(content, category switch
                {
                    RetrowaveBindingCategory.Driving => "Driving",
                    RetrowaveBindingCategory.Camera => "Camera",
                    _ => "Menu",
                });

                for (var i = 0; i < definitions.Count; i++)
                {
                    if (definitions[i].Category != category)
                    {
                        continue;
                    }

                    CreateSettingsBindingRow(content, definitions[i]);
                }
            }

            var resetButton = CreateSettingsButton(content, "ResetBindings", "Reset Defaults", new Color(0.16f, 0.24f, 0.4f, 0.96f), () =>
            {
                RetrowaveInputBindings.ResetToDefaults();
                _pendingGameplaySettingsBindingAction = null;
                RebuildGameplaySettingsContent();
                RefreshGameplaySettingsStatus();
            });
            resetButton.gameObject.GetComponent<LayoutElement>().preferredHeight = 46f;
        }

        private void CreateSettingsSection(Transform parent, string label)
        {
            var text = CreateSettingsText(parent, $"{label}Section", label.ToUpperInvariant(), 21f, FontStyles.Bold, TextAlignmentOptions.Left, new Color(0.66f, 0.9f, 1f, 1f));
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 34f;
            layout.preferredHeight = 34f;
        }

        private void CreateSettingsNote(Transform parent, string label)
        {
            var text = CreateSettingsText(parent, "Note", label, 14f, FontStyles.Italic, TextAlignmentOptions.Left, new Color(0.72f, 0.84f, 0.92f, 0.94f));
            text.textWrappingMode = TextWrappingModes.Normal;
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 34f;
            layout.preferredHeight = 34f;
        }

        private void CreateSettingsToggleRow(Transform parent, string label, bool value, Action<bool> onChanged)
        {
            CreateSettingsChoiceRow(parent, label, new[] { "Off", "On" }, value ? 1 : 0, index => onChanged(index == 1));
        }

        private void CreateSettingsChoiceRow(Transform parent, string label, string[] choices, int selectedIndex, Action<int> onChanged)
        {
            var row = CreateSettingsRow(parent, label, 58f);

            var group = CreateUiObject("Choices", row.transform, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var groupLayout = group.GetComponent<HorizontalLayoutGroup>();
            groupLayout.spacing = 8f;
            groupLayout.childControlWidth = false;
            groupLayout.childControlHeight = true;
            groupLayout.childForceExpandWidth = false;
            groupLayout.childForceExpandHeight = true;
            group.GetComponent<LayoutElement>().preferredWidth = Mathf.Max(220f, choices.Length * 104f);

            for (var i = 0; i < choices.Length; i++)
            {
                var optionIndex = i;
                var selected = optionIndex == selectedIndex;
                var button = CreateSettingsButton(group.transform, $"{choices[i]}Choice", choices[i], selected ? new Color(0.14f, 0.42f, 0.66f, 0.98f) : new Color(0.13f, 0.18f, 0.3f, 0.96f), () =>
                {
                    onChanged(optionIndex);
                    RebuildGameplaySettingsContent();
                    RefreshGameplaySettingsStatus();
                });
                button.gameObject.GetComponent<LayoutElement>().preferredWidth = 96f;
            }
        }

        private void CreateSettingsSliderRow(Transform parent, string label, float value, Action<float> onChanged)
        {
            var row = CreateSettingsRow(parent, label, 70f);

            var valueText = CreateSettingsText(row.transform, "Value", $"{Mathf.RoundToInt(value * 100f)}%", 16f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 72f;

            var slider = CreateSettingsSlider(row.transform, value);
            slider.gameObject.GetComponent<LayoutElement>().preferredWidth = 330f;
            slider.onValueChanged.AddListener(newValue =>
            {
                valueText.text = $"{Mathf.RoundToInt(newValue * 100f)}%";
                onChanged(newValue);
            });
        }

        private void CreateSettingsBindingRow(Transform parent, RetrowaveBindingDefinition definition)
        {
            var row = CreateSettingsRow(parent, definition.Label, 48f);
            var value = _pendingGameplaySettingsBindingAction == definition.Action
                ? "Press key..."
                : RetrowaveInputBindings.GetBindingDisplayName(definition.Action);
            var button = CreateSettingsButton(row.transform, $"{definition.Action}Binding", value, new Color(0.14f, 0.22f, 0.38f, 0.96f), () =>
            {
                _pendingGameplaySettingsBindingAction = definition.Action;
                RefreshGameplaySettingsBindingTexts();
                RefreshGameplaySettingsStatus();
            });
            button.gameObject.GetComponent<LayoutElement>().preferredWidth = 210f;
            _gameplaySettingsBindingValueTexts[definition.Action] = button.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        private GameObject CreateSettingsRow(Transform parent, string label, float height)
        {
            var row = CreateUiObject($"{label}Row", parent, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<Image>().color = new Color(0.1f, 0.15f, 0.24f, 0.82f);
            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 8, 8);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var labelText = CreateSettingsText(row.transform, "Label", label, 17f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, Color.white);
            var labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelLayout.minWidth = 250f;

            return row;
        }

        private Slider CreateSettingsSlider(Transform parent, float value)
        {
            var sliderObject = CreateUiObject("Slider", parent, typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
            var sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.sizeDelta = new Vector2(320f, 26f);

            var background = CreateUiObject("Background", sliderObject.transform, typeof(RectTransform), typeof(Image));
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 12f);
            background.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.14f, 1f);

            var fillArea = CreateUiObject("Fill Area", sliderObject.transform, typeof(RectTransform));
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.offsetMin = new Vector2(6f, -6f);
            fillAreaRect.offsetMax = new Vector2(-6f, 6f);

            var fill = CreateUiObject("Fill", fillArea.transform, typeof(RectTransform), typeof(Image));
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(0.91f, 0.28f, 0.74f, 1f);

            var handleArea = CreateUiObject("Handle Slide Area", sliderObject.transform, typeof(RectTransform));
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);

            var handle = CreateUiObject("Handle", handleArea.transform, typeof(RectTransform), typeof(Image));
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(22f, 22f);
            handle.GetComponent<Image>().color = new Color(0.39f, 0.84f, 1f, 1f);

            var slider = sliderObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Mathf.Clamp01(value);
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        private Button CreateSettingsButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var image = buttonObject.GetComponent<Image>();
            image.color = color;
            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = 150f;
            layout.preferredHeight = 38f;

            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.22f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateSettingsText(buttonObject.transform, "Label", label, 16f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        private TMP_Text CreateSettingsText(Transform parent, string name, string value, float fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
        {
            var textObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(TextMeshProUGUI));
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _gameplayMenuFont != null ? _gameplayMenuFont : TMP_Settings.defaultFontAsset;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void RefreshGameplaySettingsBindingTexts()
        {
            foreach (var pair in _gameplaySettingsBindingValueTexts)
            {
                pair.Value.text = _pendingGameplaySettingsBindingAction == pair.Key
                    ? "Press key..."
                    : RetrowaveInputBindings.GetBindingDisplayName(pair.Key);
            }
        }

        private void RefreshGameplaySettingsStatus()
        {
            if (_gameplaySettingsStatusText == null)
            {
                return;
            }

            _gameplaySettingsStatusText.text = _pendingGameplaySettingsBindingAction.HasValue
                ? $"Listening for {RetrowaveInputBindings.GetDefinition(_pendingGameplaySettingsBindingAction.Value).Label}..."
                : "Adjust game, controls, video, and bindings. Changes save immediately.";
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void ClearUiChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
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
            var choosingUtility = IsChoosingUtilityRole();
            var matchManager = GetActiveMatchManager();
            var title = choosingUtility ? "Choose Utility Role" : (forceSelection ? "Choose Your Role" : "Match Menu");

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 210f, 70f, 420f, 340f), GUI.skin.window);
            GUILayout.Label(title);

            if (choosingUtility)
            {
                GUILayout.Label("Pick a lightweight vehicle identity for this team.");
                GUILayout.Label("Shortcuts: 1 = Striker, 2 = Defender, 3 = Runner, 4 = Disruptor");
            }
            else if (forceSelection)
            {
                GUILayout.Label("Pick blue, pink, or spectator before jumping fully into the lobby.");
                GUILayout.Label("Shortcuts: 1 or B = Blue, 2 or P = Pink, 3 or S = Spectator");
            }
            else
            {
                GUILayout.Label("Swap teams, spectate, or control the match flow from here.");
            }

            GUILayout.Space(10f);

            if (choosingUtility)
            {
                DrawUtilityRoleButton("Striker", RetrowaveUtilityRole.Striker);
                DrawUtilityRoleButton("Defender", RetrowaveUtilityRole.Defender);
                DrawUtilityRoleButton("Runner", RetrowaveUtilityRole.Runner);
                DrawUtilityRoleButton("Disruptor", RetrowaveUtilityRole.Disruptor);
            }
            else
            {
                DrawRoleButton("Join Blue Team", RetrowaveLobbyRole.Blue);
                DrawRoleButton("Join Pink Team", RetrowaveLobbyRole.Pink);
                DrawRoleButton("Spectate", RetrowaveLobbyRole.Spectator);
            }

            if (!forceSelection && matchManager != null && TryGetLocalLobbyEntry(out var entry) && entry.IsHost)
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

            if (TryRequestRoleSelection(role))
            {
                _showPauseMenu = role != RetrowaveLobbyRole.Spectator;
            }
        }

        private void DrawUtilityRoleButton(string label, RetrowaveUtilityRole role)
        {
            if (!GUILayout.Button(label, GUILayout.Height(32f)))
            {
                return;
            }

            if (TryRequestUtilityRoleSelection(role))
            {
                _showPauseMenu = false;
            }
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

        private bool CanCycleSpectatorTargets()
        {
            var matchManager = GetActiveMatchManager();

            if (matchManager == null
                || (!matchManager.IsWarmup && !matchManager.IsCountdown && !matchManager.IsLiveMatch))
            {
                return false;
            }

            if (!TryGetLocalLobbyEntry(out var entry))
            {
                return false;
            }

            return entry.HasSelectedRole
                   && entry.ActiveRole == RetrowaveLobbyRole.Spectator
                   && RetrowavePlayerController.LocalOwner == null
                   && GetSpectatorTargets().Count > 0;
        }

        private List<RetrowavePlayerController> GetSpectatorTargets()
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
            if (!HasConfirmedRoleSelection())
            {
                if (HasPendingRoleSelectionRequest())
                {
                    return false;
                }

                return RetrowavePlayerController.LocalPlayer != null || TryGetLocalLobbyEntry(out _);
            }

            if (RequiresUtilityRoleSelection())
            {
                return true;
            }

            ClearPendingRoleSelectionRequest();
            return false;
        }

        private bool RequiresUtilityRoleSelection()
        {
            var localPlayer = RetrowavePlayerController.LocalPlayer;

            if (localPlayer == null)
            {
                return false;
            }

            return localPlayer.HasSelectedRole
                   && localPlayer.IsArenaParticipant
                   && !localPlayer.HasSelectedUtilityRole;
        }

        private bool IsChoosingUtilityRole()
        {
            return HasConfirmedRoleSelection() && RequiresUtilityRoleSelection();
        }

        private bool IsChoosingTeamRole()
        {
            return !HasConfirmedRoleSelection() && !HasPendingRoleSelectionRequest();
        }

        private bool HasConfirmedRoleSelection()
        {
            if (RetrowavePlayerController.LocalPlayer != null && RetrowavePlayerController.LocalPlayer.HasSelectedRole)
            {
                return true;
            }

            return TryGetLocalLobbyEntry(out var entry) && entry.HasSelectedRole;
        }

        private bool HasPendingRoleSelectionRequest()
        {
            if (!_roleSelectionRequestPending)
            {
                return false;
            }

            if (Time.unscaledTime <= _roleSelectionRequestExpiresAtRealtime)
            {
                return true;
            }

            ClearPendingRoleSelectionRequest();
            return false;
        }

        private void MarkRoleSelectionRequestPending()
        {
            _roleSelectionRequestPending = true;
            _roleSelectionRequestExpiresAtRealtime = Time.unscaledTime + RoleSelectionRequestTimeoutSeconds;
        }

        private void ClearPendingRoleSelectionRequest()
        {
            _roleSelectionRequestPending = false;
            _roleSelectionRequestExpiresAtRealtime = 0f;
        }

        private bool ShouldBlockGameplayInput()
        {
            var matchManager = GetActiveMatchManager();
            var gameplayLocked = _goalCelebrationVisible
                                 || (matchManager != null && matchManager.IsGameplayLocked);

            return _networkManager != null
                   && _networkManager.IsListening
                   && IsGameplayScene(SceneManager.GetActiveScene())
                   && (_showPauseMenu || RequiresRoleSelection() || gameplayLocked || IsGameplaySettingsVisible());
        }

        private static string GetTeamLabel(RetrowaveTeam team)
        {
            return team == RetrowaveTeam.Blue ? "Blue Team" : "Pink Team";
        }

        private static string GetRarePowerUpLabel(RetrowaveRarePowerUpType type)
        {
            return type switch
            {
                RetrowaveRarePowerUpType.NeonSnareTrail => "Neon snare",
                RetrowaveRarePowerUpType.GravityBomb => "Gravity bomb",
                RetrowaveRarePowerUpType.ChronoDome => "Chrono dome",
                _ => "None",
            };
        }

        private void RefreshHudRarePowerUpIndicator(RetrowaveRarePowerUpType type)
        {
            if (_hudRarePowerUpRoot == null)
            {
                return;
            }

            var isArmed = type != RetrowaveRarePowerUpType.None;
            _hudRarePowerUpRoot.SetActive(isArmed);

            if (!isArmed)
            {
                return;
            }

            var color = RetrowavePlayerController.GetRarePowerUpColor(type);

            if (_hudRarePowerUpIconImage != null)
            {
                _hudRarePowerUpIconImage.color = color;
            }

            if (_hudRarePowerUpText != null)
            {
                _hudRarePowerUpText.text = $"ARMED {GetRarePowerUpLabel(type).ToUpperInvariant()}";
                _hudRarePowerUpText.color = Color.Lerp(color, Color.white, 0.18f);
            }
        }

        private void RefreshStyleNotification(RetrowavePlayerController localPlayer)
        {
            if (_hudStyleNotificationRoot == null || _hudStyleNotificationText == null || localPlayer == null)
            {
                return;
            }

            var serial = localPlayer.LastStyleAwardSerial;

            if (serial != 0 && serial != _observedStyleAwardSerial)
            {
                _observedStyleAwardSerial = serial;
                var points = Mathf.Max(1, Mathf.RoundToInt(localPlayer.LastStyleAwardPoints));
                var styleEvent = localPlayer.LastStyleAwardEvent;
                var color = GetStyleEventColor(styleEvent);
                _hudStyleNotificationText.text = $"+{points} STYLE  {RetrowaveStyleEventCatalog.GetLabel(styleEvent).ToUpperInvariant()}";
                _hudStyleNotificationText.color = Color.Lerp(color, Color.white, 0.18f);
                _hudStyleNotificationRoot.SetActive(true);
                _styleNotificationHideAtRealtime = Time.unscaledTime + 1.8f;
            }

            if (_hudStyleNotificationRoot.activeSelf && Time.unscaledTime >= _styleNotificationHideAtRealtime)
            {
                _hudStyleNotificationRoot.SetActive(false);
            }
        }

        private void HideStyleNotification(bool resetObservedSerial)
        {
            if (_hudStyleNotificationRoot != null)
            {
                _hudStyleNotificationRoot.SetActive(false);
            }

            _styleNotificationHideAtRealtime = 0f;

            if (resetObservedSerial)
            {
                _observedStyleAwardSerial = 0;
            }
        }

        private void RefreshTargetPointers(RetrowavePlayerController localPlayer, RetrowaveMatchManager matchManager)
        {
            if (localPlayer == null)
            {
                HideTargetPointers();
                return;
            }

            RefreshObjectivePointer(localPlayer, matchManager);
            RefreshRareBeaconPointer(localPlayer);
        }

        private void RefreshObjectivePointer(RetrowavePlayerController localPlayer, RetrowaveMatchManager matchManager)
        {
            if (_hudObjectivePointerRoot == null || _hudObjectivePointerText == null)
            {
                return;
            }

            if (matchManager == null
                || !matchManager.TryGetComponent<RetrowaveArenaObjectiveSystem>(out var objectives)
                || objectives.ActiveObjectiveType == RetrowaveArenaObjectiveType.None)
            {
                _hudObjectivePointerRoot.SetActive(false);
                return;
            }

            var distance = Vector3.Distance(localPlayer.transform.position, objectives.ObjectivePosition);
            var objectiveColor = RetrowaveArenaObjectiveCatalog.GetColor(objectives.ActiveObjectiveType);
            var teamPrefix = objectives.CapturingTeamValue switch
            {
                (int)RetrowaveTeam.Blue => "Blue",
                (int)RetrowaveTeam.Pink => "Pink",
                _ => "--",
            };
            var text = $"OBJ {teamPrefix}  {Mathf.RoundToInt(distance)}m\n{RetrowaveArenaObjectiveCatalog.GetLabel(objectives.ActiveObjectiveType).ToUpperInvariant()} {Mathf.RoundToInt(objectives.CaptureProgressNormalized * 100f)}%";
            UpdateTargetPointer(
                _hudObjectivePointerRoot,
                _hudObjectivePointerArrowRect,
                _hudObjectivePointerArrowText,
                _hudObjectivePointerText,
                localPlayer.transform.position,
                objectives.ObjectivePosition,
                text,
                objectiveColor);
        }

        private void RefreshRareBeaconPointer(RetrowavePlayerController localPlayer)
        {
            if (_hudRareBeaconPointerRoot == null || _hudRareBeaconPointerText == null)
            {
                return;
            }

            var beacon = FindNearestRareBeacon(localPlayer.transform.position);

            if (beacon == null)
            {
                _hudRareBeaconPointerRoot.SetActive(false);
                return;
            }

            var beaconPosition = beacon.transform.position;
            var distance = Vector3.Distance(localPlayer.transform.position, beaconPosition);
            var color = RetrowavePlayerController.GetRarePowerUpColor(beacon.HeldType);
            var progressText = beacon.RequiresCapture ? $"{Mathf.RoundToInt(beacon.CaptureProgress * 100f)}%" : "READY";
            var text = $"RARE {Mathf.RoundToInt(distance)}m\n{GetRarePowerUpLabel(beacon.HeldType).ToUpperInvariant()} {progressText}";
            UpdateTargetPointer(
                _hudRareBeaconPointerRoot,
                _hudRareBeaconPointerArrowRect,
                _hudRareBeaconPointerArrowText,
                _hudRareBeaconPointerText,
                localPlayer.transform.position,
                beaconPosition,
                text,
                color);
        }

        private static RarePowerUpPickupBeacon FindNearestRareBeacon(Vector3 fromPosition)
        {
            RarePowerUpPickupBeacon nearest = null;
            var nearestDistance = float.MaxValue;
            var activeBeacons = RarePowerUpPickupBeacon.Active;

            for (var i = 0; i < activeBeacons.Count; i++)
            {
                var beacon = activeBeacons[i];

                if (beacon == null || !beacon.IsActive)
                {
                    continue;
                }

                var distance = (beacon.transform.position - fromPosition).sqrMagnitude;

                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearest = beacon;
                nearestDistance = distance;
            }

            return nearest;
        }

        private static void UpdateTargetPointer(
            GameObject root,
            RectTransform arrowRect,
            TMP_Text arrowText,
            TMP_Text labelText,
            Vector3 fromPosition,
            Vector3 targetPosition,
            string label,
            Color color)
        {
            if (root == null || labelText == null)
            {
                return;
            }

            root.SetActive(true);
            labelText.text = label;
            labelText.color = Color.Lerp(color, Color.white, 0.2f);

            if (arrowText != null)
            {
                arrowText.color = color;
            }

            if (arrowRect == null)
            {
                return;
            }

            var camera = Camera.main;
            var direction = targetPosition - fromPosition;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f)
            {
                arrowRect.localRotation = Quaternion.identity;
                return;
            }

            direction.Normalize();
            var forward = camera != null ? camera.transform.forward : Vector3.forward;
            var right = camera != null ? camera.transform.right : Vector3.right;
            forward.y = 0f;
            right.y = 0f;

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }

            forward.Normalize();
            right.Normalize();
            var angle = Mathf.Atan2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)) * Mathf.Rad2Deg;
            arrowRect.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }

        private void HideTargetPointers()
        {
            if (_hudObjectivePointerRoot != null)
            {
                _hudObjectivePointerRoot.SetActive(false);
            }

            if (_hudRareBeaconPointerRoot != null)
            {
                _hudRareBeaconPointerRoot.SetActive(false);
            }
        }

        private static Color GetStyleEventColor(RetrowaveStyleEvent styleEvent)
        {
            return styleEvent switch
            {
                RetrowaveStyleEvent.TeamCombo => new Color(0.16f, 1f, 0.78f, 1f),
                RetrowaveStyleEvent.Pass => new Color(0.48f, 0.86f, 1f, 1f),
                RetrowaveStyleEvent.ObjectiveCapture => new Color(1f, 0.72f, 0.16f, 1f),
                RetrowaveStyleEvent.CleanLanding => new Color(0.46f, 1f, 0.52f, 1f),
                RetrowaveStyleEvent.FlipTrick => new Color(1f, 0.42f, 0.9f, 1f),
                RetrowaveStyleEvent.AerialManeuver => new Color(0.5f, 0.72f, 1f, 1f),
                RetrowaveStyleEvent.AerialTouch => new Color(0.42f, 0.94f, 1f, 1f),
                _ => new Color(0.72f, 1f, 0.78f, 1f),
            };
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
                MarkRoleSelectionRequestPending();
                return true;
            }

            var matchManager = GetActiveMatchManager();

            if (matchManager != null)
            {
                matchManager.RequestRoleSelection(role);
                MarkRoleSelectionRequestPending();
                return true;
            }

            return false;
        }

        private bool TryRequestUtilityRoleSelection(RetrowaveUtilityRole role)
        {
            if (RetrowavePlayerController.LocalPlayer == null)
            {
                return false;
            }

            RetrowavePlayerController.LocalPlayer.RequestUtilityRoleSelection(role);
            ClearPendingRoleSelectionRequest();
            return true;
        }

        private void ShutdownSession()
        {
            ForceShutdownSession(false, false);
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
                ForceShutdownSession(false, false);
            }
        }

        private IEnumerator LoadTestArena()
        {
            _showPauseMenu = false;
            _showScoreboard = false;
            ClearPendingRoleSelectionRequest();
            ClearGoalCelebrationState();
            ClearPodiumPresentation();

            if (!IsTestArenaScene(SceneManager.GetActiveScene()))
            {
                SceneManager.LoadScene(TestArenaSceneName, LoadSceneMode.Single);
            }

            while (!IsTestArenaScene(SceneManager.GetActiveScene()))
            {
                yield return null;
            }

            yield return null;
            ApplyScenePresentation(SceneManager.GetActiveScene());
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
                _address = ResolvePreferredAddress();
                _port = 7777;

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
            if (_networkManager == null)
            {
                return;
            }

            if (_networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }
        }

        private void ApplyScenePresentation(Scene scene)
        {
            if (IsGameplayScene(scene))
            {
                ApplyGameplayPhysicsTiming();
                RetrowaveArenaBuilder.EnsureBuilt();
                RetrowaveArenaBuilder.SetActive(true);
                RetrowaveCameraRig.EnsureCamera();
                RetrowaveCameraRig.ShowOverview();
                EnsureGameplayMenuOverlay();
                EnsureGameplayHudOverlay();
                return;
            }

            if (IsTestArenaScene(scene))
            {
                ApplyGameplayPhysicsTiming();
                RetrowaveArenaBuilder.EnsureBuilt();
                RetrowaveArenaBuilder.SetActive(true);
                RetrowaveCameraRig.EnsureCamera();
                RetrowaveCameraRig.ShowOverview();
                ClearPodiumPresentation();
                DestroyGameplayMenuOverlay();
                DestroyGameplaySettingsOverlay();
                DestroyGameplayHudOverlay();
                RetrowaveTestArenaManager.Ensure(scene);
                return;
            }

            RestoreDefaultPhysicsTiming();
            RetrowaveArenaBuilder.SetActive(false);
            ClearPodiumPresentation();
            DestroyGameplayMenuOverlay();
            DestroyGameplaySettingsOverlay();
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
            _goalCelebrationAssist = string.Empty;
            SetLocalTimeScale(1f);
        }

        private void SetLocalTimeScale(float scale)
        {
            var clampedScale = Mathf.Clamp(scale, 0.05f, 1f);
            Time.timeScale = clampedScale;
            Time.fixedDeltaTime = GetActiveFixedDeltaTimeBase() * clampedScale;
        }

        private void ApplyGameplayPhysicsTiming()
        {
            Time.fixedDeltaTime = GameplayFixedDeltaTime * Mathf.Clamp(Time.timeScale, 0.05f, 1f);
        }

        private void RestoreDefaultPhysicsTiming()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }

        private float GetActiveFixedDeltaTimeBase()
        {
            var scene = SceneManager.GetActiveScene();
            return IsGameplayScene(scene) || IsTestArenaScene(scene)
                ? GameplayFixedDeltaTime
                : _defaultFixedDeltaTime;
        }

        private static bool IsGameplayScene(Scene scene)
        {
            return scene.name == GameplaySceneName;
        }

        private static bool IsTestArenaScene(Scene scene)
        {
            return scene.name == TestArenaSceneName;
        }

        private static GameObject CreatePlayerPrefab(bool isTemplate = true, bool includeNetworking = true)
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

            if (includeNetworking)
            {
                prefab.AddComponent<NetworkObject>();
                ConfigurePhysicsNetworkTransform(prefab.AddComponent<NetworkTransform>());
                var networkRigidbody = prefab.AddComponent<NetworkRigidbody>();
                networkRigidbody.UseRigidBodyForMotion = true;
            }

            prefab.AddComponent<VehicleStatusEffects>();
            prefab.AddComponent<VehicleOverdriveSystem>();
            prefab.AddComponent<VehicleStyleMeter>();
            prefab.AddComponent<RetrowavePlayerController>();
            prefab.AddComponent<RetrowaveVehicleEngineAudio>();
            prefab.AddComponent<RetrowaveVehicleProductionFx>();
            prefab.AddComponent<RarePowerUpInventory>();
            prefab.AddComponent<NeonSnareTrailPowerUp>();
            prefab.AddComponent<GravityBombPowerUp>();
            prefab.AddComponent<ChronoDomePowerUp>();

            if (!TryAttachSportCarVisual(prefab.transform))
            {
                CreateFallbackVehicleVisual(prefab.transform);
            }

            FinalizeRuntimePrefab(prefab, 0xA1000001u, isTemplate);
            return prefab;
        }

        private static GameObject CreateBallPrefab(bool isTemplate = true, bool includeNetworking = true)
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

            if (includeNetworking)
            {
                prefab.AddComponent<NetworkObject>();
                ConfigurePhysicsNetworkTransform(prefab.AddComponent<NetworkTransform>());
                var networkRigidbody = prefab.AddComponent<NetworkRigidbody>();
                networkRigidbody.UseRigidBodyForMotion = true;
            }

            prefab.AddComponent<RetrowaveBallStateController>();
            prefab.AddComponent<RetrowaveBall>();
            prefab.GetComponent<MeshRenderer>().sharedMaterial = CreateBallSurfaceMaterial();
            FinalizeRuntimePrefab(prefab, 0xA1000002u, isTemplate);
            return prefab;
        }

        private static void ConfigurePhysicsNetworkTransform(NetworkTransform networkTransform)
        {
            if (networkTransform == null)
            {
                return;
            }

            networkTransform.UseUnreliableDeltas = true;
            networkTransform.Interpolate = true;
            networkTransform.UseQuaternionSynchronization = true;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.PositionInterpolationType = NetworkTransform.InterpolationTypes.SmoothDampening;
            networkTransform.RotationInterpolationType = NetworkTransform.InterpolationTypes.SmoothDampening;
            networkTransform.ScaleInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
            networkTransform.PositionLerpSmoothing = true;
            networkTransform.RotationLerpSmoothing = true;
            networkTransform.UseHalfFloatPrecision = false;
            networkTransform.PositionMaxInterpolationTime = PhysicsNetworkInterpolationSeconds;
            networkTransform.RotationMaxInterpolationTime = PhysicsNetworkInterpolationSeconds;
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

        private static GameObject CreatePowerUpPrefab(bool isTemplate = true, bool includeNetworking = true)
        {
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "RT PowerUp";
            prefab.SetActive(false);
            prefab.transform.localScale = Vector3.one * 1.05f;

            var collider = prefab.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = Vector3.one * 1.7f;

            if (includeNetworking)
            {
                prefab.AddComponent<NetworkObject>();
                prefab.AddComponent<NetworkTransform>();
            }

            prefab.AddComponent<RetrowavePowerUp>();
            prefab.GetComponent<MeshRenderer>().sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.18f, 0.14f, 0.28f),
                new Color(0.95f, 0.4f, 1f) * 2.5f,
                0.9f,
                0f);
            FinalizeRuntimePrefab(prefab, 0xA1000003u, isTemplate);
            return prefab;
        }

        private static GameObject CreateRarePowerUpPickupBeaconPrefab(bool isTemplate = true, bool includeNetworking = true)
        {
            var prefab = new GameObject("RT Rare PowerUp Beacon");
            prefab.SetActive(false);
            var collider = prefab.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 3f;

            if (includeNetworking)
            {
                prefab.AddComponent<NetworkObject>();
                prefab.AddComponent<NetworkTransform>();
            }

            prefab.AddComponent<RarePowerUpPickupBeacon>();
            FinalizeRuntimePrefab(prefab, 0xA1000005u, isTemplate);
            return prefab;
        }

        private static GameObject CreateNeonTrailSegmentPrefab(bool isTemplate = true, bool includeNetworking = true)
        {
            var prefab = new GameObject("RT Neon Trail Segment");
            prefab.SetActive(false);
            var collider = prefab.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(1.85f, 1.4f, 1.35f);

            if (includeNetworking)
            {
                prefab.AddComponent<NetworkObject>();
                prefab.AddComponent<NetworkTransform>();
            }

            prefab.AddComponent<NeonTrailSegment>();
            FinalizeRuntimePrefab(prefab, 0xA1000006u, isTemplate);
            return prefab;
        }

        private static GameObject CreateGravityBombDevicePrefab(bool isTemplate = true, bool includeNetworking = true)
        {
            var prefab = new GameObject("RT Gravity Bomb Device");
            prefab.SetActive(false);
            var collider = prefab.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 9f;

            if (includeNetworking)
            {
                prefab.AddComponent<NetworkObject>();
                prefab.AddComponent<NetworkTransform>();
            }

            prefab.AddComponent<GravityBombDevice>();
            FinalizeRuntimePrefab(prefab, 0xA1000007u, isTemplate);
            return prefab;
        }

        private static GameObject CreateChronoDomeFieldPrefab(bool isTemplate = true, bool includeNetworking = true)
        {
            var prefab = new GameObject("RT Chrono Dome Field");
            prefab.SetActive(false);
            var collider = prefab.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 10f;

            if (includeNetworking)
            {
                prefab.AddComponent<NetworkObject>();
                prefab.AddComponent<NetworkTransform>();
            }

            prefab.AddComponent<ChronoDomeField>();
            FinalizeRuntimePrefab(prefab, 0xA1000008u, isTemplate);
            return prefab;
        }

        private static GameObject CreateMatchManagerPrefab(bool isTemplate = true)
        {
            var prefab = new GameObject("RT Match Manager");
            prefab.SetActive(false);
            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<RetrowaveMatchManager>();
            prefab.AddComponent<RarePowerUpSpawner>();
            prefab.AddComponent<RetrowaveArenaObjectiveSystem>();
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
