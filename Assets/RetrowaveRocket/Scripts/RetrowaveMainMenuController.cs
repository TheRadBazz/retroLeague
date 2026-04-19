using System.Collections;
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

        private UIMenuManager _menuManager;
        private TMP_FontAsset _font;
        private TMP_InputField _addressField;
        private TMP_InputField _portField;
        private TMP_Text _statusText;
        private Button _hostButton;
        private Button _joinButton;
        private Button _backButton;

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
            BuildConnectionPanel();
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
                    label.text = "Retrowave local multiplayer arena. Host a server, boost up the walls, and dunk on your friends.";
                }
            }
        }

        private void BuildConnectionPanel()
        {
            var root = new GameObject("Retrowave Connection Panel", typeof(RectTransform), typeof(Image));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(_menuManager.playMenu.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 470f);

            var panelImage = root.GetComponent<Image>();
            panelImage.color = new Color(0.04f, 0.06f, 0.12f, 0.94f);

            var outline = root.AddComponent<Outline>();
            outline.effectColor = RetrowaveStyle.BlueGlow;
            outline.effectDistance = new Vector2(2f, 2f);

            CreateText(root.transform, "ConnectTitle", "JOIN THE ARENA", 36, FontStyles.Bold, new Vector2(0f, 180f), new Vector2(620f, 60f), _menuManager.themeController.textColor);
            CreateText(root.transform, "ConnectBody", "Host on this machine or join another device on your local network.", 20, FontStyles.Normal, new Vector2(0f, 130f), new Vector2(640f, 48f), new Color(0.84f, 0.88f, 0.95f, 1f));

            _addressField = CreateInputRow(root.transform, "Server Address", RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.DefaultAddress : "127.0.0.1", new Vector2(0f, 45f), false);
            _portField = CreateInputRow(root.transform, "Port", RetrowaveGameBootstrap.Instance != null ? RetrowaveGameBootstrap.Instance.DefaultPort : "7777", new Vector2(0f, -45f), true);

            _hostButton = CreateButton(root.transform, "HostButton", "HOST LOCAL SERVER", new Vector2(-180f, -150f), new Color(0.07f, 0.44f, 0.93f, 1f), HandleHostClicked);
            _joinButton = CreateButton(root.transform, "JoinButton", "JOIN SERVER", new Vector2(0f, -150f), new Color(0.92f, 0.27f, 0.58f, 1f), HandleJoinClicked);
            _backButton = CreateButton(root.transform, "BackButton", "BACK", new Vector2(180f, -150f), new Color(0.16f, 0.2f, 0.26f, 1f), HandleBackClicked);

            _statusText = CreateText(root.transform, "StatusText", "Choose host to run locally, or enter another machine's LAN IP to join.", 18, FontStyles.Normal, new Vector2(0f, -215f), new Vector2(650f, 60f), new Color(0.57f, 0.86f, 1f, 1f));
            _statusText.alignment = TextAlignmentOptions.Center;
        }

        private TMP_InputField CreateInputRow(Transform parent, string label, string value, Vector2 anchoredPosition, bool numeric)
        {
            var group = new GameObject($"{label} Row", typeof(RectTransform));
            var groupRect = group.GetComponent<RectTransform>();
            groupRect.SetParent(parent, false);
            groupRect.anchorMin = new Vector2(0.5f, 0.5f);
            groupRect.anchorMax = new Vector2(0.5f, 0.5f);
            groupRect.pivot = new Vector2(0.5f, 0.5f);
            groupRect.anchoredPosition = anchoredPosition;
            groupRect.sizeDelta = new Vector2(620f, 72f);

            var labelText = CreateText(group.transform, $"{label} Label", label.ToUpperInvariant(), 18, FontStyles.Bold, new Vector2(-205f, 0f), new Vector2(180f, 44f), _menuManager.themeController.currentColor);
            labelText.alignment = TextAlignmentOptions.MidlineLeft;

            var fieldObject = new GameObject($"{label} Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            var fieldRect = fieldObject.GetComponent<RectTransform>();
            fieldRect.SetParent(group.transform, false);
            fieldRect.anchorMin = new Vector2(0.5f, 0.5f);
            fieldRect.anchorMax = new Vector2(0.5f, 0.5f);
            fieldRect.pivot = new Vector2(0.5f, 0.5f);
            fieldRect.anchoredPosition = new Vector2(105f, 0f);
            fieldRect.sizeDelta = new Vector2(390f, 54f);

            var fieldImage = fieldObject.GetComponent<Image>();
            fieldImage.color = new Color(0.08f, 0.1f, 0.16f, 1f);

            var fieldOutline = fieldObject.AddComponent<Outline>();
            fieldOutline.effectColor = new Color(0.25f, 0.8f, 1f, 0.75f);
            fieldOutline.effectDistance = new Vector2(1f, 1f);

            var textViewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = textViewport.GetComponent<RectTransform>();
            viewportRect.SetParent(fieldObject.transform, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(16f, 8f);
            viewportRect.offsetMax = new Vector2(-16f, -8f);

            var placeholder = CreateText(textViewport.transform, "Placeholder", label, 20, FontStyles.Normal, Vector2.zero, Vector2.zero, new Color(0.5f, 0.58f, 0.68f, 0.9f));
            StretchToParent(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;

            var text = CreateText(textViewport.transform, "Text", value, 20, FontStyles.Normal, Vector2.zero, Vector2.zero, Color.white);
            StretchToParent(text.rectTransform);
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var input = fieldObject.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value;
            input.contentType = numeric ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.caretColor = _menuManager.themeController.currentColor;
            input.selectionColor = new Color(0.14f, 0.45f, 0.75f, 0.45f);

            return input;
        }

        private Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(160f, 52f);

            var image = buttonObject.GetComponent<Image>();
            image.color = color;

            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.2f, 0.2f, 0.24f, 0.75f);
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var labelText = CreateText(buttonObject.transform, "Label", label, 18, FontStyles.Bold, Vector2.zero, Vector2.zero, Color.white);
            StretchToParent(labelText.rectTransform);
            labelText.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private TMP_Text CreateText(Transform parent, string name, string value, float fontSize, FontStyles fontStyle, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size == Vector2.zero ? new Vector2(240f, 36f) : size;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            return text;
        }

        private void FocusDefaultButton()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();

            if (eventSystem != null && _hostButton != null)
            {
                eventSystem.SetSelectedGameObject(_hostButton.gameObject);
            }
        }

        private static void EnsureEventSystemIsInputSystemCompatible()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                var uiModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
                uiModule.AssignDefaultActions();
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

        private void HandleHostClicked()
        {
            if (RetrowaveGameBootstrap.Instance == null)
            {
                _statusText.text = "Network bootstrap is missing.";
                return;
            }

            if (RetrowaveGameBootstrap.Instance.BeginHostFromMenu(_addressField.text, _portField.text, out var message))
            {
                SetInteractable(false);
            }

            _statusText.text = message;
        }

        private void HandleJoinClicked()
        {
            if (RetrowaveGameBootstrap.Instance == null)
            {
                _statusText.text = "Network bootstrap is missing.";
                return;
            }

            if (RetrowaveGameBootstrap.Instance.BeginClientFromMenu(_addressField.text, _portField.text, out var message))
            {
                SetInteractable(false);
            }

            _statusText.text = message;
        }

        private void HandleBackClicked()
        {
            _menuManager.ReturnMenu();
        }

        private void SetInteractable(bool isInteractable)
        {
            _addressField.interactable = isInteractable;
            _portField.interactable = isInteractable;
            _hostButton.interactable = isInteractable;
            _joinButton.interactable = isInteractable;
            _backButton.interactable = isInteractable;
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
