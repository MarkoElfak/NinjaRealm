using System;
using System.IO;
using UnityEngine;

namespace NinjaRealm.Core.Progression
{
    /// <summary>
    /// Reads and writes <see cref="PlayerProfile"/> as JSON. Every file carries a version; a file
    /// from an older version runs through the migrations after it, in order, so old saves keep
    /// loading. To change the format: raise <see cref="CurrentVersion"/> and add one migration.
    /// When an older file is loaded, a copy of it is kept beside the save before it is rewritten.
    /// </summary>
    public static class SaveFile
    {
        public const int CurrentVersion = 3;

        /// <summary>Migrations[i] turns a version i profile into version i + 1.</summary>
        static readonly Action<PlayerProfile>[] Migrations =
        {
            // 0 to 1: files written before saves had a version. Nothing to change.
            profile => { },

            // 1 to 2: the campaign. Beaten lineups become cleared stages, which needs the content
            // to know which stage uses which lineup, so ProfileService does it when it loads.
            profile => { },

            // 2 to 3: the Shrine's runes. Older saves own none; ProfileService lights the free altar slots on load.
            profile => { },
        };

        public static string ToJson(PlayerProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.version = CurrentVersion;
            return JsonUtility.ToJson(profile, true);
        }

        /// <summary>Reads a profile and brings it up to the current version. False, with the reason, when the file can't be used.</summary>
        public static bool TryRead(string json, out PlayerProfile profile, out string problem) =>
            TryRead(json, out profile, out problem, out _);

        /// <summary>Like <see cref="TryRead(string, out PlayerProfile, out string)"/>, and also gives the version the file was written in.</summary>
        public static bool TryRead(string json, out PlayerProfile profile, out string problem, out int fileVersion)
        {
            profile = null;
            problem = null;
            fileVersion = 0;
            if (string.IsNullOrWhiteSpace(json))
            {
                problem = "The save file is empty.";
                return false;
            }

            try
            {
                profile = JsonUtility.FromJson<PlayerProfile>(json);
            }
            catch (ArgumentException exception)
            {
                problem = $"The save file isn't valid JSON: {exception.Message}";
                return false;
            }

            if (profile == null)
            {
                problem = "The save file is empty.";
                return false;
            }

            fileVersion = Math.Max(0, profile.version);
            if (profile.version > CurrentVersion)
            {
                problem = $"The save file is from a newer version of the game (save version {profile.version}, this game reads up to {CurrentVersion}).";
                profile = null;
                return false;
            }

            for (var version = Math.Max(0, profile.version); version < CurrentVersion; version++)
                Migrations[version](profile);
            profile.version = CurrentVersion;
            return true;
        }
    }

    /// <summary>Where the save file lives.</summary>
    public interface ISaveStorage
    {
        /// <summary>The saved text, or null when there is no save yet.</summary>
        string Read();

        void Write(string text);

        /// <summary>Moves an unreadable save aside so a new one can start without losing it.</summary>
        void SetAside();

        /// <summary>Keeps a copy of the save as it is now, before a newer version of the game rewrites it. The label names the copy, such as "v1".</summary>
        void KeepCopy(string label);
    }

    /// <summary>A save file on disk. Writes go to a temporary file first, then replace the old one.</summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        public FileSaveStorage(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Path { get; }

        public string Read() => File.Exists(Path) ? File.ReadAllText(Path) : null;

        public void Write(string text)
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, text);
            if (File.Exists(Path)) File.Replace(temporary, Path, null);
            else File.Move(temporary, Path);
        }

        public void SetAside()
        {
            if (!File.Exists(Path)) return;
            var aside = System.IO.Path.ChangeExtension(Path, $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(Path, aside);
        }

        public void KeepCopy(string label)
        {
            if (!File.Exists(Path)) return;
            var copy = System.IO.Path.ChangeExtension(Path, $".{label}.json");
            if (!File.Exists(copy)) File.Copy(Path, copy);
        }
    }

    /// <summary>A save kept in memory, for tests.</summary>
    public sealed class MemorySaveStorage : ISaveStorage
    {
        public MemorySaveStorage(string text = null)
        {
            Text = text;
        }

        public string Text { get; private set; }

        public int Writes { get; private set; }

        public string SetAsideText { get; private set; }

        /// <summary>The label of the last copy kept, and its text.</summary>
        public string CopyLabel { get; private set; }

        public string CopyText { get; private set; }

        public string Read() => Text;

        public void Write(string text)
        {
            Text = text;
            Writes++;
        }

        public void SetAside()
        {
            SetAsideText = Text;
            Text = null;
        }

        public void KeepCopy(string label)
        {
            CopyLabel = label;
            CopyText = Text;
        }
    }
}
