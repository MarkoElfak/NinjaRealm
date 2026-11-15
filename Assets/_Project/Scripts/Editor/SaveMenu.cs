using System.IO;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>Menu items for the player's save file while testing.</summary>
    public static class SaveMenu
    {
        [MenuItem("Ninja Realm/Save/Show Save File")]
        public static void ShowSaveFile()
        {
            var path = GameProfile.SavePath;
            if (File.Exists(path)) EditorUtility.RevealInFinder(path);
            else Debug.Log($"[Save] No save yet. Press Play to start one at {path}.");
        }

        [MenuItem("Ninja Realm/Save/Delete Save File")]
        public static void DeleteSaveFile()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Save] Leave play mode first, or the game writes the save again.");
                return;
            }

            var path = GameProfile.SavePath;
            if (!File.Exists(path))
            {
                Debug.Log("[Save] There is no save file.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete Save File", $"Delete {path}? The next play starts a new save.", "Delete", "Cancel")) return;
            File.Delete(path);
            GameProfile.Unload();
            Debug.Log($"[Save] Deleted {path}.");
        }

        /// <summary>Adds plenty of every resource to the running game's save, for trying out the roster screen.</summary>
        [MenuItem("Ninja Realm/Save/Give Test Resources (Play Mode)")]
        public static void GiveTestResources()
        {
            var profile = GameProfile.Current;
            if (!EditorApplication.isPlaying || profile == null)
            {
                Debug.LogError("[Save] Press Play first.");
                return;
            }

            var progression = profile.Progression;
            profile.Grant(new[]
            {
                new ResourceAmount(progression.Gold, 5000),
                new ResourceAmount(progression.Karma, 5000),
                new ResourceAmount(progression.Steel, 200),
            });
            Debug.Log("[Save] Added 5000 Gold, 5000 Karma and 200 Steel.");
        }
    }
}
