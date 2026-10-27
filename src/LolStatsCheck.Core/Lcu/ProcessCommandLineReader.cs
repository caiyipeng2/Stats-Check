using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LolStatsCheck.Core.Lcu;

internal static class ProcessCommandLineReader
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessVmRead = 0x0010;

    public static string? TryRead(Process process)
    {
        if (!Environment.Is64BitProcess)
        {
            return null;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, false, process.Id);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var status = NtQueryInformationProcess(handle, 0, out var info,
                Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0 || info.PebBaseAddress == IntPtr.Zero)
            {
                return null;
            }

            var processParameters = ReadIntPtr(handle, info.PebBaseAddress + 0x20);
            if (processParameters == IntPtr.Zero)
            {
                return null;
            }

            var length = ReadUInt16(handle, processParameters + 0x70);
            var buffer = ReadIntPtr(handle, processParameters + 0x78);
            if (length == 0 || length > 32768 || buffer == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[length];
            if (!ReadProcessMemory(handle, buffer, bytes, bytes.Length, out var read) || read.ToInt64() < length)
            {
                return null;
            }

            return Encoding.Unicode.GetString(bytes);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
    }

    private static IntPtr ReadIntPtr(IntPtr process, IntPtr address)
    {
        var buffer = new byte[IntPtr.Size];
        return ReadProcessMemory(process, address, buffer, buffer.Length, out _)
            ? (IntPtr)BitConverter.ToInt64(buffer, 0)
            : IntPtr.Zero;
    }

    private static ushort ReadUInt16(IntPtr process, IntPtr address)
    {
        var buffer = new byte[2];
        return ReadProcessMemory(process, address, buffer, buffer.Length, out _)
            ? BitConverter.ToUInt16(buffer, 0)
            : (ushort)0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesRead);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        out ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);
}
