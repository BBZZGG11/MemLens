using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MemLens
{
    internal static class Cli
    {
        public static int Run(string[] args)
        {
            string command = args[0].ToLowerInvariant();
            string output = ResolveOutput(args, 1, "MemLens_" + command.TrimStart('-') + ".txt");

            if (command == "--help" || command == "-h" || command == "/?")
            {
                ReportWriter.WriteTextFile(output, Usage());
                Console.WriteLine("Help written to: " + output);
                return 0;
            }

            if (command == "--version")
            {
                ReportWriter.WriteTextFile(output, "MemLens 1.0.0" + Environment.NewLine);
                Console.WriteLine("MemLens 1.0.0");
                return 0;
            }

            if (command == "--selftest")
                return SelfTest(output);

            if (command == "--trim-test")
                return TrimTest(output);

            if (command == "--export")
            {
                string csv = args.Length > 1 && !args[1].StartsWith("--")
                    ? Path.GetFullPath(args[1])
                    : Path.Combine(Path.GetTempPath(), "MemLens_export.csv");
                SystemSnapshot s = new MemScanner().Scan(null);
                ReportWriter.WriteCsv(s, csv);
                Console.WriteLine("CSV written to: " + csv);
                return s.Processes.Count > 0 ? 0 : 1;
            }

            if (command == "--scan")
            {
                SystemSnapshot s = new MemScanner().Scan(null);
                ReportWriter.WriteTextFile(output, ReportWriter.BuildSummary(s, 40));
                Console.WriteLine("Scan written to: " + output);
                return s.Processes.Count > 0 ? 0 : 1;
            }

            if (command == "--purge-low" || command == "--purge-all")
            {
                return PurgeTest(output, command == "--purge-low" ? Native.MemoryPurgeLowPriorityStandbyList : Native.MemoryPurgeStandbyList);
            }

            ReportWriter.WriteTextFile(output, Usage());
            Console.WriteLine("Unknown option. Help written to: " + output);
            return 2;
        }

        private static int SelfTest(string output)
        {
            StringBuilder sb = new StringBuilder();
            bool pass = true;
            sb.AppendLine("MemLens self-test");
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("OS: " + Environment.OSVersion.VersionString + " (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")");
            sb.AppendLine("Admin: " + Native.IsAdministrator());
            sb.AppendLine("Processors: " + Environment.ProcessorCount);
            sb.AppendLine("CLR: " + Environment.Version);
            sb.AppendLine();

            SystemSnapshot s = null;
            try
            {
                s = new MemScanner().Scan(null);
            }
            catch (Exception ex)
            {
                Check(sb, "scan did not throw", false, ex.Message, ref pass);
            }

            if (s != null)
            {
                Check(sb, "physical memory > 0", s.TotalPhys > 0, Fmt.Bytes(s.TotalPhys), ref pass);
                Check(sb, "available <= physical", s.AvailPhys > 0 && s.AvailPhys <= s.TotalPhys, Fmt.Bytes(s.AvailPhys) + " / " + Fmt.Bytes(s.TotalPhys), ref pass);
                Check(sb, "standby list readable", s.MemoryListKnown, s.MemoryListKnown ? Fmt.Bytes(s.StandbyBytes) : "NtQuerySystemInformation failed", ref pass);
                Check(sb, "page lists fit in physical memory", s.StandbyBytes + s.ModifiedBytes + s.FreeBytes <= s.TotalPhys + 256L * 1024 * 1024, Fmt.Bytes(s.StandbyBytes + s.ModifiedBytes + s.FreeBytes), ref pass);
                Check(sb, "processes enumerated", s.Processes.Count >= 5, s.Processes.Count.ToString(), ref pass);
                Check(sb, "process memory readable", CountAccessible(s.Processes) >= 5, CountAccessible(s.Processes).ToString() + " accessible", ref pass);
                Check(sb, "actionable processes found", s.ActionableCount >= 1, s.ActionableCount.ToString(), ref pass);
                Check(sb, "commit limit >= commit", s.CommitLimit == 0 || s.CommitTotal <= s.CommitLimit, Fmt.Bytes(s.CommitTotal) + " / " + Fmt.Bytes(s.CommitLimit), ref pass);

                bool estimatesValid = true;
                string bad = "";
                foreach (ProcInfo p in s.Processes)
                {
                    if (p.ReclaimEstimate < 0 || (ulong)p.ReclaimEstimate > p.WorkingSet)
                    {
                        estimatesValid = false;
                        bad = p.Name + " " + p.ReclaimEstimate + " / " + p.WorkingSet;
                        break;
                    }
                }
                Check(sb, "reclaim estimates bounded", estimatesValid, bad, ref pass);

                int cpuKnown = 0;
                foreach (ProcInfo p in s.Processes) if (p.CpuPercent >= 0) cpuKnown++;
                Check(sb, "CPU sample readable", cpuKnown >= 10, cpuKnown.ToString() + " processes", ref pass);

                sb.AppendLine();
                sb.AppendLine("=== system snapshot ===");
                sb.AppendLine(ReportWriter.BuildSummary(s, 12));
            }

            IntPtr self = Native.GetCurrentProcess();
            uint original = Native.GetMemoryPriority(self);
            bool setLow = Native.SetMemoryPriority(self, 4);
            uint low = Native.GetMemoryPriority(self);
            bool restore = Native.SetMemoryPriority(self, 5);
            uint normal = Native.GetMemoryPriority(self);
            Check(sb, "memory priority query", original >= 1 && original <= 5, "priority=" + original, ref pass);
            Check(sb, "memory priority set", setLow && low == 4, "set=" + setLow + " priority=" + low, ref pass);
            Check(sb, "memory priority restore", restore && normal == 5, "set=" + restore + " priority=" + normal, ref pass);

            sb.AppendLine();
            sb.AppendLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
            ReportWriter.WriteTextFile(output, sb.ToString());
            Console.WriteLine((pass ? "PASS" : "FAIL") + " — " + output);
            return pass ? 0 : 1;
        }

        private static int TrimTest(string output)
        {
            StringBuilder sb = new StringBuilder();
            bool pass = true;
            sb.AppendLine("MemLens trim-test");
            sb.AppendLine("This launches a private notepad.exe, trims it once, then closes it.");
            sb.AppendLine();

            Process process = null;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo("notepad.exe");
                start.UseShellExecute = true;
                process = Process.Start(start);
                if (process == null) throw new Exception("notepad.exe did not start");
                Thread.Sleep(1200);

                ProcInfo info = new ProcInfo();
                info.Pid = process.Id;
                info.Name = "notepad";
                TrimResult result = new MemScanner().Trim(info);

                sb.AppendLine("PID: " + info.Pid);
                sb.AppendLine("Open/trim: " + result.Ok);
                sb.AppendLine("Error: " + Native.Win32Text(result.Error));
                sb.AppendLine("Working set before: " + Fmt.Bytes(result.Before));
                sb.AppendLine("Working set after:  " + Fmt.Bytes(result.After));
                sb.AppendLine("Measured reduction: " + Fmt.Bytes((ulong)result.Freed));
                Check(sb, "notepad started", true, "", ref pass);
                Check(sb, "EmptyWorkingSet succeeded", result.Ok, Native.Win32Text(result.Error), ref pass);
                Check(sb, "working set decreased", result.Freed > 0 || (result.Before > 0 && result.After < result.Before), Fmt.Bytes(result.Before) + " -> " + Fmt.Bytes(result.After), ref pass);
            }
            catch (Exception ex)
            {
                Check(sb, "trim test completed", false, ex.Message, ref pass);
            }
            finally
            {
                if (process != null)
                {
                    try { process.Kill(); } catch { }
                    try { process.WaitForExit(2000); } catch { }
                    try { process.Dispose(); } catch { }
                }
            }

            sb.AppendLine();
            sb.AppendLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
            ReportWriter.WriteTextFile(output, sb.ToString());
            Console.WriteLine((pass ? "PASS" : "FAIL") + " — " + output);
            return pass ? 0 : 1;
        }

        private static int PurgeTest(string output, int command)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MemLens standby purge test");
            sb.AppendLine("Admin: " + Native.IsAdministrator());
            sb.AppendLine("Mode: " + (command == Native.MemoryPurgeStandbyList ? "all standby" : "low-priority standby"));
            bool knownBefore;
            ulong[] before = Native.QueryMemoryList(out knownBefore);
            int status;
            bool ok = Native.PurgeStandby(command, out status);
            bool knownAfter;
            ulong[] after = Native.QueryMemoryList(out knownAfter);
            sb.AppendLine("Result: " + ok);
            sb.AppendLine("NTSTATUS: " + Native.NtStatusText(status));
            if (knownBefore && knownAfter)
                sb.AppendLine("Standby pages: " + SumStandby(before) + " -> " + SumStandby(after));
            sb.AppendLine();
            sb.AppendLine(ok ? "RESULT: PASS" : "RESULT: expected failure without administrator, or privilege error");
            ReportWriter.WriteTextFile(output, sb.ToString());
            Console.WriteLine((ok ? "PASS" : "FAIL") + " — " + output);
            return ok ? 0 : 1;
        }

        private static ulong SumStandby(ulong[] values)
        {
            if (values == null) return 0;
            ulong total = 0;
            for (int i = 5; i <= 12 && i < values.Length; i++) total += values[i];
            return total;
        }

        private static int CountAccessible(List<ProcInfo> processes)
        {
            int count = 0;
            foreach (ProcInfo p in processes) if (p.Accessible) count++;
            return count;
        }

        private static void Check(StringBuilder sb, string name, bool condition, string detail, ref bool pass)
        {
            if (!condition) pass = false;
            sb.AppendLine((condition ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
        }

        private static string ResolveOutput(string[] args, int index, string defaultName)
        {
            if (args.Length > index && !args[index].StartsWith("--"))
                return Path.GetFullPath(args[index]);
            return Path.Combine(Path.GetTempPath(), defaultName);
        }

        private static string Usage()
        {
            return
                "MemLens 内存透视 1.0.0\r\n" +
                "\r\n" +
                "用法:\r\n" +
                "  MemLens.exe                       启动图形界面\r\n" +
                "  MemLens.exe --scan [报告.txt]     只扫描并输出文本报告\r\n" +
                "  MemLens.exe --export [报告.csv]   扫描并输出 CSV\r\n" +
                "  MemLens.exe --selftest [报告.txt] 运行内置自检\r\n" +
                "  MemLens.exe --trim-test [报告.txt] 启动记事本并实测一次工作集回收\r\n" +
                "  MemLens.exe --purge-low [报告.txt] 清理低优先级待机缓存（需管理员）\r\n" +
                "  MemLens.exe --purge-all [报告.txt] 清理全部待机缓存（需管理员）\r\n" +
                "\r\n" +
                "说明:\r\n" +
                "  默认报告写在 %TEMP% 下。图形界面不会自动清理，所有操作都需要勾选并确认。\r\n";
        }
    }
}
