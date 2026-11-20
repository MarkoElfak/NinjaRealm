using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using UnityEngine;

namespace NinjaRealm.Presentation.Audio
{
    /// <summary>
    /// Says a ninja's voice lines (see <see cref="VoiceBook"/>): a random line each time, never
    /// the same one twice in a row, played at the ninja's pitch. A line that has no recording yet
    /// is still returned, so it shows as text.
    /// </summary>
    public sealed class NinjaVoices : MonoBehaviour
    {
        const float Volume = 0.9f;

        /// <summary>How long a line stays on screen at least, in seconds.</summary>
        public const float MinShowSeconds = 2.5f;

        VoiceBook book;
        AudioSource source;
        readonly Dictionary<string, int> lastLine = new Dictionary<string, int>();
        readonly System.Random random = new System.Random();

        /// <summary>The voice lines, read from the text file once.</summary>
        public VoiceBook Book
        {
            get
            {
                if (book != null) return book;
                var file = Resources.Load<TextAsset>(VoiceBook.ResourcePath);
                book = VoiceBook.Parse(file != null ? file.text : null);
                if (file == null) Debug.LogWarning($"[Voices] No Resources/{VoiceBook.ResourcePath}.txt, so the ninjas say nothing.");
                foreach (var problem in book.Problems) Debug.LogWarning($"[Voices] VoiceLines.txt: {problem}");
                return book;
            }
        }

        /// <summary>Reads the text file again, after it changed.</summary>
        public void Reload() => book = null;

        /// <summary>
        /// Says one of the ninja's lines and returns it, with how long to show it; null when the
        /// ninja has no lines.
        /// </summary>
        public string Speak(NinjaDefinition ninja, out float seconds)
        {
            seconds = 0f;
            var voice = ninja != null ? Book.Find(ninja.Id) : null;
            if (voice == null || voice.Lines.Count == 0) return null;

            var index = random.Next(voice.Lines.Count);
            if (voice.Lines.Count > 1 && lastLine.TryGetValue(voice.NinjaId, out var last) && index == last)
                index = (index + 1 + random.Next(voice.Lines.Count - 1)) % voice.Lines.Count;
            lastLine[voice.NinjaId] = index;

            seconds = MinShowSeconds;
            var clip = Resources.Load<AudioClip>(VoiceBook.ClipPath(voice.NinjaId, index));
            if (clip != null && Application.isPlaying)
            {
                if (source == null)
                {
                    source = gameObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                }

                source.Stop();
                source.clip = clip;
                source.pitch = voice.Pitch;
                source.volume = Volume;
                source.Play();
                seconds = Mathf.Max(MinShowSeconds, clip.length / Mathf.Max(0.1f, voice.Pitch) + 1f);
            }

            return voice.Lines[index];
        }
    }
}
