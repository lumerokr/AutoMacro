using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class Preferences
    {
        internal int Mode = 0, Interval = 100;
        internal bool HoldWindow;
        internal Keys Macro = Keys.Space, Toggle = Keys.F6, Stop = Keys.F8;
        internal static Preferences ResetDefaults(IEnumerable<Keys> reserved)
        {
            HashSet<Keys> used = new HashSet<Keys>();
            foreach (Keys key in reserved) if (key != Keys.None) used.Add(InputRules.NormalizeKey(key));
            Preferences value = new Preferences();
            value.Macro = Available(value.Macro, used, true);
            value.Toggle = Available(value.Toggle, used, false);
            value.Stop = Available(value.Stop, used, false);
            return value;
        }
        static Keys Available(Keys preferred, HashSet<Keys> used, bool input)
        {
            if (used.Add(InputRules.NormalizeKey(preferred))) return preferred;
            if (input) for (int code = (int)Keys.A; code <= (int)Keys.Z; code++)
                if (used.Add((Keys)code)) return (Keys)code;
            for (int code = (int)Keys.F1; code <= (int)Keys.F24; code++)
                if (used.Add((Keys)code)) return (Keys)code;
            for (int code = 8; code <= 254; code++)
                if (InputRules.ValidShortcut((Keys)code) && used.Add(InputRules.NormalizeKey((Keys)code))) return (Keys)code;
            throw new InvalidOperationException(L.T("중복되지 않는 Clicker 키를 찾지 못했습니다. 매크로 실행 단축키를 확인하세요."));
        }
        internal static Preferences Load(string path)
        {
            if (!DataStore.Exists(path)) return new Preferences();
            DataNode root = DataStore.Load(path);
            if (root.Name != "AutoMacro" || (int?)root.Attribute("version") != 1) throw new FormatException("Unsupported preferences");
            Preferences value = new Preferences();
            value.HoldWindow = (bool?)root.Element("HoldWindow") ?? false;
            value.Mode = (int)root.Element("Mode"); value.Interval = InputRules.ClampInterval((int)root.Element("Interval"));
            value.Macro = (Keys)(int)root.Element("Macro"); value.Toggle = (Keys)(int)root.Element("Toggle"); value.Stop = (Keys)(int)root.Element("Stop");
            if (value.Mode < 0 || value.Mode > 3 || (int)value.Macro < 8 || (int)value.Macro > 254 ||
                !InputRules.ValidShortcut(value.Toggle) || !InputRules.ValidShortcut(value.Stop) || InputRules.ValidateKeys(value.Toggle, value.Stop, value.Macro) != null)
                throw new FormatException("Invalid preferences");
            
            return value;
        }

        internal void Save(string path)
        {
            SettingsStore.Update(path, new DataNode("Mode", Mode), new DataNode("Interval", Interval),
                new DataNode("Macro", (int)Macro), new DataNode("Toggle", (int)Toggle), new DataNode("Stop", (int)Stop), new DataNode("HoldWindow", HoldWindow));
        }
    }
}
