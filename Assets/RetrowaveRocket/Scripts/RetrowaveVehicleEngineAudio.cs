#pragma warning disable 0649

using UnityEngine;

namespace RetrowaveRocket
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RetrowavePlayerController))]
    public sealed class RetrowaveVehicleEngineAudio : MonoBehaviour
    {
        private const string ResourceClipRoot = "RetrowaveRocket/Audio/V10_Italian/";
        private const string EditorClipRoot = "Assets/Car Engine Sound - V10 Italian/Assets/Audio/V10_Italian/";

        private static readonly float[] BandCenters =
        {
            0.08f,
            0.2f,
            0.35f,
            0.5f,
            0.65f,
            0.8f,
            0.94f,
        };

        [SerializeField] private float _localVolume = 0.42f;
        [SerializeField] private float _remoteVolume = 0.28f;
        [SerializeField] private float _engineMasterVolume = 0.42f;
        [SerializeField] private float _idleLoopVolume = 0.9f;
        [SerializeField] private float _rpmLoopVolume = 0.18f;
        [SerializeField] private float _maxRpmVolume = 0.14f;
        [SerializeField] private float _rpmRiseSpeed = 7.5f;
        [SerializeField] private float _rpmFallSpeed = 3.4f;
        [SerializeField] private float _loadRiseSpeed = 10f;
        [SerializeField] private float _loadFallSpeed = 5.5f;
        [SerializeField] private float _speedForVeryHighRpm = 34f;
        [SerializeField] private float _coastOffLoopSpeedThreshold = 1.25f;
        [SerializeField] private float _startupLoopDelayPadding = 0.05f;
        [SerializeField] private float _sourceMinDistance = 5f;
        [SerializeField] private float _sourceMaxDistance = 78f;
        [SerializeField] private AudioClip _startupClip;
        [SerializeField] private AudioClip _engineStopClip;
        [SerializeField] private AudioClip _idleClip;
        [SerializeField] private AudioClip _maxRpmClip;
        [SerializeField] private AudioClip _aggressivenessOffClip;
        [SerializeField] private AudioClip _idleLowOnClip;
        [SerializeField] private AudioClip _lowOnClip;
        [SerializeField] private AudioClip _lowMedOnClip;
        [SerializeField] private AudioClip _medOnClip;
        [SerializeField] private AudioClip _medHighOnClip;
        [SerializeField] private AudioClip _highOnClip;
        [SerializeField] private AudioClip _veryHighOnClip;
        [SerializeField] private AudioClip _idleLowOffClip;
        [SerializeField] private AudioClip _lowOffClip;
        [SerializeField] private AudioClip _lowMedOffClip;
        [SerializeField] private AudioClip _medOffClip;
        [SerializeField] private AudioClip _medHighOffClip;
        [SerializeField] private AudioClip _highOffClip;
        [SerializeField] private AudioClip _veryHighOffClip;

        private RetrowavePlayerController _player;
        private Rigidbody _body;
        private AudioSource _idleSource;
        private AudioSource _maxRpmSource;
        private AudioSource _oneShotSource;
        private AudioSource[] _onSources;
        private AudioSource[] _offSources;
        private AudioClip[] _onClips;
        private AudioClip[] _offClips;
        private float _rpm;
        private float _load;
        private float _startupLoopsMutedUntil;
        private bool _wasActive;
        private bool _hasEverBeenActive;

        private void Awake()
        {
            _player = GetComponent<RetrowavePlayerController>();
            _body = GetComponent<Rigidbody>();
            LoadDefaultClips();
            BuildClipArrays();
            BuildAudioSources();
        }

        private void Update()
        {
            if (_player == null)
            {
                return;
            }

            var isActive = _player.IsSpawned && _player.IsArenaParticipant;

            if (isActive && !_wasActive)
            {
                MuteAllLoopsImmediate();
                PlayOneShot(_startupClip, 0.78f);
                _startupLoopsMutedUntil = Time.time + GetStartupLoopMuteDuration();
                StartEngineLoops();
                _hasEverBeenActive = true;
            }
            else if (!isActive && _wasActive && _hasEverBeenActive)
            {
                PlayOneShot(_engineStopClip, 0.72f);
            }

            _wasActive = isActive;

            if (!isActive)
            {
                FadeAllLoopsToSilence();
                return;
            }

            UpdateEngineState();
            UpdateLoopSources();
        }

        private void LoadDefaultClips()
        {
            LoadClip(ref _startupClip, "startup");
            LoadClip(ref _engineStopClip, "engine_stop");
            LoadClip(ref _idleClip, "idle");
            LoadClip(ref _maxRpmClip, "maxRPM");
            LoadClip(ref _aggressivenessOffClip, "aggressiveness_off_fx");
            LoadClip(ref _idleLowOnClip, "idle_low_on");
            LoadClip(ref _lowOnClip, "low_on");
            LoadClip(ref _lowMedOnClip, "low_med_on");
            LoadClip(ref _medOnClip, "med_on");
            LoadClip(ref _medHighOnClip, "med_high_on");
            LoadClip(ref _highOnClip, "high_on");
            LoadClip(ref _veryHighOnClip, "very_high_on");
            LoadClip(ref _idleLowOffClip, "idle_low_off");
            LoadClip(ref _lowOffClip, "low_off");
            LoadClip(ref _lowMedOffClip, "low_med_off");
            LoadClip(ref _medOffClip, "med_off");
            LoadClip(ref _medHighOffClip, "med_high_off");
            LoadClip(ref _highOffClip, "high_off");
            LoadClip(ref _veryHighOffClip, "very_high_off");
        }

        private static void LoadClip(ref AudioClip clip, string clipName)
        {
            if (clip != null)
            {
                return;
            }

            clip = Resources.Load<AudioClip>(ResourceClipRoot + clipName);

#if UNITY_EDITOR
            if (clip == null)
            {
                clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"{EditorClipRoot}{clipName}.wav");
            }
#endif
        }

        private void BuildClipArrays()
        {
            _onClips = new[]
            {
                _idleLowOnClip,
                _lowOnClip,
                _lowMedOnClip,
                _medOnClip,
                _medHighOnClip,
                _highOnClip,
                _veryHighOnClip,
            };

            _offClips = new[]
            {
                _idleLowOffClip,
                _lowOffClip,
                _lowMedOffClip,
                _medOffClip,
                _medHighOffClip,
                _highOffClip,
                _veryHighOffClip,
            };
        }

        private void BuildAudioSources()
        {
            _oneShotSource = CreateSource("Engine One Shots", null, false);
            _idleSource = CreateSource("Engine Idle", _idleClip, true);
            _maxRpmSource = CreateSource("Engine Max RPM", _maxRpmClip, true);
            _onSources = CreateLoopSources("Engine On", _onClips);
            _offSources = CreateLoopSources("Engine Off", _offClips);
        }

        private AudioSource[] CreateLoopSources(string prefix, AudioClip[] clips)
        {
            var sources = new AudioSource[clips.Length];

            for (var i = 0; i < clips.Length; i++)
            {
                sources[i] = CreateSource($"{prefix} {i}", clips[i], true);
            }

            return sources;
        }

        private AudioSource CreateSource(string sourceName, AudioClip clip, bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.volume = 0f;
            source.pitch = 1f;
            source.spatialBlend = 0.86f;
            source.dopplerLevel = 0.16f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = Mathf.Max(0.1f, _sourceMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 1f, _sourceMaxDistance);
            return source;
        }

        private void StartEngineLoops()
        {
            EnsureLoopPlaying(_idleSource);
            EnsureLoopPlaying(_maxRpmSource);
            EnsureLoopsPlaying(_onSources);
            EnsureLoopsPlaying(_offSources);
        }

        private static void EnsureLoopsPlaying(AudioSource[] sources)
        {
            if (sources == null)
            {
                return;
            }

            for (var i = 0; i < sources.Length; i++)
            {
                EnsureLoopPlaying(sources[i]);
            }
        }

        private static void EnsureLoopPlaying(AudioSource source)
        {
            if (source != null && source.clip != null && !source.isPlaying)
            {
                source.Play();
            }
        }

        private void UpdateEngineState()
        {
            var throttle = Mathf.Clamp(_player.EngineAudioThrottle, -1f, 1f);
            var throttleLoad = Mathf.Abs(throttle);
            var boostLoad = _player.EngineAudioBoosting ? 0.35f : 0f;
            var targetLoad = Mathf.Clamp01(throttleLoad + boostLoad);
            var speedRpm = Mathf.Clamp01(_player.CurrentSpeed / Mathf.Max(1f, _speedForVeryHighRpm));
            var targetRpm = 0.05f;

            if (targetLoad > 0.05f)
            {
                var minimumLoadedRpm = 0.08f + throttleLoad * 0.08f + (_player.EngineAudioBoosting ? 0.08f : 0f);
                targetRpm = Mathf.Clamp01(Mathf.Max(speedRpm, minimumLoadedRpm));
            }
            else if (_player.CurrentSpeed > _coastOffLoopSpeedThreshold)
            {
                targetRpm = Mathf.Clamp01(speedRpm * 0.72f);
            }

            var rpmRate = targetRpm > _rpm ? _rpmRiseSpeed : _rpmFallSpeed;
            var loadRate = targetLoad > _load ? _loadRiseSpeed : _loadFallSpeed;

            _rpm = Mathf.MoveTowards(_rpm, targetRpm, rpmRate * Time.deltaTime);
            _load = Mathf.MoveTowards(_load, targetLoad, loadRate * Time.deltaTime);

            if (_body != null && _body.linearVelocity.sqrMagnitude < 0.12f && targetLoad < 0.05f)
            {
                _rpm = Mathf.MoveTowards(_rpm, 0.05f, _rpmFallSpeed * Time.deltaTime);
            }
        }

        private void UpdateLoopSources()
        {
            if (Time.time < _startupLoopsMutedUntil)
            {
                MuteAllLoopsImmediate();
                return;
            }

            var sfxVolume = RetrowaveGameSettings.SfxVolume;
            var volumeBase = sfxVolume * Mathf.Clamp01(_engineMasterVolume) * (_player.IsOwner ? _localVolume : _remoteVolume);
            var throttleActive = Mathf.Abs(_player.EngineAudioThrottle) > 0.05f || _player.EngineAudioBoosting;
            var movingWithoutThrottle = !throttleActive && _player.CurrentSpeed > _coastOffLoopSpeedThreshold;
            var onBlend = Mathf.SmoothStep(0f, 1f, _load);
            var offBlend = movingWithoutThrottle ? 1f - onBlend : 0f;
            var idleWeight = Mathf.Clamp01(1f - _rpm * 1.85f);
            var basePitch = Mathf.Lerp(0.92f, 1.08f, _rpm);

            SetSource(_idleSource, volumeBase * _idleLoopVolume * Mathf.Lerp(0.42f, 0.07f, onBlend) * Mathf.Max(0.18f, idleWeight), Mathf.Lerp(0.92f, 1.03f, _rpm));

            for (var i = 0; i < BandCenters.Length; i++)
            {
                var bandWeight = GetBandWeight(_rpm, i);
                SetSource(_onSources[i], volumeBase * _rpmLoopVolume * bandWeight * onBlend, basePitch);
                SetSource(_offSources[i], volumeBase * _rpmLoopVolume * bandWeight * offBlend * Mathf.Lerp(0.82f, 0.38f, idleWeight), basePitch * 0.98f);
            }

            var maxWeight = Mathf.InverseLerp(0.88f, 1f, _rpm) * Mathf.Lerp(0.25f, 1f, onBlend);
            SetSource(_maxRpmSource, volumeBase * _maxRpmVolume * maxWeight, Mathf.Lerp(0.95f, 1.08f, maxWeight));
        }

        private static float GetBandWeight(float rpm, int index)
        {
            var center = BandCenters[Mathf.Clamp(index, 0, BandCenters.Length - 1)];
            const float width = 0.18f;
            return Mathf.Clamp01(1f - Mathf.Abs(rpm - center) / width);
        }

        private static void SetSource(AudioSource source, float volume, float pitch)
        {
            if (source == null || source.clip == null)
            {
                return;
            }

            if (!source.isPlaying)
            {
                source.Play();
            }

            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.72f, 1.32f);
        }

        private void PlayOneShot(AudioClip clip, float volumeScale)
        {
            if (_oneShotSource == null || clip == null)
            {
                return;
            }

            _oneShotSource.volume = RetrowaveGameSettings.SfxVolume * Mathf.Clamp01(_engineMasterVolume) * (_player != null && _player.IsOwner ? _localVolume : _remoteVolume);
            _oneShotSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        private void FadeAllLoopsToSilence()
        {
            FadeSource(_idleSource);
            FadeSource(_maxRpmSource);
            FadeSources(_onSources);
            FadeSources(_offSources);
            _rpm = Mathf.MoveTowards(_rpm, 0f, _rpmFallSpeed * Time.deltaTime);
            _load = Mathf.MoveTowards(_load, 0f, _loadFallSpeed * Time.deltaTime);
        }

        private float GetStartupLoopMuteDuration()
        {
            return _startupClip != null
                ? Mathf.Max(0f, _startupClip.length + _startupLoopDelayPadding)
                : 0f;
        }

        private void MuteAllLoopsImmediate()
        {
            MuteSourceImmediate(_idleSource);
            MuteSourceImmediate(_maxRpmSource);
            MuteSourcesImmediate(_onSources);
            MuteSourcesImmediate(_offSources);
        }

        private static void MuteSourcesImmediate(AudioSource[] sources)
        {
            if (sources == null)
            {
                return;
            }

            for (var i = 0; i < sources.Length; i++)
            {
                MuteSourceImmediate(sources[i]);
            }
        }

        private static void MuteSourceImmediate(AudioSource source)
        {
            if (source != null)
            {
                source.volume = 0f;
            }
        }

        private static void FadeSources(AudioSource[] sources)
        {
            if (sources == null)
            {
                return;
            }

            for (var i = 0; i < sources.Length; i++)
            {
                FadeSource(sources[i]);
            }
        }

        private static void FadeSource(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.volume = Mathf.MoveTowards(source.volume, 0f, Time.deltaTime * 1.8f);
        }
    }
}
