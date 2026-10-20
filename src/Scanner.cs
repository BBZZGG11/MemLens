using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MemLens
{
    internal sealed class MemScanner
    {
        private const int ScanSampleMs = 650;
        private const long Mb = 1024L * 1024L;

        // These are deliberately conservative. The app only offers actions on
        // processes in the current session, and never on Windows' own machinery.
        private static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "system idle process", "registry", "memory compression", "secure system",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm",
            "dwm", "fontdrvhost", "sihost", "ctfmon", "explorer", "svchost", "audiodg",
            "msmpeng", "securityhealthservice", "wudfhost", "taskhostw", "dllhost",
            "runtimebroker", "shellexperiencehost", "startmenuexperiencehost",
            "applicationframehost", "textinputhost", "lockapp", "searchhost",
            "spoolsv", "wmiPrvSE".ToLowerInvariant()
        };

        private sealed class Entry
        {
            public Process Process;
            public int Pid;
            public string Name;
            public int SessionId;
            public IntPtr Handle;
            public ulong Cpu0;
            public bool CpuKnown;
        }

        public SystemSnapshot Scan(Action<string> progress)
        {
            SystemSnapshot snapshot = new SystemSnapshot();
            snapshot.CapturedAt = DateTime.Now;
            int pageSize = Native.GetPageSize();

            if (progress != null) progress("读取系统内存概况...");

            Native.MEMORYSTATUSEX mem;
            if (Native.TryGetSystemMemory(out mem))
            {
                snapshot.TotalPhys = mem.ullTotalPhys;
                snapshot.AvailPhys = mem.ullAvailPhys;
                snapshot.TotalPageFile = mem.ullTotalPageFile;
                snapshot.AvailPageFile = mem.ullAvailPageFile;
                snapshot.MemoryLoad = mem.dwMemoryLoad;
            }

            Native.PERFORMANCE_INFORMATION perf;
            if (Native.TryGetPerformanceInfo(out perf))
            {
                if (perf.PageSize.ToUInt64() > 0)
                    pageSize = (int)perf.PageSize.ToUInt64();

                snapshot.CommitTotal = perf.CommitTotal.ToUInt64() * (ulong)pageSize;
                snapshot.CommitLimit = perf.CommitLimit.ToUInt64() * (ulong)pageSize;
                snapshot.SystemCache = perf.SystemCache.ToUInt64() * (ulong)pageSize;
                snapshot.KernelPaged = perf.KernelPaged.ToUInt64() * (ulong)pageSize;
                snapshot.KernelNonpaged = perf.KernelNonpaged.ToUInt64() * (ulong)pageSize;
                snapshot.ThreadCount = (int)perf.ThreadCount;
                snapshot.HandleCount = (int)perf.HandleCount;
            }

            if (progress != null) progress("读取待机缓存与已修改页...");

            bool listKnown;
            ulong[] memoryList = Native.QueryMemoryList(out listKnown);
            snapshot.MemoryListKnown = listKnown;
            if (listKnown && memoryList != null && memoryList.Length >= 22)
            {
                ulong pages = (ulong)pageSize;
                snapshot.ZeroBytes = memoryList[0] * pages;
                snapshot.FreeBytes = (memoryList[0] + memoryList[1]) * pages;
                snapshot.ModifiedBytes = memoryList[2] * pages;

                ulong standby = 0;
                for (int i = 5; i <= 12; i++) standby += memoryList[i];
                snapshot.StandbyBytes = standby * pages;
            }

            if (progress != null) progress("枚举进程并采样 CPU...");

            Process current = Process.GetCurrentProcess();
            int currentPid = current.Id;
            int currentSession = current.SessionId;
            List<Entry> entries = new List<Entry>();
            Process[] processes = null;

            try
            {
                processes = Process.GetProcesses();
                foreach (Process process in processes)
                {
                    Entry entry = new Entry();
                    entry.Process = process;
                    entry.Pid = process.Id;
                    entry.Name = SafeName(process);
                    entry.SessionId = SafeSession(process);

                    entry.Handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, entry.Pid);
                    if (entry.Handle == IntPtr.Zero)
                        entry.Handle = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, entry.Pid);

                    if (entry.Handle != IntPtr.Zero)
                    {
                        entry.Cpu0 = Native.GetProcessCpuTime(entry.Handle);
                        entry.CpuKnown = true;
                    }
                    entries.Add(entry);
                }

                DateTime sampleStart = DateTime.UtcNow;
                Thread.Sleep(ScanSampleMs);
                DateTime sampleEnd = DateTime.UtcNow;
                double seconds = (sampleEnd - sampleStart).TotalSeconds;
                if (seconds <= 0) seconds = ScanSampleMs / 1000.0;

                int index = 0;
                foreach (Entry entry in entries)
                {
                    index++;
                    if (progress != null && index % 20 == 0)
                        progress("读取进程内存 " + index + "/" + entries.Count + "...");

                    if (entry.Handle == IntPtr.Zero || !Native.IsProcessAlive(entry.Handle))
                        continue;

                    ProcInfo info = new ProcInfo();
                    info.Pid = entry.Pid;
                    info.Name = entry.Name;
                    info.SessionId = entry.SessionId;
                    info.SameSession = entry.SessionId == currentSession;
                    info.IsSelf = entry.Pid == currentPid;
                    info.Critical = IsProtected(entry.Name, entry.Pid, entry.SessionId, currentSession, currentPid);

                    Native.PROCESS_MEMORY_COUNTERS_EX2 counters;
                    bool memOk = Native.TryGetProcessMemory(entry.Handle, out counters);
                    if (memOk)
                    {
                        info.WorkingSet = counters.WorkingSetSize.ToUInt64();
                        info.PrivateWorkingSet = counters.PrivateWorkingSetSize.ToUInt64();
                        info.PrivateBytes = counters.PrivateUsage.ToUInt64();
                        info.SharedCommit = counters.SharedCommitUsage.ToUInt64();
                    }
                    else
                    {
                        TryReadFallbackMemory(entry.Process, info);
                    }

                    if (info.PrivateWorkingSet > info.WorkingSet)
                        info.PrivateWorkingSet = info.WorkingSet;
                    if (info.PrivateWorkingSet == 0 && info.WorkingSet > 0 && info.PrivateBytes > 0)
                        info.PrivateWorkingSet = Math.Min(info.WorkingSet, info.PrivateBytes);

                    info.Accessible = memOk || info.WorkingSet > 0;

                    info.MemoryPriority = Native.GetMemoryPriority(entry.Handle);
                    info.PriorityKnown = info.MemoryPriority >= 1 && info.MemoryPriority <= 5;

                    if (entry.CpuKnown)
                    {
                        ulong cpu1 = Native.GetProcessCpuTime(entry.Handle);
                        if (cpu1 >= entry.Cpu0)
                        {
                            double cpu = (cpu1 - entry.Cpu0) / 10000000.0 / seconds / Environment.ProcessorCount * 100.0;
                            if (cpu < 0) cpu = 0;
                            if (cpu > 100) cpu = 100;
                            info.CpuPercent = cpu;
                        }
                    }

                    info.ReclaimEstimate = EstimateReclaim(info.WorkingSet, info.PrivateWorkingSet, info.CpuPercent);
                    info.Note = BuildNote(info);
                    snapshot.Processes.Add(info);
                }
            }
            catch (Exception ex)
            {
                snapshot.ScanError = ex.Message;
            }
            finally
            {
                foreach (Entry entry in entries)
                {
                    if (entry.Handle != IntPtr.Zero) Native.CloseHandle(entry.Handle);
                    try { entry.Process.Dispose(); } catch { }
                }
                try { current.Dispose(); } catch { }
            }

            if (snapshot.ProcessCount <= 0)
                snapshot.ProcessCount = snapshot.Processes.Count;
            if (snapshot.ThreadCount <= 0)
                snapshot.ThreadCount = 0;

            snapshot.ProcessReclaimTotal = 0;
            snapshot.ActionableCount = 0;
            foreach (ProcInfo info in snapshot.Processes)
            {
                if (info.CanAct)
                {
                    snapshot.ActionableCount++;
                    snapshot.ProcessReclaimTotal += info.ReclaimEstimate;
                }
            }

            if (progress != null) progress("扫描完成");
            return snapshot;
        }

        public TrimResult Trim(ProcInfo info)
        {
            TrimResult result = new TrimResult();
            result.Pid = info.Pid;
            result.Name = info.Name;
            int error;
            ulong before, after;
            bool ok = Native.TrimProcessByPid(info.Pid, out before, out after, out error);
            result.Ok = ok;
            result.Before = before;
            result.After = after;
            result.Error = error;
            return result;
        }

        public bool SetPriority(ProcInfo info, uint priority, out int error)
        {
            return Native.SetMemoryPriorityByPid(info.Pid, priority, out error);
        }

        public static Dictionary<int, long> CapturePrivateBytes()
        {
            Dictionary<int, long> result = new Dictionary<int, long>();
            Process[] processes = null;
            try
            {
                processes = Process.GetProcesses();
                foreach (Process process in processes)
                {
                    IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                    if (handle == IntPtr.Zero) continue;
                    try
                    {
                        Native.PROCESS_MEMORY_COUNTERS_EX2 counters;
                        if (Native.TryGetProcessMemory(handle, out counters))
                            result[process.Id] = (long)counters.PrivateUsage.ToUInt64();
                    }
                    finally
                    {
                        Native.CloseHandle(handle);
                    }
                }
            }
            catch
            {
                // A process list can change while it is being read. Partial data is fine.
            }
            finally
            {
                if (processes != null)
                {
                    foreach (Process process in processes)
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }
            return result;
        }

        public static long EstimateReclaim(ulong workingSet, ulong privateWorkingSet, double cpuPercent)
        {
            if (workingSet < 12L * Mb) return 0;
            if (privateWorkingSet > workingSet) privateWorkingSet = workingSet;

            double shared = workingSet - privateWorkingSet;
            double factor = 0.08;
            if (cpuPercent >= 0 && cpuPercent < 1.0)
                factor = 0.35;
            else if (cpuPercent < 0)
                factor = 0.18;

            double value = shared * 0.90 + privateWorkingSet * factor;
            double cap = workingSet - 4.0 * Mb;
            if (value > cap) value = cap;
            if (value < 0) value = 0;
            return (long)value;
        }

        private static void TryReadFallbackMemory(Process process, ProcInfo info)
        {
            try { info.WorkingSet = (ulong)process.WorkingSet64; } catch { }
            try { info.PrivateBytes = (ulong)process.PrivateMemorySize64; } catch { }
            if (info.WorkingSet > 0 && info.PrivateBytes > 0)
                info.PrivateWorkingSet = Math.Min(info.WorkingSet, info.PrivateBytes);
        }

        private static string BuildNote(ProcInfo info)
        {
            if (info.IsSelf) return "本工具";
            if (!info.SameSession) return "其他会话";
            if (info.Critical) return "系统关键";
            if (!info.Accessible) return "受保护";
            if (info.CpuPercent >= 15) return "活跃，慎回收";
            if (info.CpuPercent >= 0 && info.CpuPercent < 1 && info.ReclaimEstimate >= 100 * Mb)
                return "空闲，可限制";
            if (info.ReclaimEstimate >= 20 * Mb)
                return "有可回收页";
            return "占用小";
        }

        private static bool IsProtected(string name, int pid, int sessionId, int currentSession, int currentPid)
        {
            if (pid <= 4) return true;
            if (pid == currentPid) return true;
            if (sessionId != currentSession) return true;
            if (string.IsNullOrEmpty(name)) return true;
            if (ProtectedNames.Contains(name)) return true;
            if (name.Equals("MemLens", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string SafeName(Process process)
        {
            try { return process.ProcessName; }
            catch { return "PID " + process.Id; }
        }

        private static int SafeSession(Process process)
        {
            try { return process.SessionId; }
            catch { return 0; }
        }
    }
}
