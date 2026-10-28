using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LolStatsCheck.Core.Lcu;

internal static class ProcessCommandLineReader
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessVmRead = 0x0010;
    private const int ProcessCommandLineInformation = 60;
    private const int MaxCommandLineLength = 32768;

    public static string? TryRead(Process process)
    {
        var commandLine = TryReadWithLimitedQuery(process.Id);
        if (!string.IsNullOrWhiteSpace(commandLine))
        {
            return commandLine;
        }

        return TryReadFromPeb(process.Id);
    }

    // 国服客户端以管理员权限启动时，PROCESS_VM_READ 会被拒绝；
    // ProcessCommandLineInformation 只需要 PROCESS_QUERY_LIMITED_INFORMATION。
    private static string? TryReadWithLimitedQuery(int processId)
    {
        IntPtr handle = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var required);
            if (required <= 0 || required > MaxCommandLineLength)
            {
                return null;
            }

            buffer = Marshal.AllocHGlobal(required);
            var status = NtQueryInformationProcess(
                handle, ProcessCommandLineInformation, buffer, required, out _);
            if (status != 0)
            {
                return null;
            }

            var value = Marshal.PtrToStructure<UnicodeString>(buffer);
            if (value.Length == 0 || value.Buffer == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
    }

    private static string? TryReadFromPeb(int processId)
    {
        if (!Environment.Is64BitProcess)
        {
            return null;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, false, processId);
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
            if (length == 0 || length > MaxCommandLineLength || buffer == IntPtr.Zero)
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
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
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

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass, IntPtr processInformation,
        int processInformationLength, out int returnLength);
}
