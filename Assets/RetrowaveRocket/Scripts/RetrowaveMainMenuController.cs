using System.Collections;
using System.Collections.Generic;
using SlimUI.ModernMenu;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RetrowaveRocket
{
    public sealed class RetrowaveMainMenuController : MonoBehaviour
    {
        private const string ControllerName = "Retrowave Main Menu Controller";

        private enum ConnectionTab
        {
            Join = 0,
            Host = 1,
        }

        private static readonly int[] RoundDurationOptions = { 60, 90, 120, 180, 240, 300, 420, 600, 900 };
        private static readonly int[] RoundCountOptions = { 1, 2, 3, 5, 7, 9, 12 };
        private static readonly int[] MaxPlayerOptions = { 2, 4, 6, 8, 10, 12, 16, 20, 24, 28, 32, 36, 40 };
        private static readonly RetrowaveArenaSizePreset[] ArenaSizeOptions =
        {
            RetrowaveArenaSizePreset.Auto,
            RetrowaveArenaSizePreset.Compact,
            RetrowaveArenaSizePreset.Standard,
            RetrowaveArenaSizePreset.Expanded,
            RetrowaveArenaSizePreset.Stadium,
            RetrowaveArenaSizePreset.Mega,
        };

        private readonly List<Selectable> _interactiveControls = new();

        private UIMenuManager _menuManager;
        private TMP_FontAsset _font;
        private TMP_InputField _joinNameField;
        private TMP_InputField _joinAddressField;
        private TMP_InputField _joinPortField;
        private TMP_InputField _hostNameField;
        private TMP_InputField _hostAddressField;
        private TMP_InputField _hostPortField;
        private TMP_Text _roundDurationValueText;
        private TMP_Text _roundCountValueText;
        private TMP_Text _maxPlayersValueText;
        private TMP_Text _arenaSizeValueText;
        private TMP_Text _hostSummaryText;
        private TMP_Text _statusText;
        private TMP_Text _scrollHintText;
        private Button _joinTabButton;
        private Button _hostTabButton;
        private Image _joinTabImage;
        private Image _hostTabImage;
        private Button _primaryActionButton;
        private TMP_Text _primaryActionLabel;
        private Button _backButton;
        private GameObject _screenCanvasRoot;
        private ScrollRect _contentScrollRect;
        private Scrollbar _contentScrollbar;
        private GameObject _joinPanel;
        private GameObject _hostPanel;
        private ConnectionTab _activeTab = ConnectionTab.Host;
        private int _roundDurationIndex = 5;
        private int _roundCountIndex = 2;
        private int _maxPlayersIndex = 1;
        private int _arenaSizeIndex = 0;
        private bool _syncingDisplayNameFields;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryCreateController(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryCreateController(scene);
        }

        private static void TryCreateController(Scene scene)
        {
            if (scene.name != RetrowaveGameBootstrap.MainMenuSceneName)
            {
                return;
            }

            if (FindFirstObjectByType<RetrowaveMainMenuController>() != null)
            {
                return;
            }

            var controllerObject = new GameObject(ControllerName);
            controllerObject.AddComponent<RetrowaveMainMenuController>();
        }

        private IEnumerator Start()
        {
            yield return null;
            EnsureEventSystemIsInputSystemCompatible();

            _menuManager = FindFirstObjectByType<UIMenuManager>();

            if (_menuManager == null)
            {
                Debug.LogWarning("SlimUI menu manager was not found in MainMenu scene.");
                yield break;
            }

            var existingLabel = FindFirstObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            _font = existingLabel != null ? existingLabel.font : null;

            ConfigureHeadline();
            BuildTabbedPlayPanel();
            FocusDefaultButton();
        }

        private void ConfigureHeadline()
        {
            var labels = _menuManager.mainMenu.GetComponentsInChildren<TextMeshProUGUI>(true);

            foreach (var label in labels)
            {
                var lower = label.text.Trim().ToLowerInvariant();

                if (lower is "modern menu" or "play game")
                {
                    label.text = "THROTTLEBALL";
                }
                else if (lower.Contains("lorem") || lower.Contains("ipsum"))
                {
                    label.text = "Retrowave arena soccer with LAN hosting, warmup spectating, and procedural match setup.";
                }
            }
        }

        private void LateUpdate()
        {
            if (_screenCanvasRoot == null || _menuManager == null || _menuManager.playMenu == null)
            {
                return;
            }

            var shouldBeVisible = _menuManager.playMenu.activeInHierarchy;

            if (_screenCanvasRoot.activeSelf != shouldBeVisible)
            {
                _screenCanvasRoot.SetActive(shouldBeVisible);
            }
        }

        private void BuildTabbedPlayPanel()
        {
            HideExistingPlayMenuContents();

            var screenCanvas = CreateUiObject(
                "Retrowave Play Screen Canvas",
                transform,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _screenCanvasRoot = screenCanvas;
            StretchToParent(screenCanvas.GetComponent<RectTransform>());

            var canvas = screenCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 25;
            canvas.worldCamera = null;

            var scaler = screenCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = CreateUiObject("Retrowave Play Panel", screenCanvas.transform, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -26f);
            rootRect.sizeDelta = new Vector2(980f, 700f);

            var rootImage = root.GetComponent<Image>();
            rootImage.color = new Color(0.04f, 0.06f, 0.12f, 0.95f);
            rootImage.raycastTarget = false;

            var outline = root.AddComponent<Outline>();
            outline.effectColor = RetrowaveStyle.BlueGlow;
            outline.effectDistance = new Vector2(2f, 2f);

            var rootLayout = root.GetComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(28, 28, 24, 24);
            rootLayout.spacing = 14f;
            rootLayout.childControlHeight = false;
            rootLayout.childControlWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.childForceExpandWidth = true;

            root.GetComponent<LayoutElement>().preferredWidth = 980f;
            root.GetComponent<LayoutElement>().preferredHeight = 700f;

            CreateHeaderText(root.transform, "Title", "PLAY THROTTLEBALL", 34f, FontStyles.Bold, Color.white, 44f);
            CreateHeaderText(root.transform, "Body", "Switch between joining a running LAN server or hosting a custom procedural match with tuned round length, player cap, and arena scale.", 18f, FontStyles.Normal, new Color(0.86f, 0.91f, 0.96f, 1f), 48f);

            BuildTabBar(root.transform);
            BuildContentPanels(root.transform);

            _statusText = CreateHeaderText(root.transform, "Status", "Host creates the match on this machine. Join connects to another device on your LAN.", 17f, FontStyles.Normal, new Color(0.57f, 0.86f, 1f, 1f), 42f);
            _statusText.alignment = TextAlignmentOptions.MidlineLeft;

            BuildFooter(root.transform);
            UpdateHostSettingsSummary();
            SelectTab(ConnectionTab.Host);
            _screenCanvasRoot.SetActive(_menuManager.playMenu.activeInHierarchy);
        }

        private void BuildTabBar(Transform parent)
        {
            var tabRow = CreateUiObject("Tab Row", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var tabLayout = tabRow.GetComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 14f;
            tabLayout.childControlHeight = true;
            tabLayout.childControlWidth = true;
            tabLayout.childForceExpandHeight = false;
            tabLayout.childForceExpandWidth = true;
            tabRow.GetComponent<LayoutElement>().preferredHeight = 52f;

            (_joinTabButton, _joinTabImage) = CreateTabButton(tabRow.transform, "Join Game", () => SelectTab(ConnectionTab.Join));
            (_hostTabButton, _hostTabImage) = CreateTabButton(tabRow.transform, "Host Game", () => SelectTab(ConnectionTab.Host));
        }

        private void BuildContentPanels(Transform parent)
        {
            var contentFrame = CreateUiObject("Content Frame", parent, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(ScrollRect));
            contentFrame.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.15f, 0.92f);
            contentFrame.GetComponent<Image>().raycastTarget = true;
            contentFrame.GetComponent<LayoutElement>().preferredHeight = 340f;

            var contentRect = contentFrame.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.sizeDelta = new Vector2(0f, 340f);

            var viewport = CreateUiObject("Viewport", contentFrame.transform, typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewportRect = viewport.GetComponent<RectTransform>();
            StretchToParent(viewportRect);
            viewportRect.offsetMax = new Vector2(-26f, 0f);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);

            var scrollbarObject = CreateUiObject("Scrollbar", contentFrame.transform, typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-16f, 18f);
            scrollbarRect.offsetMax = new Vector2(-4f, -18f);

            var scrollbarImage = scrollbarObject.GetComponent<Image>();
            scrollbarImage.color = new Color(0.1f, 0.15f, 0.24f, 0.95f);

            var handleObject = CreateUiObject("Handle", scrollbarObject.transform, typeof(RectTransform), typeof(Image));
            var handleRect = handleObject.GetComponent<RectTransform>();
            StretchToParent(handleRect);
            handleRect.offsetMin = new Vector2(1f, 1f);
            handleRect.offsetMax = new Vector2(-1f, -1f);
            handleObject.GetComponent<Image>().color = new Color(0.2f, 0.82f, 1f, 0.95f);

            _joinPanel = CreateStretchPanel("Join Panel", viewport.transform);
            _hostPanel = CreateStretchPanel("Host Panel", viewport.transform);

            _contentScrollRect = contentFrame.GetComponent<ScrollRect>();
            _contentScrollRect.viewport = viewportRect;
            _contentScrollRect.horizontal = false;
            _contentScrollRect.vertical = true;
            _contentScrollRect.scrollSensitivity = 26f;
            _contentScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _contentScrollRect.onValueChanged.AddListener(_ => UpdateScrollIndicators());

            _contentScrollbar = scrollbarObject.GetComponent<Scrollbar>();
            _contentScrollbar.direction = Scrollbar.Direction.BottomToTop;
            _contentScrollbar.handleRect = handleRect;
            _contentScrollbar.targetGraphic = handleObject.GetComponent<Image>();
            _contentScrollbar.size = 0.25f;
            _contentScrollRect.verticalScrollbar = _contentScrollbar;
            _contentScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            _scrollHintText = CreateOverlayHint(contentFrame.transform, "ScrollHint", "Scroll to see more options");

            BuildJoinPanel(_joinPanel.transform);
            BuildHostPanel(_hostPanel.transform);
        }

        private void BuildJoinPanel(Transform parent)
        {
            ConfigurePanelLayout(parent);
            CreatePanelText(parent, "JoinIntro", "Join an active server by entering the host machine's LAN IP and port. Use the host tab on another machine to create the match first.", 18f, new Color(0.89f, 0.92f, 0.96f, 1f), 48f);
            _joinNameField = CreateInputRow(parent, "Display Name", RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.PreferredDisplayName : "Player", characterLimit: 24);
            _joinAddressField = CreateInputRow(parent, "Server Address", RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.DefaultAddress : "127.0.0.1");
            _joinPortField = CreateInputRow(parent, "Port", RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.DefaultPort : "7777", numeric: true);
            CreatePanelText(parent, "JoinHint", "Tip: the host's local address is shown in the host tab, so copying that value between devices should be enough on the same network.", 16f, new Color(0.72f, 0.82f, 0.92f, 1f), 44f);
            BindDisplayNameField(_joinNameField, isHostField: false);
        }

        private void BuildHostPanel(Transform parent)
        {
            ConfigurePanelLayout(parent);
            CreatePanelText(parent, "HostIntro", "Host a match locally and let the arena scale up as player count increases. Larger lobbies automatically force a larger generated field so movement never feels cramped.", 18f, new Color(0.89f, 0.92f, 0.96f, 1f), 48f);

            _hostNameField = CreateInputRow(parent, "Display Name", RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.PreferredDisplayName : "Host", characterLimit: 24);
            var hostAddress = RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.SuggestedHostAddress : "127.0.0.1";
            _hostAddressField = CreateInputRow(parent, "Host Address", hostAddress);
            _hostPortField = CreateInputRow(parent, "Port", RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.DefaultPort : "7777", numeric: true);

            _roundDurationValueText = CreateSelectorRow(parent, "Round Time", () => StepRoundDuration(-1), () => StepRoundDuration(1));
            _roundCountValueText = CreateSelectorRow(parent, "Rounds", () => StepRoundCount(-1), () => StepRoundCount(1));
            _maxPlayersValueText = CreateSelectorRow(parent, "Max Players", () => StepMaxPlayers(-1), () => StepMaxPlayers(1));
            _arenaSizeValueText = CreateSelectorRow(parent, "Arena Size", () => StepArenaSize(-1), () => StepArenaSize(1));
            _hostSummaryText = CreatePanelText(parent, "HostSummary", string.Empty, 16f, new Color(0.59f, 0.86f, 1f, 1f), 68f);
            BindDisplayNameField(_hostNameField, isHostField: true);
        }

        private void BuildFooter(Transform parent)
        {
            var footer = CreateUiObject("Footer", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var layout = footer.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            footer.GetComponent<LayoutElement>().preferredHeight = 54f;

            _primaryActionButton = CreateActionButton(footer.transform, "Primary Action", "HOST MATCH", new Color(0.08f, 0.48f, 0.96f, 1f), HandlePrimaryActionClicked, 260f);
            _primaryActionLabel = _primaryActionButton.GetComponentInChildren<TextMeshProUGUI>(true);
            _backButton = CreateActionButton(footer.transform, "Back Action", "BACK", new Color(0.16f, 0.2f, 0.26f, 1f), HandleBackClicked, 180f);
        }

        private void StepRoundDuration(int direction)
        {
            _roundDurationIndex = WrapIndex(_roundDurationIndex + direction, RoundDurationOptions.Length);
            UpdateHostSettingsSummary();
        }

        private void StepRoundCount(int direction)
        {
            _roundCountIndex = WrapIndex(_roundCountIndex + direction, RoundCountOptions.Length);
            UpdateHostSettingsSummary();
        }

        private void StepMaxPlayers(int direction)
        {
            _maxPlayersIndex = WrapIndex(_maxPlayersIndex + direction, MaxPlayerOptions.Length);
            UpdateHostSettingsSummary();
        }

        private void StepArenaSize(int direction)
        {
            _arenaSizeIndex = WrapIndex(_arenaSizeIndex + direction, ArenaSizeOptions.Length);
            UpdateHostSettingsSummary();
        }

        private void UpdateHostSettingsSummary()
        {
            if (_roundDurationValueText == null || _roundCountValueText == null || _maxPlayersValueText == null || _arenaSizeValueText == null || _hostSummaryText == null)
            {
                return;
            }

            _roundDurationValueText.text = FormatRoundDuration(RoundDurationOptions[_roundDurationIndex]);
            _roundCountValueText.text = $"{RoundCountOptions[_roundCountIndex]} rounds";
            _maxPlayersValueText.text = $"{MaxPlayerOptions[_maxPlayersIndex]} players";
            _arenaSizeValueText.text = GetArenaSizeLabel(ArenaSizeOptions[_arenaSizeIndex]);

            var resolvedLayout = RetrowaveArenaLayout.Resolve(BuildHostSettings());
            var effectivePreset = (RetrowaveArenaSizePreset)Mathf.Max((int)ArenaSizeOptions[_arenaSizeIndex], ResolveMinimumArenaPreset(MaxPlayerOptions[_maxPlayersIndex]));
            var totalSeconds = RoundDurationOptions[_roundDurationIndex] * RoundCountOptions[_roundCountIndex];
            var sizeLead = ArenaSizeOptions[_arenaSizeIndex] == RetrowaveArenaSizePreset.Auto
                ? $"Auto scale resolves to {GetArenaSizeLabel(effectivePreset).ToLowerInvariant()}"
                : $"{GetArenaSizeLabel(effectivePreset)} size";
            _hostSummaryText.text = $"{FormatRoundDuration(RoundDurationOptions[_roundDurationIndex])} x {RoundCountOptions[_roundCountIndex]} rounds = {FormatRoundDuration(totalSeconds)} match time. {sizeLead} with {resolvedLayout.PowerUpPositions.Length} power-up lanes.";
        }

        private RetrowaveMatchSettings BuildHostSettings()
        {
            return new RetrowaveMatchSettings(
                RoundDurationOptions[_roundDurationIndex],
                RoundCountOptions[_roundCountIndex],
                MaxPlayerOptions[_maxPlayersIndex],
                ArenaSizeOptions[_arenaSizeIndex]);
        }

        private void SelectTab(ConnectionTab tab)
        {
            _activeTab = tab;

            if (_joinPanel != null)
            {
                _joinPanel.SetActive(tab == ConnectionTab.Join);
            }

            if (_hostPanel != null)
            {
                _hostPanel.SetActive(tab == ConnectionTab.Host);
            }

            if (_contentScrollRect != null)
            {
                _contentScrollRect.content = (tab == ConnectionTab.Join ? _joinPanel : _hostPanel).GetComponent<RectTransform>();
                Canvas.ForceUpdateCanvases();
                _contentScrollRect.verticalNormalizedPosition = 1f;
                UpdateScrollIndicators();
            }

            ApplyTabVisual(_joinTabButton, _joinTabImage, tab == ConnectionTab.Join);
            ApplyTabVisual(_hostTabButton, _hostTabImage, tab == ConnectionTab.Host);

            if (_primaryActionLabel != null)
            {
                _primaryActionLabel.text = tab == ConnectionTab.Join ? "JOIN SERVER" : "HOST MATCH";
            }

            if (_statusText != null)
            {
                _statusText.text = tab == ConnectionTab.Join
                    ? "Enter the host machine's address and port, then join the live lobby."
                    : "Tune round length, round count, player cap, and arena scale before spinning up the local host.";
            }
        }

        private void HandlePrimaryActionClicked()
        {
            if (RetrowaveGameBootstrap.Instance == null)
            {
                _statusText.text = "Network bootstrap is missing.";
                return;
            }

            if (_activeTab == ConnectionTab.Join)
            {
                if (RetrowaveGameBootstrap.Instance.BeginClientFromMenu(_joinNameField.text, _joinAddressField.text, _joinPortField.text, out var joinMessage))
                {
                    SetInteractable(false);
                }

                _statusText.text = joinMessage;
                return;
            }

            if (RetrowaveGameBootstrap.Instance.BeginHostFromMenu(_hostNameField.text, _hostAddressField.text, _hostPortField.text, BuildHostSettings(), out var hostMessage))
            {
                SetInteractable(false);
            }

            _statusText.text = hostMessage;
        }

        private void HandleBackClicked()
        {
            _menuManager.ReturnMenu();
        }

        private void SetInteractable(bool isInteractable)
        {
            for (var i = 0; i < _interactiveControls.Count; i++)
            {
                if (_interactiveControls[i] != null)
                {
                    _interactiveControls[i].interactable = isInteractable;
                }
            }
        }

        private void FocusDefaultButton()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();

            if (eventSystem != null && _primaryActionButton != null)
            {
                eventSystem.SetSelectedGameObject(_primaryActionButton.gameObject);
            }
        }

        private void HideExistingPlayMenuContents()
        {
            foreach (Transform child in _menuManager.playMenu.transform)
            {
                child.gameObject.SetActive(false);
            }
        }

        private static void EnsureEventSystemIsInputSystemCompatible()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
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

        private void ConfigurePanelLayout(Transform panel)
        {
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 20, 20);
            layout.spacing = 12f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
        }

        private GameObject CreateStretchPanel(string name, Transform parent)
        {
            var panel = CreateUiObject(name, parent, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 0f);
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return panel;
        }

        private TMP_Text CreateHeaderText(Transform parent, string name, string value, float fontSize, FontStyles fontStyle, Color color, float preferredHeight)
        {
            var textObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private TMP_Text CreatePanelText(Transform parent, string name, string value, float fontSize, Color color, float preferredHeight)
        {
            return CreateHeaderText(parent, name, value, fontSize, FontStyles.Normal, color, preferredHeight);
        }

        private TMP_InputField CreateInputRow(Transform parent, string label, string value, bool numeric = false, int characterLimit = 0)
        {
            var row = CreateUiObject($"{label} Row", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().preferredHeight = 58f;

            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 14f;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            CreateFixedLabel(row.transform, label, 180f);

            var fieldObject = CreateUiObject($"{label} Input", row.transform, typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            fieldObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            fieldObject.GetComponent<LayoutElement>().minWidth = 360f;
            fieldObject.GetComponent<LayoutElement>().preferredHeight = 54f;

            var fieldImage = fieldObject.GetComponent<Image>();
            fieldImage.color = new Color(0.08f, 0.1f, 0.16f, 1f);

            var fieldOutline = fieldObject.AddComponent<Outline>();
            fieldOutline.effectColor = new Color(0.25f, 0.8f, 1f, 0.75f);
            fieldOutline.effectDistance = new Vector2(1f, 1f);

            var viewport = CreateUiObject("Text Area", fieldObject.transform, typeof(RectTransform), typeof(RectMask2D));
            StretchToParent(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<RectTransform>().offsetMin = new Vector2(16f, 8f);
            viewport.GetComponent<RectTransform>().offsetMax = new Vector2(-16f, -8f);

            var placeholder = CreateTextElement(viewport.transform, "Placeholder", label, 20f, new Color(0.5f, 0.58f, 0.68f, 0.9f));
            StretchToParent(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;

            var text = CreateTextElement(viewport.transform, "Text", value, 20f, Color.white);
            StretchToParent(text.rectTransform);
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var input = fieldObject.GetComponent<TMP_InputField>();
            input.textViewport = viewport.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value;
            input.contentType = numeric ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.caretColor = RetrowaveStyle.BlueGlow;
            input.selectionColor = new Color(0.14f, 0.45f, 0.75f, 0.45f);
            input.characterLimit = characterLimit;

            RegisterInteractive(input);
            return input;
        }

        private void BindDisplayNameField(TMP_InputField field, bool isHostField)
        {
            if (field == null)
            {
                return;
            }

            field.onValueChanged.AddListener(value => SyncDisplayNameFields(value, isHostField));
        }

        private void SyncDisplayNameFields(string value, bool fromHostField)
        {
            if (_syncingDisplayNameFields)
            {
                return;
            }

            _syncingDisplayNameFields = true;

            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            if (normalized.Length > 24)
            {
                normalized = normalized[..24];
            }

            if (fromHostField)
            {
                if (_joinNameField != null && _joinNameField.text != normalized)
                {
                    _joinNameField.text = normalized;
                }

                if (_hostNameField != null && _hostNameField.text != normalized)
                {
                    _hostNameField.text = normalized;
                }
            }
            else
            {
                if (_hostNameField != null && _hostNameField.text != normalized)
                {
                    _hostNameField.text = normalized;
                }

                if (_joinNameField != null && _joinNameField.text != normalized)
                {
                    _joinNameField.text = normalized;
                }
            }

            _syncingDisplayNameFields = false;
        }

        private TMP_Text CreateValueRow(Transform parent, string label, string value)
        {
            var row = CreateUiObject($"{label} Row", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().preferredHeight = 58f;

            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 14f;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            CreateFixedLabel(row.transform, label, 220f);

            var valueContainer = CreateUiObject($"{label} Value", row.transform, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            valueContainer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            valueContainer.GetComponent<LayoutElement>().preferredHeight = 54f;
            valueContainer.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.16f, 1f);
            valueContainer.GetComponent<Image>().raycastTarget = false;

            var outline = valueContainer.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
            outline.effectDistance = new Vector2(1f, 1f);

            var valueText = CreateTextElement(valueContainer.transform, "Value", value, 20f, Color.white);
            StretchToParent(valueText.rectTransform);
            valueText.margin = new Vector4(16f, 8f, 16f, 8f);
            valueText.alignment = TextAlignmentOptions.MidlineLeft;
            return valueText;
        }

        private TMP_Text CreateSelectorRow(Transform parent, string label, UnityEngine.Events.UnityAction onPrevious, UnityEngine.Events.UnityAction onNext)
        {
            var row = CreateUiObject($"{label} Row", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().preferredHeight = 58f;

            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 14f;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            CreateFixedLabel(row.transform, label, 180f);
            CreateSelectorButton(row.transform, "Prev", "<", onPrevious);

            var valueContainer = CreateUiObject($"{label} Value", row.transform, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            valueContainer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            valueContainer.GetComponent<LayoutElement>().minWidth = 280f;
            valueContainer.GetComponent<LayoutElement>().preferredHeight = 54f;
            valueContainer.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.16f, 1f);
            valueContainer.GetComponent<Image>().raycastTarget = false;

            var valueText = CreateTextElement(valueContainer.transform, "Value", string.Empty, 20f, Color.white);
            StretchToParent(valueText.rectTransform);
            valueText.alignment = TextAlignmentOptions.Center;

            CreateSelectorButton(row.transform, "Next", ">", onNext);
            return valueText;
        }

        private void CreateFixedLabel(Transform parent, string label, float width)
        {
            var labelObject = CreateUiObject($"{label} Label", parent, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            labelObject.GetComponent<LayoutElement>().preferredWidth = width;
            labelObject.GetComponent<LayoutElement>().minWidth = width;
            labelObject.GetComponent<LayoutElement>().preferredHeight = 54f;

            var labelText = labelObject.GetComponent<TextMeshProUGUI>();
            labelText.font = _font;
            labelText.text = label.ToUpperInvariant();
            labelText.fontSize = 18f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = new Color(0.62f, 0.86f, 1f, 1f);
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;
        }

        private Button CreateSelectorButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            return CreateActionButton(parent, name, label, new Color(0.18f, 0.26f, 0.38f, 1f), onClick, 60f);
        }

        private Button CreateActionButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick, float width)
        {
            var buttonObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.GetComponent<LayoutElement>().preferredWidth = width;
            buttonObject.GetComponent<LayoutElement>().preferredHeight = 54f;

            var image = buttonObject.GetComponent<Image>();
            image.color = color;

            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.16f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.2f, 0.2f, 0.24f, 0.7f);
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var labelText = CreateTextElement(buttonObject.transform, "Label", label, 18f, Color.white);
            StretchToParent(labelText.rectTransform);
            labelText.alignment = TextAlignmentOptions.Center;

            RegisterInteractive(button);
            return button;
        }

        private (Button button, Image image) CreateTabButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var button = CreateActionButton(parent, $"{label} Tab", label, new Color(0.12f, 0.16f, 0.24f, 1f), onClick, 0f);
            button.GetComponent<LayoutElement>().flexibleWidth = 1f;
            return (button, button.GetComponent<Image>());
        }

        private TMP_Text CreateTextElement(Transform parent, string name, string value, float fontSize, Color color)
        {
            var textObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(TextMeshProUGUI));
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private GameObject CreateUiObject(string name, Transform parent, params System.Type[] components)
        {
            var gameObject = new GameObject(name, components);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private void ApplyTabVisual(Button button, Image image, bool isActive)
        {
            if (button == null || image == null)
            {
                return;
            }

            var activeColor = new Color(0.08f, 0.42f, 0.9f, 1f);
            var inactiveColor = new Color(0.12f, 0.16f, 0.24f, 1f);
            var baseColor = isActive ? activeColor : inactiveColor;
            image.color = baseColor;

            var colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.12f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
        }

        private void RegisterInteractive(Selectable selectable)
        {
            if (selectable != null)
            {
                _interactiveControls.Add(selectable);
            }
        }

        private TMP_Text CreateOverlayHint(Transform parent, string name, string value)
        {
            var hintObject = CreateUiObject(name, parent, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = hintObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-30f, 10f);
            rect.sizeDelta = new Vector2(260f, 26f);

            var text = hintObject.GetComponent<TextMeshProUGUI>();
            text.font = _font;
            text.text = value;
            text.fontSize = 14f;
            text.alignment = TextAlignmentOptions.BottomRight;
            text.color = new Color(0.62f, 0.86f, 1f, 0.95f);
            text.raycastTarget = false;
            return text;
        }

        private void UpdateScrollIndicators()
        {
            if (_contentScrollRect == null || _contentScrollRect.content == null || _contentScrollRect.viewport == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            var overflow = _contentScrollRect.content.rect.height > _contentScrollRect.viewport.rect.height + 2f;

            if (_contentScrollbar != null)
            {
                _contentScrollbar.gameObject.SetActive(overflow);
            }

            if (_scrollHintText != null)
            {
                _scrollHintText.gameObject.SetActive(overflow);
                _scrollHintText.text = _contentScrollRect.verticalNormalizedPosition > 0.05f
                    ? "Scroll to see more options"
                    : "End of options";
            }
        }

        private static int WrapIndex(int value, int length)
        {
            if (length <= 0)
            {
                return 0;
            }

            while (value < 0)
            {
                value += length;
            }

            return value % length;
        }

        private static int ResolveMinimumArenaPreset(int maxPlayers)
        {
            return (int)RetrowaveArenaLayout.RecommendPreset(maxPlayers);
        }

        private static string GetArenaSizeLabel(RetrowaveArenaSizePreset preset)
        {
            return preset switch
            {
                RetrowaveArenaSizePreset.Auto => "Auto",
                RetrowaveArenaSizePreset.Compact => "Compact",
                RetrowaveArenaSizePreset.Standard => "Standard",
                RetrowaveArenaSizePreset.Expanded => "Expanded",
                RetrowaveArenaSizePreset.Stadium => "Stadium",
                _ => "Mega",
            };
        }

        private static string FormatRoundDuration(int seconds)
        {
            return $"{Mathf.Max(1, seconds / 60)} min";
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
    }
}
