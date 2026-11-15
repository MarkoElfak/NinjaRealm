using System;
using System.IO;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Progression;
using UnityEngine;

namespace NinjaRealm.Presentation
{
    /// <summary>
    /// The player's save, loaded once and shared by every screen. It lives in
    /// <see cref="SavePath"/>, and every change is written straight away.
    /// </summary>
    public static class GameProfile
    {
        public const string FileName = "profile.json";

        static ProfileService current;

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>The loaded save, or null before <see cref="Load"/>.</summary>
        public static ProfileService Current => current;

        /// <summary>
        /// Loads the save for this content, starting a new one if there is none. Returns null, with
        /// an error in the console, when the content has no progression settings.
        /// </summary>
        public static ProfileService Load(ContentDatabase content)
        {
            if (current != null && current.Content == content) return current;
            if (content == null) return null;

            current = ProfileService.Load(new FileSaveStorage(SavePath), content, DateTime.UtcNow.Ticks);
            if (current == null)
                Debug.LogError("[Save] The content has no Progression asset. Run Ninja Realm > Content > Create Prototype Content.");
            else if (current.LoadProblem != null)
                Debug.LogWarning($"[Save] {current.LoadProblem} Started a new save; the old file was kept next to {SavePath}.");
            return current;
        }

        /// <summary>Forgets the loaded save, so the next <see cref="Load"/> reads the file again.</summary>
        public static void Unload() => current = null;

        // Play mode can start without a domain reload, which would keep the last session's save.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => current = null;
    }
}
