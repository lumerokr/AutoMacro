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
        static DataNode Defaults()
        {
            return new DataNode("AutoMacro", new DataField("version", 1), new DataNode("Mode", 0), new DataNode("Interval", 100),
                new DataNode("Macro", (int)System.Windows.Forms.Keys.Space), new DataNode("Toggle", (int)System.Windows.Forms.Keys.F6),
                new DataNode("Stop", (int)System.Windows.Forms.Keys.F8), new DataNode("HoldWindow", false),
                new DataNode("Language", L.WindowsDefault()), new DataNode("NoticeVersion", 1), new DataNode("Acknowledged", false));
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
                return (!version.HasValue || version.Value == 1) && (!accepted.HasValue || version.HasValue);
            }
            catch { return false; }
        }
        internal static void Reset(string path)
        {
            lock (Gate) { RecoveryFiles.Preserve(path); DataStore.Save(path, Defaults(), false); }
        }
        internal static void Update(string path, params DataNode[] fields)
        {
            lock (Gate)
            {
                DataNode root = Read(path);
                foreach (DataNode field in fields)
                {
                    DataNode previous = root.Element(field.Name); if (previous != null) previous.Remove();
                    root.Add(field);
                }
                DataStore.Save(path, root, false);
            }
        }
    }
}
