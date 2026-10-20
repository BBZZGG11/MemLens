using System;
using System.Collections.Generic;
using System.Globalization;

namespace MemLens
{
    internal sealed class ProcInfo
    {
        public int Pid;
        public string Name;
        public int SessionId;
        public bool SameSession;
        public bool Accessible;
        public bool Critical;
        public bool IsSelf;
        public ulong WorkingSet;
        public ulong PrivateWorkingSet;
        public ulong PrivateBytes;
        public ulong SharedCommit;
        public uint MemoryPriority;
        public bool PriorityKnown;
        public double CpuPercent;
        public long GrowthBytes;
        public bool GrowthKnown;
        public int GrowthSeconds;
        public long ReclaimEstimate;
        public string Note;

        public ProcInfo()
        {
            Name = "";
            Note = "";
            CpuPercent = -1;
            MemoryPriority = 0;
            ReclaimEstimate = 0;
        }

        public bool CanAct
        {
            get { return Accessible && !Critical && !IsSelf && SameSession; }
        }

        public ulong SharedWorkingSet
        {
            get
            {
                if (WorkingSet <= PrivateWorkingSet) return 0;
                return WorkingSet - PrivateWorkingSet;
            }
        }

        public double WorkingSetMb
        {
            get { return WorkingSet / 1048576.0; }
        }
    }

    internal sealed class SystemSnapshot
    {
        public DateTime CapturedAt;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public uint MemoryLoad;
        public ulong CommitTotal;
        public ulong CommitLimit;
        public ulong SystemCache;
        public ulong KernelPaged;
        public ulong KernelNonpaged;
        public ulong StandbyBytes;
        public ulong ModifiedBytes;
        public ulong FreeBytes;
        public ulong ZeroBytes;
        public bool MemoryListKnown;
        public int ProcessCount;
        public int ThreadCount;
        public int HandleCount;
        public List<ProcInfo> Processes;
        public long ProcessReclaimTotal;
        public int ActionableCount;
        public string ScanError;

        public SystemSnapshot()
        {
            Processes = new List<ProcInfo>();
            CapturedAt = DateTime.Now;
            ScanError = "";
        }

        public double AvailablePercent
        {
            get
            {
                if (TotalPhys == 0) return 0;
                return AvailPhys * 100.0 / TotalPhys;
            }
        }

        public double UsedPercent
        {
            get { return 100.0 - AvailablePercent; }
        }

        public double CommitPercent
        {
            get
            {
                if (CommitLimit == 0) return 0;
                return CommitTotal * 100.0 / CommitLimit;
            }
        }

        public ulong UsedBytes
        {
            get
            {
                if (TotalPhys <= AvailPhys) return 0;
                return TotalPhys - AvailPhys;
            }
        }
    }

    internal sealed class TrimResult
    {
        public int Pid;
        public string Name;
        public bool Ok;
        public ulong Before;
        public ulong After;
        public int Error;

        public long Freed
        {
            get
            {
                if (Before <= After) return 0;
                return (long)(Before - After);
            }
        }
    }

    internal static class Fmt
    {
        public static string Bytes(ulong bytes)
        {
            double mb = bytes / 1048576.0;
            if (mb >= 1024.0) return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (mb >= 100.0) return mb.ToString("0", CultureInfo.InvariantCulture) + " MB";
            if (mb >= 10.0) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (mb > 0.05) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return "0 MB";
        }

        public static string SignedBytes(long bytes)
        {
            if (bytes == 0) return "0 MB";
            string sign = bytes > 0 ? "+" : "-";
            ulong abs = bytes > 0 ? (ulong)bytes : (ulong)(-bytes);
            return sign + Bytes(abs);
        }

        public static string Mb(double mb)
        {
            if (mb >= 1024.0) return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (mb >= 100.0) return mb.ToString("0", CultureInfo.InvariantCulture) + " MB";
            return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        public static string Percent(double percent)
        {
            if (percent < 0) return "—";
            return percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        public static string Priority(uint priority)
        {
            switch (priority)
            {
                case 1: return "最低 (1)";
                case 2: return "较低 (2)";
                case 3: return "中等 (3)";
                case 4: return "偏低 (4)";
                case 5: return "正常 (5)";
                default: return "未知";
            }
        }

        public static string Csv(string value)
        {
            if (value == null) value = "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
