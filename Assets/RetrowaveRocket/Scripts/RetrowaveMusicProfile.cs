using System;
using UnityEngine;

namespace RetrowaveRocket
{
    public enum RetrowaveMusicContext
    {
        None = 0,
        MainMenu = 1,
        WarmupLobby = 2,
        LiveMatch = 3,
        GoalScored = 4,
    }

    [Serializable]
    public sealed class RetrowaveMusicCue
    {
        public AudioClip[] Tracks = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float Volume = 0.9f;
        public bool Loop = true;

        public bool HasTracks => Tracks != null && Tracks.Length > 0;

        public AudioClip ResolveClip(AudioClip currentClip = null)
        {
            if (!HasTracks)
            {
                return null;
            }

            if (Tracks.Length == 1)
            {
                return Tracks[0];
            }

            var attempts = Tracks.Length;
            var index = UnityEngine.Random.Range(0, Tracks.Length);

            while (attempts-- > 0 && Tracks[index] == currentClip)
            {
                index = (index + 1) % Tracks.Length;
            }

            return Tracks[index];
        }
    }

    [CreateAssetMenu(fileName = "RetrowaveMusicProfile", menuName = "RetrowaveRocket/Audio/Music Profile")]
    public sealed class RetrowaveMusicProfile : ScriptableObject
    {
        [Range(0f, 1f)] public float MasterVolume = 0.8f;
        [Min(0.05f)] public float CrossfadeDuration = 1.4f;
        public RetrowaveMusicCue MainMenu = new RetrowaveMusicCue();
        public RetrowaveMusicCue WarmupLobby = new RetrowaveMusicCue();
        public RetrowaveMusicCue LiveMatch = new RetrowaveMusicCue();
        public RetrowaveMusicCue GoalScored = new RetrowaveMusicCue { Loop = false };

        public RetrowaveMusicCue GetCue(RetrowaveMusicContext context)
        {
            return context switch
            {
                RetrowaveMusicContext.MainMenu => MainMenu,
                RetrowaveMusicContext.WarmupLobby => WarmupLobby,
                RetrowaveMusicContext.LiveMatch => LiveMatch,
                RetrowaveMusicContext.GoalScored => GoalScored,
                _ => null,
            };
        }
    }
}
