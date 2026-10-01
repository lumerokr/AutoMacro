using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Threading;

namespace AutoMacro
{
    internal static class UpdateTests
    {
        static void Check(bool value, string message) { if (!value) throw new Exception("Update test: " + message); }
        static void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch (UpdateFailure) { rejected = true; } Check(rejected, name); }
        static Dictionary<string, object> Asset(string tag)
        {
            return new Dictionary<string, object> {
                { "name", UpdateService.AssetName }, { "state", "uploaded" }, { "size", 1234 },
                { "digest", "sha256:" + new string('a', 64) },
                { "browser_download_url", "https://github.com/lumerokr/AutoMacro/releases/download/" + tag + "/" + UpdateService.AssetName }
            };
        }
        internal static void Run()
        {
            Check(UpdateService.ParseVersion("v1.10.0") > UpdateService.ParseVersion("1.9.0"), "numeric version comparison");
            foreach (string value in new string[] { null, "1.0", "1.0.0-beta", "1.0.0.1", "-1.0.0", "01.0.0", "99999.0.0" })
                Reject(delegate { UpdateService.ParseVersion(value); }, "invalid version " + value);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            var asset = Asset("v1.1.0"); var root = new Dictionary<string, object> { { "tag_name", "v1.1.0" }, { "draft", false }, { "prerelease", false }, { "assets", new object[] { asset } } };
            ReleaseUpdate release = UpdateService.ParseRelease(serializer.Serialize(root));
            Check(release.Version.ToString(3) == "1.1.0" && release.Url != null, "release asset accepted");
            root["draft"] = true; Reject(delegate { UpdateService.ParseRelease(serializer.Serialize(root)); }, "draft excluded"); root["draft"] = false;
            root["prerelease"] = true; Reject(delegate { UpdateService.ParseRelease(serializer.Serialize(root)); }, "prerelease excluded"); root["prerelease"] = false;
            root["assets"] = new object[] { asset, asset }; Reject(delegate { UpdateService.ParseRelease(serializer.Serialize(root)); }, "duplicate asset rejected"); root["assets"] = new object[] { asset };
            asset["browser_download_url"] = "https://example.com/program.zip"; Reject(delegate { UpdateService.ParseRelease(serializer.Serialize(root)); }, "foreign download rejected");
            asset = Asset("v1.1.0"); root["assets"] = new object[] { asset };
            asset["digest"] = null; Reject(delegate { UpdateService.ParseRelease(serializer.Serialize(root)); }, "missing checksum rejected");
            asset["digest"] = "sha256:" + new string('a', 64); asset["size"] = UpdateService.MaxBytes + 1;
            Reject(delegate { UpdateService.ParseRelease(serializer.Serialize(root)); }, "oversized asset rejected");
            root["assets"] = new object[0]; Check(UpdateService.ParseRelease(serializer.Serialize(root)).Url == null, "release without asset readable");
            Reject(delegate { UpdateService.ParseRelease("{}"); }, "unrelated JSON rejected");

            string folder = Path.Combine(Path.GetTempPath(), "AutoMacro-update-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try
            {
                string zipPath = Path.Combine(folder, "update.zip"), payload = Path.Combine(folder, "payload.exe");
                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) zip.CreateEntryFromFile(typeof(AppInfo).Assembly.Location, "Auto Macro.exe");
                release = new ReleaseUpdate { Size = new FileInfo(zipPath).Length, Digest = UpdateService.Hash(zipPath), Version = UpdateService.ParseVersion(AppInfo.Version) };
                UpdateService.Extract(zipPath, payload, release); Check(UpdateService.Hash(payload) == UpdateService.Hash(typeof(AppInfo).Assembly.Location), "verified executable extracted"); File.Delete(payload);
                release.Digest = new string('0', 64); Reject(delegate { UpdateService.Extract(zipPath, payload, release); }, "corrupt download rejected");
                Check(!File.Exists(payload), "no extraction before checksum validation"); release.Digest = UpdateService.Hash(zipPath);
                release.Version = new Version("99.0.0"); Reject(delegate { UpdateService.Extract(zipPath, payload, release); }, "executable version mismatch"); File.Delete(payload);
                File.Delete(zipPath);
                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) zip.CreateEntryFromFile(typeof(AppInfo).Assembly.Location, "../Auto Macro.exe");
                release.Size = new FileInfo(zipPath).Length; release.Digest = UpdateService.Hash(zipPath);
                Reject(delegate { UpdateService.Extract(zipPath, payload, release); }, "zip traversal rejected"); Check(!File.Exists(payload), "traversal writes nothing");
                File.Delete(zipPath);
                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) { zip.CreateEntryFromFile(typeof(AppInfo).Assembly.Location, "Auto Macro.exe"); zip.CreateEntry("settings.json"); }
                release.Size = new FileInfo(zipPath).Length; release.Digest = UpdateService.Hash(zipPath);
                Reject(delegate { UpdateService.Extract(zipPath, payload, release); }, "user data entry rejected");

