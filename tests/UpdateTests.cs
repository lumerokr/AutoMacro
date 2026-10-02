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
        static readonly string FutureVersion = new Version(new Version(AppInfo.Version).Major, new Version(AppInfo.Version).Minor + 1, 0).ToString();
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
            Check(release.Notes == "", "old release without notes accepted");
            root["body"] = "## Changes\n- Unicode: 한국어 / 日本語\nhttps://example.com";
            Check(UpdateService.ParseRelease(serializer.Serialize(root)).Notes == (string)root["body"], "release notes remain plain text and preserve Unicode");
            root["body"] = 123; Reject(delegate { UpdateService.ParseRelease(serializer.Serialize(root)); }, "non-text release notes rejected");
            root["body"] = null; Check(UpdateService.ParseRelease(serializer.Serialize(root)).Notes == "", "null release notes accepted");
            ReleaseUpdate installed = new ReleaseUpdate { Version = UpdateService.ParseVersion(AppInfo.Version), Tag = "v" + AppInfo.Version, Notes = "# Updated\n한국어 日本語" };
            Check(UpdateService.ParseNotesJson(UpdateService.NotesJson(installed)).Notes == installed.Notes, "cached notes round trip");
            Reject(delegate { UpdateService.ParseNotesJson(UpdateService.NotesJson(new ReleaseUpdate { Version = new Version(FutureVersion), Notes = "wrong release" })); }, "cache version must match installed executable");
            Reject(delegate { UpdateService.ParseNotesJson("{}"); }, "broken cached notes rejected");
            foreach (string title in new string[] { "### 다운로드", "## 안내", "## Downloads", "### Notice", "## downloads ##", "다운로드", "Notice", "## ダウンロード" })
                Check(UpdateNotesDialog.ChangesOnly("# Changes\n- Fixed input\n\n" + title + "\nhidden download or notice").Replace("\r", "") == "# Changes\n- Fixed input", "notes cutoff: " + title);
            Check(UpdateNotesDialog.ChangesOnly("- Downloads were fixed\n## Notice handling\nKeep this").Contains("Keep this"), "cutoff matches section titles only");
            Check(UpdateNotesDialog.ChangesOnly("```text\n## Downloads\n```\nKeep this\n## Notice\nhidden").Contains("Keep this"), "code blocks do not trigger cutoff");
            Check(UpdateNotesDialog.ChangesOnly(null) == "" && UpdateNotesDialog.ChangesOnly("## Downloads\nasset links") == "", "empty changelog handled");
            string bilingual = "# 한국어\n\n### Auto Macro 1.1.0\n\n### 업데이트\n\n- 한국어 변경 내역\n\n### 다운로드\n- 파일\n### 안내\n- 안내 문구\n\n# English\n\n### Auto Macro 1.1.0\n\n### Update\n\n- English changes\n\n### Downloads\n- assets\n### Notice\n- notice text";
            string koreanChanges = UpdateNotesDialog.ChangesOnly(bilingual, "ko"), englishChanges = UpdateNotesDialog.ChangesOnly(bilingual, "en");
            Check(koreanChanges.Contains("한국어 변경 내역") && !koreanChanges.Contains("다운로드") && !koreanChanges.Contains("English"), "Korean release section only");
            Check(englishChanges.Contains("English changes") && !englishChanges.Contains("Downloads") && !englishChanges.Contains("한국어"), "English release section only");
            Check(UpdateNotesDialog.ChangesOnly(bilingual, "ja") == englishChanges, "missing Japanese notes fall back to English");
            Check(UpdateNotesDialog.ChangesOnly(bilingual + "\n# 日本語\n### 更新\n- 日本語の変更\n### ダウンロード\n- ファイル", "ja").Contains("日本語の変更"), "Japanese section supported when provided");
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
                Check(UpdateInstaller.ReadStartupNotes(stage) == null, "older installer has no cached notes");
                string notesPath = Path.Combine(stage, "release-notes.json");
                File.WriteAllText(notesPath, UpdateService.NotesJson(new ReleaseUpdate { Version = new Version(AppInfo.Version), Notes = "cached before ready" }));
                ReleaseUpdate cached = UpdateInstaller.ReadStartupNotes(stage);
                Check(cached != null && cached.Notes == "cached before ready", "cache read before helper cleanup");
                File.WriteAllText(notesPath, "broken"); Check(UpdateInstaller.ReadStartupNotes(stage) == null, "broken cache falls back to fetch without failing startup");
                File.Delete(notesPath); Check(cached.Notes == "cached before ready", "in-memory notes survive cleanup");
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
                        dialog.ShowRelease(new ReleaseUpdate { Version = new Version(FutureVersion) }); Check(!install.Enabled, "missing asset cannot install");
                        dialog.ShowRelease(new ReleaseUpdate { Version = new Version(FutureVersion), Url = "test" }); Check(install.Enabled, "new update can install");
                        dialog.Close();
                    }
                    using (UpdateDialog notice = new UpdateDialog(new ReleaseUpdate { Version = new Version(FutureVersion), Url = "test" }, true))
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
                    using (UpdateNotesDialog notes = new UpdateNotesDialog(new ReleaseUpdate { Version = new Version(AppInfo.Version), Notes = "# Unicode\n한국어 日本語" }, delegate { throw new Exception("Cached notes must not use the network"); }))
                    {
                        notes.Show(); Application.DoEvents();
                        Check(notes.Text == L.T("업데이트 완료"), "localized release notes title");
                        RichTextBox text = null; Button close = null;
                        foreach (Control control in notes.Controls) { if (control is RichTextBox) text = (RichTextBox)control; if (control is Button && control.Text == L.T("닫기")) close = (Button)control; }
                        Check(text != null && text.ReadOnly && !text.DetectUrls && text.Text.Contains("한국어 日本語") && text.BackColor == Theme.Background, "read-only themed cached release notes");
                        Check(close != null, "release notes can be dismissed"); close.PerformClick();
                    }
                }
            }
            finally { L.Current = language; }
            VerifyNotesFetch();
        }
        static void VerifyNotesFetch()
        {
            int calls = 0;
            using (UpdateNotesDialog notes = new UpdateNotesDialog(null, delegate(CancellationToken token)
            {
                if (Interlocked.Increment(ref calls) == 1) throw new UpdateFailure(UpdateService.Network);
                return new ReleaseUpdate { Version = new Version(AppInfo.Version), Notes = "fetched release notes" };
            }))
            {
                notes.Show(); Button retry = null; RichTextBox text = null;
                foreach (Control control in notes.Controls) { if (control is Button && control.Text == L.T("다시 시도")) retry = (Button)control; if (control is RichTextBox) text = (RichTextBox)control; }
                Check(retry != null && text != null, "retry and notes controls present");
                PumpUntil(delegate { return retry.Visible && retry.Enabled; }, "network failure allows retry");
                retry.PerformClick(); PumpUntil(delegate { return text.Text == "fetched release notes"; }, "notes retry succeeds");
                Check(calls == 2 && !retry.Visible, "successful retry displays installed version notes"); notes.Close();
            }
            int entered = 0, cancelled = 0;
            using (UpdateNotesDialog notes = new UpdateNotesDialog(null, delegate(CancellationToken token)
            {
                Interlocked.Exchange(ref entered, 1);
                token.WaitHandle.WaitOne(2500);
                if (token.IsCancellationRequested) Interlocked.Exchange(ref cancelled, 1);
                token.ThrowIfCancellationRequested(); return null;
            }))
            {
                notes.Show(); PumpUntil(delegate { return entered == 1; }, "notes fetch begins"); notes.Close();
                PumpUntil(delegate { return cancelled == 1; }, "closing notes cancels network request");
            }
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
                        return new ReleaseUpdate { Version = new Version(scenario == 1 ? "0.9.0" : scenario == 2 ? AppInfo.Version : FutureVersion) };
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
                try { token.WaitHandle.WaitOne(2500); token.ThrowIfCancellationRequested(); return new ReleaseUpdate { Version = new Version(FutureVersion) }; }
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
