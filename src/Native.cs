using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MemLens
{
    /// <summary>
    /// Thin, dependency-free wrappers around the Windows memory APIs used by MemLens.
    /// Everything here is read-only unless the name starts with Set/Purge/Trim.
    /// </summary>
    internal static class Native
    {
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_SET_QUOTA = 0x0100;
        public const uint PROCESS_SET_INFORMATION = 0x0200;

        private const uint STILL_ACTIVE = 259;
        private const int SystemMemoryListInformation = 0x50;
        private const int ProcessMemoryPriority = 0;

        public const int MemoryPurgeStandbyList = 4;
        public const int MemoryPurgeLowPriorityStandbyList = 5;

        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_MEMORY_COUNTERS_EX2
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public UIntPtr SharedCommitUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_PRIORITY_INFORMATION
        {
            public uint MemoryPriority;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint LowDateTime;
            public uint HighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_INFO
        {
            public ushort wProcessorArchitecture;
            public ushort wReserved;
            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort dwProcessorLevel;
            public ushort dwProcessorRevision;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetProcessMemoryInfo(IntPtr hProcess, ref PROCESS_MEMORY_COUNTERS_EX2 ppsmemCounters, uint cb);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME lpCreationTime, out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessInformation(IntPtr hProcess, int ProcessInformationClass, ref MEMORY_PRIORITY_INFORMATION ProcessInformation, uint ProcessInformationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr hProcess, int ProcessInformationClass, ref MEMORY_PRIORITY_INFORMATION ProcessInformation, uint ProcessInformationSize);

        [DllImport("kernel32.dll")]
        public static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern void SetLastError(uint dwErrCode);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        public static bool TryGetSystemMemory(out MEMORYSTATUSEX info)
        {
            info = new MEMORYSTATUSEX();
            return GlobalMemoryStatusEx(info);
        }

        public static bool TryGetPerformanceInfo(out PERFORMANCE_INFORMATION info)
        {
            info = new PERFORMANCE_INFORMATION();
            info.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
            return GetPerformanceInfo(ref info, info.cb);
        }

        public static int GetPageSize()
        {
            SYSTEM_INFO si;
            GetSystemInfo(out si);
            if (si.dwPageSize <= 0) return 4096;
            return (int)si.dwPageSize;
        }

        /// <summary>
        /// Returns the 22 ULONG_PTR fields of SYSTEM_MEMORY_LIST_INFORMATION.
        /// Index 0=zero, 1=free, 2=modified, 3=modified-no-write, 4=bad,
        /// 5..12=standby by priority, 13..20=repurposed by priority, 21=modified-pagefile.
        /// </summary>
        public static ulong[] QueryMemoryList(out bool known)
        {
            const int fieldCount = 22;
            int size = fieldCount * IntPtr.Size;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) Marshal.WriteByte(buffer, i, 0);
                int returned;
                int status = NtQuerySystemInformation(SystemMemoryListInformation, buffer, size, out returned);
                if (status != 0)
                {
                    known = false;
                    return null;
                }

                ulong[] fields = new ulong[fieldCount];
                for (int i = 0; i < fieldCount; i++)
                {
                    if (IntPtr.Size == 8)
                        fields[i] = (ulong)Marshal.ReadInt64(buffer, i * 8);
                    else
                        fields[i] = (uint)Marshal.ReadInt32(buffer, i * 4);
                }
                known = true;
                return fields;
            }
            catch
            {
                known = false;
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static ulong GetProcessCpuTime(IntPtr handle)
        {
            FILETIME creation, exit, kernel, user;
            if (!GetProcessTimes(handle, out creation, out exit, out kernel, out user)) return 0;
            return ToUlong(kernel) + ToUlong(user);
        }

        public static bool IsProcessAlive(IntPtr handle)
        {
            uint code;
            if (!GetExitCodeProcess(handle, out code)) return false;
            return code == STILL_ACTIVE;
        }

        public static bool TryGetProcessMemory(IntPtr handle, out PROCESS_MEMORY_COUNTERS_EX2 counters)
        {
            counters = new PROCESS_MEMORY_COUNTERS_EX2();
            counters.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX2));
            return GetProcessMemoryInfo(handle, ref counters, counters.cb);
        }

        public static uint GetMemoryPriority(IntPtr handle)
        {
            MEMORY_PRIORITY_INFORMATION info = new MEMORY_PRIORITY_INFORMATION();
            uint size = (uint)Marshal.SizeOf(typeof(MEMORY_PRIORITY_INFORMATION));
            if (!GetProcessInformation(handle, ProcessMemoryPriority, ref info, size)) return 0;
            return info.MemoryPriority;
        }

        public static bool SetMemoryPriority(IntPtr handle, uint priority)
        {
            MEMORY_PRIORITY_INFORMATION info = new MEMORY_PRIORITY_INFORMATION();
            info.MemoryPriority = priority;
            uint size = (uint)Marshal.SizeOf(typeof(MEMORY_PRIORITY_INFORMATION));
            return SetProcessInformation(handle, ProcessMemoryPriority, ref info, size);
        }

        public static bool SetMemoryPriorityByPid(int pid, uint priority, out int error)
        {
            error = 0;
            IntPtr handle = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            try
            {
                bool ok = SetMemoryPriority(handle, priority);
                if (!ok) error = Marshal.GetLastWin32Error();
                return ok;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static bool TrimProcessByPid(int pid, out ulong before, out ulong after, out int error)
        {
            before = 0;
            after = 0;
            error = 0;

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, pid);
            if (handle == IntPtr.Zero)
                handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
            if (handle == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                PROCESS_MEMORY_COUNTERS_EX2 beforeCounters;
                if (TryGetProcessMemory(handle, out beforeCounters))
                    before = beforeCounters.WorkingSetSize.ToUInt64();

                if (!EmptyWorkingSet(handle))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }

                System.Threading.Thread.Sleep(180);

                PROCESS_MEMORY_COUNTERS_EX2 afterCounters;
                if (TryGetProcessMemory(handle, out afterCounters))
                    after = afterCounters.WorkingSetSize.ToUInt64();

                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static bool PurgeStandby(int command, out int status)
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int cmd = command;
            status = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, 4);
            return status == 0;
        }

        public static bool EnablePrivilege(string name)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out token)) return false;
                LUID luid;
                if (!LookupPrivilegeValue(null, name, out luid)) return false;

                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Luid = luid;
                tp.Attributes = 0x0002; // SE_PRIVILEGE_ENABLED

                SetLastError(0);
                bool ok = AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                int error = Marshal.GetLastWin32Error();
                // AdjustTokenPrivileges returns TRUE even when not all privileges were assigned.
                return ok && error != 1300;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static string NtStatusText(int status)
        {
            uint value = unchecked((uint)status);
            switch (value)
            {
                case 0x00000000: return "成功";
                case 0xC0000022: return "访问被拒绝 (0xC0000022)";
                case 0xC0000061: return "权限不足，需要管理员 (0xC0000061)";
                case 0xC000000D: return "参数无效 (0xC000000D)";
                default: return "0x" + value.ToString("X8");
            }
        }

        public static string Win32Text(int error)
        {
            switch (error)
            {
                case 0: return "成功";
                case 5: return "访问被拒绝 (5)";
                case 87: return "参数错误 (87)";
                case 1168: return "找不到进程 (1168)";
                case 1314: return "缺少所需特权 (1314)";
                default: return "错误 " + error;
            }
        }

        private static ulong ToUlong(FILETIME time)
        {
            return ((ulong)time.HighDateTime << 32) | time.LowDateTime;
        }
    }
}
