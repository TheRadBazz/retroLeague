using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RetrowaveRocket
{
    public sealed class RetrowaveTestArenaManager : MonoBehaviour
    {
        private const string ManagerName = "Retrowave Test Arena Manager";
        private const string ControlsSeenKey = "RetrowaveTestArena.ControlsSeen";
        private const float ControlsOverlayDurationSeconds = 16f;
        private const float TipMinimumVisibleSeconds = 4.5f;
        private const float OfflineGoalResetCooldownSeconds = 1f;

        private GameObject _hudRoot;
        private GameObject _controlsRoot;
        private GameObject _tipRoot;
        private GameObject _readoutRoot;
        private GameObject _gaugeRoot;
        private GameObject _scoreStripRoot;
        private GameObject _infoRoot;
        private GameObject _infoCollapsedRoot;
        private GameObject _objectivePointerRoot;
        private GameObject _rareBeaconPointerRoot;
        private GameObject _rarePowerRoot;
        private GameObject _styleNotificationRoot;
        private TMP_Text _statusText;
        private TMP_Text _scoreStateText;
        private TMP_Text _scoreClockText;
        private TMP_Text _blueScoreText;
        private TMP_Text _pinkScoreText;
        private TMP_Text _infoToggleText;
        private TMP_Text _infoCollapsedText;
        private TMP_Text _infoConnectionText;
        private TMP_Text _infoPhaseText;
        private TMP_Text _infoRoleText;
        private TMP_Text _infoHintText;
        private TMP_Text _controlsTitleText;
        private TMP_Text _controlsBodyText;
        private TMP_Text _tipText;
        private TMP_Text _readoutText;
        private TMP_Text _gaugeTitleText;
        private TMP_Text _gaugeStatusText;
        private TMP_Text _speedValueText;
        private TMP_Text _speedLabelText;
        private TMP_Text _boostValueText;
        private TMP_Text _heatValueText;
        private TMP_Text _styleValueText;
        private TMP_Text _rarePowerText;
        private TMP_Text _styleNotificationText;
        private RectTransform _objectivePointerArrowRect;
        private TMP_Text _objectivePointerArrowText;
        private TMP_Text _objectivePointerText;
        private RectTransform _rareBeaconPointerArrowRect;
        private TMP_Text _rareBeaconPointerArrowText;
        private TMP_Text _rareBeaconPointerText;
        private Image _gaugeSpeedFillImage;
        private Image _gaugeBoostFillImage;
        private Image _gaugeHeatFillImage;
        private Image _gaugeStyleFillImage;
        private RectTransform _gaugeSpeedBarRect;
        private RectTransform _gaugeSpeedMarkerRect;
        private RectTransform _gaugeBoostBarRect;
        private RectTransform _gaugeBoostMarkerRect;
        private RectTransform _gaugeHeatBarRect;
        private RectTransform _gaugeHeatMarkerRect;
        private RectTransform _gaugeStyleBarRect;
        private RectTransform _gaugeStyleMarkerRect;
        private TMP_FontAsset _font;
        private string _sessionStatus = "Starting offline warmup...";
        private string _tipTextValue = string.Empty;
        private float _controlsOverlayUntilRealtime;
        private float _tipCanChangeAtRealtime;
        private float _styleNotificationHideAtRealtime;
        private float _reconcileTimer;
        private float _nextGoalAllowedAtRealtime;
        private int _observedStyleAwardSerial;
        private int _blueScore;
        private int _pinkScore;
        private bool _showTips = true;
        private bool _showHudInfoPanel = true;
        private bool _sessionReady;
        private RetrowavePlayerController _localPlayer;
        private RetrowaveBall _localBall;
        private RarePowerUpSpawner _offlineRareSpawner;
        private RetrowaveArenaObjectiveSystem _offlineObjectiveSystem;
        private readonly List<RetrowavePowerUp> _offlinePowerUps = new();

        public static RetrowaveTestArenaManager Instance { get; private set; }
        public bool IsLocalSettingsVisible => RetrowaveGameBootstrap.Instance != null && RetrowaveGameBootstrap.Instance.IsSharedSettingsOverlayVisible();

        public static void TearDownForSceneExit()
        {
            if (Instance != null)
            {
                Instance.TearDownLocalUiForSceneExit();
            }
        }

        public static void Ensure(Scene scene)
        {
            if (scene.name != RetrowaveGameBootstrap.TestArenaSceneName)
            {
                return;
            }

            if (FindAnyObjectByType<RetrowaveTestArenaManager>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            var managerObject = new GameObject(ManagerName);
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            managerObject.AddComponent<RetrowaveTestArenaManager>();
        }

        private IEnumerator Start()
        {
            Instance = this;
            var existingLabel = FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            _font = existingLabel != null ? existingLabel.font : TMP_Settings.defaultFontAsset;
            BuildHud();
            yield return null;
            yield return EnsureOfflineFreeplaySession();
        }

        private void Update()
        {
            HandleKeyboardShortcuts();
            RefreshHudInputState();
            ReconcileLocalSession();
            RefreshHud();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            TearDownLocalUiForSceneExit();

            _localPlayer = null;
            _localBall = null;
        }

        private IEnumerator EnsureOfflineFreeplaySession()
        {
            var bootstrap = RetrowaveGameBootstrap.EnsureInstance();

            for (var i = 0; i < 60 && bootstrap == null; i++)
            {
                yield return null;
                bootstrap = RetrowaveGameBootstrap.Instance;
            }

            if (bootstrap == null)
            {
                _sessionStatus = "Test Arena bootstrap is missing.";
                yield break;
            }

            _sessionStatus = "Starting offline freeplay...";
            yield return null;
            EnsureOfflineSceneObjects(bootstrap);
        }

        private void ReconcileLocalSession()
        {
            _reconcileTimer -= Time.unscaledDeltaTime;

            if (_reconcileTimer > 0f)
            {
                return;
            }

            _reconcileTimer = 0.75f;

            var bootstrap = RetrowaveGameBootstrap.EnsureInstance();

            if (bootstrap == null)
            {
                return;
            }

            EnsureOfflineSceneObjects(bootstrap);
        }

        private void EnsureOfflineSceneObjects(RetrowaveGameBootstrap bootstrap)
        {
            if (bootstrap == null)
            {
                _sessionReady = false;
                _sessionStatus = "Test Arena bootstrap is missing.";
                return;
            }

            RetrowaveArenaConfig.ApplyMatchSettings(RetrowaveMatchSettings.Default);

            if (_localPlayer == null)
            {
                var playerObject = bootstrap.CreateOfflinePlayerInstance();

                if (playerObject == null)
                {
                    _sessionReady = false;
                    _sessionStatus = "Failed to create the Test Arena vehicle.";
                    return;
                }

                bootstrap.MoveRuntimeInstanceToGameplayScene(playerObject);
                playerObject.name = "Test Arena Player";
                playerObject.SetActive(true);
                _localPlayer = playerObject.GetComponent<RetrowavePlayerController>();
            }

            if (_localPlayer == null)
            {
                _sessionReady = false;
                _sessionStatus = "The Test Arena vehicle controller is missing.";
                return;
            }

            if (!_localPlayer.IsArenaParticipant)
            {
                var spawnPosition = RetrowaveArenaConfig.GetSpawnPoint(RetrowaveTeam.Blue, 0, 1);
                _localPlayer.ConfigureOfflineSession(
                    bootstrap.PreferredDisplayName,
                    RetrowaveTeam.Blue,
                    RetrowaveUtilityRole.Runner,
                    spawnPosition);
            }

            if (_localBall == null)
            {
                var ballObject = bootstrap.CreateOfflineBallInstance();

                if (ballObject != null)
                {
                    bootstrap.MoveRuntimeInstanceToGameplayScene(ballObject);
                    ballObject.name = "Test Arena Ball";
                    ballObject.SetActive(true);
                    _localBall = ballObject.GetComponent<RetrowaveBall>();

                    if (_localBall != null)
                    {
                        _localBall.EnableOfflineMode();
                        _localBall.ResetBall();
                    }
                }
            }

            EnsureOfflinePowerUps(bootstrap);
            EnsureOfflineRareSpawner(bootstrap);
            EnsureOfflineObjectiveSystem();

            _sessionReady = _localPlayer != null && _localPlayer.IsArenaParticipant;
            _sessionStatus = _sessionReady
                ? "Testing Arena freeplay"
                : "Waiting for local vehicle spawn...";
        }

        private void EnsureOfflinePowerUps(RetrowaveGameBootstrap bootstrap)
        {
            if (bootstrap == null)
            {
                return;
            }

            var powerUpPositions = RetrowaveArenaConfig.PowerUpPositions;

            while (_offlinePowerUps.Count < powerUpPositions.Length)
            {
                var powerUpObject = bootstrap.CreateOfflinePowerUpInstance();

                if (powerUpObject == null)
                {
                    break;
                }

                bootstrap.MoveRuntimeInstanceToGameplayScene(powerUpObject);
                powerUpObject.name = $"Test Arena PowerUp {_offlinePowerUps.Count}";
                powerUpObject.SetActive(true);
                var powerUp = powerUpObject.GetComponent<RetrowavePowerUp>();

                if (powerUp != null)
                {
                    powerUp.EnableOfflineMode();
                    powerUp.InitializeOffline(_offlinePowerUps.Count % 2 == 0
                        ? RetrowavePowerUpType.BoostRefill
                        : RetrowavePowerUpType.SpeedBurst);
                    _offlinePowerUps.Add(powerUp);
                }
                else
                {
                    Destroy(powerUpObject);
                    break;
                }
            }

            for (var i = 0; i < _offlinePowerUps.Count && i < powerUpPositions.Length; i++)
            {
                var powerUp = _offlinePowerUps[i];

                if (powerUp == null)
                {
                    continue;
                }

                var powerUpObject = powerUp.gameObject;
                powerUpObject.transform.SetPositionAndRotation(powerUpPositions[i], Quaternion.identity);
            }
        }

        private void EnsureOfflineRareSpawner(RetrowaveGameBootstrap bootstrap)
        {
            if (_offlineRareSpawner != null)
            {
                return;
            }

            var spawnerObject = new GameObject("Test Arena Rare Power Spawner");
            bootstrap.MoveRuntimeInstanceToGameplayScene(spawnerObject);
            _offlineRareSpawner = spawnerObject.AddComponent<RarePowerUpSpawner>();
            _offlineRareSpawner.ConfigureOfflineFreeplay(
                minDelaySeconds: 7f,
                maxDelaySeconds: 14f,
                spawnImmediately: true,
                requireCapture: true,
                captureSeconds: 5f,
                allowReplacement: false);
        }

        private void EnsureOfflineObjectiveSystem()
        {
            if (_offlineObjectiveSystem != null)
            {
                return;
            }

            var objectiveObject = new GameObject("Test Arena Objective System");
            SceneManager.MoveGameObjectToScene(objectiveObject, gameObject.scene);
            _offlineObjectiveSystem = objectiveObject.AddComponent<RetrowaveArenaObjectiveSystem>();
            _offlineObjectiveSystem.ConfigureOfflineFreeplay(
                minDelaySeconds: 9f,
                maxDelaySeconds: 16f,
                activeDurationSeconds: 24f,
                captureSeconds: 3.25f,
                spawnImmediately: true);
        }

        private RetrowavePlayerController ResolveLocalPlayer()
        {
            if (_localPlayer != null)
            {
                return _localPlayer;
            }

            _localPlayer = RetrowavePlayerController.LocalOwner;

            if (_localPlayer == null)
            {
                _localPlayer = FindAnyObjectByType<RetrowavePlayerController>(FindObjectsInactive.Exclude);
            }

            return _localPlayer;
        }

        private void HandleKeyboardShortcuts()
        {
            var keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                _showTips = !_showTips;

                if (_showTips)
                {
                    _controlsOverlayUntilRealtime = 0f;
                    _tipTextValue = string.Empty;
                    _tipCanChangeAtRealtime = 0f;
                }
                else
                {
                    SetTipHudVisible(false);
                }
            }

            if (keyboard.hKey.wasPressedThisFrame)
            {
                _showHudInfoPanel = !_showHudInfoPanel;
            }

            if (RetrowaveInputBindings.WasPressedThisFrame(keyboard, RetrowaveBindingAction.Pause))
            {
                ToggleSharedSettingsOverlay();
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                OpenSharedSettingsOverlay();
            }
        }

        private void RefreshHudInputState()
        {
            if (_hudRoot == null)
            {
                return;
            }

            var raycaster = _hudRoot.GetComponent<GraphicRaycaster>();

            if (raycaster == null)
            {
                return;
            }

            raycaster.enabled = !IsLocalSettingsVisible;
        }

        private void RefreshHud()
        {
            var localPlayer = ResolveLocalPlayer();
            RefreshOfflineGoalFallback();

            if (_statusText != null)
            {
                _statusText.gameObject.SetActive(!_sessionReady);
                _statusText.text = _sessionReady
                    ? "TESTING ARENA  •  F1 tips  •  F2 settings  •  Esc settings"
                    : _sessionStatus;
            }

            var showHud = RetrowaveGameSettings.ShowHud;

            if (_readoutRoot != null)
            {
                _readoutRoot.SetActive(showHud);
            }

            if (_gaugeRoot != null)
            {
                _gaugeRoot.SetActive(showHud);
            }

            if (_infoRoot != null)
            {
                _infoRoot.SetActive(showHud && _showHudInfoPanel);
            }

            if (_infoCollapsedRoot != null)
            {
                _infoCollapsedRoot.SetActive(showHud && !_showHudInfoPanel);
            }

            RefreshScoreStrip();
            RefreshInfoPanel(localPlayer);
            RefreshControlsOverlay(localPlayer);
            RefreshTip(localPlayer, null);
            RefreshReadout(localPlayer);
            RefreshGauge(localPlayer);
            RefreshObjectivePointer(localPlayer);
            RefreshRareBeaconPointer(localPlayer);
            RefreshStyleNotification(localPlayer);
        }

        private void ToggleSharedSettingsOverlay()
        {
            var bootstrap = RetrowaveGameBootstrap.Instance;

            if (bootstrap == null)
            {
                return;
            }

            if (bootstrap.IsSharedSettingsOverlayVisible())
            {
                bootstrap.CloseSharedSettingsOverlay();
            }
            else
            {
                bootstrap.OpenSharedSettingsOverlay();
            }
        }

        private void OpenSharedSettingsOverlay()
        {
            RetrowaveGameBootstrap.Instance?.OpenSharedSettingsOverlay();
        }

        private void RefreshOfflineGoalFallback()
        {
            if (_localBall == null || Time.unscaledTime < _nextGoalAllowedAtRealtime)
            {
                return;
            }

            var position = _localBall.transform.position;
            var insideGoalLane = Mathf.Abs(position.x) <= RetrowaveArenaConfig.GoalHalfWidth * 0.98f;
            var pastGoalLine = Mathf.Abs(position.z) >= RetrowaveArenaConfig.FlatHalfLength + RetrowaveArenaConfig.GoalDepth * 0.32f;
            var insideGoalHeight = position.y <= RetrowaveArenaConfig.GoalHeight + 1.2f;

            if (!insideGoalLane || !pastGoalLine || !insideGoalHeight)
            {
                return;
            }

            HandleOfflineGoal(position.z < 0f ? RetrowaveTeam.Blue : RetrowaveTeam.Pink);
        }

        private void RefreshControlsOverlay(RetrowavePlayerController localPlayer)
        {
            if (_controlsRoot == null)
            {
                return;
            }

            if (!_showTips || PlayerPrefs.GetInt(ControlsSeenKey, 0) != 0 || localPlayer == null)
            {
                _controlsRoot.SetActive(false);
                return;
            }

            if (_controlsOverlayUntilRealtime <= 0f)
            {
                _controlsOverlayUntilRealtime = Time.unscaledTime + ControlsOverlayDurationSeconds;
            }

            if (Time.unscaledTime >= _controlsOverlayUntilRealtime)
            {
                PlayerPrefs.SetInt(ControlsSeenKey, 1);
                PlayerPrefs.Save();
                _controlsRoot.SetActive(false);
                return;
            }

            _controlsRoot.SetActive(true);

            if (_controlsTitleText != null)
            {
                _controlsTitleText.text = "TEST ARENA FREEPLAY";
            }

            if (_controlsBodyText != null)
            {
                _controlsBodyText.text = BuildControlsText();
            }
        }

        private string BuildControlsText()
        {
            return $"Drive {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.DriveForward)}/{RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.DriveReverse)}"
                   + $"  •  Steer {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.SteerLeft)}/{RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.SteerRight)}"
                   + $"  •  Boost {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.Boost)}\n"
                   + $"Jump/glide {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.Jump)}"
                   + $"  •  Rare power {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.ActivateRarePowerUp)}"
                   + $"  •  Reset {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.ResetCar)}\n"
                   + "F1 tips  •  Esc settings";
        }

        private void RefreshTip(RetrowavePlayerController localPlayer, RetrowaveMatchManager matchManager)
        {
            if (_tipRoot == null || _tipText == null)
            {
                return;
            }

            if (!_showTips || localPlayer == null)
            {
                SetTipHudVisible(false);
                return;
            }

            var nextTip = BuildTip(localPlayer, matchManager);

            if (string.IsNullOrWhiteSpace(nextTip))
            {
                SetTipHudVisible(false);
                _tipTextValue = string.Empty;
                return;
            }

            if (!string.Equals(nextTip, _tipTextValue, System.StringComparison.Ordinal)
                && (string.IsNullOrEmpty(_tipTextValue) || Time.unscaledTime >= _tipCanChangeAtRealtime))
            {
                _tipTextValue = nextTip;
                _tipCanChangeAtRealtime = Time.unscaledTime + TipMinimumVisibleSeconds;
            }

            _tipText.text = _tipTextValue;
            _tipRoot.SetActive(true);
        }

        private string BuildTip(RetrowavePlayerController localPlayer, RetrowaveMatchManager matchManager)
        {
            var objectives = _offlineObjectiveSystem;

            if (objectives != null && objectives.ActiveObjectiveType != RetrowaveArenaObjectiveType.None)
            {
                return $"OBJECTIVE: Hold uncontested to capture. Capture grants overdrive. {Mathf.RoundToInt(objectives.CaptureProgressNormalized * 100f)}%";
            }

            if (localPlayer.TryGetComponent<RarePowerUpInventory>(out var rareInventory) && rareInventory.HasHeldPowerUp)
            {
                return $"RARE POWER: Press {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.ActivateRarePowerUp)} to activate {GetRarePowerUpLabel(rareInventory.HeldType)}.";
            }

            var nearestBeacon = FindNearestRareBeacon(localPlayer.transform.position);

            if (nearestBeacon != null)
            {
                var distance = Vector3.Distance(localPlayer.transform.position, nearestBeacon.transform.position);

                if (distance <= 80f)
                {
                    return nearestBeacon.RequiresCapture
                        ? $"RARE BEACON: Stay inside the circle to capture. {Mathf.RoundToInt(nearestBeacon.CaptureProgress * 100f)}%"
                        : "RARE BEACON: First player through claims the power-up.";
                }
            }

            if (localPlayer.BoostAmount <= 18f)
            {
                return "BOOST: Ease off to recharge. Running dry delays recharge and forces a fresh boost press.";
            }

            if (!localPlayer.IsGroundedForHud && localPlayer.BoostAmount > 10f)
            {
                return $"AIR CONTROL: Hold {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.Jump)} while falling to glide toward your nose.";
            }

            if (localPlayer.HeatNormalized > 0.65f)
            {
                return "HEAT: High heat adds threat, but worsens grip and recharge. Let it cool between pushes.";
            }

            if (localPlayer.StyleNormalized < 0.18f && localPlayer.CurrentSpeed > 7f)
            {
                return "STYLE: Drifts, wall rides, clean landings, flips, passes and aerial touches build style.";
            }

            if (localPlayer.CurrentSpeed < 2.2f)
            {
                return $"START: Drive with {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.DriveForward)} and steer with {RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.SteerLeft)}/{RetrowaveInputBindings.GetBindingDisplayName(RetrowaveBindingAction.SteerRight)}.";
            }

            return "FREEPLAY: Practice wall rides, flips, clean landings and controlled touches. Style raises capture speed and boost efficiency.";
        }

        private void RefreshReadout(RetrowavePlayerController localPlayer)
        {
            if (_readoutRoot == null || _readoutText == null)
            {
                return;
            }

            if (localPlayer == null)
            {
                _readoutText.text = "FREEPLAY INPUT  waiting for local vehicle...";
                _readoutRoot.SetActive(true);
                return;
            }

            var rareLabel = "--";

            if (localPlayer.TryGetComponent<RarePowerUpInventory>(out var rareInventory) && rareInventory.HasHeldPowerUp)
            {
                rareLabel = GetRarePowerUpLabel(rareInventory.HeldType).ToUpperInvariant();
            }

            _readoutText.text =
                $"INPUT  THR {FormatSignedInput(localPlayer.LocalThrottleInput)}  STEER {FormatSignedInput(localPlayer.LocalSteerInput)}  ROLL {FormatSignedInput(localPlayer.LocalRollInput)}  "
                + $"{(localPlayer.LocalBoostHeld ? "BOOST ON" : "BOOST --")}  {(localPlayer.LocalJumpHeld ? "JUMP HELD" : "JUMP --")}  RARE {rareLabel}\n"
                + $"SPD {localPlayer.CurrentSpeed:0.0}  BOOST {Mathf.RoundToInt(localPlayer.BoostNormalized * 100f)}%  HEAT {Mathf.RoundToInt(localPlayer.HeatNormalized * 100f)}%  STYLE {Mathf.RoundToInt(localPlayer.StyleNormalized * 100f)}%  "
                + (localPlayer.IsGroundedForHud ? "GROUNDED" : "AIRBORNE");
            _readoutRoot.SetActive(true);
        }

        private void RefreshGauge(RetrowavePlayerController localPlayer)
        {
            if (_gaugeRoot == null)
            {
                return;
            }

            if (localPlayer == null)
            {
                if (_gaugeTitleText != null)
                {
                    _gaugeTitleText.text = "TEST ARENA";
                }

                if (_gaugeStatusText != null)
                {
                    _gaugeStatusText.text = _sessionReady ? "Waiting for local vehicle" : _sessionStatus;
                }

                if (_speedValueText != null)
                {
                    _speedValueText.text = "--";
                }

                if (_speedLabelText != null)
                {
                    _speedLabelText.text = "SPEED";
                }

                if (_boostValueText != null)
                {
                    _boostValueText.text = "--";
                }

                if (_heatValueText != null)
                {
                    _heatValueText.text = "HEAT --";
                }

                if (_styleValueText != null)
                {
                    _styleValueText.text = "STYLE --";
                }

                if (_gaugeSpeedFillImage != null)
                {
                    _gaugeSpeedFillImage.fillAmount = 0f;
                }

                if (_gaugeBoostFillImage != null)
                {
                    _gaugeBoostFillImage.fillAmount = 0f;
                }

                if (_gaugeHeatFillImage != null)
                {
                    _gaugeHeatFillImage.fillAmount = 0f;
                }

                if (_gaugeStyleFillImage != null)
                {
                    _gaugeStyleFillImage.fillAmount = 0f;
                }

                UpdateGaugeBarMarker(_gaugeSpeedBarRect, _gaugeSpeedMarkerRect, 0f);
                UpdateGaugeBarMarker(_gaugeBoostBarRect, _gaugeBoostMarkerRect, 0f);
                UpdateGaugeBarMarker(_gaugeHeatBarRect, _gaugeHeatMarkerRect, 0f);
                UpdateGaugeBarMarker(_gaugeStyleBarRect, _gaugeStyleMarkerRect, 0f);

                if (_rarePowerRoot != null)
                {
                    _rarePowerRoot.SetActive(false);
                }

                return;
            }

            var hasRarePowerUp = localPlayer.TryGetComponent<RarePowerUpInventory>(out var rareInventory) && rareInventory.HasHeldPowerUp;
            var heldType = hasRarePowerUp ? rareInventory.HeldType : RetrowaveRarePowerUpType.None;

            if (_gaugeTitleText != null)
            {
                _gaugeTitleText.text = $"{localPlayer.Team.ToString().ToUpperInvariant()} {RetrowaveUtilityRoleCatalog.GetLabel(localPlayer.UtilityRole).ToUpperInvariant()}";
            }

            if (_gaugeStatusText != null)
            {
                _gaugeStatusText.text = localPlayer.IsStunned
                    ? "STUNNED"
                    : localPlayer.IsSlowed
                    ? "SLOWED"
                    : localPlayer.IsOvercharged
                    ? "OVERDRIVE"
                    : localPlayer.IsOverheated
                    ? "OVERHEATED"
                    : localPlayer.HasSpeedBoost
                    ? "SPEED BURST"
                    : localPlayer.IsGroundedForHud
                    ? "Grounded"
                    : "Airborne";
            }

            if (_speedValueText != null)
            {
                _speedValueText.text = $"{localPlayer.CurrentSpeed:0.0}";
            }

            if (_speedLabelText != null)
            {
                _speedLabelText.text = "SPEED";
            }

            if (_boostValueText != null)
            {
                _boostValueText.text = $"{localPlayer.BoostAmount:0}%";
            }

            if (_heatValueText != null)
            {
                _heatValueText.text = localPlayer.IsOverheated
                    ? "HEAT OVER"
                    : $"HEAT {Mathf.RoundToInt(localPlayer.HeatNormalized * 100f)}%";
            }

            if (_styleValueText != null)
            {
                _styleValueText.text = $"STYLE {Mathf.RoundToInt(localPlayer.StyleNormalized * 100f)}%";
            }

            if (_gaugeSpeedFillImage != null)
            {
                _gaugeSpeedFillImage.fillAmount = localPlayer.SpeedNormalized;
            }

            if (_gaugeBoostFillImage != null)
            {
                _gaugeBoostFillImage.fillAmount = localPlayer.BoostNormalized;
            }

            if (_gaugeHeatFillImage != null)
            {
                _gaugeHeatFillImage.fillAmount = localPlayer.HeatNormalized;
                _gaugeHeatFillImage.color = localPlayer.IsOvercharged
                    ? Color.Lerp(new Color(1f, 0.86f, 0.24f, 1f), Color.white, Mathf.PingPong(Time.time * 3.5f, 0.18f))
                    : localPlayer.IsOverheated
                    ? Color.Lerp(new Color(1f, 0.08f, 0.04f, 1f), Color.white, Mathf.PingPong(Time.time * 4f, 0.25f))
                    : Color.Lerp(new Color(1f, 0.5f, 0.08f, 1f), new Color(1f, 0.1f, 0.04f, 1f), localPlayer.HeatNormalized);
            }

            if (_gaugeStyleFillImage != null)
            {
                _gaugeStyleFillImage.fillAmount = localPlayer.StyleNormalized;
                _gaugeStyleFillImage.color = Color.Lerp(new Color(0.14f, 1f, 0.48f, 1f), new Color(0.94f, 1f, 0.2f, 1f), localPlayer.StyleNormalized);
            }

            UpdateGaugeBarMarker(_gaugeSpeedBarRect, _gaugeSpeedMarkerRect, localPlayer.SpeedNormalized);
            UpdateGaugeBarMarker(_gaugeBoostBarRect, _gaugeBoostMarkerRect, localPlayer.BoostNormalized);
            UpdateGaugeBarMarker(_gaugeHeatBarRect, _gaugeHeatMarkerRect, localPlayer.HeatNormalized);
            UpdateGaugeBarMarker(_gaugeStyleBarRect, _gaugeStyleMarkerRect, localPlayer.StyleNormalized);

            if (_rarePowerRoot != null)
            {
                _rarePowerRoot.SetActive(hasRarePowerUp);
            }

            if (_rarePowerText != null)
            {
                _rarePowerText.text = $"ARMED {GetRarePowerUpLabel(heldType).ToUpperInvariant()}";
            }
        }

        private void RefreshStyleNotification(RetrowavePlayerController localPlayer)
        {
            if (_styleNotificationRoot == null || _styleNotificationText == null || localPlayer == null)
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
                _styleNotificationText.text = $"+{points} STYLE  {RetrowaveStyleEventCatalog.GetLabel(styleEvent).ToUpperInvariant()}";
                _styleNotificationText.color = Color.Lerp(color, Color.white, 0.18f);
                _styleNotificationRoot.SetActive(true);
                _styleNotificationHideAtRealtime = Time.unscaledTime + 1.8f;
            }

            if (_styleNotificationRoot.activeSelf && Time.unscaledTime >= _styleNotificationHideAtRealtime)
            {
                _styleNotificationRoot.SetActive(false);
            }
        }

        private void RefreshScoreStrip()
        {
            if (_scoreStripRoot == null)
            {
                return;
            }

            var showHud = RetrowaveGameSettings.ShowHud;
            _scoreStripRoot.SetActive(showHud);

            if (_scoreStateText != null)
            {
                _scoreStateText.text = "TESTING ARENA";
            }

            if (_scoreClockText != null)
            {
                _scoreClockText.text = "FREEPLAY";
            }

            if (_blueScoreText != null)
            {
                _blueScoreText.text = _blueScore.ToString();
            }

            if (_pinkScoreText != null)
            {
                _pinkScoreText.text = _pinkScore.ToString();
            }
        }

        private void RefreshInfoPanel(RetrowavePlayerController localPlayer)
        {
            if (_infoToggleText != null)
            {
                _infoToggleText.text = "H HIDE";
            }

            if (_infoCollapsedText != null)
            {
                _infoCollapsedText.text = "H show arena info";
            }

            if (_infoConnectionText != null)
            {
                _infoConnectionText.text = "OFFLINE  single-player freeplay arena";
            }

            if (_infoPhaseText != null)
            {
                _infoPhaseText.text = _sessionReady
                    ? $"Freeplay live. Blue {_blueScore} - {_pinkScore} Pink. Goals reset the ball and your car."
                    : _sessionStatus;
            }

            if (_infoRoleText != null)
            {
                _infoRoleText.text = localPlayer != null
                    ? $"Role: {localPlayer.Team} {RetrowaveUtilityRoleCatalog.GetLabel(localPlayer.UtilityRole)}\nPractice boost routing, rare captures, wall rides, aerials, and clean landings."
                    : "Waiting for your local Test Arena car to spawn.";
            }

            if (_infoHintText != null)
            {
                var hint = "Esc settings  •  F1 tips  •  H hide panel";

                if (_offlineObjectiveSystem != null && _offlineObjectiveSystem.ActiveObjectiveType != RetrowaveArenaObjectiveType.None)
                {
                    hint += $"\nObjective live: {RetrowaveArenaObjectiveCatalog.GetLabel(_offlineObjectiveSystem.ActiveObjectiveType)} {Mathf.RoundToInt(_offlineObjectiveSystem.CaptureProgressNormalized * 100f)}%";
                }
                else
                {
                    hint += "\nObjectives and rare beacons rotate in automatically during freeplay.";
                }

                _infoHintText.text = hint;
            }
        }

        private void RefreshObjectivePointer(RetrowavePlayerController localPlayer)
        {
            if (_objectivePointerRoot == null || _objectivePointerText == null)
            {
                return;
            }

            if (localPlayer == null || _offlineObjectiveSystem == null || _offlineObjectiveSystem.ActiveObjectiveType == RetrowaveArenaObjectiveType.None)
            {
                _objectivePointerRoot.SetActive(false);
                return;
            }

            var distance = Vector3.Distance(localPlayer.transform.position, _offlineObjectiveSystem.ObjectivePosition);
            var objectiveColor = RetrowaveArenaObjectiveCatalog.GetColor(_offlineObjectiveSystem.ActiveObjectiveType);
            var teamPrefix = _offlineObjectiveSystem.CapturingTeamValue switch
            {
                (int)RetrowaveTeam.Blue => "Blue",
                (int)RetrowaveTeam.Pink => "Pink",
                _ => "--",
            };
            var text = $"OBJ {teamPrefix}  {Mathf.RoundToInt(distance)}m\n{RetrowaveArenaObjectiveCatalog.GetLabel(_offlineObjectiveSystem.ActiveObjectiveType).ToUpperInvariant()} {Mathf.RoundToInt(_offlineObjectiveSystem.CaptureProgressNormalized * 100f)}%";
            UpdateTargetPointer(
                _objectivePointerRoot,
                _objectivePointerArrowRect,
                _objectivePointerArrowText,
                _objectivePointerText,
                localPlayer.transform.position,
                _offlineObjectiveSystem.ObjectivePosition,
                text,
                objectiveColor);
        }

        private void RefreshRareBeaconPointer(RetrowavePlayerController localPlayer)
        {
            if (_rareBeaconPointerRoot == null || _rareBeaconPointerText == null)
            {
                return;
            }

            if (localPlayer == null)
            {
                _rareBeaconPointerRoot.SetActive(false);
                return;
            }

            var beacon = FindNearestRareBeacon(localPlayer.transform.position);

            if (beacon == null)
            {
                _rareBeaconPointerRoot.SetActive(false);
                return;
            }

            var beaconPosition = beacon.transform.position;
            var distance = Vector3.Distance(localPlayer.transform.position, beaconPosition);
            var color = RetrowavePlayerController.GetRarePowerUpColor(beacon.HeldType);
            var progressText = beacon.RequiresCapture ? $"{Mathf.RoundToInt(beacon.CaptureProgress * 100f)}%" : "READY";
            var text = $"RARE {Mathf.RoundToInt(distance)}m\n{GetRarePowerUpLabel(beacon.HeldType).ToUpperInvariant()} {progressText}";
            UpdateTargetPointer(
                _rareBeaconPointerRoot,
                _rareBeaconPointerArrowRect,
                _rareBeaconPointerArrowText,
                _rareBeaconPointerText,
                localPlayer.transform.position,
                beaconPosition,
                text,
                color);
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

        public void HandleOfflineGoal(RetrowaveTeam defendedGoal)
        {
            if (Time.unscaledTime < _nextGoalAllowedAtRealtime)
            {
                return;
            }

            _nextGoalAllowedAtRealtime = Time.unscaledTime + OfflineGoalResetCooldownSeconds;

            if (defendedGoal == RetrowaveTeam.Blue)
            {
                _pinkScore++;
            }
            else
            {
                _blueScore++;
            }

            RetrowaveArenaAudio.PlayGoalCelebration(Vector3.zero);
            RetrowaveArenaAudio.PlayCrowdCheer(RetrowaveCrowdCheerIntensity.Goal, Vector3.zero, 1f);
            _localBall?.ResetBall();
            _localPlayer?.ResetToSpawn();
            _sessionStatus = $"Goal scored. Blue {_blueScore} - {_pinkScore} Pink";
            RefreshScoreStrip();
        }

        private void SetTipHudVisible(bool isVisible)
        {
            if (_tipRoot != null)
            {
                _tipRoot.SetActive(isVisible);
            }

            if (_controlsRoot != null && !isVisible)
            {
                _controlsRoot.SetActive(false);
            }
        }

        private RarePowerUpPickupBeacon FindNearestRareBeacon(Vector3 position)
        {
            var nearest = default(RarePowerUpPickupBeacon);
            var nearestDistanceSqr = float.PositiveInfinity;
            var beacons = RarePowerUpPickupBeacon.Active;

            for (var i = 0; i < beacons.Count; i++)
            {
                var beacon = beacons[i];

                if (beacon == null || !beacon.IsActive)
                {
                    continue;
                }

                var distanceSqr = (beacon.transform.position - position).sqrMagnitude;

                if (distanceSqr >= nearestDistanceSqr)
                {
                    continue;
                }

                nearestDistanceSqr = distanceSqr;
                nearest = beacon;
            }

            return nearest;
        }

        private void BuildHud()
        {
            _hudRoot = CreateUiObject(
                "Retrowave Test Arena HUD",
                transform,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas = _hudRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35;

            var scaler = _hudRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _statusText = CreateText(
                _hudRoot.transform,
                "Status",
                "Starting offline warmup...",
                17f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -28f),
                new Vector2(560f, 32f),
                new Color(0.72f, 0.92f, 1f, 0.95f));

            _scoreStripRoot = CreatePanel(
                _hudRoot.transform,
                "ScoreStrip",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -26f),
                new Vector2(560f, 108f),
                new Color(0.03f, 0.06f, 0.12f, 0.9f),
                new Color(0.15f, 0.85f, 1f, 0.45f));
            _scoreStateText = CreateText(
                _scoreStripRoot.transform,
                "State",
                "TESTING ARENA",
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -14f),
                new Vector2(360f, 26f),
                new Color(0.6f, 0.91f, 1f, 1f));
            _blueScoreText = CreateText(
                _scoreStripRoot.transform,
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
            _scoreClockText = CreateText(
                _scoreStripRoot.transform,
                "Clock",
                "FREEPLAY",
                30f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -6f),
                new Vector2(220f, 50f),
                Color.white);
            _pinkScoreText = CreateText(
                _scoreStripRoot.transform,
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

            _styleNotificationRoot = CreatePanel(
                _hudRoot.transform,
                "StyleNotification",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -112f),
                new Vector2(440f, 48f),
                new Color(0.015f, 0.04f, 0.08f, 0.86f),
                new Color(0.14f, 1f, 0.48f, 0.36f));
            _styleNotificationText = CreateText(
                _styleNotificationRoot.transform,
                "Text",
                string.Empty,
                17f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(400f, 28f),
                new Color(0.68f, 1f, 0.82f, 1f));
            _styleNotificationRoot.SetActive(false);

            _objectivePointerRoot = CreatePanel(
                _hudRoot.transform,
                "ObjectivePointer",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(-176f, -218f),
                new Vector2(320f, 52f),
                new Color(0.02f, 0.06f, 0.1f, 0.82f),
                new Color(1f, 0.72f, 0.16f, 0.46f));
            _objectivePointerArrowText = CreateText(
                _objectivePointerRoot.transform,
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
            _objectivePointerArrowRect = _objectivePointerArrowText.rectTransform;
            _objectivePointerText = CreateText(
                _objectivePointerRoot.transform,
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
            _objectivePointerRoot.SetActive(false);

            _rareBeaconPointerRoot = CreatePanel(
                _hudRoot.transform,
                "RareBeaconPointer",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(176f, -218f),
                new Vector2(320f, 52f),
                new Color(0.02f, 0.06f, 0.1f, 0.82f),
                new Color(0.42f, 1f, 0.72f, 0.46f));
            _rareBeaconPointerArrowText = CreateText(
                _rareBeaconPointerRoot.transform,
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
            _rareBeaconPointerArrowRect = _rareBeaconPointerArrowText.rectTransform;
            _rareBeaconPointerText = CreateText(
                _rareBeaconPointerRoot.transform,
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
            _rareBeaconPointerRoot.SetActive(false);

            _infoRoot = CreatePanel(
                _hudRoot.transform,
                "InfoPanel",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -36f),
                new Vector2(540f, 312f),
                new Color(0.04f, 0.08f, 0.14f, 0.88f),
                new Color(0.11f, 0.74f, 0.95f, 0.36f));

            CreateText(
                _infoRoot.transform,
                "InfoHeader",
                "ARENA STATUS",
                22f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -16f),
                new Vector2(270f, 34f),
                Color.white);

            _infoToggleText = CreateText(
                _infoRoot.transform,
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

            _infoConnectionText = CreateText(
                _infoRoot.transform,
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

            _infoPhaseText = CreateText(
                _infoRoot.transform,
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

            _infoRoleText = CreateText(
                _infoRoot.transform,
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

            _infoHintText = CreateText(
                _infoRoot.transform,
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

            _infoCollapsedRoot = CreatePanel(
                _hudRoot.transform,
                "InfoCollapsed",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -36f),
                new Vector2(230f, 54f),
                new Color(0.03f, 0.07f, 0.13f, 0.82f),
                new Color(0.11f, 0.74f, 0.95f, 0.28f));

            _infoCollapsedText = CreateText(
                _infoCollapsedRoot.transform,
                "CollapsedText",
                "H show arena info",
                16f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(180f, 28f),
                new Color(0.72f, 0.92f, 1f, 1f));
            _infoCollapsedRoot.SetActive(false);

            _tipRoot = CreatePanel(
                _hudRoot.transform,
                "ReactiveTip",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-28f, 306f),
                new Vector2(430f, 146f),
                new Color(0.02f, 0.06f, 0.1f, 0.84f),
                new Color(0.58f, 0.9f, 1f, 0.34f));
            _tipText = CreateText(
                _tipRoot.transform,
                "Text",
                string.Empty,
                15f,
                FontStyles.Bold,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(18f, -16f),
                new Vector2(394f, 112f),
                new Color(0.82f, 0.95f, 1f, 1f));
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            _tipRoot.SetActive(false);

            _controlsRoot = CreatePanel(
                _hudRoot.transform,
                "FirstTimeControls",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 140f),
                new Vector2(760f, 166f),
                new Color(0.02f, 0.05f, 0.1f, 0.9f),
                new Color(0.42f, 1f, 0.72f, 0.38f));
            _controlsTitleText = CreateText(
                _controlsRoot.transform,
                "Title",
                string.Empty,
                21f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -16f),
                new Vector2(520f, 30f),
                new Color(0.68f, 1f, 0.82f, 1f));
            _controlsBodyText = CreateText(
                _controlsRoot.transform,
                "Body",
                string.Empty,
                16f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -52f),
                new Vector2(690f, 92f),
                new Color(0.92f, 0.98f, 1f, 0.98f));
            _controlsBodyText.lineSpacing = 4f;
            _controlsRoot.SetActive(false);

            _readoutRoot = CreatePanel(
                _hudRoot.transform,
                "FreeplayReadout",
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(28f, 28f),
                new Vector2(760f, 72f),
                new Color(0.015f, 0.035f, 0.07f, 0.86f),
                new Color(0.11f, 0.87f, 1f, 0.28f));
            _readoutText = CreateText(
                _readoutRoot.transform,
                "Text",
                string.Empty,
                13f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(710f, 52f),
                new Color(0.86f, 0.95f, 1f, 0.96f));
            _readoutText.textWrappingMode = TextWrappingModes.NoWrap;

            _gaugeRoot = CreatePanel(
                _hudRoot.transform,
                "GaugePanel",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-28f, 28f),
                new Vector2(430f, 260f),
                new Color(0.03f, 0.05f, 0.11f, 0.9f),
                new Color(0.96f, 0.34f, 0.74f, 0.28f));

            _gaugeTitleText = CreateText(
                _gaugeRoot.transform,
                "GaugeTitle",
                "TEST ARENA",
                18f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(22f, -18f),
                new Vector2(210f, 28f),
                Color.white);

            _gaugeStatusText = CreateText(
                _gaugeRoot.transform,
                "GaugeStatus",
                string.Empty,
                15f,
                FontStyles.Normal,
                TextAlignmentOptions.Right,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -18f),
                new Vector2(190f, 24f),
                new Color(0.57f, 0.86f, 1f, 0.95f));

            _rarePowerRoot = CreatePanel(
                _gaugeRoot.transform,
                "RarePower",
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-20f, -46f),
                new Vector2(220f, 32f),
                new Color(0.03f, 0.08f, 0.12f, 0.86f),
                new Color(0.12f, 1f, 0.4f, 0.42f));
            _rarePowerText = CreateText(
                _rarePowerRoot.transform,
                "RarePowerText",
                string.Empty,
                13f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(188f, 22f),
                Color.white);
            _rarePowerRoot.SetActive(false);

            _speedValueText = CreateText(
                _gaugeRoot.transform,
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

            _speedLabelText = CreateText(
                _gaugeRoot.transform,
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

            CreateGaugeBar(
                _gaugeRoot.transform,
                "SpeedBar",
                new Vector2(24f, -130f),
                new Vector2(382f, 18f),
                new Color(0.96f, 0.34f, 0.74f, 1f),
                out _gaugeSpeedFillImage,
                out _gaugeSpeedBarRect,
                out _gaugeSpeedMarkerRect);

            _boostValueText = CreateText(
                _gaugeRoot.transform,
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

            CreateText(
                _gaugeRoot.transform,
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

            CreateGaugeBar(
                _gaugeRoot.transform,
                "BoostBar",
                new Vector2(24f, -172f),
                new Vector2(382f, 14f),
                new Color(0.11f, 0.87f, 1f, 1f),
                out _gaugeBoostFillImage,
                out _gaugeBoostBarRect,
                out _gaugeBoostMarkerRect);

            _heatValueText = CreateText(
                _gaugeRoot.transform,
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

            CreateGaugeBar(
                _gaugeRoot.transform,
                "HeatBar",
                new Vector2(24f, -214f),
                new Vector2(382f, 10f),
                new Color(1f, 0.38f, 0.08f, 1f),
                out _gaugeHeatFillImage,
                out _gaugeHeatBarRect,
                out _gaugeHeatMarkerRect);

            _styleValueText = CreateText(
                _gaugeRoot.transform,
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

            CreateGaugeBar(
                _gaugeRoot.transform,
                "StyleBar",
                new Vector2(24f, -248f),
                new Vector2(382f, 10f),
                new Color(0.14f, 1f, 0.48f, 1f),
                out _gaugeStyleFillImage,
                out _gaugeStyleBarRect,
                out _gaugeStyleMarkerRect);
        }

        private void ReturnToMainMenu()
        {
            RetrowaveGameBootstrap.Instance?.CloseSharedSettingsOverlay();
            Time.timeScale = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            TearDownLocalUiForSceneExit();
            var bootstrap = RetrowaveGameBootstrap.Instance;

            if (bootstrap != null)
            {
                bootstrap.ReturnToMainMenu();
                return;
            }

            if (SceneManager.GetActiveScene().name != RetrowaveGameBootstrap.MainMenuSceneName)
            {
                SceneManager.LoadScene(RetrowaveGameBootstrap.MainMenuSceneName, LoadSceneMode.Single);
            }
        }

        private void TearDownLocalUiForSceneExit()
        {
            if (_hudRoot != null)
            {
                Destroy(_hudRoot);
                _hudRoot = null;
            }
        }

        private void CreateGaugeBar(
            Transform parent,
            string name,
            Vector2 anchoredPosition,
            Vector2 size,
            Color fillColor,
            out Image fillImage,
            out RectTransform barRect,
            out RectTransform markerRect)
        {
            var root = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image));
            barRect = root.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 1f);
            barRect.anchoredPosition = anchoredPosition;
            barRect.sizeDelta = size;
            root.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.18f, 0.95f);

            var fill = CreateUiObject("Fill", root.transform, typeof(RectTransform), typeof(Image));
            var fillRect = fill.GetComponent<RectTransform>();
            StretchToParent(fillRect);
            fillImage = fill.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;

            var marker = CreateUiObject("Marker", root.transform, typeof(RectTransform), typeof(Image));
            markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = new Vector2(0f, 0.5f);
            markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.anchoredPosition = Vector2.zero;
            markerRect.sizeDelta = new Vector2(18f, 18f);
            var markerImage = marker.GetComponent<Image>();
            markerImage.color = Color.Lerp(fillColor, Color.white, 0.55f);
            var markerOutline = marker.AddComponent<Outline>();
            markerOutline.effectColor = new Color(0.03f, 0.04f, 0.08f, 0.92f);
            markerOutline.effectDistance = new Vector2(1f, 1f);
        }

        private static void UpdateGaugeBarMarker(RectTransform barRect, RectTransform markerRect, float normalized)
        {
            if (barRect == null || markerRect == null)
            {
                return;
            }

            var width = Mathf.Max(0f, barRect.rect.width);
            var halfMarkerWidth = markerRect.rect.width * 0.5f;
            markerRect.anchoredPosition = new Vector2(Mathf.Lerp(halfMarkerWidth, Mathf.Max(halfMarkerWidth, width - halfMarkerWidth), Mathf.Clamp01(normalized)), 0f);
        }

        private GameObject CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color background,
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
            image.color = background;
            image.raycastTarget = false;

            var outline = panel.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(2f, 2f);
            return panel;
        }

        private TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
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
            rect.sizeDelta = size;

            var text = textObject.GetComponent<TextMeshProUGUI>();

            if (_font != null)
            {
                text.font = _font;
            }

            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateHudButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.08f, 0.48f, 0.96f, 0.96f);

            var outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.58f, 0.9f, 1f, 0.38f);
            outline.effectDistance = new Vector2(2f, 2f);

            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = Color.Lerp(image.color, Color.white, 0.14f);
            colors.pressedColor = Color.Lerp(image.color, Color.black, 0.14f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(onClick);

            var text = CreateText(
                buttonObject.transform,
                "Label",
                label,
                17f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                Color.white);
            text.rectTransform.offsetMin = new Vector2(16f, 8f);
            text.rectTransform.offsetMax = new Vector2(-16f, -8f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 13f;
            text.fontSizeMax = 17f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        private static GameObject CreateUiObject(string name, Transform parent, params System.Type[] components)
        {
            var gameObject = new GameObject(name, components);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
        }

        private static string FormatSignedInput(float value)
        {
            return Mathf.Abs(value) < 0.005f ? "0.00" : value.ToString("+0.00;-0.00");
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

        private static Color GetStyleEventColor(RetrowaveStyleEvent styleEvent)
        {
            return styleEvent switch
            {
                RetrowaveStyleEvent.CleanLanding => new Color(0.42f, 1f, 0.72f, 1f),
                RetrowaveStyleEvent.FlipTrick => new Color(1f, 0.42f, 0.92f, 1f),
                RetrowaveStyleEvent.WallRide => new Color(0.46f, 0.84f, 1f, 1f),
                RetrowaveStyleEvent.TeamCombo => new Color(1f, 0.82f, 0.26f, 1f),
                RetrowaveStyleEvent.Pass => new Color(0.52f, 0.9f, 1f, 1f),
                RetrowaveStyleEvent.PowerPlay => new Color(0.24f, 0.78f, 1f, 1f),
                RetrowaveStyleEvent.ObjectiveCapture => new Color(1f, 0.72f, 0.16f, 1f),
                _ => new Color(0.72f, 1f, 0.82f, 1f),
            };
        }
    }
}
