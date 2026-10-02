using System;
using System.IO;
using System.Threading;

namespace AutoMacro
{
    internal static class StorageCleanupTests
    {
        static void Check(bool condition, string message)
        { if (!condition) throw new Exception("Storage/cleanup test: " + message); }
        internal static void Run()
        {
            string original = AppInfo.TestDataDirectory;
            string data = Path.Combine(original, "isolated-data-" + Guid.NewGuid().ToString("N"));
            try
            {
                AppInfo.TestDataDirectory = null;
                Check(AppInfo.DataDirectory == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoMacro"), "LocalAppData default");
                AppInfo.TestDataDirectory = data;
                AppInfo.InitializeDataDirectory();
                Check(!File.Exists(AppInfo.SettingsPath) && !File.Exists(AppInfo.LibraryPath), "new directory starts empty without legacy import");
                Preferences preferences = new Preferences { Mode = 3, Interval = 37, Macro = System.Windows.Forms.Keys.A, Toggle = System.Windows.Forms.Keys.F6, Stop = System.Windows.Forms.Keys.F8 };
                preferences.Save(AppInfo.SettingsPath);
                Check(Preferences.Load(AppInfo.SettingsPath).Interval == 37, "settings round trip in data directory");
                Check(Path.GetDirectoryName(AppInfo.LibraryPath) == data, "macro library uses same data directory");
                AppInfo.InitializeDataDirectory();
                Check(Preferences.Load(AppInfo.SettingsPath).Interval == 37, "startup preserves existing AppData settings");
            }
            finally
            {
                AppInfo.TestDataDirectory = original;
                File.Delete(Path.Combine(data, "settings.json"));
                Directory.Delete(data);
            }

            string stage = Path.Combine(Path.GetDirectoryName(typeof(AppInfo).Assembly.Location), ".automacro-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            string backup = Path.Combine(stage, "previous.exe"), helper = Path.Combine(stage, "installer.exe"), ready = Path.Combine(stage, "ready");
            Thread cleaner = null;
            try
            {
                File.WriteAllText(backup, "rollback"); File.WriteAllText(helper, "helper"); File.WriteAllText(ready, "ready");
                UpdateInstaller.CleanAfterStartup(stage, 300);
                Check(File.Exists(backup) && File.Exists(ready), "failed/pending update backup and readiness preserved");
                using (FileStream locked = new FileStream(helper, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    File.Delete(backup);
                    cleaner = new Thread(delegate() { UpdateInstaller.CleanAfterStartup(stage, 5000); });
                    cleaner.Start(); Thread.Sleep(400);
                    Check(File.Exists(helper), "locked running helper waits for release");
                }
                Check(cleaner.Join(6000), "background cleanup completes");
                Check(!Directory.Exists(stage), "staging removed without another app launch");
            }
            finally
            {
                if (cleaner != null && cleaner.IsAlive) cleaner.Join(6000);
                if (Directory.Exists(stage))
                {
                    foreach (string name in new string[] { "previous.exe", "installer.exe", "ready" }) File.Delete(Path.Combine(stage, name));
                    Directory.Delete(stage);
                }
            }
        }
    }
}
