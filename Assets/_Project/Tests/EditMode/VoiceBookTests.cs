using System.IO;
using System.Linq;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Prototype;
using NinjaRealm.Presentation.Audio;
using NUnit.Framework;
using UnityEngine;

namespace NinjaRealm.Tests
{
    /// <summary>The voice lines file: how it is read, and that the one in the project covers every ninja.</summary>
    public class VoiceBookTests
    {
        const string LinesFile = "Assets/_Project/Audio/Resources/Voices/VoiceLines.txt";

        [Test]
        public void Parse_ReadsBlocksSettingsAndLines()
        {
            var book = VoiceBook.Parse(
                "# a note\n" +
                "[kaito]\n" +
                "voice = Daniel\n" +
                "rate = 190\n" +
                "pitch = 0.9\n" +
                "My blade is ready.\n" +
                "\n" +
                "One cut = one win.\n" +
                "[hana]\r\n" +
                "Stay close.\r\n");

            Assert.IsEmpty(book.Problems, string.Join("\n", book.Problems));
            Assert.AreEqual(2, book.Voices.Count);

            var kaito = book.Find("kaito");
            Assert.AreEqual("Daniel", kaito.MacVoice);
            Assert.AreEqual(190, kaito.Rate);
            Assert.AreEqual(0.9f, kaito.Pitch, 1e-5f);
            CollectionAssert.AreEqual(new[] { "My blade is ready.", "One cut = one win." }, kaito.Lines);

            var hana = book.Find("hana");
            Assert.AreEqual("", hana.MacVoice);
            Assert.AreEqual(VoiceBook.DefaultRate, hana.Rate);
            Assert.AreEqual(1f, hana.Pitch);
            CollectionAssert.AreEqual(new[] { "Stay close." }, hana.Lines);
            Assert.IsNull(book.Find("goro"));
        }

        [Test]
        public void Parse_ReportsProblemsAndKeepsGoing()
        {
            var book = VoiceBook.Parse("Nobody says this.\n[]\n[mei]\npitch = loud\nrate = 5\nHello.\n[jiro]\n[mei]\nAgain.\n");

            Assert.AreEqual(6, book.Problems.Count, string.Join("\n", book.Problems));
            var mei = book.Find("mei");
            Assert.AreEqual(1f, mei.Pitch);
            Assert.AreEqual(VoiceBook.DefaultRate, mei.Rate);
            CollectionAssert.AreEqual(new[] { "Hello.", "Again." }, mei.Lines);
            Assert.IsNotNull(book.Find("jiro"));
        }

        [Test]
        public void ClipNames_AreNumberedFromOne()
        {
            Assert.AreEqual("kaito_01", VoiceBook.ClipName("kaito", 0));
            Assert.AreEqual("Voices/kaito/kaito_12", VoiceBook.ClipPath("kaito", 11));
        }

        [Test]
        public void ProjectVoiceLines_GiveEveryNinjaLines()
        {
            Assert.IsTrue(File.Exists(LinesFile), $"{LinesFile} is missing.");
            var book = VoiceBook.Parse(File.ReadAllText(LinesFile));
            Assert.IsEmpty(book.Problems, string.Join("\n", book.Problems));

            var sink = new InMemoryContentSink();
            PrototypeContent.Build(sink);
            try
            {
                var ninjas = sink.Definitions.OfType<NinjaDefinition>().ToList();
                Assert.IsNotEmpty(ninjas);
                var missing = ninjas.Where(n => book.Find(n.Id) == null || book.Find(n.Id).Lines.Count == 0).Select(n => n.Id).ToList();
                Assert.IsEmpty(missing, "These ninjas have no voice lines: " + string.Join(", ", missing));

                var unknown = book.Voices.Where(v => ninjas.All(n => n.Id != v.NinjaId)).Select(v => v.NinjaId).ToList();
                Assert.IsEmpty(unknown, "These voice blocks name no ninja: " + string.Join(", ", unknown));
            }
            finally
            {
                foreach (var definition in sink.Definitions) Object.DestroyImmediate(definition);
            }
        }
    }
}
