using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RetrowaveRocket
{
    public sealed class RetrowaveMusicManager : MonoBehaviour
    {
        private const string ProfileResourcePath = "RetrowaveRocket/RetrowaveMusicProfile";

        private static RetrowaveMusicManager _instance;

        private readonly AudioSource[] _sources = new AudioSource[2];
        [SerializeField] private RetrowaveMusicProfile _assignedProfile;
        private RetrowaveMusicProfile _profile;
        private Coroutine _fadeRoutine;
        private int _activeSourceIndex;
        private RetrowaveMusicContext _activeContext = RetrowaveMusicContext.None;
        private AudioClip _activeClip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var existingManager = Object.FindFirstObjectByType<RetrowaveMusicManager>(FindObjectsInactive.Include);

            if (existingManager != null)
            {
                _instance = existingManager;
                return;
            }

            var managerObject = new GameObject("Retrowave Music Manager");
            DontDestroyOnLoad(managerObject);
            managerObject.AddComponent<RetrowaveMusicManager>();
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
            RetrowaveGameSettings.SettingsApplied += HandleSettingsApplied;
            _profile = _assignedProfile != null
                ? _assignedProfile
                : Resources.Load<RetrowaveMusicProfile>(ProfileResourcePath);

            for (var i = 0; i < _sources.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                source.volume = 0f;
                _sources[i] = source;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                RetrowaveGameSettings.SettingsApplied -= HandleSettingsApplied;
            }
        }

        private void Update()
        {
            if (_profile == null)
            {
                return;
            }

            var targetContext = ResolveContext();

            if (targetContext != _activeContext)
            {
                PlayContext(targetContext);
                return;
            }

            if (_activeContext == RetrowaveMusicContext.None)
            {
                return;
            }

            var cue = _profile.GetCue(_activeContext);

            if (cue == null || cue.Loop || _sources[_activeSourceIndex].isPlaying)
            {
                RefreshActiveSourceVolume();
                return;
            }

            // Goal stingers are intentionally non-looping; let the state change drive the next track.
        }

        private RetrowaveMusicContext ResolveContext()
        {
            var activeScene = SceneManager.GetActiveScene();

            if (activeScene.name == RetrowaveGameBootstrap.MainMenuSceneName)
            {
                return RetrowaveMusicContext.MainMenu;
            }

            if (activeScene.name != RetrowaveGameBootstrap.GameplaySceneName)
            {
                return RetrowaveMusicContext.None;
            }

            var matchManager = RetrowaveMatchManager.Instance;

            if (matchManager != null && matchManager.IsGoalCelebrationActive)
            {
                return RetrowaveMusicContext.GoalScored;
            }

            if (matchManager == null || matchManager.IsWarmup || matchManager.IsPodium || matchManager.IsMatchComplete)
            {
                return RetrowaveMusicContext.WarmupLobby;
            }

            return RetrowaveMusicContext.LiveMatch;
        }

        private void PlayContext(RetrowaveMusicContext context)
        {
            if (_profile == null)
            {
                return;
            }

            var cue = _profile.GetCue(context);

            if (cue == null || !cue.HasTracks)
            {
                FadeOutToSilence();
                _activeContext = context;
                _activeClip = null;
                return;
            }

            var nextClip = cue.ResolveClip(_activeContext == context ? _activeClip : null);

            if (nextClip == null)
            {
                return;
            }

            if (_activeContext == context && _activeClip == nextClip && _sources[_activeSourceIndex].isPlaying)
            {
                return;
            }

            _activeContext = context;
            _activeClip = nextClip;
            CrossfadeTo(nextClip, cue.Loop, ResolveTargetVolume(cue));
        }

        private void CrossfadeTo(AudioClip clip, bool loop, float targetVolume)
        {
            if (clip == null)
            {
                FadeOutToSilence();
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            var incomingIndex = 1 - _activeSourceIndex;
            var incoming = _sources[incomingIndex];
            incoming.clip = clip;
            incoming.loop = loop;
            incoming.volume = 0f;
            incoming.Play();

            var outgoing = _sources[_activeSourceIndex];

            if (!outgoing.isPlaying || outgoing.clip == null)
            {
                outgoing.Stop();
                incoming.volume = targetVolume;
                _activeSourceIndex = incomingIndex;
                return;
            }

            _fadeRoutine = StartCoroutine(FadeBetweenSources(outgoing, incoming, targetVolume));
            _activeSourceIndex = incomingIndex;
        }

        private void FadeOutToSilence()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            var source = _sources[_activeSourceIndex];

            if (!source.isPlaying)
            {
                return;
            }

            _fadeRoutine = StartCoroutine(FadeOutSource(source));
        }

        private IEnumerator FadeBetweenSources(AudioSource outgoing, AudioSource incoming, float targetVolume)
        {
            var duration = Mathf.Max(0.05f, _profile.CrossfadeDuration);
            var elapsed = 0f;
            var outgoingStart = outgoing.volume;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                outgoing.volume = Mathf.Lerp(outgoingStart, 0f, t);
                incoming.volume = Mathf.Lerp(0f, targetVolume, t);
                yield return null;
            }

            outgoing.Stop();
            outgoing.clip = null;
            outgoing.volume = 0f;
            incoming.volume = targetVolume;
            _fadeRoutine = null;
        }

        private IEnumerator FadeOutSource(AudioSource source)
        {
            var duration = Mathf.Max(0.05f, _profile.CrossfadeDuration);
            var elapsed = 0f;
            var startVolume = source.volume;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(startVolume, 0f, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            source.Stop();
            source.clip = null;
            source.volume = 0f;
            _fadeRoutine = null;
        }

        private void HandleSettingsApplied()
        {
            RefreshActiveSourceVolume();
        }

        private void RefreshActiveSourceVolume()
        {
            if (_profile == null || _activeContext == RetrowaveMusicContext.None || _fadeRoutine != null)
            {
                return;
            }

            var cue = _profile.GetCue(_activeContext);

            if (cue == null || _sources[_activeSourceIndex] == null)
            {
                return;
            }

            _sources[_activeSourceIndex].volume = ResolveTargetVolume(cue);
        }

        private float ResolveTargetVolume(RetrowaveMusicCue cue)
        {
            return cue == null
                ? 0f
                : cue.Volume * _profile.MasterVolume * RetrowaveGameSettings.MusicVolume;
        }
    }
}
