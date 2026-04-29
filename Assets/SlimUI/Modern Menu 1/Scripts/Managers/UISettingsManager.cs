using System;
using System.Collections;
using System.Collections.Generic;
using RetrowaveRocket;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SlimUI.ModernMenu
{
    public class UISettingsManager : MonoBehaviour
    {
        public enum Platform
        {
            Desktop,
            Mobile,
        }

        public Platform platform;

        [Header("MOBILE SETTINGS")]
        public GameObject mobileSFXtext;
        public GameObject mobileMusictext;
        public GameObject mobileShadowofftextLINE;
        public GameObject mobileShadowlowtextLINE;
        public GameObject mobileShadowhightextLINE;

        [Header("VIDEO SETTINGS")]
        public GameObject fullscreentext;
        public GameObject ambientocclusiontext;
        public GameObject shadowofftextLINE;
        public GameObject shadowlowtextLINE;
        public GameObject shadowhightextLINE;
        public GameObject aaofftextLINE;
        public GameObject aa2xtextLINE;
        public GameObject aa4xtextLINE;
        public GameObject aa8xtextLINE;
        public GameObject vsynctext;
        public GameObject motionblurtext;
        public GameObject texturelowtextLINE;
        public GameObject texturemedtextLINE;
        public GameObject texturehightextLINE;
        public GameObject cameraeffectstext;

        [Header("GAME SETTINGS")]
        public GameObject showhudtext;
        public GameObject tooltipstext;
        public GameObject difficultynormaltext;
        public GameObject difficultynormaltextLINE;
        public GameObject difficultyhardcoretext;
        public GameObject difficultyhardcoretextLINE;

        [Header("CONTROLS SETTINGS")]
        public GameObject invertmousetext;

        [Header("SLIDERS")]
        public GameObject musicSlider;
        public GameObject sensitivityXSlider;
        public GameObject sensitivityYSlider;
        public GameObject mouseSmoothSlider;

        private UIMenuManager _menuManager;
        private readonly Dictionary<RetrowaveBindingAction, TMP_Text> _bindingValueTexts = new();
        private readonly Dictionary<RetrowaveBindingCategory, RectTransform> _bindingCategoryContainers = new();
        private readonly Dictionary<RetrowaveBindingCategory, LayoutElement> _bindingCategoryLayouts = new();
        private readonly Dictionary<RetrowaveCameraEffectPreset, Button> _cameraEffectButtons = new();
        private TMP_Text _bindingStatusText;
        private TMP_Text _bindingHintText;
        private RectTransform _bindingFrameRect;
        private RectTransform _bindingContentRect;
        private ScrollRect _bindingScrollRect;
        private Coroutine _bindingLayoutRefresh;
        private RetrowaveBindingAction? _pendingBindingAction;
        private GameObject _runtimeOverlayCanvasRoot;
        private GameObject _bindingRoot;
        private GameObject _cameraEffectsPickerRoot;
        private GameObject _cameraEffectsBackdrop;
        private TMP_Text _cameraEffectsPickerHint;
        private int _cameraEffectsOpenedFrame = -1;

        private void OnEnable()
        {
            RetrowaveGameSettings.SettingsApplied += SyncUiFromSettings;
            RetrowaveInputBindings.BindingsChanged += SyncBindingUi;
        }

        private void OnDisable()
        {
            RetrowaveGameSettings.SettingsApplied -= SyncUiFromSettings;
            RetrowaveInputBindings.BindingsChanged -= SyncBindingUi;
        }

        private void Start()
        {
            _menuManager = FindFirstObjectByType<UIMenuManager>();
            RetrowavePostProcessingController.EnsureInstance();
            RetrowaveGameSettings.Reapply();
            DisableLegacyMenuMusic();
            HideUnsupportedControls();
            PrepareRuntimeOverlayCanvas();
            PrepareKeyBindingPanel();
            PrepareCameraEffectsPicker();
            SyncUiFromSettings();
            SyncBindingUi();
        }

        private void Update()
        {
            EnforceRuntimeSettingsLayout();

            if (!_pendingBindingAction.HasValue)
            {
                return;
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;

            if (RetrowaveInputBindings.TryGetPressedKeyThisFrame(keyboard, out var key))
            {
                RetrowaveInputBindings.SetBinding(_pendingBindingAction.Value, key);
                _pendingBindingAction = null;
                RefreshBindingStatus();
            }
        }

        public void FullScreen()
        {
            RetrowaveGameSettings.SetFullscreen(!RetrowaveGameSettings.Fullscreen);
        }

        public void MusicSlider()
        {
            var slider = GetSlider(musicSlider);

            if (slider != null)
            {
                RetrowaveGameSettings.SetMusicVolume(slider.value);
            }
        }

        public void SensitivityXSlider()
        {
            var slider = GetSlider(sensitivityXSlider);

            if (slider != null)
            {
                RetrowaveGameSettings.SetLookSensitivityX(slider.value);
            }
        }

        public void SensitivityYSlider()
        {
            var slider = GetSlider(sensitivityYSlider);

            if (slider != null)
            {
                RetrowaveGameSettings.SetLookSensitivityY(slider.value);
            }
        }

        public void SensitivitySmoothing()
        {
            // Throttleball does not use camera smoothing for gameplay right now.
        }

        public void ShowHUD()
        {
            RetrowaveGameSettings.SetShowHud(!RetrowaveGameSettings.ShowHud);
        }

        public void MobileSFXMute()
        {
            // Mobile-specific menu audio toggles are not part of the current desktop-first game flow.
        }

        public void MobileMusicMute()
        {
            // Mobile-specific menu audio toggles are not part of the current desktop-first game flow.
        }

        public void ToolTips()
        {
            // Tooltips are not used as a standalone gameplay system in the current game flow.
        }

        public void NormalDifficulty()
        {
            // Difficulty switching is not used in the current match ruleset.
        }

        public void HardcoreDifficulty()
        {
            // Difficulty switching is not used in the current match ruleset.
        }

        public void ShadowsOff()
        {
            RetrowaveGameSettings.SetShadowQuality(RetrowaveShadowQuality.Off);
        }

        public void ShadowsLow()
        {
            RetrowaveGameSettings.SetShadowQuality(RetrowaveShadowQuality.Low);
        }

        public void ShadowsHigh()
        {
            RetrowaveGameSettings.SetShadowQuality(RetrowaveShadowQuality.High);
        }

        public void MobileShadowsOff()
        {
            ShadowsOff();
        }

        public void MobileShadowsLow()
        {
            ShadowsLow();
        }

        public void MobileShadowsHigh()
        {
            ShadowsHigh();
        }

        public void vsync()
        {
            RetrowaveGameSettings.SetVSync(!RetrowaveGameSettings.VSync);
        }

        public void InvertMouse()
        {
            RetrowaveGameSettings.SetInvertLook(!RetrowaveGameSettings.InvertLook);
        }

        public void MotionBlur()
        {
            RetrowaveGameSettings.SetMotionBlur(!RetrowaveGameSettings.MotionBlur);
        }

        public void AmbientOcclusion()
        {
            RetrowaveGameSettings.SetAmbientOcclusion(!RetrowaveGameSettings.AmbientOcclusion);
        }

        public void CameraEffects()
        {
            PrepareCameraEffectsPicker();

            if (_cameraEffectsPickerRoot == null || _cameraEffectsBackdrop == null)
            {
                return;
            }

            var shouldOpen = !_cameraEffectsPickerRoot.activeSelf;
            _cameraEffectsBackdrop.SetActive(shouldOpen);
            _cameraEffectsPickerRoot.SetActive(shouldOpen);
            _cameraEffectsOpenedFrame = shouldOpen ? Time.frameCount : -1;
            SyncCameraEffectsPicker();
        }

        public void OpenKeyBindingsCanvas()
        {
            PrepareKeyBindingPanel();

            if (_bindingRoot == null)
            {
                return;
            }

            _pendingBindingAction = null;
            _bindingRoot.SetActive(true);
            _bindingRoot.transform.SetAsLastSibling();
            SyncBindingUi();
            RefreshKeyBindingLayout(resetToTop: true);

            if (_bindingLayoutRefresh != null)
            {
                StopCoroutine(_bindingLayoutRefresh);
            }

            _bindingLayoutRefresh = StartCoroutine(RefreshKeyBindingLayoutNextFrame());
        }

        public void CloseKeyBindingsCanvas()
        {
            _pendingBindingAction = null;

            if (_bindingRoot != null)
            {
                _bindingRoot.SetActive(false);
            }

            if (_bindingLayoutRefresh != null)
            {
                StopCoroutine(_bindingLayoutRefresh);
                _bindingLayoutRefresh = null;
            }

            RefreshBindingStatus();
        }

        public void CloseRuntimeSettingsOverlays()
        {
            CloseKeyBindingsCanvas();
            CloseCameraEffectsPicker();
        }

        public void TexturesLow()
        {
            RetrowaveGameSettings.SetTextureQuality(RetrowaveTextureQuality.Low);
        }

        public void TexturesMed()
        {
            RetrowaveGameSettings.SetTextureQuality(RetrowaveTextureQuality.Medium);
        }

        public void TexturesHigh()
        {
            RetrowaveGameSettings.SetTextureQuality(RetrowaveTextureQuality.High);
        }

        private void SyncUiFromSettings()
        {
            SyncSlider(musicSlider, RetrowaveGameSettings.MusicVolume);
            SyncSlider(sensitivityXSlider, RetrowaveGameSettings.LookSensitivityXNormalized);
            SyncSlider(sensitivityYSlider, RetrowaveGameSettings.LookSensitivityYNormalized);

            SetText(fullscreentext, RetrowaveGameSettings.Fullscreen ? "on" : "off");
            SetText(showhudtext, RetrowaveGameSettings.ShowHud ? "on" : "off");
            SetText(vsynctext, RetrowaveGameSettings.VSync ? "on" : "off");
            SetText(invertmousetext, RetrowaveGameSettings.InvertLook ? "on" : "off");
            SetText(motionblurtext, RetrowaveGameSettings.MotionBlur ? "on" : "off");
            SetText(ambientocclusiontext, RetrowaveGameSettings.AmbientOcclusion ? "on" : "off");
            SetText(cameraeffectstext, FormatCameraPreset(RetrowaveGameSettings.CameraEffectPreset));
            ConfigureSingleLineValueText(cameraeffectstext);
            SyncCameraEffectsPicker();

            SetChoiceState(
                shadowofftextLINE,
                shadowlowtextLINE,
                shadowhightextLINE,
                (int)RetrowaveGameSettings.ShadowQuality);

            SetChoiceState(
                mobileShadowofftextLINE,
                mobileShadowlowtextLINE,
                mobileShadowhightextLINE,
                (int)RetrowaveGameSettings.ShadowQuality);

            SetChoiceState(
                texturelowtextLINE,
                texturemedtextLINE,
                texturehightextLINE,
                (int)RetrowaveGameSettings.TextureQuality);
        }

        private void HideUnsupportedControls()
        {
            HideSettingRow(tooltipstext);
            HideSettingRow(difficultynormaltext);
            HideSettingRow(difficultyhardcoretext);
            HideSettingRow(mouseSmoothSlider);
            HideSettingRow(mobileSFXtext);
            HideSettingRow(mobileMusictext);
            HideSettingRow(aaofftextLINE);
            HideSettingRow(aa2xtextLINE);
            HideSettingRow(aa4xtextLINE);
            HideSettingRow(aa8xtextLINE);
            HideTextLabel(_menuManager != null ? _menuManager.PanelVideo : null, "anti aliasing");
            HideTextLabel(_menuManager != null ? _menuManager.PanelVideo : null, "async");
            HideTextLabel(_menuManager != null ? _menuManager.PanelVideo : null, "a-sync");
            HideTextLabel(_menuManager != null ? _menuManager.PanelGame : null, "game difficulty");
            HideTextLabel(_menuManager != null ? _menuManager.PanelControls : null, "mouse smoothing");
            HideNamedObject(_menuManager != null ? _menuManager.PanelVideo : null, "AAOff_Btn");
            HideNamedObject(_menuManager != null ? _menuManager.PanelVideo : null, "AA2X_Btn");
            HideNamedObject(_menuManager != null ? _menuManager.PanelVideo : null, "AA4X_Btn");
            HideNamedObject(_menuManager != null ? _menuManager.PanelVideo : null, "AA8X_Btn");
            HideKeyBindingSubTabs();
        }

        private static void HideControlButtonFromLine(GameObject lineObject)
        {
            if (lineObject == null || lineObject.transform.parent == null)
            {
                return;
            }

            lineObject.transform.parent.gameObject.SetActive(false);
        }

        private void HideKeyBindingSubTabs()
        {
            if (_menuManager == null)
            {
                return;
            }

            SafeSetActive(_menuManager.PanelMovement, false);
            SafeSetActive(_menuManager.PanelCombat, false);
            SafeSetActive(_menuManager.PanelGeneral, false);
            HideControlButtonFromLine(_menuManager.lineMovement);
            HideControlButtonFromLine(_menuManager.lineCombat);
            HideControlButtonFromLine(_menuManager.lineGeneral);
            HideTextLabel(_menuManager.PanelKeyBindings, "visual template only*");
            HideTextLabel(_menuManager.PanelKeyBindings, "movement");
            HideTextLabel(_menuManager.PanelKeyBindings, "combat");
            HideTextLabel(_menuManager.PanelKeyBindings, "general");
        }

        private static void HideSettingRow(GameObject reference)
        {
            if (reference == null)
            {
                return;
            }

            var target = reference.transform;

            if (target.parent != null
                && !string.Equals(target.parent.name, "PanelVideo", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.parent.name, "PanelControls", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.parent.name, "PanelGame", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.parent.name, "PanelKeyBindings", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.parent.name, "MovementPanel", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.parent.name, "CombatPanel", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.parent.name, "GeneralPanel", StringComparison.OrdinalIgnoreCase))
            {
                target = target.parent;
            }

            target.gameObject.SetActive(false);
        }

        private static void HideTextLabel(GameObject root, string textValue)
        {
            if (root == null)
            {
                return;
            }

            var texts = root.GetComponentsInChildren<TMP_Text>(true);

            for (var i = 0; i < texts.Length; i++)
            {
                if (!string.Equals(texts[i].text.Trim(), textValue, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                texts[i].gameObject.SetActive(false);
            }
        }

        private static void HideNamedObject(GameObject root, string objectName)
        {
            if (root == null)
            {
                return;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);

            for (var i = 0; i < transforms.Length; i++)
            {
                if (!string.Equals(transforms[i].name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                transforms[i].gameObject.SetActive(false);
            }
        }

        private static Slider GetSlider(GameObject sliderObject)
        {
            return sliderObject != null ? sliderObject.GetComponent<Slider>() : null;
        }

        private static void SyncSlider(GameObject sliderObject, float value)
        {
            var slider = GetSlider(sliderObject);

            if (slider != null)
            {
                slider.SetValueWithoutNotify(value);
            }
        }

        private static void SetText(GameObject textObject, string value)
        {
            if (textObject == null)
            {
                return;
            }

            var text = textObject.GetComponent<TMP_Text>();

            if (text != null)
            {
                text.text = value;
            }
        }

        private static void ConfigureSingleLineValueText(GameObject textObject)
        {
            if (textObject == null)
            {
                return;
            }

            var text = textObject.GetComponent<TMP_Text>();

            if (text == null)
            {
                return;
            }

            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.maxVisibleLines = 1;
        }

        private static void SetChoiceState(GameObject offLine, GameObject middleLine, GameObject highLine, int selectedIndex)
        {
            SafeSetActive(offLine, selectedIndex == 0);
            SafeSetActive(middleLine, selectedIndex == 1);
            SafeSetActive(highLine, selectedIndex == 2);
        }

        private static void SafeSetActive(GameObject gameObject, bool isActive)
        {
            if (gameObject != null)
            {
                gameObject.SetActive(isActive);
            }
        }

        private void DisableLegacyMenuMusic()
        {
            var audioSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < audioSources.Length; i++)
            {
                var source = audioSources[i];

                if (source == null || source.GetComponent<RetrowaveMusicManager>() != null)
                {
                    continue;
                }

                if (source.loop && source.playOnAwake && source.clip != null)
                {
                    source.Stop();
                    source.loop = false;
                    source.playOnAwake = false;
                    source.enabled = false;
                }
            }
        }

        private void PrepareRuntimeOverlayCanvas()
        {
            if (_runtimeOverlayCanvasRoot != null)
            {
                return;
            }

            var existing = GameObject.Find("RetrowaveRuntimeSettingsCanvas");
            _runtimeOverlayCanvasRoot = existing != null
                ? existing
                : new GameObject("RetrowaveRuntimeSettingsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _runtimeOverlayCanvasRoot.transform.SetParent(null, false);

            var canvas = _runtimeOverlayCanvasRoot.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = _runtimeOverlayCanvasRoot.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            var scaler = _runtimeOverlayCanvasRoot.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = _runtimeOverlayCanvasRoot.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (_runtimeOverlayCanvasRoot.GetComponent<GraphicRaycaster>() == null)
            {
                _runtimeOverlayCanvasRoot.AddComponent<GraphicRaycaster>();
            }

            _runtimeOverlayCanvasRoot.SetActive(true);
        }

        private void PrepareKeyBindingPanel()
        {
            PrepareRuntimeOverlayCanvas();

            if (_runtimeOverlayCanvasRoot == null)
            {
                return;
            }

            var panelTransform = _runtimeOverlayCanvasRoot.transform;
            var existingRoot = panelTransform.Find("RuntimeBindingsRoot");
            var wasActive = existingRoot != null && existingRoot.gameObject.activeSelf;
            var rootObject = existingRoot != null ? existingRoot.gameObject : CreateBindingRoot(panelTransform);
            rootObject.SetActive(true);
            rootObject.transform.SetAsLastSibling();
            _bindingRoot = rootObject;
            BuildBindingRows(rootObject.GetComponent<RectTransform>());
            rootObject.SetActive(wasActive);
        }

        private GameObject CreateBindingRoot(Transform parent)
        {
            var root = new GameObject("RuntimeBindingsRoot", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var background = root.GetComponent<Image>();
            background.color = new Color(0.01f, 0.02f, 0.04f, 0.46f);
            background.raycastTarget = true;

            return root;
        }

        private void BuildBindingRows(RectTransform root)
        {
            if (root.childCount > 0)
            {
                if (_bindingValueTexts.Count > 0)
                {
                    return;
                }

                ClearChildren(root);
            }

            _bindingValueTexts.Clear();
            _bindingCategoryContainers.Clear();
            _bindingCategoryLayouts.Clear();

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(root, false);
            var frameRect = frame.GetComponent<RectTransform>();
            _bindingFrameRect = frameRect;
            frameRect.anchorMin = new Vector2(0.24f, 0.11f);
            frameRect.anchorMax = new Vector2(0.94f, 0.9f);
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;
            var frameImage = frame.GetComponent<Image>();
            frameImage.color = new Color(0.08f, 0.12f, 0.2f, 0.92f);
            frameImage.raycastTarget = true;

            var header = new GameObject("Header", typeof(RectTransform), typeof(VerticalLayoutGroup));
            header.transform.SetParent(frame.transform, false);
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 84f);
            headerRect.anchoredPosition = new Vector2(0f, -14f);

            var headerLayout = header.GetComponent<VerticalLayoutGroup>();
            headerLayout.padding = new RectOffset(18, 18, 0, 0);
            headerLayout.spacing = 4f;
            headerLayout.childAlignment = TextAnchor.UpperLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = false;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;

            var title = CreateText(header.transform, "Title", "KEY BINDINGS", 28, FontStyles.Bold, TextAlignmentOptions.Left);
            title.color = Color.white;
            _bindingHintText = CreateText(header.transform, "Hint", "Click a binding, then press the key you want to use.", 15, FontStyles.Normal, TextAlignmentOptions.Left);
            _bindingHintText.color = new Color(0.77f, 0.89f, 0.95f, 0.95f);
            _bindingStatusText = CreateText(header.transform, "Status", string.Empty, 14, FontStyles.Italic, TextAlignmentOptions.Left);
            _bindingStatusText.color = new Color(0.45f, 0.9f, 1f, 1f);

            var closeButton = CreateBindingButton(frame.transform, "Back");
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-18f, -18f);
            closeRect.sizeDelta = new Vector2(118f, 34f);
            closeButton.onClick.AddListener(() =>
            {
                CloseKeyBindingsCanvas();

                if (_menuManager != null)
                {
                    _menuManager.ControlsPanel();
                }
            });

            var scrollRoot = new GameObject("BindingsScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            scrollRoot.transform.SetParent(frame.transform, false);
            var scrollRectTransform = scrollRoot.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(14f, 96f);
            scrollRectTransform.offsetMax = new Vector2(-26f, -126f);
            var viewportImage = scrollRoot.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.09f);
            viewportImage.raycastTarget = true;
            scrollRoot.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scrollRoot.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);
            _bindingContentRect = contentRect;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(10, 10, 10, 132);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (RetrowaveBindingCategory category in Enum.GetValues(typeof(RetrowaveBindingCategory)))
            {
                var categoryContainer = CreateCategoryContainer(content.transform, category);
                _bindingCategoryContainers[category] = categoryContainer;
            }

            var definitions = RetrowaveInputBindings.AllDefinitions;

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                CreateBindingRow(_bindingCategoryContainers[definition.Category], definition);
            }

            RefreshBindingCategoryHeights(definitions);

            var scrollbarObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(frame.transform, false);
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(10f, 0f);
            scrollbarRect.anchoredPosition = new Vector2(-10f, -24f);
            scrollbarRect.offsetMin = new Vector2(-10f, 96f);
            scrollbarRect.offsetMax = new Vector2(0f, -126f);
            var scrollbarImage = scrollbarObject.GetComponent<Image>();
            scrollbarImage.color = new Color(0.15f, 0.2f, 0.3f, 0.9f);

            var handleSlidingArea = new GameObject("Sliding Area", typeof(RectTransform));
            handleSlidingArea.transform.SetParent(scrollbarObject.transform, false);
            var slidingAreaRect = handleSlidingArea.GetComponent<RectTransform>();
            slidingAreaRect.anchorMin = Vector2.zero;
            slidingAreaRect.anchorMax = Vector2.one;
            slidingAreaRect.offsetMin = new Vector2(1f, 1f);
            slidingAreaRect.offsetMax = new Vector2(-1f, -1f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleSlidingArea.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 1f);
            handleRect.anchorMax = new Vector2(1f, 1f);
            handleRect.pivot = new Vector2(0.5f, 1f);
            handleRect.sizeDelta = new Vector2(0f, 42f);
            handle.GetComponent<Image>().color = new Color(0.39f, 0.72f, 1f, 0.96f);

            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.value = 1f;

            var scrollRect = scrollRoot.GetComponent<ScrollRect>();
            scrollRect.viewport = scrollRectTransform;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 24f;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _bindingScrollRect = scrollRect;

            var footer = new GameObject("Footer", typeof(RectTransform), typeof(Image));
            footer.transform.SetParent(frame.transform, false);
            var footerRect = footer.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(0f, 72f);
            footerRect.anchoredPosition = new Vector2(0f, 12f);
            var footerImage = footer.GetComponent<Image>();
            footerImage.color = new Color(0.07f, 0.12f, 0.19f, 0.96f);
            footerImage.raycastTarget = false;

            var resetButton = CreateBindingButton(footer.transform, "Reset Defaults");
            var resetRect = resetButton.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(0.5f, 0.5f);
            resetRect.anchorMax = new Vector2(0.5f, 0.5f);
            resetRect.pivot = new Vector2(0.5f, 0.5f);
            resetRect.anchoredPosition = Vector2.zero;
            resetRect.sizeDelta = new Vector2(220f, 30f);
            resetButton.onClick.AddListener(() =>
            {
                RetrowaveInputBindings.ResetToDefaults();
                _pendingBindingAction = null;
                RefreshBindingStatus();
            });
        }

        private RectTransform CreateCategoryContainer(Transform parent, RetrowaveBindingCategory category)
        {
            var categoryRoot = new GameObject($"{category}Bindings", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
            categoryRoot.transform.SetParent(parent, false);

            var layout = categoryRoot.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = categoryRoot.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layoutElement = categoryRoot.GetComponent<LayoutElement>();
            layoutElement.minHeight = 56f;
            layoutElement.preferredHeight = 56f;
            _bindingCategoryLayouts[category] = layoutElement;

            var header = CreateText(categoryRoot.transform, $"{category}Header", category switch
            {
                RetrowaveBindingCategory.Driving => "Driving",
                RetrowaveBindingCategory.Camera => "Camera",
                _ => "Menu"
            }, 18, FontStyles.Bold, TextAlignmentOptions.Left);
            header.rectTransform.sizeDelta = new Vector2(0f, 30f);
            var headerLayout = header.gameObject.AddComponent<LayoutElement>();
            headerLayout.minHeight = 30f;
            headerLayout.preferredHeight = 30f;

            return categoryRoot.GetComponent<RectTransform>();
        }

        private void CreateBindingRow(Transform parent, RetrowaveBindingDefinition definition)
        {
            var row = new GameObject($"{definition.Action}Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(parent, false);

            var image = row.GetComponent<Image>();
            image.color = new Color(0.11f, 0.16f, 0.24f, 0.92f);
            image.raycastTarget = false;

            var rowRect = row.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0f, 42f);

            var rowLayoutElement = row.GetComponent<LayoutElement>();
            rowLayoutElement.minHeight = 42f;
            rowLayoutElement.preferredHeight = 42f;

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 8, 8);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var label = CreateText(row.transform, $"{definition.Action}Label", definition.Label, 18, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            label.transform.SetAsFirstSibling();

            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelLayout.minWidth = 220f;

            var button = CreateBindingButton(row.transform, RetrowaveInputBindings.GetBindingDisplayName(definition.Action));
            var valueText = button.GetComponentInChildren<TMP_Text>(true);
            _bindingValueTexts[definition.Action] = valueText;
            button.onClick.AddListener(() =>
            {
                _pendingBindingAction = definition.Action;
                RefreshBindingStatus();
                SyncBindingUi();
            });
        }

        private void RefreshBindingCategoryHeights(IReadOnlyList<RetrowaveBindingDefinition> definitions)
        {
            foreach (RetrowaveBindingCategory category in Enum.GetValues(typeof(RetrowaveBindingCategory)))
            {
                if (!_bindingCategoryLayouts.TryGetValue(category, out var layout))
                {
                    continue;
                }

                var rowCount = 0;

                for (var i = 0; i < definitions.Count; i++)
                {
                    if (definitions[i].Category == category)
                    {
                        rowCount++;
                    }
                }

                layout.preferredHeight = 32f + (rowCount * 48f) + (Mathf.Max(0, rowCount - 1) * 6f);
            }
        }

        private Button CreateBindingButton(Transform parent, string label)
        {
            var buttonObject = new GameObject($"{label}Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.14f, 0.22f, 0.38f, 0.96f);

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 176f;
            layoutElement.preferredHeight = 28f;

            var text = CreateText(buttonObject.transform, "Label", label, 15, FontStyles.Bold, TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;

            return buttonObject.GetComponent<Button>();
        }

        private TMP_Text CreateText(Transform parent, string name, string value, int size, FontStyles style, TextAlignmentOptions alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.color = Color.white;
            text.font = TMP_Settings.defaultFontAsset;
            text.raycastTarget = false;
            return text;
        }

        private void SyncBindingUi()
        {
            foreach (var pair in _bindingValueTexts)
            {
                pair.Value.text = _pendingBindingAction == pair.Key
                    ? "Press key..."
                    : RetrowaveInputBindings.GetBindingDisplayName(pair.Key);
            }

            RefreshBindingStatus();
        }

        private void RefreshBindingStatus()
        {
            if (_bindingStatusText == null)
            {
                return;
            }

            _bindingStatusText.text = _pendingBindingAction.HasValue
                ? $"Listening for {RetrowaveInputBindings.GetDefinition(_pendingBindingAction.Value).Label}..."
                : "Bindings save immediately and carry into the match.";
        }

        private IEnumerator RefreshKeyBindingLayoutNextFrame()
        {
            yield return null;
            RefreshKeyBindingLayout(resetToTop: true);
            _bindingLayoutRefresh = null;
        }

        private void RefreshKeyBindingLayout(bool resetToTop)
        {
            if (_bindingContentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_bindingContentRect);
            }

            if (_bindingFrameRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_bindingFrameRect);
            }

            Canvas.ForceUpdateCanvases();

            if (_bindingScrollRect == null)
            {
                return;
            }

            _bindingScrollRect.StopMovement();

            if (resetToTop)
            {
                _bindingScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void PrepareCameraEffectsPicker()
        {
            PrepareRuntimeOverlayCanvas();

            if (_runtimeOverlayCanvasRoot == null || cameraeffectstext == null)
            {
                return;
            }

            var parentTransform = _runtimeOverlayCanvasRoot.transform;

            var existingBackdrop = parentTransform.Find("CameraEffectsBackdrop");
            _cameraEffectsBackdrop = existingBackdrop != null
                ? existingBackdrop.gameObject
                : CreateCameraEffectsBackdrop(parentTransform);

            var existingRoot = parentTransform.Find("CameraEffectsPicker");
            if (existingRoot != null && _cameraEffectButtons.Count == 0)
            {
                existingRoot.SetParent(null, false);

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(existingRoot.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(existingRoot.gameObject);
                }

                existingRoot = null;
            }

            _cameraEffectsPickerRoot = existingRoot != null
                ? existingRoot.gameObject
                : CreateCameraEffectsPicker(parentTransform);
            _cameraEffectsBackdrop.SetActive(false);
            _cameraEffectsPickerRoot.SetActive(false);
            SyncCameraEffectsPicker();
        }

        private GameObject CreateCameraEffectsBackdrop(Transform parent)
        {
            var backdrop = new GameObject("CameraEffectsBackdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(parent, false);
            var rect = backdrop.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = backdrop.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.34f);
            var button = backdrop.GetComponent<Button>();
            button.onClick.AddListener(CloseCameraEffectsPicker);
            return backdrop;
        }

        private GameObject CreateCameraEffectsPicker(Transform parent)
        {
            var root = new GameObject("CameraEffectsPicker", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            root.transform.SetAsLastSibling();
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.54f, 0.16f);
            rect.anchorMax = new Vector2(0.88f, 0.82f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var rootImage = root.GetComponent<Image>();
            rootImage.color = new Color(0.05f, 0.1f, 0.18f, 0.94f);
            rootImage.raycastTarget = true;

            var title = CreateText(root.transform, "Title", "Camera Effects", 20, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, 26f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -12f);

            _cameraEffectsPickerHint = CreateText(root.transform, "Hint", "Pick a preset and preview it live in the menu scene.", 13, FontStyles.Italic, TextAlignmentOptions.TopLeft);
            _cameraEffectsPickerHint.rectTransform.anchorMin = new Vector2(0f, 1f);
            _cameraEffectsPickerHint.rectTransform.anchorMax = new Vector2(1f, 1f);
            _cameraEffectsPickerHint.rectTransform.pivot = new Vector2(0.5f, 1f);
            _cameraEffectsPickerHint.rectTransform.sizeDelta = new Vector2(0f, 22f);
            _cameraEffectsPickerHint.rectTransform.anchoredPosition = new Vector2(0f, -38f);
            _cameraEffectsPickerHint.color = new Color(0.76f, 0.88f, 0.95f, 0.9f);

            var closeButton = CreateBindingButton(root.transform, "Close");
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-12f, -12f);
            closeRect.sizeDelta = new Vector2(84f, 28f);
            closeButton.onClick.AddListener(CloseCameraEffectsPicker);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.offsetMin = new Vector2(12f, 12f);
            viewportRect.offsetMax = new Vector2(-30f, -86f);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.12f);
            viewportImage.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(0, 0, 0, 0);
            contentLayout.spacing = 8f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _cameraEffectButtons.Clear();

            foreach (RetrowaveCameraEffectPreset preset in Enum.GetValues(typeof(RetrowaveCameraEffectPreset)))
            {
                var optionButton = CreateCameraEffectOption(content.transform, preset);
                _cameraEffectButtons[preset] = optionButton;
            }

            var scrollbarObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(root.transform, false);
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-10f, 12f);
            scrollbarRect.offsetMax = new Vector2(-2f, -86f);
            scrollbarObject.GetComponent<Image>().color = new Color(0.15f, 0.2f, 0.3f, 0.9f);

            var slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
            slidingArea.transform.SetParent(scrollbarObject.transform, false);
            var slidingRect = slidingArea.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(1f, 1f);
            slidingRect.offsetMax = new Vector2(-1f, -1f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(slidingArea.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 1f);
            handleRect.anchorMax = new Vector2(1f, 1f);
            handleRect.pivot = new Vector2(0.5f, 1f);
            handleRect.sizeDelta = new Vector2(0f, 40f);
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
            scrollRect.scrollSensitivity = 24f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            return root;
        }

        private Button CreateCameraEffectOption(Transform parent, RetrowaveCameraEffectPreset preset)
        {
            var buttonObject = new GameObject($"{preset}Option", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.1f, 0.16f, 0.24f, 0.94f);

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.minHeight = 76f;
            layoutElement.preferredHeight = 76f;

            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                RetrowaveGameSettings.SetCameraEffectPreset(preset);
                CloseCameraEffectsPicker();
            });

            var title = CreateText(buttonObject.transform, "Title", GetCameraPresetTitle(preset), 16, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(14f, -28f);
            title.rectTransform.offsetMax = new Vector2(-14f, -8f);
            title.alignment = TextAlignmentOptions.MidlineLeft;

            var description = CreateText(buttonObject.transform, "Description", GetCameraPresetDescription(preset), 13, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            description.rectTransform.anchorMin = new Vector2(0f, 0f);
            description.rectTransform.anchorMax = new Vector2(1f, 0f);
            description.rectTransform.pivot = new Vector2(0.5f, 0f);
            description.rectTransform.anchoredPosition = new Vector2(0f, 10f);
            description.rectTransform.sizeDelta = new Vector2(-28f, 34f);
            description.enableWordWrapping = true;
            description.fontSize = 11;
            description.color = new Color(0.82f, 0.9f, 0.97f, 0.92f);

            return button;
        }

        private void SyncCameraEffectsPicker()
        {
            if (_cameraEffectButtons.Count == 0)
            {
                return;
            }

            foreach (var pair in _cameraEffectButtons)
            {
                var isActivePreset = pair.Key == RetrowaveGameSettings.CameraEffectPreset;
                var image = pair.Value.GetComponent<Image>();

                if (image != null)
                {
                    image.color = isActivePreset
                        ? new Color(0.18f, 0.31f, 0.5f, 0.98f)
                        : new Color(0.1f, 0.16f, 0.24f, 0.94f);
                }
            }
        }

        private void EnforceRuntimeSettingsLayout()
        {
            HideKeyBindingSubTabs();
            HideUnsupportedControls();

            if (_bindingRoot != null)
            {
                if (_bindingRoot.activeSelf)
                {
                    _bindingRoot.transform.SetAsLastSibling();
                }
            }

            if (_cameraEffectsBackdrop != null && _cameraEffectsBackdrop.activeSelf)
            {
                _cameraEffectsBackdrop.transform.SetAsLastSibling();
            }

            if (_cameraEffectsPickerRoot != null && _cameraEffectsPickerRoot.activeSelf)
            {
                _cameraEffectsPickerRoot.transform.SetAsLastSibling();
            }
        }

        private void CloseCameraEffectsPicker()
        {
            if (_cameraEffectsBackdrop != null)
            {
                _cameraEffectsBackdrop.SetActive(false);
            }

            if (_cameraEffectsPickerRoot != null)
            {
                _cameraEffectsPickerRoot.SetActive(false);
            }

            _cameraEffectsOpenedFrame = -1;
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static string FormatCameraPreset(RetrowaveCameraEffectPreset preset)
        {
            return preset switch
            {
                RetrowaveCameraEffectPreset.Clean => "clean",
                RetrowaveCameraEffectPreset.Cinematic => "cinema",
                RetrowaveCameraEffectPreset.Neon => "neon",
                _ => "retro",
            };
        }

        private static string GetCameraPresetTitle(RetrowaveCameraEffectPreset preset)
        {
            return preset switch
            {
                RetrowaveCameraEffectPreset.Clean => "Clean",
                RetrowaveCameraEffectPreset.Cinematic => "Cinematic",
                RetrowaveCameraEffectPreset.Neon => "Neon",
                _ => "Retro"
            };
        }

        private static string GetCameraPresetDescription(RetrowaveCameraEffectPreset preset)
        {
            return preset switch
            {
                RetrowaveCameraEffectPreset.Clean => "Low bloom, minimal stylisation, and the clearest overall read.",
                RetrowaveCameraEffectPreset.Cinematic => "Heavier contrast and moodier grading with a stronger dramatic falloff.",
                RetrowaveCameraEffectPreset.Neon => "Bright bloom, stronger chromatic separation, and a more exaggerated arcade look.",
                _ => "Balanced retrowave glow with readable contrast for the default match presentation."
            };
        }
    }
}
