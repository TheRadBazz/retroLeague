#pragma warning disable 0649

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RetrowaveRocket
{
    public enum RetrowaveAudioPriority
    {
        Ambient = 190,
        Gameplay = 110,
        Important = 48,
        Goal = 18,
    }

    public enum RetrowaveCrowdCheerIntensity
    {
        Soft = 0,
        Strong = 1,
        Goal = 2,
    }

    public sealed class RetrowaveArenaAudio : MonoBehaviour
    {
        private const string CrowdAssetRoot = "Assets/Gregor Quendel - Free Crowd Cheering Sounds/";
        private const string ImpactAssetPath = "Assets/PUNCH_CLEAN_HEAVY_10.wav";

        private static RetrowaveArenaAudio _instance;

        [SerializeField] private float _ambienceVolume = 0.62f;
        [SerializeField] private float _cheerBedVolume = 0.36f;
        [SerializeField] private float _ambientFadeSpeed = 0.42f;
        [SerializeField] private AudioClip _ambienceLoop;
        [SerializeField] private AudioClip _cheeringAmbienceLoop;
        [SerializeField] private AudioClip[] _softCheers;
        [SerializeField] private AudioClip[] _strongCheers;
        [SerializeField] private AudioClip[] _goalCheers;
        [SerializeField] private AudioClip _impactClip;

        private AudioSource _ambienceSource;
        private AudioSource _cheerBedSource;
        private float _duckUntilRealtime;
        private float _duckMultiplier = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var audioObject = new GameObject("Retrowave Arena Audio");
            DontDestroyOnLoad(audioObject);
            audioObject.AddComponent<RetrowaveArenaAudio>();
        }

        public static void PlayCue(
            AudioClip clip,
            Vector3 position,
            RetrowaveAudioPriority priority = RetrowaveAudioPriority.Gameplay,
            float volumeScale = 1f,
            float spatialBlend = 0.86f,
            float pitch = 1f)
        {
            if (clip == null)
            {
                return;
            }

            Instance.PlayTransientCue(clip, position, priority, RetrowaveGameSettings.SfxVolume, volumeScale, spatialBlend, pitch);
        }

        public static void PlayRarePowerCue(AudioClip clip, Vector3 position, float volumeScale = 1f)
        {
            TriggerPriorityDucking(0.68f, 1.45f);

            if (clip != null)
            {
                PlayCue(clip, position, RetrowaveAudioPriority.Important, Mathf.Max(0.1f, volumeScale), 0.75f);
                return;
            }

            var instance = Instance;
            instance.PlayTransientCue(instance._impactClip, position, RetrowaveAudioPriority.Important, RetrowaveGameSettings.SfxVolume, 0.34f * Mathf.Max(0.1f, volumeScale), 0.78f, UnityEngine.Random.Range(0.88f, 0.96f));
        }

        public static void PlayGoalCelebration(Vector3 position)
        {
            TriggerPriorityDucking(0.54f, 2.4f);
            Instance.PlayCrowdCheerInternal(RetrowaveCrowdCheerIntensity.Goal, position, 1.06f);
        }

        public static void PlayCrowdCheer(RetrowaveCrowdCheerIntensity intensity, Vector3 position, float volumeScale = 1f)
        {
            Instance.PlayCrowdCheerInternal(intensity, position, volumeScale);
        }

        public static void PlayImpact(Vector3 position, float intensity = 1f)
        {
            var instance = Instance;
            var volumeScale = Mathf.Lerp(0.16f, 0.62f, Mathf.Clamp01(intensity));
            instance.PlayTransientCue(instance._impactClip, position, RetrowaveAudioPriority.Gameplay, RetrowaveGameSettings.SfxVolume, volumeScale, 0.92f, UnityEngine.Random.Range(0.94f, 1.05f));
        }

        public static void PlayObjectiveCapture(Vector3 position)
        {
            TriggerPriorityDucking(0.76f, 1.15f);
            Instance.PlayCrowdCheerInternal(RetrowaveCrowdCheerIntensity.Strong, position, 0.64f);
        }

        public static void TriggerPriorityDucking(float multiplier, float durationSeconds)
        {
            var instance = Instance;
            instance._duckMultiplier = Mathf.Min(instance._duckMultiplier, Mathf.Clamp(multiplier, 0.1f, 1f));
            instance._duckUntilRealtime = Mathf.Max(instance._duckUntilRealtime, Time.unscaledTime + Mathf.Max(0f, durationSeconds));
            RetrowaveMusicManager.DuckForPriorityCue(multiplier, durationSeconds);
        }

        private static RetrowaveArenaAudio Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                Install();
                return _instance;
            }
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
            LoadDefaultClips();
            _ambienceSource = CreateLoopSource("Crowd Ambience", _ambienceLoop, RetrowaveAudioPriority.Ambient);
            _cheerBedSource = CreateLoopSource("Crowd Cheer Bed", _cheeringAmbienceLoop, RetrowaveAudioPriority.Ambient);
        }

        private void Update()
        {
            var targetSceneActive = SceneManager.GetActiveScene().name == RetrowaveGameBootstrap.GameplaySceneName;
            var targetVolume = targetSceneActive ? RetrowaveGameSettings.MusicVolume : 0f;
            UpdateLoopSource(_ambienceSource, _ambienceLoop, targetVolume * _ambienceVolume);
            UpdateLoopSource(_cheerBedSource, _cheeringAmbienceLoop, targetVolume * _cheerBedVolume);
        }

        private void LoadDefaultClips()
        {
            LoadClip(ref _ambienceLoop, "Gregor Quendel - Free Crowd Cheering Sounds - 10 - Ambience");
            LoadClip(ref _cheeringAmbienceLoop, "Gregor Quendel - Free Crowd Cheering Sounds - 09 - Ambience and cheering");
            LoadClip(ref _impactClip, ImpactAssetPath);

            _softCheers ??= new AudioClip[0];
            _strongCheers ??= new AudioClip[0];
            _goalCheers ??= new AudioClip[0];

            if (_softCheers.Length == 0)
            {
                _softCheers = BuildClipArray(
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 05 - Soft cheering - I"),
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 06 - Soft cheering - II"),
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 07 - Soft cheering and chatter"));
            }

            if (_strongCheers.Length == 0)
            {
                _strongCheers = BuildClipArray(
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 03 - Strong cheering - I"),
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 04 - Strong cheering - II - Short"),
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 08 - Rhythmic cheering"));
            }

            if (_goalCheers.Length == 0)
            {
                _goalCheers = BuildClipArray(
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 01 - Strong cheering and strong rhythmic cheering"),
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 02 - Strong cheering and soft rhythmic cheering"),
                    LoadClip("Gregor Quendel - Free Crowd Cheering Sounds - 03 - Strong cheering - I"));
            }
        }

        private static AudioClip[] BuildClipArray(params AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return Array.Empty<AudioClip>();
            }

            var count = 0;

            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    count++;
                }
            }

            if (count == clips.Length)
            {
                return clips;
            }

            var compact = new AudioClip[count];
            var writeIndex = 0;

            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    compact[writeIndex++] = clips[i];
                }
            }

            return compact;
        }

        private static AudioClip LoadClip(string clipNameOrAssetPath)
        {
            var libraryClip = RetrowaveAudioLibrary.Resolve(clipNameOrAssetPath);

            if (libraryClip != null)
            {
                return libraryClip;
            }

#if UNITY_EDITOR
            var assetPath = clipNameOrAssetPath.StartsWith("Assets/")
                ? clipNameOrAssetPath
                : $"{CrowdAssetRoot}{clipNameOrAssetPath}.wav";
            return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
#else
            return null;
#endif
        }

        private static void LoadClip(ref AudioClip clip, string clipNameOrAssetPath)
        {
            if (clip == null)
            {
                clip = LoadClip(clipNameOrAssetPath);
            }
        }

        private AudioSource CreateLoopSource(string sourceName, AudioClip clip, RetrowaveAudioPriority priority)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.name = sourceName;
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = (int)priority;
            source.volume = 0f;
            source.ignoreListenerPause = true;
            return source;
        }

        private void UpdateLoopSource(AudioSource source, AudioClip clip, float targetVolume)
        {
            if (source == null || clip == null)
            {
                return;
            }

            if (source.clip != clip)
            {
                source.clip = clip;
            }

            if (!source.isPlaying)
            {
                source.Play();
            }

            source.volume = Mathf.MoveTowards(source.volume, Mathf.Clamp01(targetVolume), _ambientFadeSpeed * Time.unscaledDeltaTime);
        }

        private void PlayCrowdCheerInternal(RetrowaveCrowdCheerIntensity intensity, Vector3 position, float volumeScale)
        {
            var clips = intensity switch
            {
                RetrowaveCrowdCheerIntensity.Goal => _goalCheers,
                RetrowaveCrowdCheerIntensity.Strong => _strongCheers,
                _ => _softCheers,
            };
            var clip = ResolveRandomClip(clips);

            if (clip == null)
            {
                return;
            }

            var priority = intensity == RetrowaveCrowdCheerIntensity.Goal
                ? RetrowaveAudioPriority.Goal
                : RetrowaveAudioPriority.Important;
            var volume = intensity switch
            {
                RetrowaveCrowdCheerIntensity.Goal => 1.12f,
                RetrowaveCrowdCheerIntensity.Strong => 0.82f,
                _ => 0.64f,
            };
            PlayTransientCue(clip, position, priority, RetrowaveGameSettings.MusicVolume, volume * volumeScale, 0f, UnityEngine.Random.Range(0.97f, 1.03f));
        }

        private static AudioClip ResolveRandomClip(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return null;
            }

            for (var attempt = 0; attempt < clips.Length; attempt++)
            {
                var clip = clips[UnityEngine.Random.Range(0, clips.Length)];

                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        private void PlayTransientCue(
            AudioClip clip,
            Vector3 position,
            RetrowaveAudioPriority priority,
            float channelVolume,
            float volumeScale,
            float spatialBlend,
            float pitch)
        {
            if (clip == null)
            {
                return;
            }

            var cueObject = new GameObject($"Retrowave Cue - {clip.name}");
            cueObject.transform.position = position;
            var source = cueObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = false;
            source.playOnAwake = false;
            source.priority = (int)priority;
            source.volume = Mathf.Clamp01(channelVolume * volumeScale);
            source.pitch = Mathf.Clamp(pitch, 0.5f, 1.5f);
            source.spatialBlend = Mathf.Clamp01(spatialBlend);
            source.dopplerLevel = 0.05f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = spatialBlend > 0.01f ? 7f : 10000f;
            source.maxDistance = spatialBlend > 0.01f ? 90f : 10000f;
            source.Play();
            Destroy(cueObject, clip.length / Mathf.Max(0.1f, source.pitch) + 0.25f);
        }

        private float GetDuckingMultiplier()
        {
            if (Time.unscaledTime <= _duckUntilRealtime)
            {
                return _duckMultiplier;
            }

            _duckMultiplier = Mathf.MoveTowards(_duckMultiplier, 1f, Time.unscaledDeltaTime * 1.3f);
            return _duckMultiplier;
        }
    }
}
