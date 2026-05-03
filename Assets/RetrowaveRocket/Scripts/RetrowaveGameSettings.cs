using System;
using UnityEngine;

namespace RetrowaveRocket
{
    public enum RetrowaveShadowQuality
    {
        Off = 0,
        Low = 1,
        High = 2,
    }

    public enum RetrowaveTextureQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    public enum RetrowaveCameraEffectPreset
    {
        Clean = 0,
        Retro = 1,
        Cinematic = 2,
        Neon = 3,
    }

    public enum RetrowaveVfxDensity
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    public static class RetrowaveGameSettings
    {
        private const string MusicVolumeKey = "MusicVolume";
        private const string SfxVolumeKey = "SFXVolume";
        private const string FullscreenKey = "Fullscreen";
        private const string ShowHudKey = "ShowHUD";
        private const string InvertLookKey = "Inverted";
        private const string VSyncKey = "VSync";
        private const string ShadowQualityKey = "Shadows";
        private const string TextureQualityKey = "Textures";
        private const string MotionBlurKey = "MotionBlur";
        private const string AmbientOcclusionKey = "AmbientOcclusion";
        private const string CameraEffectsKey = "CameraEffects";
        private const string VfxDensityKey = "VfxDensity";
        private const string SensitivityXKey = "XSensitivity";
        private const string SensitivityYKey = "YSensitivity";

        private const float DefaultMusicVolume = 0.85f;
        private const float DefaultSfxVolume = 1f;
        private const float DefaultSensitivityX = 0.5f;
        private const float DefaultSensitivityY = 0.5f;

        private static bool _initialized;

        public static event Action SettingsApplied;

        public static float MusicVolume
        {
            get
            {
                EnsureInitialized();
                return GetFloat(MusicVolumeKey, DefaultMusicVolume);
            }
        }

        public static float SfxVolume
        {
            get
            {
                EnsureInitialized();
                return GetFloat(SfxVolumeKey, DefaultSfxVolume);
            }
        }

        public static bool Fullscreen
        {
            get
            {
                EnsureInitialized();
                return GetBool(FullscreenKey, true);
            }
        }

        public static bool ShowHud
        {
            get
            {
                EnsureInitialized();
                return GetBool(ShowHudKey, true);
            }
        }

        public static bool InvertLook
        {
            get
            {
                EnsureInitialized();
                return GetBool(InvertLookKey, false);
            }
        }

        public static bool VSync
        {
            get
            {
                EnsureInitialized();
                return GetBool(VSyncKey, true);
            }
        }

        public static RetrowaveShadowQuality ShadowQuality
        {
            get
            {
                EnsureInitialized();
                return (RetrowaveShadowQuality)Mathf.Clamp(PlayerPrefs.GetInt(ShadowQualityKey, (int)RetrowaveShadowQuality.High), 0, 2);
            }
        }

        public static RetrowaveTextureQuality TextureQuality
        {
            get
            {
                EnsureInitialized();
                return (RetrowaveTextureQuality)Mathf.Clamp(PlayerPrefs.GetInt(TextureQualityKey, (int)RetrowaveTextureQuality.High), 0, 2);
            }
        }

        public static float LookSensitivityXNormalized
        {
            get
            {
                EnsureInitialized();
                return Mathf.Clamp01(GetFloat(SensitivityXKey, DefaultSensitivityX));
            }
        }

        public static float LookSensitivityYNormalized
        {
            get
            {
                EnsureInitialized();
                return Mathf.Clamp01(GetFloat(SensitivityYKey, DefaultSensitivityY));
            }
        }

        public static float MouseSensitivityXMultiplier => Mathf.Lerp(0.45f, 1.95f, LookSensitivityXNormalized);
        public static float MouseSensitivityYMultiplier => Mathf.Lerp(0.45f, 1.95f, LookSensitivityYNormalized);
        public static float StickSensitivityXMultiplier => Mathf.Lerp(0.45f, 1.95f, LookSensitivityXNormalized);
        public static float StickSensitivityYMultiplier => Mathf.Lerp(0.45f, 1.95f, LookSensitivityYNormalized);

        public static bool MotionBlur
        {
            get
            {
                EnsureInitialized();
                return GetBool(MotionBlurKey, false);
            }
        }

        public static bool AmbientOcclusion
        {
            get
            {
                EnsureInitialized();
                return GetBool(AmbientOcclusionKey, true);
            }
        }

        public static RetrowaveCameraEffectPreset CameraEffectPreset
        {
            get
            {
                EnsureInitialized();
                return (RetrowaveCameraEffectPreset)Mathf.Clamp(PlayerPrefs.GetInt(CameraEffectsKey, (int)RetrowaveCameraEffectPreset.Retro), 0, 3);
            }
        }

        public static RetrowaveVfxDensity VfxDensity
        {
            get
            {
                EnsureInitialized();
                return (RetrowaveVfxDensity)Mathf.Clamp(PlayerPrefs.GetInt(VfxDensityKey, (int)RetrowaveVfxDensity.High), 0, 2);
            }
        }

        public static float VfxDensityMultiplier => VfxDensity switch
        {
            RetrowaveVfxDensity.Low => 0.55f,
            RetrowaveVfxDensity.Medium => 0.8f,
            _ => 1f,
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            EnsureInitialized();
        }

        public static void SetMusicVolume(float value)
        {
            EnsureInitialized();
            PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
            NotifyApplied(applyRuntimeSettings: false);
        }

        public static void SetSfxVolume(float value)
        {
            EnsureInitialized();
            PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
            NotifyApplied(applyRuntimeSettings: false);
        }

        public static void SetFullscreen(bool value)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
            NotifyApplied(applyRuntimeSettings: true);
        }

