using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace NexusBuilder
{
    public static class PeMetadataPatcher
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

        private static int Pad4(int n) => (n + 3) & ~3;

        /// <summary>
        /// Updates the target managed DLL name in the .NET AppHost PE executable.
        /// In .NET 8 AppHost, the DLL name is stored as a UTF-8 null-terminated string
        /// inside a 1024-byte buffer placeholder.
        /// </summary>
        public static bool UpdateAppHostDllName(string exePath, string oldDllName, string newDllName)
        {
            try
            {
                if (!File.Exists(exePath)) return false;
                byte[] bytes = File.ReadAllBytes(exePath);

                byte[] oldBytes = Encoding.UTF8.GetBytes(oldDllName + "\0");
                byte[] newBytes = Encoding.UTF8.GetBytes(newDllName + "\0");

                int idx = IndexOfBytes(bytes, oldBytes);
                if (idx == -1)
                {
                    string contentAscii = Encoding.ASCII.GetString(bytes);
                    idx = contentAscii.IndexOf(oldDllName + "\0", StringComparison.OrdinalIgnoreCase);
                }

                if (idx == -1) return false;

                int clearLen = Math.Max(oldBytes.Length, newBytes.Length);
                for (int i = 0; i < clearLen + 32 && (idx + i) < bytes.Length; i++)
                {
                    bytes[idx + i] = 0;
                }

                Array.Copy(newBytes, 0, bytes, idx, newBytes.Length);
                File.WriteAllBytes(exePath, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a valid VS_VERSIONINFO resource buffer containing the custom
        /// application name, publisher/author, and version numbers.
        /// When Windows displays the UAC prompt (Run as Administrator), it reads FileDescription
        /// from this resource to display the program name.
        /// </summary>
        public static byte[] CreateVersionInfoResource(string appName, string appAuthor, string version)
        {
            var vParts = (version ?? "8.2.0.0").Split('.');
            ushort v1 = (ushort)(vParts.Length > 0 && ushort.TryParse(vParts[0], out var p1) ? p1 : 8);
            ushort v2 = (ushort)(vParts.Length > 1 && ushort.TryParse(vParts[1], out var p2) ? p2 : 2);
            ushort v3 = (ushort)(vParts.Length > 2 && ushort.TryParse(vParts[2], out var p3) ? p3 : 0);
            ushort v4 = (ushort)(vParts.Length > 3 && ushort.TryParse(vParts[3], out var p4) ? p4 : 0);

            byte[] MakeStr(string k, string v)
            {
                byte[] kb = Encoding.Unicode.GetBytes(k + "\0");
                byte[] vb = Encoding.Unicode.GetBytes((v ?? "") + "\0");
                int headLen = 6 + kb.Length;
                int padVal = Pad4(headLen) - headLen;
                int sLen = headLen + padVal + vb.Length;
                int padAfter = Pad4(sLen) - sLen;
                byte[] b = new byte[sLen + padAfter];
                BitConverter.GetBytes((ushort)sLen).CopyTo(b, 0);
                BitConverter.GetBytes((ushort)((v ?? "").Length + 1)).CopyTo(b, 2);
                BitConverter.GetBytes((ushort)1).CopyTo(b, 4);
                kb.CopyTo(b, 6);
                vb.CopyTo(b, headLen + padVal);
                return b;
            }

            using var msStrings = new MemoryStream();
            byte[] s1 = MakeStr("Comments", appName); msStrings.Write(s1, 0, s1.Length);
            byte[] s2 = MakeStr("CompanyName", appAuthor); msStrings.Write(s2, 0, s2.Length);
            byte[] s3 = MakeStr("FileDescription", appName); msStrings.Write(s3, 0, s3.Length);
            byte[] s4 = MakeStr("FileVersion", version ?? "8.2.0.0"); msStrings.Write(s4, 0, s4.Length);
            byte[] s5 = MakeStr("InternalName", appName + ".dll"); msStrings.Write(s5, 0, s5.Length);
            byte[] s6 = MakeStr("LegalCopyright", "Copyright (C) 2026 " + appAuthor); msStrings.Write(s6, 0, s6.Length);
            byte[] s7 = MakeStr("OriginalFilename", appName + ".exe"); msStrings.Write(s7, 0, s7.Length);
            byte[] s8 = MakeStr("ProductName", appName); msStrings.Write(s8, 0, s8.Length);
            byte[] s9 = MakeStr("ProductVersion", version ?? "8.2.0.0"); msStrings.Write(s9, 0, s9.Length);
            byte[] strings = msStrings.ToArray();

            // StringTable: key = "000004b0" (Language neutral, Unicode codepage 1200)
            byte[] stKey = Encoding.Unicode.GetBytes("000004b0\0");
            int stHeadLen = 6 + stKey.Length;
            int stPad = Pad4(stHeadLen) - stHeadLen;
            int stLen = stHeadLen + stPad + strings.Length;
            int stTotal = Pad4(stLen);
            byte[] stBuf = new byte[stTotal];
            BitConverter.GetBytes((ushort)stLen).CopyTo(stBuf, 0);
            BitConverter.GetBytes((ushort)0).CopyTo(stBuf, 2);
            BitConverter.GetBytes((ushort)1).CopyTo(stBuf, 4);
            stKey.CopyTo(stBuf, 6);
            strings.CopyTo(stBuf, stHeadLen + stPad);

            // StringFileInfo: key = "StringFileInfo"
            byte[] sfiKey = Encoding.Unicode.GetBytes("StringFileInfo\0");
            int sfiHeadLen = 6 + sfiKey.Length;
            int sfiPad = Pad4(sfiHeadLen) - sfiHeadLen;
            int sfiLen = sfiHeadLen + sfiPad + stBuf.Length;
            int sfiTotal = Pad4(sfiLen);
            byte[] sfiBuf = new byte[sfiTotal];
            BitConverter.GetBytes((ushort)sfiLen).CopyTo(sfiBuf, 0);
            BitConverter.GetBytes((ushort)0).CopyTo(sfiBuf, 2);
            BitConverter.GetBytes((ushort)1).CopyTo(sfiBuf, 4);
            sfiKey.CopyTo(sfiBuf, 6);
            stBuf.CopyTo(sfiBuf, sfiHeadLen + sfiPad);

            // VarFileInfo: Translation = 0x0000, 0x04b0 (Neutral, Unicode)
            byte[] varKey = Encoding.Unicode.GetBytes("Translation\0");
            int varHead = 6 + varKey.Length;
            int varPad = Pad4(varHead) - varHead;
            byte[] varData = new byte[4];
            BitConverter.GetBytes((ushort)0x0000).CopyTo(varData, 0);
            BitConverter.GetBytes((ushort)0x04b0).CopyTo(varData, 2);
            int varLen = varHead + varPad + 4;
            byte[] varBuf = new byte[Pad4(varLen)];
            BitConverter.GetBytes((ushort)varLen).CopyTo(varBuf, 0);
            BitConverter.GetBytes((ushort)4).CopyTo(varBuf, 2);
            BitConverter.GetBytes((ushort)0).CopyTo(varBuf, 4);
            varKey.CopyTo(varBuf, 6);
            varData.CopyTo(varBuf, varHead + varPad);

            byte[] vfiKey = Encoding.Unicode.GetBytes("VarFileInfo\0");
            int vfiHead = 6 + vfiKey.Length;
            int vfiPad = Pad4(vfiHead) - vfiHead;
            int vfiLen = vfiHead + vfiPad + varBuf.Length;
            byte[] vfiBuf = new byte[Pad4(vfiLen)];
            BitConverter.GetBytes((ushort)vfiLen).CopyTo(vfiBuf, 0);
            BitConverter.GetBytes((ushort)0).CopyTo(vfiBuf, 2);
            BitConverter.GetBytes((ushort)1).CopyTo(vfiBuf, 4);
            vfiKey.CopyTo(vfiBuf, 6);
            varBuf.CopyTo(vfiBuf, vfiHead + vfiPad);

            // VS_VERSIONINFO root header
            byte[] rootKey = Encoding.Unicode.GetBytes("VS_VERSION_INFO\0");
            int rootHead = 6 + rootKey.Length;
            int rootPad1 = Pad4(rootHead) - rootHead;

            byte[] ffi = new byte[52];
            BitConverter.GetBytes(0xFEEF04BDu).CopyTo(ffi, 0);
            BitConverter.GetBytes(0x00010000u).CopyTo(ffi, 4);
            BitConverter.GetBytes(v2).CopyTo(ffi, 8);   BitConverter.GetBytes(v1).CopyTo(ffi, 10);
            BitConverter.GetBytes(v4).CopyTo(ffi, 12);  BitConverter.GetBytes(v3).CopyTo(ffi, 14);
            BitConverter.GetBytes(v2).CopyTo(ffi, 16);  BitConverter.GetBytes(v1).CopyTo(ffi, 18);
            BitConverter.GetBytes(v4).CopyTo(ffi, 20);  BitConverter.GetBytes(v3).CopyTo(ffi, 22);
            BitConverter.GetBytes(0x0000003Fu).CopyTo(ffi, 24);
            BitConverter.GetBytes(0u).CopyTo(ffi, 28);
            BitConverter.GetBytes(0x00040004u).CopyTo(ffi, 32); // VOS_NT_WINDOWS32
            BitConverter.GetBytes(0x00000001u).CopyTo(ffi, 36); // VFT_APP
            BitConverter.GetBytes(0u).CopyTo(ffi, 40);

            int rootHeadAndVal = rootHead + rootPad1 + 52;
            int rootPad2 = Pad4(rootHeadAndVal) - rootHeadAndVal;

            int totalLen = rootHeadAndVal + rootPad2 + sfiBuf.Length + vfiBuf.Length;
            byte[] rootBuf = new byte[totalLen];
            BitConverter.GetBytes((ushort)totalLen).CopyTo(rootBuf, 0);
            BitConverter.GetBytes((ushort)52).CopyTo(rootBuf, 2);
            BitConverter.GetBytes((ushort)0).CopyTo(rootBuf, 4);
            rootKey.CopyTo(rootBuf, 6);
            ffi.CopyTo(rootBuf, rootHead + rootPad1);
            sfiBuf.CopyTo(rootBuf, rootHeadAndVal + rootPad2);
            vfiBuf.CopyTo(rootBuf, rootHeadAndVal + rootPad2 + sfiBuf.Length);

            return rootBuf;
        }

        /// <summary>
        /// Injects the generated VS_VERSIONINFO into the executable using Windows UpdateResource API.
        /// </summary>
        public static bool UpdateVersionInfo(string exePath, string appName, string appAuthor, string version)
        {
            try
            {
                if (!File.Exists(exePath)) return false;
                byte[] resData = CreateVersionInfoResource(appName, appAuthor, version);

                IntPtr hUpdate = BeginUpdateResource(exePath, false);
                if (hUpdate == IntPtr.Zero) return false;

                // RT_VERSION = 16, lpName = 1
                UpdateResource(hUpdate, (IntPtr)16, (IntPtr)1, 0, resData, (uint)resData.Length);
                UpdateResource(hUpdate, (IntPtr)16, (IntPtr)1, 1033, resData, (uint)resData.Length);
                UpdateResource(hUpdate, (IntPtr)16, (IntPtr)1, 1049, resData, (uint)resData.Length);

                return EndUpdateResource(hUpdate, false);
            }
            catch
            {
                return false;
            }
        }

        private static int IndexOfBytes(byte[] source, byte[] pattern)
        {
            if (pattern.Length == 0 || source.Length < pattern.Length) return -1;
            int max = source.Length - pattern.Length;
            for (int i = 0; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
