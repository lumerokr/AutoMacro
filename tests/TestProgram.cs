using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
namespace AutoMacro
{
    internal static class TestProgram
    {
        [STAThread] static int Main(string[] args)
        {
            // Test processes must never read or write the user's AppData.
            AppInfo.TestDataDirectory = AppDomain.CurrentDomain.BaseDirectory;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--ui-test")
            {
                try { MacroTests.RunUI(); MacroFeatureTests.RunUI(); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 20; }
            }
            if (args.Length > 0 && args[0] == "--self-test")
            {
                try
                {
                    if (Marshal.SizeOf(typeof(Native.Input)) != (IntPtr.Size == 8 ? 40 : 28)) return 1;
                    if (ThumbHook.Decode(0x00010000) != Keys.XButton1 || ThumbHook.Decode(0x00020000) != Keys.XButton2 || ThumbHook.Decode(0) != Keys.None) return 10;
                    if (MainForm.ClampInterval(0) != 10 || MainForm.ClampInterval(-1) != 10 || MainForm.ClampInterval(100) != 100) return 2;
                    for (int mode = 0; mode < 3; mode++)
                    {
                        Native.Input[] pair = Native.MakeInputs(mode, Keys.Space);
                        uint flag = mode == 0 ? 2u : mode == 1 ? 8u : 32u;
                        if (pair[0].data.mouse.flags != flag || pair[1].data.mouse.flags != flag * 2) return 3;
                    }
                    Native.Input[] keys = Native.MakeInputs(3, Keys.Left);
                    if (keys[0].type != 1 || keys[0].data.keyboard.flags != 1 || keys[1].data.keyboard.flags != 3) return 4;
                    keys = Native.MakeInputs(3, Keys.A);
                    if (keys[0].data.keyboard.key != 65 || keys[1].data.keyboard.flags != 2) return 5;
                    if (MainForm.ValidateKeys(Keys.F6, Keys.F6, Keys.Space) == null ||
                        MainForm.ValidateKeys(Keys.F6, Keys.F8, Keys.F6) == null ||
                        MainForm.ValidateKeys(Keys.F6, Keys.F8, Keys.F8) == null ||
                        MainForm.ValidateKeys(Keys.F9, Keys.F10, Keys.F6) != null ||
                        MainForm.ValidateKeys(Keys.ControlKey, Keys.F8, Keys.RControlKey) == null) return 6;
                    using (MainForm form = new MainForm(false)) { form.CreateControl(); if (!form.VerifyConflictGuards()) return 7; }
                    using (KeyPicker picker = new KeyPicker(L.T("검사"), delegate(Keys key) { return key == Keys.F6 ? L.T("중복 키") : null; }))
                    { if (!picker.VerifyCaptureRelease()) return 8; }
                    string testPath = Path.Combine(Path.GetTempPath(), "AutoMacro-test-" + Guid.NewGuid().ToString("N") + ".json");
                    try
                    {
                        Preferences p = new Preferences { Mode = 3, Interval = 25, Macro = Keys.A, Toggle = Keys.XButton1, Stop = Keys.XButton2 };
                        p.Save(testPath); p.Interval = 30; p.Save(testPath);
                        Preferences restored = Preferences.Load(testPath);
                        if (restored.Mode != 3 || restored.Interval != 30 || restored.Macro != Keys.A || restored.Toggle != Keys.XButton1 || restored.Stop != Keys.XButton2) return 11;
                        File.WriteAllText(testPath, "broken JSON");
                        bool rejected = false; try { Preferences.Load(testPath); } catch { rejected = true; }
                        if (!rejected) return 12;
                    }
                    finally { if (File.Exists(testPath)) File.Delete(testPath); }
                    StorageCleanupTests.Run();
                    MacroFeatureTests.Run();
                    MacroTests.Run();
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 9; }
            }
            if (args.Length > 0 && args[0] == "--fresh-start-test")
            {
                try { MacroTests.RunFreshStartup(); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            return 2;
        }
    }
}
