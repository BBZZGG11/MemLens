#requires -Version 5.1
<#
MemLens 只读诊断脚本
用法:
  powershell -ExecutionPolicy Bypass -File .\Get-MemoryDiagnosis.ps1
  powershell -ExecutionPolicy Bypass -File .\Get-MemoryDiagnosis.ps1 -Top 30 -ExportCsv
说明:
  1. 不修改系统，不回收内存。
  2. “可回收”是启发式估算；待机缓存是 Windows 的正常文件缓存，不是故障。
  3. 需要 Windows 10 / PowerShell 5.1；不需要管理员。
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 200)]
    [int]$Top = 20,
    [switch]$ExportCsv,
    [string]$CsvPath,
    [switch]$NoColor
)

$ErrorActionPreference = 'Stop'

$native = @'
using System;
using System.Runtime.InteropServices;

public static class MemDiagNative
{
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
        public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable;
        public UIntPtr SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS_EX2
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage, PeakPagefileUsage, PrivateUsage;
        public UIntPtr PrivateWorkingSetSize, SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_PRIORITY_INFORMATION { public uint MemoryPriority; }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint LowDateTime, HighDateTime; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_INFO
    {
        public ushort wProcessorArchitecture, wReserved;
        public uint dwPageSize;
        public IntPtr lpMinimumApplicationAddress, lpMaximumApplicationAddress, dwActiveProcessorMask;
        public uint dwNumberOfProcessors, dwProcessorType, dwAllocationGranularity;
        public ushort dwProcessorLevel, dwProcessorRevision;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

    [DllImport("psapi.dll", SetLastError = true)]
    static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint cb);

    [DllImport("psapi.dll", SetLastError = true)]
    static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX2 counters, uint cb);

    [DllImport("ntdll.dll")]
    static extern int NtQuerySystemInformation(int cls, IntPtr buffer, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr process, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessInformation(IntPtr process, int cls, ref MEMORY_PRIORITY_INFORMATION info, uint size);

    [DllImport("kernel32.dll")]
    static extern void GetSystemInfo(out SYSTEM_INFO info);

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static ulong[] Global()
    {
        MEMORYSTATUSEX m = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(m)) return null;
        return new ulong[] { m.ullTotalPhys, m.ullAvailPhys, m.ullTotalPageFile, m.ullAvailPageFile, m.dwMemoryLoad };
    }

    public static ulong[] Perf()
    {
        PERFORMANCE_INFORMATION p = new PERFORMANCE_INFORMATION();
        p.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
        if (!GetPerformanceInfo(ref p, p.cb)) return null;
        return new ulong[] {
            p.CommitTotal.ToUInt64(), p.CommitLimit.ToUInt64(), p.SystemCache.ToUInt64(),
            p.KernelPaged.ToUInt64(), p.KernelNonpaged.ToUInt64(), p.PageSize.ToUInt64(),
            p.ProcessCount, p.ThreadCount, p.HandleCount
        };
    }

    public static int PageSize()
    {
        SYSTEM_INFO si;
        GetSystemInfo(out si);
        return si.dwPageSize > 0 ? (int)si.dwPageSize : 4096;
    }

    // zero, free, modified, standby
    public static ulong[] MemoryList()
    {
        int fields = 22;
        int size = fields * IntPtr.Size;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++) Marshal.WriteByte(buffer, i, 0);
            int returned;
            if (NtQuerySystemInformation(0x50, buffer, size, out returned) != 0) return null;
            ulong[] v = new ulong[fields];
            for (int i = 0; i < fields; i++)
                v[i] = IntPtr.Size == 8 ? (ulong)Marshal.ReadInt64(buffer, i * 8) : (uint)Marshal.ReadInt32(buffer, i * 4);
            ulong standby = 0;
            for (int i = 5; i <= 12; i++) standby += v[i];
            return new ulong[] { v[0], v[1], v[2], standby };
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // working set, private working set, private commit, shared commit
    public static ulong[] ProcessMem(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            PROCESS_MEMORY_COUNTERS_EX2 c = new PROCESS_MEMORY_COUNTERS_EX2();
            c.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX2));
            if (!GetProcessMemoryInfo(h, ref c, c.cb)) return null;
            return new ulong[] {
                c.WorkingSetSize.ToUInt64(), c.PrivateWorkingSetSize.ToUInt64(),
                c.PrivateUsage.ToUInt64(), c.SharedCommitUsage.ToUInt64()
            };
        }
        finally { CloseHandle(h); }
    }

    public static uint Priority(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return 0;
        try
        {
            MEMORY_PRIORITY_INFORMATION info = new MEMORY_PRIORITY_INFORMATION();
            uint size = (uint)Marshal.SizeOf(typeof(MEMORY_PRIORITY_INFORMATION));
            if (!GetProcessInformation(h, 0, ref info, size)) return 0;
            return info.MemoryPriority;
        }
        finally { CloseHandle(h); }
    }

    public static ulong CpuTime(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return 0;
        try
        {
            FILETIME creation, exit, kernel, user;
            if (!GetProcessTimes(h, out creation, out exit, out kernel, out user)) return 0;
            return ((ulong)kernel.HighDateTime << 32 | kernel.LowDateTime) + ((ulong)user.HighDateTime << 32 | user.LowDateTime);
        }
        finally { CloseHandle(h); }
    }
}
'@

