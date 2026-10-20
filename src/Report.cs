using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MemLens
{
    internal static class ReportWriter
    {
        public static string BuildSummary(SystemSnapshot s, int top)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MemLens 内存透视 — 诊断报告");
            sb.AppendLine("生成时间: " + s.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("系统: Windows " + Environment.OSVersion.Version.Major + "." + Environment.OSVersion.Version.Minor + " (build " + Environment.OSVersion.Version.Build + "), " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"));
            sb.AppendLine("权限: " + (Native.IsAdministrator() ? "管理员" : "普通用户"));
            sb.AppendLine();
            sb.AppendLine("== 系统内存 ==");
            sb.AppendLine("物理内存: " + Fmt.Bytes(s.TotalPhys));
            sb.AppendLine("正在使用: " + Fmt.Bytes(s.UsedBytes) + "  (" + s.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%)");
            sb.AppendLine("可用内存: " + Fmt.Bytes(s.AvailPhys) + "  (" + s.AvailablePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%)");
            sb.AppendLine("待机缓存: " + Fmt.Bytes(s.StandbyBytes) + (s.MemoryListKnown ? "" : "  [读取失败]"));
            sb.AppendLine("已修改页: " + Fmt.Bytes(s.ModifiedBytes));
            sb.AppendLine("零页/空闲: " + Fmt.Bytes(s.FreeBytes));
            sb.AppendLine("系统缓存（含待机）: " + Fmt.Bytes(s.SystemCache));
            sb.AppendLine("内核分页池: " + Fmt.Bytes(s.KernelPaged));
            sb.AppendLine("内核非分页池: " + Fmt.Bytes(s.KernelNonpaged));
            if (s.CommitLimit > 0)
                sb.AppendLine("提交内存: " + Fmt.Bytes(s.CommitTotal) + " / " + Fmt.Bytes(s.CommitLimit) + "  (" + s.CommitPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%)");
            sb.AppendLine("进程/线程/句柄: " + s.ProcessCount + " / " + s.ThreadCount + " / " + s.HandleCount);
            sb.AppendLine();
            sb.AppendLine("== 可回收估算 ==");
            sb.AppendLine("系统待机 + 已修改: " + Fmt.Bytes(s.StandbyBytes + s.ModifiedBytes) + "（系统可回收缓存，精确统计）");
            sb.AppendLine("后台进程逐项合计: " + Fmt.Bytes((ulong)Math.Max(0, s.ProcessReclaimTotal)) + "（" + s.ActionableCount + " 个可操作进程，启发式估算）");
            sb.AppendLine("说明: 待机页是 Windows 的正常文件缓存，不是故障；进程可回收量是估算值，不能等同于泄漏。");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(s.ScanError))
            {
                sb.AppendLine("扫描警告: " + s.ScanError);
                sb.AppendLine();
            }

            sb.AppendLine("== 进程明细（按可回收估算排序，前 " + top + " 项）==");
            sb.AppendLine(Pad("进程", 26) + Pad("PID", 8) + Pad("工作集", 12) + Pad("私有常驻", 12) + Pad("可回收", 12) + Pad("CPU", 9) + "状态");
            List<ProcInfo> list = new List<ProcInfo>(s.Processes);
            list.Sort(delegate(ProcInfo a, ProcInfo b)
            {
                int c = b.ReclaimEstimate.CompareTo(a.ReclaimEstimate);
                if (c != 0) return c;
                return b.WorkingSet.CompareTo(a.WorkingSet);
            });

            int count = 0;
            foreach (ProcInfo p in list)
            {
                if (count >= top) break;
                if (p.WorkingSet == 0 && !p.Critical) continue;
                sb.AppendLine(Pad(p.Name, 26) + Pad(p.Pid.ToString(), 8) + Pad(Fmt.Bytes(p.WorkingSet), 12) + Pad(Fmt.Bytes(p.PrivateWorkingSet), 12) + Pad(Fmt.Bytes((ulong)Math.Max(0, p.ReclaimEstimate)), 12) + Pad(p.CpuPercent < 0 ? "—" : p.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%", 9) + p.Note);
                count++;
            }

            sb.AppendLine();
            sb.AppendLine("判定口径:");
            sb.AppendLine("  可回收估算 = 共享页×90% + 私有常驻×(空闲时35% / 活跃时8%)，并限制在工作集以内。");
            sb.AppendLine("  内存优先级 1 级表示系统在内存紧张时优先回收该进程的页；它不是硬上限。");
            sb.AppendLine("  私有提交持续增长才更接近“泄漏”；单次工作集大不等于无效占用。");
            sb.AppendLine("  本工具不会自动清理，也不修改注册表、服务或驱动。");
            return sb.ToString();
        }

        public static void WriteCsv(SystemSnapshot s, string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MemLens 内存诊断报告");
            sb.AppendLine("生成时间," + Fmt.Csv(s.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("系统," + Fmt.Csv("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " 64-bit" : " 32-bit")));
            sb.AppendLine("权限," + Fmt.Csv(Native.IsAdministrator() ? "管理员" : "普通用户"));
            sb.AppendLine();
            sb.AppendLine("系统内存");
            AppendCsvPair(sb, "物理内存(MB)", (s.TotalPhys / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "正在使用(MB)", (s.UsedBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "可用(MB)", (s.AvailPhys / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "待机缓存(MB)", (s.StandbyBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "已修改页(MB)", (s.ModifiedBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "零页/空闲(MB)", (s.FreeBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "提交总量(MB)", (s.CommitTotal / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "提交上限(MB)", (s.CommitLimit / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            AppendCsvPair(sb, "进程可回收估算(MB)", (Math.Max(0, s.ProcessReclaimTotal) / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("进程明细");
            sb.AppendLine("进程,PID,会话,可操作,内存优先级,工作集(MB),私有常驻(MB),共享页(MB),私有提交(MB),可回收估算(MB),CPU(%),10秒增长(MB),状态");

            List<ProcInfo> list = new List<ProcInfo>(s.Processes);
            list.Sort(delegate(ProcInfo a, ProcInfo b) { return b.ReclaimEstimate.CompareTo(a.ReclaimEstimate); });
            foreach (ProcInfo p in list)
            {
                sb.Append(Fmt.Csv(p.Name)).Append(',');
                sb.Append(p.Pid).Append(',');
                sb.Append(p.SessionId).Append(',');
                sb.Append(Fmt.Csv(p.CanAct ? "是" : "否")).Append(',');
                sb.Append(Fmt.Csv(p.PriorityKnown ? p.MemoryPriority.ToString() : "")).Append(',');
                sb.Append((p.WorkingSet / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append((p.PrivateWorkingSet / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append((p.SharedWorkingSet / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append((p.PrivateBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append((Math.Max(0, p.ReclaimEstimate) / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(p.CpuPercent < 0 ? "" : p.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(p.GrowthKnown ? (p.GrowthBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) : "").Append(',');
                sb.AppendLine(Fmt.Csv(p.Note));
            }

            sb.AppendLine();
            sb.AppendLine("说明");
            sb.AppendLine(Fmt.Csv("“可回收估算”是启发式估算，不是精确的无效内存；待机缓存是 Windows 的正常缓存。"));
            sb.AppendLine(Fmt.Csv("“10秒增长”为正不一定是泄漏，需结合提交内存和持续时间判断。"));
            sb.AppendLine(Fmt.Csv("内存优先级 1 级是软限制，不是硬上限。"));
            WriteTextFile(path, sb.ToString());
        }

        public static void WriteTextFile(string path, string text)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }

        private static void AppendCsvPair(StringBuilder sb, string key, string value)
        {
            sb.Append(Fmt.Csv(key)).Append(',').AppendLine(Fmt.Csv(value));
        }

        private static string Pad(string value, int width)
        {
            if (value == null) value = "";
            if (value.Length >= width) return value + " ";
            return value.PadRight(width);
        }
    }
}