                string target = Path.Combine(folder, "program.exe"), backup = Path.Combine(folder, "previous.exe");
                File.WriteAllText(target, "old executable"); File.WriteAllText(payload, "new executable");
                string settings = Path.Combine(folder, "settings.json"), library = Path.Combine(folder, "MacroLibrary.json");
                File.WriteAllText(settings, "test preferences"); File.WriteAllText(library, "test saved macros");
                string settingsHash = UpdateService.Hash(settings), libraryHash = UpdateService.Hash(library);
                bool failed = false;
                try { UpdateService.ReplaceAndStart(payload, target, backup, delegate
                { UpdateInstaller.WaitForStartup(folder, delegate { return true; }, delegate { }, 50); }); } catch (IOException) { failed = true; }
                Check(failed && File.ReadAllText(target) == "old executable", "failed launch rolls back executable");
                Check(!File.Exists(backup), "rollback consumes backup");
                File.WriteAllText(payload, "new executable"); bool started = false;
                UpdateService.ReplaceAndStart(payload, target, backup, delegate { started = true; });
                Check(started && File.ReadAllText(target) == "new executable" && File.ReadAllText(backup) == "old executable", "replacement and backup succeed");
                Check(UpdateService.Hash(settings) == settingsHash && UpdateService.Hash(library) == libraryHash, "user data preserved during replacement and rollback");
                string awaiting = Path.Combine(folder, "awaiting-user"), ready = Path.Combine(folder, "ready");
                File.WriteAllText(awaiting, "");
                Thread user = new Thread(delegate()
                {
                    Thread.Sleep(200); File.WriteAllText(ready, "test-ready"); File.Delete(awaiting);
                });
                user.Start();
                try { UpdateInstaller.WaitForStartup(folder, delegate { return false; }, delegate { throw new Exception("User notice was incorrectly timed out"); }, 50); }
                finally { user.Join(); }
                Check(File.Exists(backup), "pending notice preserves previous version backup"); File.Delete(ready);
                bool terminated = false; failed = false;
                try { UpdateInstaller.WaitForStartup(folder, delegate { return false; }, delegate { terminated = true; }, 20); } catch (IOException) { failed = true; }
                Check(failed && terminated, "unresponsive startup still times out");
                File.WriteAllText(awaiting, ""); failed = false;
                try { UpdateInstaller.WaitForStartup(folder, delegate { return true; }, delegate { }, 50); } catch (IOException) { failed = true; }
                Check(failed, "exiting while user notice is open is a failed update");
            }
            finally { Directory.Delete(folder, true); }
        }
        internal static void RunUI()
        {
            string stage = Path.Combine(Path.GetDirectoryName(typeof(AppInfo).Assembly.Location), ".automacro-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                File.WriteAllText(Path.Combine(stage, "previous.exe"), "startup marker test");
                UpdateInstaller.SetAwaitingUser(stage, true);
                Check(File.Exists(Path.Combine(stage, "awaiting-user")) && !File.Exists(Path.Combine(stage, "ready")), "user notice does not confirm installation");
                UpdateInstaller.SetAwaitingUser(stage, false);
                using (MainForm form = new MainForm(false))
                {
                    Program.AfterMainShown(form, delegate { UpdateInstaller.SignalReady(stage); });
                    Check(!File.Exists(Path.Combine(stage, "ready")), "ready marker absent before main startup");
                    form.Shown += delegate { Check(!File.Exists(Path.Combine(stage, "ready")), "ready marker absent during main initialization"); };
                    form.Show(); PumpUntil(delegate { return File.Exists(Path.Combine(stage, "ready")); }, "real ready marker");
                    Check(File.ReadAllText(Path.Combine(stage, "ready")) == AppInfo.Version && File.Exists(Path.Combine(stage, "previous.exe")), "shown main signals current version while backup remains intact"); form.Close();
                }
                using (Form form = new Form())
                {
                    Program.AfterMainShown(form, delegate { throw new IOException("Simulated handshake failure"); });
                    form.Show(); PumpUntil(delegate { return form.IsDisposed; }, "failed handshake closes new application");
                }
                Reject(delegate { UpdateInstaller.SignalReady(Path.Combine(stage, "invalid")); }, "invalid handshake location rejected");
            }
            finally
            {
                foreach (string name in new string[] { "previous.exe", "awaiting-user", "ready" }) File.Delete(Path.Combine(stage, name));
                Directory.Delete(stage);
            }
            using (MainForm form = new MainForm(false))
            {
                bool ready = false;
                Program.AfterMainShown(form, delegate { Check(form.Visible && form.IsHandleCreated, "ready requires visible main window"); ready = true; });
                Check(!ready, "construction does not confirm update");
                form.Shown += delegate { Check(!ready, "Shown initialization precedes ready callback"); };
                form.Show(); PumpUntil(delegate { return ready; }, "main startup ready callback"); form.Close();
            }
            VerifyStartupChecks();
            string language = L.Current;
            try
            {
                foreach (string value in new string[] { "ko", "en", "ja" })
                {
                    L.Current = value;
                    using (UpdateDialog dialog = new UpdateDialog())
                    {
                        dialog.Show(); Application.DoEvents(); Check(dialog.Text == L.T("업데이트"), "localized update title");
                        Button install = null; foreach (Control control in dialog.Controls) if (control is Button && control.Text == L.T("최신 버전으로 업데이트")) install = (Button)control;
                        Check(install != null && !install.Enabled, "install disabled before check");
                        dialog.ShowRelease(null); Check(!install.Enabled, "no release cannot install");
                        dialog.ShowRelease(new ReleaseUpdate { Version = new Version(AppInfo.Version), Url = "test" }); Check(!install.Enabled, "current version cannot install");
                        dialog.ShowRelease(new ReleaseUpdate { Version = new Version("0.9.0"), Url = "test" }); Check(!install.Enabled, "cannot downgrade");
                        dialog.ShowRelease(new ReleaseUpdate { Version = new Version("1.1.0") }); Check(!install.Enabled, "missing asset cannot install");
                        dialog.ShowRelease(new ReleaseUpdate { Version = new Version("1.1.0"), Url = "test" }); Check(install.Enabled, "new update can install");
                        dialog.Close();
                    }
                    using (UpdateDialog notice = new UpdateDialog(new ReleaseUpdate { Version = new Version("1.1.0"), Url = "test" }, true))
                    {
                        notice.Show(); Application.DoEvents(); Button later = null, install = null;
                        foreach (Control control in notice.Controls)
                        {
                            if (control is Button && control.Text == L.T("나중에")) later = (Button)control;
                            if (control is Button && control.Text == L.T("최신 버전으로 업데이트")) install = (Button)control;
                        }
                        Check(notice.Text == L.T("새 버전 감지") && install != null && install.Enabled, "startup notice has enabled update button in all languages");
                        Check(later != null, "startup notice has later button"); later.PerformClick();
                        Check(notice.DialogResult == DialogResult.Cancel && notice.Prepared == null, "later closes without preparing update");
                    }
                }
            }
            finally { L.Current = language; }
        }
        static void PumpUntil(Func<bool> condition, string name)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!condition()) { Application.DoEvents(); Thread.Sleep(5); if (clock.ElapsedMilliseconds > 3000) throw new Exception("Update UI test timed out: " + name); }
            Application.DoEvents();
        }
        static void PumpFor(int milliseconds)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); }
        }
        static void VerifyStartupChecks()
        {
            foreach (int scenario in new int[] { 0, 1, 2, 3, 4 })
            {
                int checks = 0, notices = 0, finished = 0; bool idle = false;
                using (Form owner = new Form())
                using (StartupUpdateCheck watcher = new StartupUpdateCheck(owner, delegate { return idle; }, delegate(ReleaseUpdate found) { notices++; }, delegate(CancellationToken token)
                {
                    Interlocked.Increment(ref checks);
                    try
                    {
                        if (scenario == 4) throw new UpdateFailure(UpdateService.Network);
                        if (scenario == 0) return null;
                        return new ReleaseUpdate { Version = new Version(scenario == 1 ? "0.9.0" : scenario == 2 ? AppInfo.Version : "1.1.0") };
                    }
                    finally { Interlocked.Exchange(ref finished, 1); }
                }))
                {
                    PumpFor(30); Check(checks == 0 && notices == 0, "no background check before main window shown");
                    owner.Show(); PumpUntil(delegate { return finished == 1; }, "startup check completes"); PumpFor(80);
                    Check(checks == 1 && notices == 0, "startup check once and does not interrupt busy UI");
                    idle = true; PumpFor(350);
                    Check(notices == (scenario == 3 ? 1 : 0), "only strictly newer version notifies; missing, older, current, network failure silent");
                    PumpFor(350); Check(checks == 1 && notices <= 1, "no repeat check or notification during session"); owner.Close();
                }
            }
            int entered = 0, completed = 0, lateNotices = 0;
            using (Form owner = new Form())
            using (StartupUpdateCheck watcher = new StartupUpdateCheck(owner, delegate { return true; }, delegate { lateNotices++; }, delegate(CancellationToken token)
            {
                Interlocked.Exchange(ref entered, 1);
                try { token.WaitHandle.WaitOne(2500); token.ThrowIfCancellationRequested(); return new ReleaseUpdate { Version = new Version("1.1.0") }; }
                finally { Interlocked.Exchange(ref completed, 1); }
            }))
            {
                owner.Show(); PumpUntil(delegate { return entered == 1; }, "slow background check starts"); owner.Close();
                PumpUntil(delegate { return completed == 1; }, "closing cancels startup check"); PumpFor(50);
                Check(lateNotices == 0, "no notification after main window closes");
            }
        }
    }
}
