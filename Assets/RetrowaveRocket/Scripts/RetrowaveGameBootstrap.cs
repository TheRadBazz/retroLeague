using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
        private enum PendingConnectionMode
        {
            None = 0,
            Host = 1,
            Client = 2,
        }

        public const string MainMenuSceneName = "MainMenu";
        public const string GameplaySceneName = "SampleScene";

        private static RetrowaveGameBootstrap _instance;
        private static readonly FieldInfo GlobalObjectIdHashField = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);

        private NetworkManager _networkManager;
        private UnityTransport _transport;
        private Transform _prefabRoot;
        private GameObject _playerPrefab;
        private GameObject _ballPrefab;
        private GameObject _powerUpPrefab;
        private GameObject _matchManagerPrefab;
        private string _address = "127.0.0.1";
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
        private int _goalCelebrationOrangeScore;
        private GameObject _gameplayMenuRoot;
        private GameObject _gameplayMenuEventSystem;
        private TMP_FontAsset _gameplayMenuFont;
        private TMP_Text _gameplayMenuTitleText;
        private TMP_Text _gameplayMenuBodyText;
        private TMP_Text _gameplayMenuHintText;
        private TMP_Text _gameplayMenuFooterText;
        private Button _gameplayBlueButton;
        private Button _gameplayOrangeButton;
        private Button _gameplaySpectateButton;
        private Button _gameplayResumeButton;
        private Button _gameplayStartButton;
        private Button _gameplayReturnButton;
        private TMP_Text _gameplayStartButtonLabel;
        private bool _gameplayMenuWasVisible;

        public static RetrowaveGameBootstrap Instance => _instance;
        public GameObject PlayerPrefab => _playerPrefab;
        public GameObject BallPrefab => _ballPrefab;
        public GameObject PowerUpPrefab => _powerUpPrefab;
        public string DefaultAddress => _address;
        public string DefaultPort => _port.ToString();

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
                SetGameplayMenuVisible(false);
                return;
            }

            if (!_networkManager.IsListening)
            {
                _showPauseMenu = false;
                _showScoreboard = false;
                ClearGoalCelebrationState();
                SetGameplayMenuVisible(false);
                return;
            }

            var keyboard = Keyboard.current;

            if (keyboard != null)
            {
                HandleRoleSelectionHotkeys(keyboard);
                _showScoreboard = keyboard.tabKey.isPressed;

                if (keyboard.escapeKey.wasPressedThisFrame && !RequiresRoleSelection())
                {
                    _showPauseMenu = !_showPauseMenu;
                }
            }

            if (_goalCelebrationVisible && Time.unscaledTime >= _goalCelebrationEndsAtRealtime)
            {
                ClearGoalCelebrationState();
            }

            RefreshGameplayMenuState();
        }

        private void HandleRoleSelectionHotkeys(Keyboard keyboard)
        {
            if (!RequiresRoleSelection() || RetrowaveMatchManager.Instance == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame || keyboard.bKey.wasPressedThisFrame)
            {
                RetrowaveMatchManager.Instance.RequestRoleSelection(RetrowaveLobbyRole.Blue);
                _showPauseMenu = false;
                return;
            }

            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame || keyboard.oKey.wasPressedThisFrame)
            {
                RetrowaveMatchManager.Instance.RequestRoleSelection(RetrowaveLobbyRole.Orange);
                _showPauseMenu = false;
                return;
            }

            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                RetrowaveMatchManager.Instance.RequestRoleSelection(RetrowaveLobbyRole.Spectator);
                _showPauseMenu = false;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                ClearGoalCelebrationState();
                DestroyGameplayMenuOverlay();
                _instance = null;
            }
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

            DrawHud();

            if (_goalCelebrationVisible)
            {
                DrawGoalCelebrationIndicator();
            }

            if (_showScoreboard)
            {
                DrawScoreboard();
            }

            if ((_showPauseMenu || RequiresRoleSelection()) && _gameplayMenuRoot == null)
            {
                DrawPauseMenu();
            }
        }

        public bool BeginHostFromMenu(string address, string portText, out string message)
        {
            return BeginConnectionFromMenu(PendingConnectionMode.Host, address, portText, out message);
        }

        public bool BeginClientFromMenu(string address, string portText, out string message)
        {
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

        public void BeginGoalCelebration(RetrowaveTeam scoringTeam, string scorerName, int blueScore, int orangeScore, float durationSeconds)
        {
            _goalCelebrationVisible = true;
            _goalCelebrationTeam = scoringTeam;
            _goalCelebrationScorer = string.IsNullOrWhiteSpace(scorerName)
                ? (scoringTeam == RetrowaveTeam.Blue ? "Blue Team" : "Orange Team")
                : scorerName;
            _goalCelebrationBlueScore = blueScore;
            _goalCelebrationOrangeScore = orangeScore;
            _goalCelebrationEndsAtRealtime = Time.unscaledTime + Mathf.Max(0.25f, durationSeconds);
            SetLocalTimeScale(0.2f);
        }

        private void EnsureNetworkRuntime()
        {
            if (_networkManager != null)
            {
                return;
            }

            var runtimeRoot = new GameObject("Retrowave Net Runtime");
            DontDestroyOnLoad(runtimeRoot);

            _networkManager = runtimeRoot.AddComponent<NetworkManager>();
            _transport = runtimeRoot.AddComponent<UnityTransport>();
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

            _networkManager.OnServerStarted += HandleServerStarted;
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
            _address = string.IsNullOrWhiteSpace(trimmedAddress)
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

            var prefabRootObject = new GameObject("Retrowave Runtime Prefabs");
            prefabRootObject.transform.SetParent(transform, false);
            DontDestroyOnLoad(prefabRootObject);
            _prefabRoot = prefabRootObject.transform;

            _playerPrefab = CreatePlayerPrefab();
            _ballPrefab = CreateBallPrefab();
            _powerUpPrefab = CreatePowerUpPrefab();
            _matchManagerPrefab = CreateMatchManagerPrefab();
            _playerPrefab.transform.SetParent(_prefabRoot, false);
            _ballPrefab.transform.SetParent(_prefabRoot, false);
            _powerUpPrefab.transform.SetParent(_prefabRoot, false);
            _matchManagerPrefab.transform.SetParent(_prefabRoot, false);
        }

        private void RegisterNetworkPrefab(GameObject prefab)
        {
            if (_networkManager.NetworkConfig.Prefabs.Contains(prefab))
            {
                return;
            }

            _networkManager.AddNetworkPrefab(prefab);
        }

        private void HandleServerStarted()
        {
            if (!_networkManager.IsServer || RetrowaveMatchManager.Instance != null)
            {
                return;
            }

            var matchManager = Instantiate(_matchManagerPrefab);
            matchManager.name = "Retrowave Match Manager";
            matchManager.SetActive(true);
            matchManager.GetComponent<NetworkObject>().Spawn();
        }

        private void DrawHud()
        {
            GUILayout.BeginArea(new Rect(18f, 14f, 420f, 190f), GUI.skin.box);

            var status = _networkManager.IsHost ? "Host" : (_networkManager.IsServer ? "Server" : "Client");
            GUILayout.Label($"{status} connected on {_address}:{_port}");
            GUILayout.Label("Esc: match menu    Tab: scoreboard");

            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager != null)
            {
                var phaseLabel = matchManager.IsWarmup ? "Warmup / Practice" : "Live Match";
                GUILayout.Label($"{phaseLabel}    Blue {matchManager.BlueScore} : {matchManager.OrangeScore} Orange");

                if (TryGetLocalLobbyEntry(out var entry))
                {
                    GUILayout.Label($"Role: {GetRoleLabel(entry)}");

                    if (!entry.HasSelectedRole)
                    {
                        GUILayout.Label("Choose a team or spectate to enter the lobby.");
                    }
                    else if (entry.IsHost && matchManager.IsWarmup)
                    {
                        GUILayout.Label(matchManager.CanStartMatch
                            ? "Both teams are ready. Open Esc to start the match."
                            : "Warm up now. Match start unlocks once blue and orange both have a player.");
                    }
                }
            }
            else
            {
                GUILayout.Label("Waiting for match manager...");
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
                GUILayout.Label("Camera: spectator overview");
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
            _gameplayOrangeButton = CreateMenuButton(panel.transform, "OrangeButton", "Join Orange Team", new Vector2(0f, -20f), new Color(1f, 0.36f, 0.18f, 1f), () => SelectGameplayRole(RetrowaveLobbyRole.Orange));
            _gameplaySpectateButton = CreateMenuButton(panel.transform, "SpectateButton", "Spectate", new Vector2(0f, -88f), new Color(0.34f, 0.18f, 0.46f, 1f), () => SelectGameplayRole(RetrowaveLobbyRole.Spectator));
            _gameplayStartButton = CreateMenuButton(panel.transform, "StartButton", "Start Match", new Vector2(0f, -164f), new Color(0.14f, 0.64f, 0.42f, 1f), HandleGameplayStartMatch);
            _gameplayStartButtonLabel = _gameplayStartButton.GetComponentInChildren<TextMeshProUGUI>(true);
            _gameplayResumeButton = CreateMenuButton(panel.transform, "ResumeButton", "Resume", new Vector2(-132f, -242f), new Color(0.12f, 0.24f, 0.36f, 1f), HandleGameplayResume);
            _gameplayReturnButton = CreateMenuButton(panel.transform, "ReturnButton", "Return To Main Menu", new Vector2(132f, -242f), new Color(0.26f, 0.12f, 0.16f, 1f), ReturnToMainMenu);

            SetGameplayMenuVisible(false);
        }

        private void DestroyGameplayMenuOverlay()
        {
            if (_gameplayMenuRoot != null)
            {
                Destroy(_gameplayMenuRoot);
                _gameplayMenuRoot = null;
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
            _gameplayOrangeButton = null;
            _gameplaySpectateButton = null;
            _gameplayResumeButton = null;
            _gameplayStartButton = null;
            _gameplayReturnButton = null;
            _gameplayStartButtonLabel = null;
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

            var forceSelection = RequiresRoleSelection();
            var wasVisible = _gameplayMenuWasVisible;
            var isVisible = _networkManager != null
                            && _networkManager.IsListening
                            && IsGameplayScene(SceneManager.GetActiveScene())
                            && (_showPauseMenu || forceSelection);

            SetGameplayMenuVisible(isVisible);
            _gameplayMenuWasVisible = isVisible;

            if (!isVisible)
            {
                return;
            }

            var matchManager = RetrowaveMatchManager.Instance;
            _gameplayMenuTitleText.text = forceSelection ? "Choose Your Role" : "Match Menu";
            _gameplayMenuBodyText.text = forceSelection
                ? "Pick blue, orange, or spectator before jumping fully into the lobby."
                : "Swap teams, spectate, or control the match flow from here.";
            _gameplayMenuHintText.text = forceSelection
                ? "Keyboard also works: 1 / B = Blue, 2 / O = Orange, 3 / S = Spectator"
                : "Esc closes this menu after you've chosen a role.";

            var hostCanStart = false;
            var hostIsPresent = false;

            if (matchManager != null && TryGetLocalLobbyEntry(out var entry))
            {
                hostIsPresent = entry.IsHost;
                hostCanStart = entry.IsHost && matchManager.CanStartMatch;
            }

            _gameplayStartButton.gameObject.SetActive(hostIsPresent);
            _gameplayResumeButton.gameObject.SetActive(!forceSelection);

            if (_gameplayStartButtonLabel != null && matchManager != null)
            {
                _gameplayStartButtonLabel.text = matchManager.IsWarmup ? "Start Match" : "Restart Match";
            }

            _gameplayStartButton.interactable = hostCanStart;
            _gameplayBlueButton.interactable = matchManager != null;
            _gameplayOrangeButton.interactable = matchManager != null;
            _gameplaySpectateButton.interactable = matchManager != null;

            if (forceSelection)
            {
                _gameplayMenuFooterText.text = "Use the buttons above to enter the arena.";
            }
            else if (hostIsPresent && !hostCanStart && matchManager != null)
            {
                _gameplayMenuFooterText.text = "Host start unlocks once at least one player is on blue and orange.";
            }
            else
            {
                _gameplayMenuFooterText.text = "Team changes apply immediately for this client.";
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

        private void SelectGameplayRole(RetrowaveLobbyRole role)
        {
            if (RetrowaveMatchManager.Instance != null)
            {
                RetrowaveMatchManager.Instance.RequestRoleSelection(role);
            }

            _showPauseMenu = false;
        }

        private void HandleGameplayStartMatch()
        {
            if (RetrowaveMatchManager.Instance == null)
            {
                return;
            }

            RetrowaveMatchManager.Instance.RequestStartMatch();
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
            text.enableWordWrapping = true;
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
                : RetrowaveStyle.OrangeBase;
            var teamLabel = _goalCelebrationTeam == RetrowaveTeam.Blue ? "Blue Team" : "Orange Team";
            var headline = $"{teamLabel.ToUpperInvariant()} SCORES!";
            var scorerLine = _goalCelebrationScorer == teamLabel
                ? $"{teamLabel} scored the goal"
                : $"{_goalCelebrationScorer} scored for {teamLabel}";
            var scoreLine = $"Blue {_goalCelebrationBlueScore}  -  {_goalCelebrationOrangeScore} Orange";

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
            var matchManager = RetrowaveMatchManager.Instance;
            var title = forceSelection ? "Choose Your Role" : "Match Menu";

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 210f, 70f, 420f, 340f), GUI.skin.window);
            GUILayout.Label(title);

            if (forceSelection)
            {
                GUILayout.Label("Pick blue, orange, or spectator before jumping fully into the lobby.");
                GUILayout.Label("Shortcuts: 1 or B = Blue, 2 or O = Orange, 3 or S = Spectator");
            }
            else
            {
                GUILayout.Label("Swap teams, spectate, or control the match flow from here.");
            }

            GUILayout.Space(10f);

            DrawRoleButton("Join Blue Team", RetrowaveLobbyRole.Blue);
            DrawRoleButton("Join Orange Team", RetrowaveLobbyRole.Orange);
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
                    GUILayout.Label("Host start unlocks once at least one player is on blue and orange.");
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

            if (RetrowaveMatchManager.Instance != null)
            {
                RetrowaveMatchManager.Instance.RequestRoleSelection(role);
            }

            _showPauseMenu = false;
        }

        private void DrawScoreboard()
        {
            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 330f, 18f, 660f, 420f), GUI.skin.box);
            GUILayout.Label(matchManager.IsWarmup
                ? "Lobby Scoreboard - Warmup"
                : $"Live Scoreboard - Blue {matchManager.BlueScore} : {matchManager.OrangeScore} Orange");

            DrawScoreboardSection("Blue Team", RetrowaveLobbyRole.Blue, matchManager);
            DrawScoreboardSection("Orange Team", RetrowaveLobbyRole.Orange, matchManager);
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
            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager != null && _networkManager != null && _networkManager.IsListening)
            {
                return matchManager.TryGetLobbyEntry(_networkManager.LocalClientId, out entry);
            }

            entry = default;
            return false;
        }

        private bool RequiresRoleSelection()
        {
            return TryGetLocalLobbyEntry(out var entry) && !entry.HasSelectedRole;
        }

        private bool ShouldBlockGameplayInput()
        {
            var celebrationActive = _goalCelebrationVisible
                                    || (RetrowaveMatchManager.Instance != null && RetrowaveMatchManager.Instance.IsGoalCelebrationActive);

            return _networkManager != null
                   && _networkManager.IsListening
                   && IsGameplayScene(SceneManager.GetActiveScene())
                   && (_showPauseMenu || RequiresRoleSelection() || celebrationActive);
        }

        private static string GetRoleLabel(RetrowaveLobbyEntry entry)
        {
            if (!entry.HasSelectedRole)
            {
                return "Unassigned";
            }

            return entry.Role switch
            {
                RetrowaveLobbyRole.Blue => "Blue Team",
                RetrowaveLobbyRole.Orange => "Orange Team",
                _ => "Spectator",
            };
        }

        private void ShutdownSession()
        {
            if (_networkManager == null || !_networkManager.IsListening)
            {
                return;
            }

            _networkManager.Shutdown();
            RetrowavePlayerController.ClearLocalOwner();
            RetrowaveCameraRig.ShowOverview();
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

            switch (_pendingConnectionMode)
            {
                case PendingConnectionMode.Host:
                    _transport.SetConnectionData(GetJoinAddressForDisplay(), _port, "0.0.0.0");
                    _networkManager.StartHost();
                    break;
                case PendingConnectionMode.Client:
                    _transport.SetConnectionData(_address, _port);
                    _networkManager.StartClient();
                    break;
            }

            _pendingConnectionMode = PendingConnectionMode.None;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _showPauseMenu = false;
            _showScoreboard = false;
            ClearGoalCelebrationState();
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

                    return address;
                }
            }
            catch
            {
                // Fall back to loopback when the local LAN address can't be resolved.
            }

            return "127.0.0.1";
        }

        private static bool IsLoopbackAddress(string address)
        {
            return string.IsNullOrWhiteSpace(address)
                   || address == "127.0.0.1"
                   || address.Equals("localhost", System.StringComparison.OrdinalIgnoreCase);
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
                return;
            }

            RetrowaveArenaBuilder.SetActive(false);
            DestroyGameplayMenuOverlay();
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

        private static GameObject CreatePlayerPrefab()
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
            prefab.AddComponent<RetrowavePlayerController>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Body Visual";
            visual.transform.SetParent(prefab.transform, false);
            visual.transform.localPosition = new Vector3(0f, -0.02f, 0f);
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
            FinalizeRuntimePrefab(prefab, 0xA1000001u);
            return prefab;
        }

        private static GameObject CreateBallPrefab()
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
            prefab.AddComponent<RetrowaveBall>();
            prefab.GetComponent<MeshRenderer>().sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.95f, 0.85f, 0.98f),
                new Color(0.45f, 0.7f, 1f) * 2.7f,
                0.95f,
                0.02f);
            FinalizeRuntimePrefab(prefab, 0xA1000002u);
            return prefab;
        }

        private static GameObject CreatePowerUpPrefab()
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
            FinalizeRuntimePrefab(prefab, 0xA1000003u);
            return prefab;
        }

        private static GameObject CreateMatchManagerPrefab()
        {
            var prefab = new GameObject("RT Match Manager");
            prefab.SetActive(false);
            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<RetrowaveMatchManager>();
            FinalizeRuntimePrefab(prefab, 0xA1000004u);
            return prefab;
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

        private static void FinalizeRuntimePrefab(GameObject prefab, uint runtimeHash)
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

            prefab.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(prefab);
        }
    }
}
