using System;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;

namespace WhileYoureUp;

partial class Mod
{
    const string OriginalWorkshopFolder = "2034960453";

    static readonly string[] OriginalSettingsClasses = { "WhileYoureUp.Mod+Settings", "JobsOfOpportunity.Mod+Settings" };

    static bool importedOriginalSettings;

    static string SettingsFileName(string folderName) => GenText.SanitizeFilename($"Mod_{folderName}_{nameof(Mod)}.xml");

    static string ImportSourceFile(string ownFileName) {
        var configFolder = GenFilePaths.ConfigFolderPath;
        var workshopFile = SettingsFileName(OriginalWorkshopFolder);
        if (workshopFile != ownFileName && File.Exists(Path.Combine(configFolder, workshopFile)))
            return Path.Combine(configFolder, workshopFile);

        return Directory.GetFiles(configFolder, "Mod_*_Mod.xml")
            .Where(path => Path.GetFileName(path) != ownFileName)
            .Where(IsOriginalSettingsFile)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    static bool IsOriginalSettingsFile(string path) {
        string text;
        try {
            text = File.ReadAllText(path);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            return false; // a file we cannot read cannot be recognized as the original's, so it is another mod's
        }
        return OriginalSettingsClasses.Any(text.Contains);
    }

    static void ImportOriginalSettings() {
        var ownFileName = SettingsFileName(mod.Content.FolderName);
        string path = null;
        try {
            path = ImportSourceFile(ownFileName);
            if (path is null) return;

            var doc = new XmlDocument();
            doc.Load(path);
            if (doc.DocumentElement?["ModSettings"] is null)
                throw new InvalidDataException("it has no ModSettings element");

            Scribe.loader.InitLoading(path);
            try {
                Scribe.EnterNode("ModSettings");
                settings.ExposeData();
                Scribe.ExitNode();
            } finally {
                Scribe.loader.FinalizeLoading();
            }
            importedOriginalSettings = true;
            Log.Message($"[{mod.Content.Name}] Imported settings from {path}.");
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or InvalidDataException) {
            Scribe.ForceStop();
            settings.ExposeData();
            importedOriginalSettings = false;
            Log.Error($"[{mod.Content.Name}] Could not import the original While You're Up settings from {path ?? GenFilePaths.ConfigFolderPath}; nothing was imported and it will be tried again next launch.\n{e}");
        }
    }
}
