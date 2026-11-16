using NinjaRealm.Presentation.Village;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Opens the village's panels from the menu while the Village scene plays, to check a panel
    /// without clicking through the scene.
    /// </summary>
    public static class VillageMenu
    {
        const string Menu = "Ninja Realm/Village/";

        [MenuItem(Menu + "Open Command Hall")]
        public static void OpenCommandHall() => Open(VillageBuildingKind.CommandHall);

        [MenuItem(Menu + "Open Command Hall, Chapter 2")]
        public static void OpenChapterTwo() => OpenChapter(2);

        [MenuItem(Menu + "Open Command Hall, Chapter 3")]
        public static void OpenChapterThree() => OpenChapter(3);

        static void OpenChapter(int number)
        {
            if (!Open(VillageBuildingKind.CommandHall)) return;
            var screen = Object.FindAnyObjectByType<VillageScreen>();
            if (screen != null) screen.ShowChapter(number);
        }

        [MenuItem(Menu + "Open Dojo")]
        public static void OpenDojo() => Open(VillageBuildingKind.Dojo);

        [MenuItem(Menu + "Open Dojo, All Ninjas")]
        public static void OpenAllNinjas()
        {
            if (!Open(VillageBuildingKind.Dojo)) return;
            var screen = Object.FindAnyObjectByType<VillageScreen>();
            if (screen != null) screen.ShowAllNinjas();
        }

        [MenuItem(Menu + "Open Mint")]
        public static void OpenMint() => Open(VillageBuildingKind.Mint);

        [MenuItem(Menu + "Open Steel Mine")]
        public static void OpenSteelMine() => Open(VillageBuildingKind.SteelMine);

        [MenuItem(Menu + "Open First Plaza Ninja")]
        public static void OpenFirstNinja()
        {
            var village = Village();
            if (village == null) return;
            var owned = village.Profile != null ? village.Profile.OwnedNinjas() : village.AllNinjas();
            if (owned.Count > 0) village.OpenNinja(owned[0]);
        }

        [MenuItem(Menu + "Close Panel")]
        public static void ClosePanel()
        {
            var village = Village();
            if (village != null) village.Close();
        }

        static bool Open(VillageBuildingKind kind)
        {
            var village = Village();
            if (village == null) return false;
            var building = village.Find(kind);
            if (building == null)
            {
                Debug.LogError($"[Village] The scene has no {VillageBuilding.NameOf(kind)}.");
                return false;
            }

            village.Open(building);
            return true;
        }

        static VillageController Village()
        {
            var village = EditorApplication.isPlaying ? Object.FindAnyObjectByType<VillageController>() : null;
            if (village == null) Debug.Log("[Village] Press Play in the Village scene first.");
            return village;
        }
    }
}
