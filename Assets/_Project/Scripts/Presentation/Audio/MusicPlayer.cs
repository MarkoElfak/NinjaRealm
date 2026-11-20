using UnityEngine;

namespace NinjaRealm.Presentation.Audio
{
    /// <summary>
    /// Plays the background music: one looping track at a time, faded across when a scene asks
    /// for another. It lives on its own object that survives scene loads, so going from the
    /// village to a battle and back changes the track without a gap. The tracks are AudioClips in
    /// a Resources/Music folder (Audio/Resources/Music), named as the constants below.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// <summary>The calm village theme.</summary>
        public const string Village = "Village";

        /// <summary>The battle theme, for the lineup, the fight and the result.</summary>
        public const string Battle = "Battle";

        /// <summary>Where the tracks live, in a Resources folder.</summary>
        public const string Folder = "Music";

        const float DefaultVolume = 0.45f;
        const float FadeSeconds = 1.5f;
        const string VolumeKey = "NinjaRealm.MusicVolume";

        static MusicPlayer instance;

        AudioSource current;
        AudioSource fading;
        string track;

        /// <summary>The music volume from 0 to 1, kept between sessions.</summary>
        public static float Volume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, DefaultVolume);
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                if (instance != null && instance.current != null && instance.fading == null) instance.current.volume = Volume;
            }
        }

        /// <summary>The track playing now, or null.</summary>
        public static string Track => instance != null ? instance.track : null;

        /// <summary>Fades to a track (one of the constants above), or does nothing if it already plays. Play mode only.</summary>
        public static void Play(string name)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(name)) return;
            if (instance == null)
            {
                var host = new GameObject("Music");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<MusicPlayer>();
            }

            instance.Switch(name);
        }

        /// <summary>Fades the music out.</summary>
        public static void Stop()
        {
            if (instance != null) instance.Switch(null);
        }

        void Switch(string name)
        {
            if (name == track) return;

            AudioClip clip = null;
            if (name != null)
            {
                clip = Resources.Load<AudioClip>($"{Folder}/{name}");
                if (clip == null)
                {
                    Debug.LogWarning($"[Music] No track Resources/{Folder}/{name}.");
                    return;
                }
            }

            track = name;
            if (fading != null) Destroy(fading);
            fading = current;
            current = null;
            if (clip == null) return;

            current = gameObject.AddComponent<AudioSource>();
            current.clip = clip;
            current.loop = true;
            current.playOnAwake = false;
            current.spatialBlend = 0f;
            current.priority = 0;
            current.volume = 0f;
            current.Play();
        }

        void Update()
        {
            var step = Time.unscaledDeltaTime / FadeSeconds;
            var target = Volume;
            if (current != null && current.volume < target) current.volume = Mathf.Min(target, current.volume + step * target);
            if (current != null && current.volume > target) current.volume = target;

            if (fading == null) return;
            fading.volume -= step * Mathf.Max(target, 0.05f);
            if (fading.volume > 0f) return;
            Destroy(fading);
            fading = null;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
