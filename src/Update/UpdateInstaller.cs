using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class UpdateInstaller
    {
        internal static PreparedUpdate Pending;
        internal static void StartPending()
        {
            if (Pending == null) return;
            try { using (Process helper = UpdateService.LaunchHelper(Pending)) { if (helper == null) throw new IOException(); } }
            catch
            {
                Pending.Discard();
                MessageBox.Show(L.T("업데이트 설치를 시작하지 못했습니다. 기존 프로그램을 다시 실행해주세요."), "Auto Macro");
            }
        }
        internal static int Run(string[] args)
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string backup = Path.Combine(folder, "previous.exe"); string target = null;
            bool installed = false, restored = false;
            L.Current = args.Length == 6 ? L.Supported(args[5]) : L.WindowsDefault();
            try
            {
                if (args.Length != 6) throw new UpdateFailure(UpdateService.Invalid);
                int parentId; if (!Int32.TryParse(args[1], out parentId) || parentId <= 0 || parentId == Process.GetCurrentProcess().Id) throw new UpdateFailure(UpdateService.Invalid);
                target = Path.GetFullPath(args[2]); Version version = UpdateService.ParseVersion(args[3]);
                if (version <= UpdateService.ParseVersion(AppInfo.Version)) throw new UpdateFailure(UpdateService.Invalid);
                string payload = Path.Combine(folder, "payload.exe");
                if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder), @"^\.automacro-update-[a-f0-9]{32}$") ||
                    !String.Equals(Path.GetDirectoryName(folder), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(Path.GetFileName(typeof(AppInfo).Assembly.Location), "installer.exe", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(target) || File.Exists(backup) || UpdateService.Hash(payload) != args[4]) throw new UpdateFailure(UpdateService.Invalid);
                UpdateService.ValidateExecutable(payload, version);
                UpdateService.ValidateExecutable(target, UpdateService.ParseVersion(AppInfo.Version));
                try
                {
                    using (Process parent = Process.GetProcessById(parentId))
                    {
                        if (!parent.HasExited)
                        {
                            try
                            {
                                if (!String.Equals(parent.MainModule.FileName, target, StringComparison.OrdinalIgnoreCase)) throw new UpdateFailure(UpdateService.Invalid);
                            }
                            catch (InvalidOperationException) { if (!parent.HasExited) throw; }
                            catch (System.ComponentModel.Win32Exception) { if (!parent.HasExited) throw; }
                        }
                        if (!parent.WaitForExit(60000)) throw new UpdateFailure("프로그램이 종료되지 않아 업데이트를 취소했습니다.");
                    }
                }
                catch (ArgumentException) { /* The old process has already exited. */ }
                string ready = Path.Combine(folder, "ready");
                if (File.Exists(ready)) throw new UpdateFailure(UpdateService.Invalid);
                // A recently released image lock may take a moment to clear.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        UpdateService.ReplaceAndStart(payload, target, backup, delegate
                        {
                            installed = true;
                            using (Process next = Process.Start(new ProcessStartInfo(target, "--update-ready " + UpdateService.Quote(folder)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) }))
                            {
                                if (next == null) throw new IOException();
                                WaitForStartup(folder, delegate { return next.HasExited; }, delegate
                                { next.Kill(); if (!next.WaitForExit(5000)) throw new IOException(); }, 30000);
                            }
                        });
                        break;
                    }
                    catch (IOException) { if (installed || attempt >= 20) throw; Thread.Sleep(250); }
                }
                // Once startup is confirmed, cleanup failure must not roll back a running app.
                try { File.Delete(backup); } catch { }
                UpdateService.Clean(folder); return 0;
            }
            catch (Exception error)
            {
                if (installed && !File.Exists(backup)) restored = true;
                if (installed && target != null && File.Exists(backup))
                {
                    try { File.Replace(backup, target, null); restored = true; } catch { }
                }
                string text;
                if (installed && !restored)
                    text = L.T("업데이트 복구에 실패했습니다. 기존 실행 파일이 다음 위치에 보존되어 있습니다.") + "\n\n" + backup;
                else if (installed)
                    text = L.T("업데이트에 실패하여 기존 버전으로 복구했습니다. 프로그램을 다시 실행해주세요.");
                else text = L.T(error is UpdateFailure ? error.Message : UpdateService.Storage);
                MessageBox.Show(text, L.T("업데이트"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (!installed || restored) UpdateService.Clean(folder);
                return 1;
            }
        }
        internal static void WaitForStartup(string folder, Func<bool> exited, Action terminate, int timeoutMs)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(folder, "ready")))
            {
                if (exited()) throw new IOException();
                // Pause only for interactive startup; exiting still counts as failure.
                if (File.Exists(Path.Combine(folder, "awaiting-user"))) clock.Restart();
                else if (clock.ElapsedMilliseconds > timeoutMs) { terminate(); throw new IOException(); }
                Thread.Sleep(Math.Min(100, Math.Max(1, timeoutMs / 10)));
            }
        }
        static string StartupFolder(string folder)
        {
            string target = typeof(AppInfo).Assembly.Location;
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if (!String.Equals(Path.GetDirectoryName(full), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase) ||
                !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^\.automacro-update-[a-f0-9]{32}$") || !File.Exists(Path.Combine(full, "previous.exe"))) throw new UpdateFailure(UpdateService.Invalid);
            return full;
        }
        internal static void SetAwaitingUser(string folder, bool awaiting)
        {
            string marker = Path.Combine(StartupFolder(folder), "awaiting-user");
            if (awaiting) File.WriteAllText(marker, ""); else File.Delete(marker);
        }
        internal static void SignalReady(string folder)
        {
            string full = StartupFolder(folder);
            File.WriteAllText(Path.Combine(full, "ready"), AppInfo.Version);
            ThreadPool.QueueUserWorkItem(delegate { CleanAfterStartup(full, 120000); });
        }
        internal static void CleanAfterStartup(string folder, int timeoutMs)
        {
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if (!String.Equals(Path.GetDirectoryName(full), Path.GetDirectoryName(typeof(AppInfo).Assembly.Location), StringComparison.OrdinalIgnoreCase) ||
                !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^\.automacro-update-[a-f0-9]{32}$")) return;
            Stopwatch clock = Stopwatch.StartNew();
            while (Directory.Exists(full) && clock.ElapsedMilliseconds < timeoutMs)
            {
                // The old helper removes this backup only after accepting readiness.
                // Never remove readiness early or discard a failed update's backup.
                if (!File.Exists(Path.Combine(full, "previous.exe"))) UpdateService.Clean(full);
                if (Directory.Exists(full)) Thread.Sleep(250);
            }
        }
        internal static void CleanCompleted()
        {
            try
            {
                foreach (string folder in Directory.GetDirectories(AppDomain.CurrentDomain.BaseDirectory, ".automacro-update-*"))
                    // Keep backups from any incomplete or unsuccessful installation.
                    if (!File.Exists(Path.Combine(folder, "previous.exe"))) UpdateService.Clean(folder);
            }
            catch { }
        }
    }
}