        public static void SetShowHud(bool value)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(ShowHudKey, value ? 1 : 0);
            NotifyApplied(applyRuntimeSettings: false);
        }

        public static void SetInvertLook(bool value)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(InvertLookKey, value ? 1 : 0);
            NotifyApplied(applyRuntimeSettings: false);
        }

        public static void SetVSync(bool value)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
            NotifyApplied(applyRuntimeSettings: true);
        }

        public static void SetShadowQuality(RetrowaveShadowQuality quality)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(ShadowQualityKey, (int)quality);
            NotifyApplied(applyRuntimeSettings: true);
        }

        public static void SetTextureQuality(RetrowaveTextureQuality quality)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(TextureQualityKey, (int)quality);
            NotifyApplied(applyRuntimeSettings: true);
        }

        public static void SetMotionBlur(bool value)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(MotionBlurKey, value ? 1 : 0);
            NotifyApplied(applyRuntimeSettings: true);
        }

        public static void SetAmbientOcclusion(bool value)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(AmbientOcclusionKey, value ? 1 : 0);
            NotifyApplied(applyRuntimeSettings: true);
        }

        public static void SetCameraEffectPreset(RetrowaveCameraEffectPreset preset)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(CameraEffectsKey, (int)preset);
            NotifyApplied(applyRuntimeSettings: true);
        }

        public static void SetVfxDensity(RetrowaveVfxDensity density)
        {
            EnsureInitialized();
            PlayerPrefs.SetInt(VfxDensityKey, (int)density);
            NotifyApplied(applyRuntimeSettings: false);
        }

        public static void SetLookSensitivityX(float normalizedValue)
        {
            EnsureInitialized();
            PlayerPrefs.SetFloat(SensitivityXKey, Mathf.Clamp01(normalizedValue));
            NotifyApplied(applyRuntimeSettings: false);
        }

        public static void SetLookSensitivityY(float normalizedValue)
        {
            EnsureInitialized();
            PlayerPrefs.SetFloat(SensitivityYKey, Mathf.Clamp01(normalizedValue));
            NotifyApplied(applyRuntimeSettings: false);
        }

        public static void Reapply()
        {
            EnsureInitialized();
            ApplyRuntimeSettings();
            SettingsApplied?.Invoke();
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            InitializeDefaults();
            _initialized = true;
            ApplyRuntimeSettings();
        }

        private static void InitializeDefaults()
        {
            SetDefaultFloat(MusicVolumeKey, DefaultMusicVolume);
            SetDefaultFloat(SfxVolumeKey, DefaultSfxVolume);
            SetDefaultInt(FullscreenKey, 1);
            SetDefaultInt(ShowHudKey, 1);
            SetDefaultInt(InvertLookKey, 0);
            SetDefaultInt(VSyncKey, 1);
            SetDefaultInt(ShadowQualityKey, (int)RetrowaveShadowQuality.High);
            SetDefaultInt(TextureQualityKey, (int)RetrowaveTextureQuality.High);
            SetDefaultInt(MotionBlurKey, 0);
            SetDefaultInt(AmbientOcclusionKey, 1);
            SetDefaultInt(CameraEffectsKey, (int)RetrowaveCameraEffectPreset.Retro);
            SetDefaultInt(VfxDensityKey, (int)RetrowaveVfxDensity.High);
            SetDefaultFloat(SensitivityXKey, DefaultSensitivityX);
            SetDefaultFloat(SensitivityYKey, DefaultSensitivityY);
        }

        private static void ApplyRuntimeSettings()
        {
            Screen.fullScreen = Fullscreen;
            QualitySettings.vSyncCount = VSync ? 1 : 0;

            switch (ShadowQuality)
            {
                case RetrowaveShadowQuality.Off:
                    QualitySettings.shadowCascades = 0;
                    QualitySettings.shadowDistance = 0f;
                    break;
                case RetrowaveShadowQuality.Low:
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.shadowDistance = 85f;
                    break;
                default:
                    QualitySettings.shadowCascades = 4;
                    QualitySettings.shadowDistance = 500f;
                    break;
            }

            QualitySettings.globalTextureMipmapLimit = TextureQuality switch
            {
                RetrowaveTextureQuality.Low => 2,
                RetrowaveTextureQuality.Medium => 1,
                _ => 0,
            };
        }

        private static void NotifyApplied(bool applyRuntimeSettings)
        {
            if (applyRuntimeSettings)
            {
                ApplyRuntimeSettings();
            }

            PlayerPrefs.Save();
            SettingsApplied?.Invoke();
        }

        private static bool GetBool(string key, bool defaultValue)
        {
            return PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;
        }

        private static float GetFloat(string key, float defaultValue)
        {
            return PlayerPrefs.GetFloat(key, defaultValue);
        }

        private static void SetDefaultInt(string key, int value)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.SetInt(key, value);
            }
        }

        private static void SetDefaultFloat(string key, float value)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.SetFloat(key, value);
            }
        }
    }
}
