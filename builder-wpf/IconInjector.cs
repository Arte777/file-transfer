using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NexusBuilder
{
    public static class IconInjector
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

        public static bool InjectIcon(string exePath, string icoPath)
        {
            try
            {
                if (!File.Exists(exePath) || !File.Exists(icoPath)) return false;
                byte[] icoBytes = File.ReadAllBytes(icoPath);
                if (icoBytes.Length < 6) return false;

                ushort count = BitConverter.ToUInt16(icoBytes, 4);
                if (count == 0) return false;

                IntPtr hUpdate = BeginUpdateResource(exePath, false);
                if (hUpdate == IntPtr.Zero) return false;

                bool ok = true;
                byte[] grpBytes = new byte[6 + count * 14];
                Array.Copy(icoBytes, 0, grpBytes, 0, 6);

                for (int i = 0; i < count; i++)
                {
                    int entryOffset = 6 + i * 16;
                    if (entryOffset + 16 > icoBytes.Length) { ok = false; break; }

                    byte width = icoBytes[entryOffset];
                    byte height = icoBytes[entryOffset + 1];
                    byte colors = icoBytes[entryOffset + 2];
                    byte reserved = icoBytes[entryOffset + 3];
                    ushort planes = BitConverter.ToUInt16(icoBytes, entryOffset + 4);
                    ushort bpp = BitConverter.ToUInt16(icoBytes, entryOffset + 6);
                    uint bytesInRes = BitConverter.ToUInt32(icoBytes, entryOffset + 8);
                    uint imgOffset = BitConverter.ToUInt32(icoBytes, entryOffset + 12);

                    ushort iconId = (ushort)(i + 1);

                    if (imgOffset + bytesInRes <= icoBytes.Length)
                    {
                        byte[] imgData = new byte[bytesInRes];
                        Array.Copy(icoBytes, (int)imgOffset, imgData, 0, (int)bytesInRes);

                        // RT_ICON = 3
                        if (!UpdateResource(hUpdate, (IntPtr)3, (IntPtr)iconId, 0, imgData, bytesInRes))
                        {
                            ok = false;
                        }
                    }

                    int grpEntryOffset = 6 + i * 14;
                    grpBytes[grpEntryOffset] = width;
                    grpBytes[grpEntryOffset + 1] = height;
                    grpBytes[grpEntryOffset + 2] = colors;
                    grpBytes[grpEntryOffset + 3] = reserved;
                    Array.Copy(BitConverter.GetBytes(planes), 0, grpBytes, grpEntryOffset + 4, 2);
                    Array.Copy(BitConverter.GetBytes(bpp), 0, grpBytes, grpEntryOffset + 6, 2);
                    Array.Copy(BitConverter.GetBytes(bytesInRes), 0, grpBytes, grpEntryOffset + 8, 4);
                    Array.Copy(BitConverter.GetBytes(iconId), 0, grpBytes, grpEntryOffset + 12, 2);
                }

                // RT_GROUP_ICON = 14
                if (!UpdateResource(hUpdate, (IntPtr)14, (IntPtr)32512, 0, grpBytes, (uint)grpBytes.Length))
                {
                    UpdateResource(hUpdate, (IntPtr)14, (IntPtr)1, 0, grpBytes, (uint)grpBytes.Length);
                }

                return EndUpdateResource(hUpdate, !ok) && ok;
            }
            catch
            {
                return false;
            }
        }
    }
}
