using NinjaRealm.Presentation.Battle;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Drives the battle scene from the menu while it plays, to check the lineup and result
    /// screens without clicking through the game view.
    /// </summary>
    public static class BattleMenu
    {
        const string Menu = "Ninja Realm/Battle/";

        [MenuItem(Menu + "Fight")]
        public static void Fight()
        {
            var battle = Battle();
            if (battle != null && battle.CanFight) battle.StartFight();
        }

        [MenuItem(Menu + "Skip to the Result")]
        public static void Skip()
        {
            var battle = Battle();
            if (battle != null) battle.SkipFight();
        }

        [MenuItem(Menu + "Next Stage")]
        public static void NextStage()
        {
            var battle = Battle();
            if (battle != null) battle.GoToNextStage();
        }

        [MenuItem(Menu + "Back to Lineup")]
        public static void BackToLineup()
        {
            var battle = Battle();
            if (battle != null) battle.EnterSetup();
        }

        [MenuItem(Menu + "Open Roster")]
        public static void OpenRoster()
        {
            var battle = Battle();
            if (battle == null) return;
            battle.EnterSetup();
            if (battle.Profile != null) battle.RosterOpen = true;
        }

        [MenuItem(Menu + "Back to Village")]
        public static void BackToVillage()
        {
            var battle = Battle();
            if (battle != null && battle.CanLeaveToVillage) battle.LeaveToVillage();
        }

        static BattleController Battle()
        {
            var battle = EditorApplication.isPlaying ? Object.FindAnyObjectByType<BattleController>() : null;
            if (battle == null) Debug.Log("[Battle] Press Play in the Battle scene first.");
            return battle;
        }
    }
}
