using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace WyuTestRunner.Scenarios
{
    static class Language
    {
        const string PackageId = "rimhunter.whileyoureup";
        const string KeyPrefix = "CodeOptimist.WhileYoureUp_";

        [Scenario("language-non-english-has-no-missing-key", SelfTest = true)]
        static void NonEnglishHasNoMissingKey(ScenarioContext s)
        {
            var language = LanguageDatabase.activeLanguage.folderName;
            s.Check(language != "English", "run this scenario with --language set to a non-English language; the active language is " + language);

            var mod = LoadedModManager.RunningModsListForReading.First(m => m.PackageId == PackageId);
            var keysFile = Path.Combine(mod.RootDir, "Languages", "English", "Keyed", "Keys.xml");
            s.Check(File.Exists(keysFile), "the mod should ship Languages/English/Keyed/Keys.xml; looked in " + keysFile);
            var keys = XDocument.Load(keysFile).Root.Elements().Select(e => e.Name.LocalName).ToList();
            s.Check(keys.Contains(KeyPrefix + "License_Notice"), "Keys.xml should hold the License_Notice key");

            var unresolved = keys.Where(k => k.Translate().RawText == k).ToList();
            s.Check(unresolved.Count == 0, $"{unresolved.Count} of {keys.Count} keys did not resolve under {language}: " + string.Join(", ", unresolved.Take(5)));

            var notice = (KeyPrefix + "License_Notice").Translate().Resolve();
            s.Check(notice.Contains("AGPL"), "the license notice should read as English text under " + language + ", got: " + notice);

            System.Threading.Thread.Sleep(1600);
            using var fs     = new FileStream(s.PlayerLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            string line;
            var mentions = new System.Collections.Generic.List<string>();
            while ((line = reader.ReadLine()) is not null)
                if (line.Contains(KeyPrefix)) mentions.Add(line);
            s.Check(mentions.Count == 0, $"Player.log mentions the mod's translation keys {mentions.Count} time(s): " + string.Join(" || ", mentions.Take(3)));
            s.RequireNoPlayerLogErrors();
        }
    }
}
