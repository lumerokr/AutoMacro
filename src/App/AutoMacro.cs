using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
[assembly: System.Reflection.AssemblyTitle("Auto Macro")]
[assembly: System.Reflection.AssemblyProduct("Auto Macro")]

namespace AutoMacro
{
    internal static class Program
    {
        internal static void AfterMainShown(Form form, Action ready)
        {
            form.Shown += delegate
            {
                form.BeginInvoke((Action)delegate
                {
                    if (form.IsDisposed || form.Disposing || !form.Visible) return;
                    try { ready(); } catch { form.Close(); }
                });
            };
        }
        static bool PrepareStartup(string updateFolder)
        {
            if (updateFolder != null) UpdateInstaller.SetAwaitingUser(updateFolder, true);
            try
            {
                AppInfo.InitializeDataDirectory();
                if (!SettingsRecoveryDialog.AllowStartup(AppInfo.SettingsPath)) return false;
                L.Initialize(AppInfo.SettingsPath);
                return StartupNotice.AllowStartup(AppInfo.SettingsPath);
            }
            finally { if (updateFolder != null) UpdateInstaller.SetAwaitingUser(updateFolder, false); }
        }
        static void RunMain(MainForm form)
        {
            // Attach only after the usage notice has been accepted.
            using (StartupUpdateCheck updates = new StartupUpdateCheck(form, delegate { return form.CanNotifyUpdate; }, form.ShowStartupUpdate))
                Application.Run(form);
        }
        [STAThread] static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--apply-update") return UpdateInstaller.Run(args);
            bool created;
            L.Initialize(AppInfo.SettingsPath);
            using (Mutex mutex = new Mutex(true, "Local\\SimpleAutoMacroApp", out created))
            {
                if (!created) { MessageBox.Show(L.T("Auto Macro가 이미 실행 중입니다."), "Auto Macro"); return 0; }
                if (args.Length == 2 && args[0] == "--update-ready")
                {
                    try { if (!PrepareStartup(args[1])) return 0; } catch { return 1; }
                    // Confirm only after OnShown initialization and the first UI message turn.
                    using (MainForm updated = new MainForm())
                    {
                        AfterMainShown(updated, delegate
                        {
                            ReleaseUpdate notes = UpdateInstaller.ReadStartupNotes(args[1]);
                            // Confirm startup before a user can leave the notes dialog open.
                            UpdateInstaller.SignalReady(args[1]);
                            updated.BeginInvoke((Action)delegate { if (!updated.IsDisposed) updated.ShowUpdateNotes(notes); });
                        });
                        RunMain(updated);
                    }
                }
                else
                {
                    UpdateInstaller.CleanCompleted();
                    try { if (!PrepareStartup(null)) return 0; }
                    catch { MessageBox.Show(L.T(UpdateService.Storage) + "\n\n" + AppInfo.DataDirectory, "Auto Macro", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                    RunMain(new MainForm());
                }
            }
            UpdateInstaller.StartPending();
            return 0;
        }
    }
}
