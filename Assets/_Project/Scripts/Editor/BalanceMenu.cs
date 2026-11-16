using System.IO;
using System.Text;
using NinjaRealm.Core.Campaign;
using NinjaRealm.Core.Prototype;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Balance checks from the menu.
    /// Batchmode: -executeMethod NinjaRealm.Editor.BalanceMenu.WalkCampaign
    /// </summary>
    public static class BalanceMenu
    {
        const string ReportPath = "Logs/campaign_walk.md";

        /// <summary>
        /// Plays the campaign three times with <see cref="CampaignWalker"/> on the numbers in
        /// PrototypeContent.cs and writes how long each stage and chapter took to Logs/campaign_walk.md.
        /// </summary>
        [MenuItem("Ninja Realm/Balance/Walk the Campaign")]
        public static void WalkCampaign()
        {
            var sink = new InMemoryContentSink();
            PrototypeContent.Build(sink);
            var database = sink.CreateDatabase();
            try
            {
                var report = new StringBuilder();
                for (var seed = 1; seed <= 3; seed++)
                {
                    var walker = new CampaignWalker(database) { Seed = seed };
                    report.AppendLine(walker.Report(walker.Run()));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, report.ToString());
                Debug.Log($"[Balance] Campaign walk written to {Path.GetFullPath(ReportPath)}\n{report}");
            }
            finally
            {
                foreach (var definition in sink.Definitions) Object.DestroyImmediate(definition);
                Object.DestroyImmediate(database);
            }
        }
    }
}
