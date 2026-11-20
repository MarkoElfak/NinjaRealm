using System;
using System.Collections.Generic;
using System.Globalization;

namespace NinjaRealm.Presentation.Audio
{
    /// <summary>
    /// The ninjas' voice lines, read from a plain text file (Audio/Resources/Voices/VoiceLines.txt)
    /// so they are easy to change. Each ninja has a block that starts with its ID in square
    /// brackets (without "ninja."); "voice =", "rate =" and "pitch =" lines set how it sounds, and every other line
    /// is one thing it can say. Lines starting with # are notes. The recordings are made from it
    /// by Ninja Realm &gt; Audio &gt; Record the Voice Lines, one clip a line, named by
    /// <see cref="ClipName"/>.
    /// </summary>
    public sealed class VoiceBook
    {
        /// <summary>The text file, as a Resources path.</summary>
        public const string ResourcePath = "Voices/VoiceLines";

        /// <summary>The folder the recordings go in, as a Resources path: Voices/&lt;ninja id&gt;/&lt;clip&gt;.</summary>
        public const string ClipFolder = "Voices";

        /// <summary>How one ninja sounds, and what it says.</summary>
        public sealed class Voice
        {
            public string NinjaId;

            /// <summary>The macOS voice that records the lines (see `say -v ?`). Empty uses the system voice.</summary>
            public string MacVoice = "";

            /// <summary>How fast the lines are recorded, in words a minute.</summary>
            public int Rate = DefaultRate;

            /// <summary>The playback pitch in the game: 1 is as recorded, lower is deeper.</summary>
            public float Pitch = 1f;

            public readonly List<string> Lines = new List<string>();
        }

        public const int DefaultRate = 175;

        readonly List<Voice> voices = new List<Voice>();

        /// <summary>Every ninja's voice, in the file's order.</summary>
        public IReadOnlyList<Voice> Voices => voices;

        /// <summary>What was wrong with the file, one line each. Lines that can't be read are skipped.</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>The voice of a ninja, by its content ID ("ninja.kaito") or its short one ("kaito"), or null.</summary>
        public Voice Find(string ninjaId)
        {
            if (string.IsNullOrEmpty(ninjaId)) return null;
            var id = ShortId(ninjaId);
            foreach (var voice in voices)
                if (string.Equals(voice.NinjaId, id, StringComparison.Ordinal)) return voice;
            return null;
        }

        /// <summary>A content ID without its kind: "ninja.kaito" is "kaito". The file uses these.</summary>
        public static string ShortId(string ninjaId)
        {
            if (string.IsNullOrEmpty(ninjaId)) return ninjaId;
            var dot = ninjaId.LastIndexOf('.');
            return dot >= 0 ? ninjaId.Substring(dot + 1) : ninjaId;
        }

        /// <summary>The recording of a line: &lt;ninja id&gt;_&lt;line number from 01&gt;.</summary>
        public static string ClipName(string ninjaId, int index) => $"{ninjaId}_{index + 1:00}";

        /// <summary>The Resources path of a line's recording.</summary>
        public static string ClipPath(string ninjaId, int index) => $"{ClipFolder}/{ninjaId}/{ClipName(ninjaId, index)}";

        public static VoiceBook Parse(string text)
        {
            var book = new VoiceBook();
            if (string.IsNullOrEmpty(text)) return book;

            Voice voice = null;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var where = $"Line {i + 1}";

                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    var id = line.EndsWith("]", StringComparison.Ordinal) ? line.Substring(1, line.Length - 2).Trim() : "";
                    if (id.Length == 0)
                    {
                        book.Problems.Add($"{where}: a ninja's block starts with its ID in square brackets, like [kaito].");
                        voice = null;
                        continue;
                    }

                    id = ShortId(id);
                    if (book.Find(id) != null)
                    {
                        book.Problems.Add($"{where}: [{id}] has a block already; its lines go in one block.");
                        voice = book.Find(id);
                        continue;
                    }

                    voice = new Voice { NinjaId = id };
                    book.voices.Add(voice);
                    continue;
                }

                if (voice == null)
                {
                    book.Problems.Add($"{where}: this line comes before any [ninja id], so no one says it.");
                    continue;
                }

                if (TrySetting(line, out var key, out var value))
                {
                    switch (key)
                    {
                        case "voice":
                            voice.MacVoice = value;
                            break;
                        case "rate":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) && rate >= 60 && rate <= 400) voice.Rate = rate;
                            else book.Problems.Add($"{where}: the rate should be a whole number of words a minute, 60 to 400.");
                            break;
                        case "pitch":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch) && pitch >= 0.5f && pitch <= 2f) voice.Pitch = pitch;
                            else book.Problems.Add($"{where}: the pitch should be a number from 0.5 to 2, like 0.9.");
                            break;
                    }

                    continue;
                }

                voice.Lines.Add(line);
            }

            foreach (var v in book.voices)
                if (v.Lines.Count == 0) book.Problems.Add($"[{v.NinjaId}] has no lines.");
            return book;
        }

        /// <summary>A "voice = ...", "rate = ..." or "pitch = ..." line. Any other line is a spoken one.</summary>
        static bool TrySetting(string line, out string key, out string value)
        {
            key = value = null;
            var equals = line.IndexOf('=');
            if (equals <= 0) return false;
            var name = line.Substring(0, equals).Trim().ToLowerInvariant();
            if (name != "voice" && name != "rate" && name != "pitch") return false;
            key = name;
            value = line.Substring(equals + 1).Trim();
            return true;
        }
    }
}
