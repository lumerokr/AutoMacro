using System;
using System.IO;

[assembly: System.Reflection.AssemblyVersion(AutoMacro.AppInfo.AssemblyVersion)]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AutoMacro.Tests")]
namespace AutoMacro
{
    internal static class AppInfo
    {
        internal const string AssemblyVersion = "1.0.2.0";
        internal static string Version { get { return typeof(AppInfo).Assembly.GetName().Version.ToString(3); } }
        internal static string TestDataDirectory;
        internal static string DataDirectory { get { return TestDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoMacro"); } }
        internal static string SettingsPath { get { return Path.Combine(DataDirectory, "settings.json"); } }
        internal static string LibraryPath { get { return Path.Combine(DataDirectory, "MacroLibrary.json"); } }
        internal static void InitializeDataDirectory() { Directory.CreateDirectory(DataDirectory); }
    }
    internal static class RecoveryFiles
    {
        internal static string Preserve(string path)
        {
            if (!File.Exists(path)) return null;
            string backup = path + ".recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bak";
            File.Copy(path, backup, false); return backup;
        }
    }
}
