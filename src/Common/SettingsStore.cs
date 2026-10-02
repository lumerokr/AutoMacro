using System;
using System.IO;

namespace AutoMacro
{
    // Every update reads the current document and replaces only its own fields.
    internal static class SettingsStore
    {
        static readonly object Gate = new object();
        internal static DataNode Read(string path)
        {
            if (File.Exists(path))
            {
                DataNode root = DataStore.Load(path);
                if (root.Name != "AutoMacro" || (int?)root.Attribute("version") != 1) throw new FormatException("Unsupported settings");
                return root;
            }
            return Defaults();
        }
        static DataNode Defaults(Preferences preferences = null)
        {
            Preferences value = preferences ?? new Preferences();
            return new DataNode("AutoMacro", new DataField("version", 1), new DataNode("Mode", value.Mode), new DataNode("Interval", value.Interval),
                new DataNode("Macro", (int)value.Macro), new DataNode("Toggle", (int)value.Toggle),
                new DataNode("Stop", (int)value.Stop), new DataNode("HoldWindow", value.HoldWindow),
                new DataNode("Language", L.WindowsDefault()), new DataNode("NoticeVersion", 1), new DataNode("Acknowledged", false), new DataNode("MacroSort", 0));
        }
        internal static bool IsValid(string path)
        {
            if (!File.Exists(path)) return true;
            try
            {
                DataNode root = Read(path); Preferences.Load(path);
                string language = (string)root.Element("Language");
                if (language != null && language != "ko" && language != "en" && language != "ja") return false;
                int? version = (int?)root.Element("NoticeVersion"); bool? accepted = (bool?)root.Element("Acknowledged");
                int sort = (int?)root.Element("MacroSort") ?? 0; if (sort < 0 || sort > 2) return false;
                return (!version.HasValue || version.Value == 1) && (!accepted.HasValue || version.HasValue);
            }
            catch { return false; }
        }
        internal static void Reset(string path, Preferences preferences = null)
        {
            lock (Gate) { RecoveryFiles.Preserve(path); DataStore.Save(path, Defaults(preferences), false); }
        }
        internal static void Update(string path, params DataNode[] fields)
        {
            lock (Gate)
            {
                DataNode root = Read(path);
                bool changed = false;
                foreach (DataNode field in fields)
                {
                    DataNode previous = root.Element(field.Name);
                    bool oldFlag, newFlag;
                    if (previous != null && (previous.Value == field.Value ||
                        Boolean.TryParse(previous.Value, out oldFlag) && Boolean.TryParse(field.Value, out newFlag) && oldFlag == newFlag)) continue;
                    changed = true;
                    if (previous != null) previous.Remove();
                    root.Add(field);
                }
                if (changed || !File.Exists(path)) DataStore.Save(path, root, false);
            }
        }
    }
}
