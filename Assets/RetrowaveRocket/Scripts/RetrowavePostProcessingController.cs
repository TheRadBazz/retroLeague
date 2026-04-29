using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RetrowaveRocket
{
    public sealed class RetrowavePostProcessingController : MonoBehaviour
    {
        private static RetrowavePostProcessingController _instance;

        private Volume _volume;
        private VolumeProfile _runtimeProfile;
        private Bloom _bloom;
        private Vignette _vignette;
        private MotionBlur _motionBlur;
        private ChromaticAberration _chromaticAberration;
        private ColorAdjustments _colorAdjustments;
        private DepthOfField _depthOfField;
        private Tonemapping _tonemapping;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureInstance();
        }

        public static void EnsureInstance()
        {
            if (_instance != null)
            {
                return;
            }

            var controllerObject = new GameObject("Retrowave Post Processing");
            DontDestroyOnLoad(controllerObject);
            _instance = controllerObject.AddComponent<RetrowavePostProcessingController>();
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
            SceneManager.sceneLoaded += HandleSceneLoaded;
            RetrowaveGameSettings.SettingsApplied += HandleSettingsApplied;
            RefreshVolumeForActiveScene();
            ApplySettings();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            RetrowaveGameSettings.SettingsApplied -= HandleSettingsApplied;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshVolumeForActiveScene();
            ApplySettings();
        }

        private void HandleSettingsApplied()
        {
            ApplySettings();
        }

        private void RefreshVolumeForActiveScene()
        {
            var activeScene = SceneManager.GetActiveScene();
            var volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Volume targetVolume = null;

            for (var i = 0; i < volumes.Length; i++)
            {
                if (!volumes[i].isGlobal || volumes[i].gameObject.scene != activeScene)
                {
                    continue;
                }

                targetVolume = volumes[i];
                break;
            }

            if (targetVolume == null)
            {
                var volumeObject = new GameObject("Retrowave Runtime Volume");
                SceneManager.MoveGameObjectToScene(volumeObject, activeScene);
                targetVolume = volumeObject.AddComponent<Volume>();
                targetVolume.isGlobal = true;
                targetVolume.priority = 25f;
                targetVolume.weight = 1f;
            }

            if (_volume == targetVolume && _runtimeProfile != null)
            {
                return;
            }

            _volume = targetVolume;

            if (_runtimeProfile != null)
            {
                Destroy(_runtimeProfile);
            }

            var sourceProfile = _volume.sharedProfile != null ? _volume.sharedProfile : _volume.profile;
            _runtimeProfile = sourceProfile != null
                ? Instantiate(sourceProfile)
                : ScriptableObject.CreateInstance<VolumeProfile>();

            _volume.sharedProfile = null;
            _volume.profile = _runtimeProfile;

            _bloom = EnsureComponent<Bloom>();
            _vignette = EnsureComponent<Vignette>();
            _motionBlur = EnsureComponent<MotionBlur>();
            _chromaticAberration = EnsureComponent<ChromaticAberration>();
            _colorAdjustments = EnsureComponent<ColorAdjustments>();
            _depthOfField = EnsureComponent<DepthOfField>();
            _tonemapping = EnsureComponent<Tonemapping>();

            _tonemapping.mode.overrideState = true;
            _tonemapping.mode.value = TonemappingMode.ACES;
            _motionBlur.quality.overrideState = true;
            _motionBlur.quality.value = MotionBlurQuality.High;
            _depthOfField.mode.overrideState = true;
            _depthOfField.mode.value = DepthOfFieldMode.Gaussian;
        }

        private void ApplySettings()
        {
            if (_volume == null || _runtimeProfile == null)
            {
                RefreshVolumeForActiveScene();
            }

            if (_volume == null || _runtimeProfile == null)
            {
                return;
            }

            ApplyEffectPreset(RetrowaveGameSettings.CameraEffectPreset);
            _motionBlur.active = RetrowaveGameSettings.MotionBlur;
            _motionBlur.intensity.overrideState = true;
            _motionBlur.intensity.value = RetrowaveGameSettings.MotionBlur
                ? ResolveMotionBlurIntensity(RetrowaveGameSettings.CameraEffectPreset)
                : 0f;
            ApplyAmbientOcclusion();
        }

        private void ApplyEffectPreset(RetrowaveCameraEffectPreset preset)
        {
            _bloom.active = true;
            _vignette.active = true;
            _chromaticAberration.active = true;
            _colorAdjustments.active = true;
            _depthOfField.active = false;

            _bloom.intensity.overrideState = true;
            _bloom.threshold.overrideState = true;
            _bloom.scatter.overrideState = true;
            _vignette.intensity.overrideState = true;
            _vignette.smoothness.overrideState = true;
            _chromaticAberration.intensity.overrideState = true;
            _colorAdjustments.postExposure.overrideState = true;
            _colorAdjustments.contrast.overrideState = true;
            _colorAdjustments.saturation.overrideState = true;

            switch (preset)
            {
                case RetrowaveCameraEffectPreset.Clean:
                    _bloom.intensity.value = 0.18f;
                    _bloom.threshold.value = 1.05f;
                    _bloom.scatter.value = 0.42f;
                    _vignette.intensity.value = 0.08f;
                    _vignette.smoothness.value = 0.28f;
                    _chromaticAberration.intensity.value = 0f;
                    _colorAdjustments.postExposure.value = 0f;
                    _colorAdjustments.contrast.value = 2f;
                    _colorAdjustments.saturation.value = 0f;
                    break;
                case RetrowaveCameraEffectPreset.Cinematic:
                    _bloom.intensity.value = 0.46f;
                    _bloom.threshold.value = 1f;
                    _bloom.scatter.value = 0.62f;
                    _vignette.intensity.value = 0.24f;
                    _vignette.smoothness.value = 0.45f;
                    _chromaticAberration.intensity.value = 0.09f;
                    _colorAdjustments.postExposure.value = -0.08f;
                    _colorAdjustments.contrast.value = 18f;
                    _colorAdjustments.saturation.value = -6f;
                    break;
                case RetrowaveCameraEffectPreset.Neon:
                    _bloom.intensity.value = 0.72f;
                    _bloom.threshold.value = 0.92f;
                    _bloom.scatter.value = 0.72f;
                    _vignette.intensity.value = 0.16f;
                    _vignette.smoothness.value = 0.35f;
                    _chromaticAberration.intensity.value = 0.2f;
                    _colorAdjustments.postExposure.value = 0.06f;
                    _colorAdjustments.contrast.value = 12f;
                    _colorAdjustments.saturation.value = 12f;
                    break;
                default:
                    _bloom.intensity.value = 0.32f;
                    _bloom.threshold.value = 1f;
                    _bloom.scatter.value = 0.54f;
                    _vignette.intensity.value = 0.18f;
                    _vignette.smoothness.value = 0.32f;
                    _chromaticAberration.intensity.value = 0.05f;
                    _colorAdjustments.postExposure.value = 0f;
                    _colorAdjustments.contrast.value = 10f;
                    _colorAdjustments.saturation.value = 4f;
                    break;
            }
        }

        private void ApplyAmbientOcclusion()
        {
            var pipelineAsset = UniversalRenderPipeline.asset;
            ScriptableRendererData rendererData = null;

            if (pipelineAsset != null && pipelineAsset.rendererDataList.Length > 0)
            {
                rendererData = pipelineAsset.rendererDataList[0];
            }

            if (rendererData != null && rendererData.TryGetRendererFeature<ScreenSpaceAmbientOcclusion>(out var feature))
            {
                feature.SetActive(RetrowaveGameSettings.AmbientOcclusion);
            }
        }

        private static float ResolveMotionBlurIntensity(RetrowaveCameraEffectPreset preset)
        {
            return preset switch
            {
                RetrowaveCameraEffectPreset.Clean => 0.18f,
                RetrowaveCameraEffectPreset.Cinematic => 0.48f,
                RetrowaveCameraEffectPreset.Neon => 0.34f,
                _ => 0.26f,
            };
        }

        private T EnsureComponent<T>() where T : VolumeComponent
        {
            if (!_runtimeProfile.TryGet(out T component))
            {
                component = _runtimeProfile.Add<T>(true);
            }

            return component;
        }
    }
}
