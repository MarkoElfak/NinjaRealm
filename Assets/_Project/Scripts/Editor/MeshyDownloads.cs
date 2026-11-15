using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Downloads Meshy files that the Meshy tools only hand out as links, such as rigged models
    /// and animations. Each line of <see cref="ListPath"/> is a project path, a tab, then the
    /// link: Assets/_Project/Art/Meshy/Animations/Run.fbx	https://assets.meshy.ai/...
    /// </summary>
    public static class MeshyDownloads
    {
        public const string ListPath = "Logs/meshy/downloads.txt";

        const string AllowedHost = "assets.meshy.ai";

        [MenuItem("Ninja Realm/Art/Download Meshy Files")]
        public static void Download()
        {
            if (!File.Exists(ListPath))
            {
                Debug.LogWarning($"[Meshy] No {ListPath}. Add one line per file: project path, a tab, then the Meshy link.");
                return;
            }

            var entries = new List<(string path, Uri url)>();
            foreach (var line in File.ReadAllLines(ListPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length != 2 || !Uri.TryCreate(parts[1].Trim(), UriKind.Absolute, out var url) ||
                    url.Scheme != Uri.UriSchemeHttps || url.Host != AllowedHost)
                {
                    Debug.LogError($"[Meshy] Skipped a line that isn't a project path and an https://{AllowedHost} link: {line}");
                    continue;
                }

                var path = parts[0].Trim().Replace('\\', '/');
                if (!MeshyArtImporter.IsMeshyAsset(path) || path.Contains(".."))
                {
                    Debug.LogError($"[Meshy] Skipped {path}: downloads go under {MeshyArtImporter.Root}.");
                    continue;
                }

                entries.Add((path, url));
            }

            var saved = new List<string>();
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            {
                try
                {
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var (path, url) = entries[i];
                        EditorUtility.DisplayProgressBar("Downloading Meshy files", path, i / (float)entries.Count);
                        try
                        {
                            var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                            File.WriteAllBytes(path, bytes);
                            saved.Add($"{path} ({bytes.Length / 1024} KB)");
                        }
                        catch (Exception exception)
                        {
                            Debug.LogError($"[Meshy] Could not download {path}: {exception.Message}");
                        }
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Meshy] Downloaded {saved.Count} of {entries.Count} files: {string.Join(", ", saved)}.");
        }
    }
}
