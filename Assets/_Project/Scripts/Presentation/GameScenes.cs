using UnityEngine.SceneManagement;

namespace NinjaRealm.Presentation
{
    /// <summary>The game's scenes, by name, and moving between them.</summary>
    public static class GameScenes
    {
        public const string Village = "Village";
        public const string Battle = "Battle";

        public static string PathOf(string scene) => $"Assets/Scenes/{scene}.unity";

        /// <summary>True when the scene is in the build settings, so it can be loaded.</summary>
        public static bool CanLoad(string scene) => SceneUtility.GetBuildIndexByScenePath(PathOf(scene)) >= 0;

        /// <summary>Loads a scene in place of the current one. The save stays loaded (<see cref="GameProfile"/>).</summary>
        public static bool Load(string scene)
        {
            if (!CanLoad(scene)) return false;
            SceneManager.LoadScene(scene);
            return true;
        }
    }
}