Add-Type -TypeDefinition $native -Language CSharp

function Format-MB([double]$mb) {
    if ($mb -ge 1024) { return ('{0:0.00} GB' -f ($mb / 1024)) }
    if ($mb -ge 100) { return ('{0:0} MB' -f $mb) }
    return ('{0:0.0} MB' -f $mb)
}

function Write-Line([string]$text, [ConsoleColor]$color = [ConsoleColor]::Gray) {
    if ($NoColor) { Write-Host $text }
    else { Write-Host $text -ForegroundColor $color }
}

$global = [MemDiagNative]::Global()
if ($null -eq $global) { throw 'GlobalMemoryStatusEx 失败。' }
$perf = [MemDiagNative]::Perf()
$list = [MemDiagNative]::MemoryList()
$page = [MemDiagNative]::PageSize()

$total = [double]$global[0]
$avail = [double]$global[1]
$used = $total - $avail
$standby = if ($null -ne $list) { [double]$list[3] * $page } else { 0 }
$modified = if ($null -ne $list) { [double]$list[2] * $page } else { 0 }
$free = if ($null -ne $list) { [double]($list[0] + $list[1]) * $page } else { 0 }
$commit = if ($null -ne $perf) { [double]$perf[0] * $page } else { 0 }
$commitLimit = if ($null -ne $perf) { [double]$perf[1] * $page } else { 0 }

$current = Get-Process -Id $PID
$currentSession = $current.SessionId
$protected = @(
    'system','system idle process','registry','memory compression','secure system',
    'smss','csrss','wininit','winlogon','services','lsass','dwm','fontdrvhost',
    'sihost','ctfmon','explorer','svchost','audiodg','msmpeng','securityhealthservice',
    'wudfhost','taskhostw','dllhost','runtimebroker','shellexperiencehost',
    'startmenuexperiencehost','applicationframehost','textinputhost','lockapp','searchhost'
)

$mb = 1048576.0
$sampleStart = Get-Date
$cpu0 = @{}
foreach ($p in Get-Process -ErrorAction SilentlyContinue) {
    if ($p.Id -eq 0) { continue }
    $cpu0[$p.Id] = [MemDiagNative]::CpuTime($p.Id)
}
Start-Sleep -Milliseconds 650
$elapsed = ((Get-Date) - $sampleStart).TotalSeconds
if ($elapsed -le 0) { $elapsed = 0.65 }
$cores = [Environment]::ProcessorCount

$rows = New-Object System.Collections.ArrayList
foreach ($p in Get-Process -ErrorAction SilentlyContinue) {
    if ($p.Id -eq 0) { continue }
    $name = $p.ProcessName
    $session = 0
    try { $session = $p.SessionId } catch { }
    $mem = [MemDiagNative]::ProcessMem($p.Id)
    if ($null -eq $mem) { continue }

    $ws = [double]$mem[0]
    $privWs = [double]$mem[1]
    $priv = [double]$mem[2]
    $shared = [math]::Max(0, $ws - $privWs)

    $cpu = -1.0
    $c1 = [MemDiagNative]::CpuTime($p.Id)
    if ($cpu0.ContainsKey($p.Id) -and $c1 -ge $cpu0[$p.Id]) {
        $cpu = (($c1 - $cpu0[$p.Id]) / 10000000.0) / $elapsed / $cores * 100.0
        if ($cpu -gt 100) { $cpu = 100 }
    }

    $priority = [MemDiagNative]::Priority($p.Id)
    $lower = $name.ToLowerInvariant()
    $critical = ($p.Id -le 4) -or ($session -ne $currentSession) -or ($protected -contains $lower) -or ($p.Id -eq $PID)
    $canAct = -not $critical

    $factor = if ($cpu -ge 0 -and $cpu -lt 1) { 0.35 } elseif ($cpu -lt 0) { 0.18 } else { 0.08 }
    $reclaim = 0.0
    if ($ws -ge 12 * $mb) {
        $reclaim = $shared * 0.90 + $privWs * $factor
        $reclaim = [math]::Min($reclaim, $ws - 4 * $mb)
        if ($reclaim -lt 0) { $reclaim = 0 }
    }

    $note = '占用小'
    if ($p.Id -eq $PID) { $note = '本脚本' }
    elseif ($session -ne $currentSession) { $note = '其他会话' }
    elseif ($critical) { $note = '系统关键' }
    elseif ($cpu -ge 15) { $note = '活跃，慎回收' }
    elseif ($cpu -ge 0 -and $cpu -lt 1 -and $reclaim -ge 100 * $mb) { $note = '空闲，可限制' }
    elseif ($reclaim -ge 20 * $mb) { $note = '有可回收页' }

    [void]$rows.Add([pscustomobject]@{
        进程 = $name
        PID = $p.Id
        会话 = $session
        可操作 = $canAct
        内存优先级 = if ($priority -ge 1 -and $priority -le 5) { $priority } else { $null }
        工作集MB = [math]::Round($ws / $mb, 1)
        私有常驻MB = [math]::Round($privWs / $mb, 1)
        共享页MB = [math]::Round($shared / $mb, 1)
        私有提交MB = [math]::Round($priv / $mb, 1)
        可回收MB = [math]::Round($reclaim / $mb, 1)
        CPU百分比 = if ($cpu -ge 0) { [math]::Round($cpu, 1) } else { $null }
        状态 = $note
    })
}

