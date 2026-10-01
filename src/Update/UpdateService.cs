using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace AutoMacro
{
    internal sealed class UpdateFailure : Exception
    {
        internal UpdateFailure(string message) : base(message) { }
    }
    internal sealed class ReleaseUpdate
    {
        internal Version Version;
        internal string Tag, Url, Digest;
        internal long Size;
    }
    internal sealed class PreparedUpdate
    {
        internal string Directory, Target, Payload, Helper, Hash, Language;
        internal Version Version;
        internal void Discard() { UpdateService.Clean(Directory); }
    }
    internal static class UpdateService
    {
        internal const string AssetName = "AutoMacro-update.zip";
        internal const long MaxBytes = 64 * 1024 * 1024;
        internal const string Invalid = "업데이트 파일이 올바르지 않습니다. 기존 프로그램은 유지됩니다.";
        internal const string Network = "업데이트 서버에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 시도하세요.";
        internal const string Storage = "업데이트를 준비하지 못했습니다. 폴더 권한과 여유 공간을 확인하세요.";
        const string Endpoint = "https://api.github.com/repos/lumerokr/AutoMacro/releases/latest";

        internal static Version ParseVersion(string text)
        {
            if (text == null || !Regex.IsMatch(text, @"^v?(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$")) throw new UpdateFailure(Invalid);
            Version version = new Version(text.TrimStart('v'));
            if (version.Major > 65534 || version.Minor > 65534 || version.Build > 65534) throw new UpdateFailure(Invalid);
            return version;
        }
        internal static ReleaseUpdate ParseRelease(string json)
        {
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null || !(root["draft"] is bool) || (bool)root["draft"] || !(root["prerelease"] is bool) || (bool)root["prerelease"]) throw new UpdateFailure(Invalid);
                string tag = root["tag_name"] as string; Version version = ParseVersion(tag);
                IList assets = root["assets"] as IList;
                ReleaseUpdate result = new ReleaseUpdate { Version = version, Tag = tag };
                if (assets == null) throw new UpdateFailure(Invalid);
                foreach (object item in assets)
                {
                    var asset = item as Dictionary<string, object>;
                    if (asset == null) throw new UpdateFailure(Invalid);
                    if ((asset["name"] as string) != AssetName) continue;
                    if (result.Url != null) throw new UpdateFailure(Invalid);
                    string url = asset["browser_download_url"] as string, digest = asset["digest"] as string;
                    if (url != "https://github.com/lumerokr/AutoMacro/releases/download/" + Uri.EscapeDataString(tag) + "/" + AssetName || digest == null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new UpdateFailure(Invalid);
                    long size = Convert.ToInt64(asset["size"]);
                    if (size <= 0 || size > MaxBytes || (asset["state"] as string) != "uploaded") throw new UpdateFailure(Invalid);
                    result.Url = url; result.Digest = digest.Substring(7).ToLowerInvariant(); result.Size = size;
                }
                // A release without an update asset can still display its version.
                return result;
            }
            catch (UpdateFailure) { throw; }
            catch (Exception) { throw new UpdateFailure(Invalid); }
        }
        internal static ReleaseUpdate Check(CancellationToken cancel)
        {
            try
            {
                using (MemoryStream output = new MemoryStream())
                {
                    if (!Fetch(Endpoint, output, 1024 * 1024, 0, cancel, null, true)) return null;
                    return ParseRelease(Encoding.UTF8.GetString(output.ToArray()));
                }
            }
            catch (UpdateFailure) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { throw new UpdateFailure(Network); }
        }
        static bool AllowedUrl(Uri uri)
        {
            return uri.Scheme == "https" && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Host == "api.github.com" || uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
        }
        static bool Fetch(string address, Stream destination, long limit, long expected, CancellationToken cancel, Action<int> progress, bool allowMissing)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 on .NET Framework.
            Uri uri = new Uri(address);
            for (int redirect = 0; redirect < 6; redirect++)
            {
                cancel.ThrowIfCancellationRequested();
                if (!AllowedUrl(uri)) throw new UpdateFailure(Invalid);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "AutoMacro/" + AppInfo.Version; request.Accept = "application/vnd.github+json";
                request.Headers["X-GitHub-Api-Version"] = "2026-03-10";
                request.AllowAutoRedirect = false; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
                using (cancel.Register(request.Abort))
                {
                    HttpWebResponse response;
                    try { response = (HttpWebResponse)request.GetResponse(); }
                    catch (WebException error)
                    {
                        cancel.ThrowIfCancellationRequested();
                        using (HttpWebResponse failed = error.Response as HttpWebResponse)
                        { if (allowMissing && failed != null && failed.StatusCode == HttpStatusCode.NotFound) return false; }
                        throw new UpdateFailure(Network);
                    }
                    using (response)
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        { uri = new Uri(uri, response.Headers["Location"]); continue; }
                        if (response.StatusCode != HttpStatusCode.OK) throw new UpdateFailure(Network);
                        if (response.ContentLength > limit || (expected > 0 && response.ContentLength >= 0 && response.ContentLength != expected)) throw new UpdateFailure(Invalid);
                        using (Stream input = response.GetResponseStream())
                        {
                            byte[] buffer = new byte[32768]; long count = 0; int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancel.ThrowIfCancellationRequested(); count += read;
                                if (count > limit || (expected > 0 && count > expected)) throw new UpdateFailure(Invalid);
                                destination.Write(buffer, 0, read);
                                if (progress != null && expected > 0) progress((int)(count * 100 / expected));
                            }
                            if (expected > 0 && count != expected) throw new UpdateFailure(Invalid);
                        }
                        return true;
                    }
                }
            }
            throw new UpdateFailure(Network);
        }
        internal static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create()) using (Stream input = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        internal static void ValidateExecutable(string path, Version version)
        {
            try
            {
                AssemblyName name = AssemblyName.GetAssemblyName(path);
                if (name.Name != typeof(AppInfo).Assembly.GetName().Name || name.Version != new Version(version.Major, version.Minor, version.Build, 0)) throw new UpdateFailure(Invalid);
            }
            catch (UpdateFailure) { throw; }
            catch (Exception) { throw new UpdateFailure(Invalid); }
        }
        internal static void Extract(string zipPath, string output, ReleaseUpdate release)
        {
            if (new FileInfo(zipPath).Length != release.Size || Hash(zipPath) != release.Digest) throw new UpdateFailure(Invalid);
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    if (zip.Entries.Count != 1) throw new UpdateFailure(Invalid);
                    ZipArchiveEntry entry = zip.Entries[0];
                    if (entry.FullName != "Auto Macro.exe" || entry.Length <= 0 || entry.Length > MaxBytes) throw new UpdateFailure(Invalid);
                    using (Stream input = entry.Open()) using (Stream file = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                    {
                        byte[] buffer = new byte[32768]; long total = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        { total += read; if (total > MaxBytes || total > entry.Length) throw new UpdateFailure(Invalid); file.Write(buffer, 0, read); }
                        if (total != entry.Length) throw new UpdateFailure(Invalid);
                    }
                }
                ValidateExecutable(output, release.Version);
            }
            catch (UpdateFailure) { throw; }
            catch (InvalidDataException) { throw new UpdateFailure(Invalid); }
        }
        internal static PreparedUpdate Prepare(ReleaseUpdate release, CancellationToken cancel, Action<int> progress)
        {
            if (release == null || release.Url == null || release.Version <= ParseVersion(AppInfo.Version)) throw new UpdateFailure(Invalid);
            string target = typeof(AppInfo).Assembly.Location;
            string folder = Path.Combine(Path.GetDirectoryName(target), ".automacro-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                string zip = Path.Combine(folder, AssetName), payload = Path.Combine(folder, "payload.exe");
                using (Stream file = new FileStream(zip, FileMode.CreateNew, FileAccess.Write)) Fetch(release.Url, file, MaxBytes, release.Size, cancel, progress, false);
                cancel.ThrowIfCancellationRequested(); Extract(zip, payload, release); cancel.ThrowIfCancellationRequested();
                string helper = Path.Combine(folder, "installer.exe"); File.Copy(target, helper, false);
                return new PreparedUpdate { Directory = folder, Target = target, Payload = payload, Helper = helper, Version = release.Version, Hash = Hash(payload), Language = L.Current };
            }
            catch (OperationCanceledException) { Clean(folder); throw; }
            catch (UpdateFailure) { Clean(folder); throw; }
            catch (WebException) { Clean(folder); throw new UpdateFailure(Network); }
            catch (Exception) { Clean(folder); throw new UpdateFailure(Storage); }
        }
        internal static void Clean(string folder)
        {
            // Only a private updater staging directory may be removed.
            if (folder == null || !Regex.IsMatch(Path.GetFileName(folder), @"^\.automacro-update-[a-f0-9]{32}$")) return;
            try
            {
                foreach (string name in new string[] { AssetName, "payload.exe", "installer.exe", "ready", "awaiting-user" })
                { string path = Path.Combine(folder, name); if (File.Exists(path)) File.Delete(path); }
                Directory.Delete(folder, false);
            }
            catch { /* A running helper is cleaned by the next application launch. */ }
        }
        internal static string Quote(string path) { return "\"" + path.Replace("\"", "") + "\""; }
        internal static Process LaunchHelper(PreparedUpdate pending)
        {
            return Process.Start(new ProcessStartInfo(pending.Helper, "--apply-update " + Process.GetCurrentProcess().Id + " " + Quote(pending.Target) + " " + pending.Version.ToString(3) + " " + pending.Hash + " " + pending.Language) { UseShellExecute = false, CreateNoWindow = true });
        }
        // File.Replace is atomic and only touches the executable, never JSON data.
        internal static void ReplaceAndStart(string payload, string target, string backup, Action start)
        {
            File.Replace(payload, target, backup);
            try { start(); }
            catch
            {
                File.Replace(backup, target, null);
                throw;
            }
        }
    }
}
