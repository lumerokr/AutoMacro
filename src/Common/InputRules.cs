using System;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class InputRules
    {
        internal static bool IsThumb(Keys key) { return key == Keys.XButton1 || key == Keys.XButton2; }
        internal static bool ValidShortcut(Keys key)
        {
            Keys normalized = NormalizeKey(key);
            return IsThumb(key) || ((int)key >= 8 && (int)key <= 254 && normalized != Keys.ControlKey && normalized != Keys.ShiftKey && normalized != Keys.Menu && key != Keys.LWin && key != Keys.RWin);
        }
        internal static string KeyName(Keys key)
        { return key == Keys.XButton1 ? L.T("마우스 4") : key == Keys.XButton2 ? L.T("마우스 5") : key.ToString(); }

        internal static int ClampInterval(int value) { return Math.Max(10, Math.Min(3600000, value)); }
        internal static Keys NormalizeKey(Keys key)
        {
            if (key == Keys.LControlKey || key == Keys.RControlKey) return Keys.ControlKey;
            if (key == Keys.LShiftKey || key == Keys.RShiftKey) return Keys.ShiftKey;
            if (key == Keys.LMenu || key == Keys.RMenu) return Keys.Menu;
            return key & Keys.KeyCode;
        }
        internal static string ValidateKeys(Keys toggle, Keys emergency, Keys macro)
        {
            if (NormalizeKey(toggle) == NormalizeKey(emergency)) return L.T("시작·중지와 긴급 정지 단축키는 달라야 합니다.");
            if (NormalizeKey(macro) == NormalizeKey(toggle) || NormalizeKey(macro) == NormalizeKey(emergency))
                return L.T("매크로 키와 제어 단축키는 중복될 수 없습니다.");
            return null;
        }
    }
}
