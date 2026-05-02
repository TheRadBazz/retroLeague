using System;
using UnityEngine;

namespace RetrowaveRocket
{
    [CreateAssetMenu(fileName = "RetrowaveAudioLibrary", menuName = "RetrowaveRocket/Audio/Audio Library")]
    public sealed class RetrowaveAudioLibrary : ScriptableObject
    {
        private const string ResourcePath = "RetrowaveRocket/RetrowaveAudioLibrary";

        [Serializable]
        public struct ClipEntry
        {
            public string Key;
            public AudioClip Clip;
        }

        private static RetrowaveAudioLibrary _cached;

        public ClipEntry[] Clips = Array.Empty<ClipEntry>();

        public static AudioClip Resolve(string key)
        {
            var library = Load();
            return library != null ? library.ResolveClip(key) : null;
        }

        private static RetrowaveAudioLibrary Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<RetrowaveAudioLibrary>(ResourcePath);
            }

            return _cached;
        }

        private AudioClip ResolveClip(string key)
        {
            if (Clips == null || Clips.Length == 0)
            {
                return null;
            }

            var normalizedKey = NormalizeKey(key);

            for (var i = 0; i < Clips.Length; i++)
            {
                var entry = Clips[i];

                if (entry.Clip == null)
                {
                    continue;
                }

                if (string.Equals(NormalizeKey(entry.Key), normalizedKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(NormalizeKey(entry.Clip.name), normalizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Clip;
                }
            }

            return null;
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var normalized = key.Trim().Replace('\\', '/');
            var slashIndex = normalized.LastIndexOf('/');

            if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
            {
                normalized = normalized.Substring(slashIndex + 1);
            }

            var dotIndex = normalized.LastIndexOf('.');

            if (dotIndex > 0)
            {
                normalized = normalized.Substring(0, dotIndex);
            }

            return normalized;
        }
    }
}
