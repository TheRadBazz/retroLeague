using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RetrowaveRocket
{
    public enum RetrowaveBindingCategory
    {
        Driving = 0,
        Camera = 1,
        Menus = 2,
    }

    public enum RetrowaveBindingAction
    {
        DriveForward = 0,
        DriveReverse = 1,
        SteerLeft = 2,
        SteerRight = 3,
        Jump = 4,
        Boost = 5,
        Brake = 6,
        ResetCar = 7,
        AirRollLeft = 8,
        AirRollRight = 9,
        ToggleMatchInfo = 10,
        Scoreboard = 11,
        Pause = 12,
        ActivateRarePowerUp = 13,
    }

    public readonly struct RetrowaveBindingDefinition
    {
        public RetrowaveBindingDefinition(RetrowaveBindingAction action, string label, RetrowaveBindingCategory category, Key defaultKey)
        {
            Action = action;
            Label = label;
            Category = category;
            DefaultKey = defaultKey;
        }

        public RetrowaveBindingAction Action { get; }
        public string Label { get; }
        public RetrowaveBindingCategory Category { get; }
        public Key DefaultKey { get; }
    }

    public static class RetrowaveInputBindings
    {
        private const string BindingPrefix = "RetrowaveBinding.";

        private static readonly RetrowaveBindingDefinition[] Definitions =
        {
            new(RetrowaveBindingAction.DriveForward, "Drive Forward", RetrowaveBindingCategory.Driving, Key.W),
            new(RetrowaveBindingAction.DriveReverse, "Drive Reverse", RetrowaveBindingCategory.Driving, Key.S),
            new(RetrowaveBindingAction.SteerLeft, "Steer Left", RetrowaveBindingCategory.Driving, Key.A),
            new(RetrowaveBindingAction.SteerRight, "Steer Right", RetrowaveBindingCategory.Driving, Key.D),
            new(RetrowaveBindingAction.Jump, "Jump", RetrowaveBindingCategory.Driving, Key.Space),
            new(RetrowaveBindingAction.Boost, "Boost", RetrowaveBindingCategory.Driving, Key.LeftShift),
            new(RetrowaveBindingAction.Brake, "Brake", RetrowaveBindingCategory.Driving, Key.LeftCtrl),
            new(RetrowaveBindingAction.ResetCar, "Reset Car", RetrowaveBindingCategory.Driving, Key.R),
            new(RetrowaveBindingAction.AirRollLeft, "Air Roll Left", RetrowaveBindingCategory.Camera, Key.Q),
            new(RetrowaveBindingAction.AirRollRight, "Air Roll Right", RetrowaveBindingCategory.Camera, Key.E),
            new(RetrowaveBindingAction.ToggleMatchInfo, "Toggle Match Info", RetrowaveBindingCategory.Menus, Key.H),
            new(RetrowaveBindingAction.Scoreboard, "Scoreboard", RetrowaveBindingCategory.Menus, Key.Tab),
            new(RetrowaveBindingAction.Pause, "Pause Menu", RetrowaveBindingCategory.Menus, Key.Escape),
            new(RetrowaveBindingAction.ActivateRarePowerUp, "Activate Rare Power-Up", RetrowaveBindingCategory.Driving, Key.F),
        };

        private static readonly Dictionary<RetrowaveBindingAction, RetrowaveBindingDefinition> DefinitionLookup = new();
        private static readonly Key[] SupportedKeys = (Key[])Enum.GetValues(typeof(Key));
        private static bool _initialized;

        public static event Action BindingsChanged;

        static RetrowaveInputBindings()
        {
            for (var i = 0; i < Definitions.Length; i++)
            {
                DefinitionLookup[Definitions[i].Action] = Definitions[i];
            }
        }

        public static IReadOnlyList<RetrowaveBindingDefinition> AllDefinitions => Definitions;

        public static RetrowaveBindingDefinition GetDefinition(RetrowaveBindingAction action)
        {
            EnsureInitialized();
            return DefinitionLookup[action];
        }

        public static Key GetBinding(RetrowaveBindingAction action)
        {
            EnsureInitialized();
            var definition = DefinitionLookup[action];
            return ParseKey(PlayerPrefs.GetString(GetBindingKey(action), definition.DefaultKey.ToString()), definition.DefaultKey);
        }

        public static void SetBinding(RetrowaveBindingAction action, Key key)
        {
            EnsureInitialized();

            if (!IsBindableKey(key))
            {
                return;
            }

            var previousKey = GetBinding(action);
            var conflictingAction = FindActionByKey(key, action);

            PlayerPrefs.SetString(GetBindingKey(action), key.ToString());

            if (conflictingAction.HasValue)
            {
                PlayerPrefs.SetString(GetBindingKey(conflictingAction.Value), previousKey.ToString());
            }

            PlayerPrefs.Save();
            BindingsChanged?.Invoke();
        }

        public static void ResetToDefaults()
        {
            EnsureInitialized();

            for (var i = 0; i < Definitions.Length; i++)
            {
                PlayerPrefs.SetString(GetBindingKey(Definitions[i].Action), Definitions[i].DefaultKey.ToString());
            }

            PlayerPrefs.Save();
            BindingsChanged?.Invoke();
        }

        public static bool IsPressed(Keyboard keyboard, RetrowaveBindingAction action)
        {
            if (keyboard == null)
            {
                return false;
            }

            var keyControl = keyboard[GetBinding(action)];
            return keyControl != null && keyControl.isPressed;
        }

        public static bool WasPressedThisFrame(Keyboard keyboard, RetrowaveBindingAction action)
        {
            if (keyboard == null)
            {
                return false;
            }

            var keyControl = keyboard[GetBinding(action)];
            return keyControl != null && keyControl.wasPressedThisFrame;
        }

        public static bool TryGetPressedKeyThisFrame(Keyboard keyboard, out Key key)
        {
            key = Key.None;

            if (keyboard == null)
            {
                return false;
            }

            for (var i = 0; i < SupportedKeys.Length; i++)
            {
                var candidate = SupportedKeys[i];

                if (!IsBindableKey(candidate))
                {
                    continue;
                }

                var keyControl = keyboard[candidate];

                if (keyControl != null && keyControl.wasPressedThisFrame)
                {
                    key = candidate;
                    return true;
                }
            }

            return false;
        }

        public static string GetBindingDisplayName(RetrowaveBindingAction action)
        {
            return ToDisplayString(GetBinding(action));
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            for (var i = 0; i < Definitions.Length; i++)
            {
                var bindingKey = GetBindingKey(Definitions[i].Action);

                if (!PlayerPrefs.HasKey(bindingKey))
                {
                    PlayerPrefs.SetString(bindingKey, Definitions[i].DefaultKey.ToString());
                }
            }

            PlayerPrefs.Save();
            _initialized = true;
        }

        private static RetrowaveBindingAction? FindActionByKey(Key key, RetrowaveBindingAction exclude)
        {
            for (var i = 0; i < Definitions.Length; i++)
            {
                var action = Definitions[i].Action;

                if (action == exclude)
                {
                    continue;
                }

                if (GetBinding(action) == key)
                {
                    return action;
                }
            }

            return null;
        }

        private static string GetBindingKey(RetrowaveBindingAction action)
        {
            return $"{BindingPrefix}{action}";
        }

        private static Key ParseKey(string rawValue, Key fallback)
        {
            return Enum.TryParse(rawValue, out Key parsed) && parsed != Key.None ? parsed : fallback;
        }

        private static bool IsBindableKey(Key key)
        {
            var keyValue = (int)key;

            return (keyValue >= (int)Key.Space && keyValue <= (int)Key.OEM5)
                   || (keyValue >= (int)Key.LeftShift && keyValue <= (int)Key.RightMeta)
                   || (keyValue >= (int)Key.LeftArrow && keyValue <= (int)Key.DownArrow)
                   || (keyValue >= (int)Key.NumpadEnter && keyValue <= (int)Key.Numpad9)
                   || (keyValue >= (int)Key.F1 && keyValue <= (int)Key.F12)
                   || key == Key.Tab
                   || key == Key.Enter
                   || key == Key.Escape
                   || key == Key.Backquote
                   || key == Key.Backslash
                   || key == Key.Backspace;
        }

        private static string ToDisplayString(Key key)
        {
            var keyValue = (int)key;

            if (keyValue >= (int)Key.A && keyValue <= (int)Key.Z)
            {
                return key.ToString().ToUpperInvariant();
            }

            if (keyValue >= (int)Key.Digit0 && keyValue <= (int)Key.Digit9)
            {
                return key.ToString().Replace("Digit", string.Empty);
            }

            if (keyValue >= (int)Key.Numpad0 && keyValue <= (int)Key.Numpad9)
            {
                return key.ToString().Replace("Numpad", "Num ");
            }

            return key switch
            {
                Key.LeftShift => "Left Shift",
                Key.RightShift => "Right Shift",
                Key.LeftCtrl => "Left Ctrl",
                Key.RightCtrl => "Right Ctrl",
                Key.LeftAlt => "Left Alt",
                Key.RightAlt => "Right Alt",
                Key.LeftMeta => "Left Cmd",
                Key.RightMeta => "Right Cmd",
                Key.UpArrow => "Up Arrow",
                Key.DownArrow => "Down Arrow",
                Key.LeftArrow => "Left Arrow",
                Key.RightArrow => "Right Arrow",
                Key.PageUp => "Page Up",
                Key.PageDown => "Page Down",
                Key.Backquote => "`",
                Key.Quote => "'",
                Key.Semicolon => ";",
                Key.Comma => ",",
                Key.Period => ".",
                Key.Slash => "/",
                Key.Backslash => "\\",
                Key.LeftBracket => "[",
                Key.RightBracket => "]",
                Key.Minus => "-",
                Key.Equals => "=",
                Key.Space => "Space",
                Key.Tab => "Tab",
                Key.Enter => "Enter",
                Key.Escape => "Escape",
                Key.Backspace => "Backspace",
                _ => key.ToString(),
            };
        }
    }
}
