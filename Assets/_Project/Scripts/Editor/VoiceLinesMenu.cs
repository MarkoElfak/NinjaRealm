using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NinjaRealm.Presentation.Audio;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Records the ninjas' voice lines (Audio/Resources/Voices/VoiceLines.txt) with the macOS
    /// text to speech voices, one clip a line, into Audio/Resources/Voices/&lt;ninja id&gt;/. Each
    /// clip remembers the text, voice and rate it was made from, so only changed lines are
    /// recorded again, and clips for removed lines are deleted.
    /// </summary>
    public static class VoiceLinesMenu
    {
        public const string Folder = "Assets/_Project/Audio/Resources/Voices";
        public const string LinesPath = Folder + "/VoiceLines.txt";
        const string Say = "/usr/bin/say";

        [MenuItem("Ninja Realm/Audio/Open the Voice Lines")]
        static void OpenLines()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(LinesPath);
            if (asset == null)
            {
                Debug.LogError($"[Voices] No {LinesPath}.");
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            AssetDatabase.OpenAsset(asset);
        }

        [MenuItem("Ninja Realm/Audio/Record the Voice Lines")]
        static void RecordChanged() => Record(false);

        [MenuItem("Ninja Realm/Audio/Record All the Voice Lines Again")]
        static void RecordAll() => Record(true);

        /// <summary>Records the lines that changed (or every line), and deletes the clips of removed lines.</summary>
        public static void Record(bool all)
        {
            if (Application.platform != RuntimePlatform.OSXEditor || !File.Exists(Say))
            {
                EditorUtility.DisplayDialog("Record the Voice Lines", "The lines are recorded with the macOS 'say' voices, so this only runs in Unity on a Mac.", "OK");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(LinesPath);
            if (asset == null)
            {
                Debug.LogError($"[Voices] No {LinesPath}.");
                return;
            }

            var book = VoiceBook.Parse(asset.text);
            foreach (var problem in book.Problems) Debug.LogWarning($"[Voices] VoiceLines.txt: {problem}");
            var installed = InstalledVoices();

            var jobs = new List<(string path, string voice, int rate, string text, string key)>();
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var voice in book.Voices)
            {
                var macVoice = voice.MacVoice;
                if (macVoice.Length > 0 && installed.Count > 0 && !installed.Contains(macVoice))
                {
                    Debug.LogWarning($"[Voices] [{voice.NinjaId}] asks for the voice '{macVoice}', which this Mac doesn't have, so its lines use the system voice. Run `say -v ?` in Terminal to list the voices.");
                    macVoice = "";
                }

                for (var i = 0; i < voice.Lines.Count; i++)
                {
                    var path = $"{Folder}/{voice.NinjaId}/{VoiceBook.ClipName(voice.NinjaId, i)}.wav";
                    keep.Add(path);
                    var key = $"{macVoice}|{voice.Rate}|{voice.Lines[i]}";
                    if (!all && File.Exists(path) && AssetImporter.GetAtPath(path)?.userData == key) continue;
                    jobs.Add((path, macVoice, voice.Rate, voice.Lines[i], key));
                }
            }

            var removed = RemoveStale(keep);
            var made = new List<(string path, string key)>();
            var temp = Path.Combine(Path.GetTempPath(), "ninja-realm-voices");
            Directory.CreateDirectory(temp);
            try
            {
                for (var i = 0; i < jobs.Count; i++)
                {
                    var job = jobs[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Recording the voice lines", job.text, i / (float)jobs.Count)) break;
                    var wav = Path.Combine(temp, Path.GetFileName(job.path));
                    if (!Speak(job.voice, job.rate, job.text, wav, temp)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(job.path));
                    File.Copy(wav, job.path, true);
                    made.Add((job.path, job.key));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            foreach (var (path, key) in made)
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;
                importer.userData = key;
                importer.SaveAndReimport();
            }

            Debug.Log($"[Voices] Recorded {made.Count} of {jobs.Count} changed lines and removed {removed} old clips. {book.Voices.Count} ninjas have lines.");
        }

        /// <summary>Records one line with `say` into a 22 kHz mono WAV.</summary>
        static bool Speak(string voice, int rate, string text, string wav, string temp)
        {
            var textFile = Path.Combine(temp, "line.txt");
            File.WriteAllText(textFile, text);
            var args = $"-r {rate} -o \"{wav}\" --file-format=WAVE --data-format=LEI16@22050 -f \"{textFile}\"";
            if (voice.Length > 0) args = $"-v \"{voice}\" " + args;
            var result = Run(Say, args, out var output);
            if (result == 0 && File.Exists(wav)) return true;
            Debug.LogError($"[Voices] say failed for \"{text}\": {output}");
            return false;
        }

        /// <summary>Deletes the clips no line uses any more, and empty ninja folders.</summary>
        static int RemoveStale(HashSet<string> keep)
        {
            if (!Directory.Exists(Folder)) return 0;
            var removed = 0;
            foreach (var dir in Directory.GetDirectories(Folder))
            {
                foreach (var file in Directory.GetFiles(dir, "*.wav"))
                {
                    var path = file.Replace('\\', '/');
                    if (keep.Contains(path)) continue;
                    if (AssetDatabase.DeleteAsset(path)) removed++;
                }

                var folder = dir.Replace('\\', '/');
                if (Directory.GetFiles(dir, "*.wav").Length == 0) AssetDatabase.DeleteAsset(folder);
            }

            return removed;
        }

        /// <summary>The names of the voices `say` has on this Mac; empty when it can't tell.</summary>
        static HashSet<string> InstalledVoices()
        {
            var voices = new HashSet<string>(StringComparer.Ordinal);
            if (Run(Say, "-v ?", out var output) != 0) return voices;
            // Each line is "Name   xx_YY    # Sample text"; a name can have spaces in it.
            var pattern = new Regex(@"^(.+?)\s+[a-z]{2,3}_[A-Za-z0-9]+\s+#");
            foreach (var line in output.Split('\n'))
            {
                var match = pattern.Match(line);
                if (match.Success) voices.Add(match.Groups[1].Value.Trim());
            }

            return voices;
        }

        static int Run(string file, string args, out string output)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(30000);
                return process.HasExited ? process.ExitCode : -1;
            }
            catch (Exception e)
            {
                output = e.Message;
                return -1;
            }
        }
    }

    /// <summary>Import settings for the music (streamed) and the voice lines (small mono clips), the first time each is imported.</summary>
    sealed class AudioImportSettings : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!(assetImporter is AudioImporter importer) || !importer.importSettingsMissing) return;
            var path = assetPath.Replace('\\', '/');
            var settings = importer.defaultSampleSettings;
            if (path.StartsWith(VoiceLinesMenu.Folder + "/", StringComparison.Ordinal))
            {
                importer.forceToMono = true;
                importer.loadInBackground = true;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.6f;
            }
            else if (path.Contains("/Resources/" + MusicPlayer.Folder + "/"))
            {
                importer.loadInBackground = true;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
            }
            else return;

            importer.defaultSampleSettings = settings;
        }
    }
}