$sumProcess = ($rows | Where-Object { $_.可操作 } | Measure-Object 可回收MB -Sum).Sum
if ($null -eq $sumProcess) { $sumProcess = 0 }

Write-Line ''
Write-Line 'MemLens 内存诊断（只读）' Cyan
Write-Line ('时间: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) DarkGray
Write-Line ('系统: Windows ' + [Environment]::OSVersion.Version + '  ' + $(if ([Environment]::Is64BitOperatingSystem) { '64-bit' } else { '32-bit' })) DarkGray
Write-Line ''
Write-Line '== 系统内存 ==' Cyan
Write-Line ('物理内存      ' + (Format-MB ($total / $mb)))
Write-Line ('正在使用      ' + (Format-MB ($used / $mb)) + ('  ({0:0.0}%)' -f ($used / $total * 100)))
Write-Line ('可用内存      ' + (Format-MB ($avail / $mb)) + ('  ({0:0.0}%)' -f ($avail / $total * 100)))
Write-Line ('待机缓存      ' + (Format-MB ($standby / $mb)) + '  (Windows 文件缓存，不是故障)') Yellow
Write-Line ('已修改页      ' + (Format-MB ($modified / $mb)))
Write-Line ('零页/空闲     ' + (Format-MB ($free / $mb)))
if ($commitLimit -gt 0) {
    Write-Line ('提交内存      ' + (Format-MB ($commit / $mb)) + ' / ' + (Format-MB ($commitLimit / $mb)) + ('  ({0:0.0}%)' -f ($commit / $commitLimit * 100)))
}
Write-Line ''
Write-Line '== 可回收估算 ==' Cyan
Write-Line ('系统待机 + 已修改   ' + (Format-MB (($standby + $modified) / $mb)) + '  (精确统计)') Yellow
Write-Line ('后台进程逐项合计    ' + (Format-MB ($sumProcess)) + '  (启发式估算，含共享页)') Yellow
Write-Line ('合计参考            ' + (Format-MB (($standby + $modified) / $mb + $sumProcess))) Yellow
Write-Line ''
Write-Line '说明: 待机缓存会在内存紧张时自动回收；进程可回收量不是精确的“无效内存”。' DarkGray
Write-Line '      私有提交持续增长才更接近泄漏；单次工作集大不等于异常。' DarkGray
Write-Line ''

Write-Line ('== Top ' + $Top + ' 进程（按可回收估算）==') Cyan
$rows | Sort-Object 可回收MB -Descending | Select-Object -First $Top |
    Format-Table 进程, PID, 工作集MB, 私有常驻MB, 可回收MB, CPU百分比, 内存优先级, 状态 -AutoSize |
    Out-String -Width 200 | Write-Host

if ($ExportCsv) {
    if ([string]::IsNullOrWhiteSpace($CsvPath)) {
        $CsvPath = Join-Path (Get-Location) ('MemLens_diagnosis_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.csv')
    }
    $rows | Sort-Object 可回收MB -Descending |
        Select-Object 进程, PID, 会话, 可操作, 内存优先级, 工作集MB, 私有常驻MB, 共享页MB, 私有提交MB, 可回收MB, CPU百分比, 状态 |
        Export-Csv -Path $CsvPath -NoTypeInformation -Encoding UTF8
    Write-Line ('CSV: ' + $CsvPath) Green
}
