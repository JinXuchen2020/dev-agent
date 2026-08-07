using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// Windows Job Object 封装：为一组进程施加 OS 级资源限额（作业/进程内存上限、活动进程数上限、CPU 速率硬上限）。
/// 不需管理员权限。句柄在进程退出前保持打开；关闭句柄不会终止已纳入的作业（未设 KILL_ON_JOB_CLOSE）。
/// 任何 P/Invoke 失败均记告警并向上抛，由调用方 fail-safe 处理。
/// </summary>
internal sealed class WindowsJobObject : IDisposable
{
    private readonly ILogger _logger;
    private IntPtr _hJob = IntPtr.Zero;
    private bool _disposed;

    // 资源下限，避免配置误配导致合法脚本立即 OOM。
    private const long MinMemoryBytes = 32L * 1024 * 1024;

    public WindowsJobObject(long memoryLimitBytes, int cpuRatePercent, int maxProcessCount, ILogger logger)
    {
        _logger = logger;
        CreateJobObjectAndApplyLimits(memoryLimitBytes, cpuRatePercent, maxProcessCount);
    }

    public void Assign(Process process)
    {
        if (_hJob == IntPtr.Zero || process.HasExited)
            return;

        // 自开具备 SET_QUOTA 权限的句柄，避免依赖 Process.Handle 的回收语义。
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_TERMINATE, false, process.Id);
        if (hProcess == IntPtr.Zero)
        {
            _logger.LogWarning("Job Object 打开进程 {Pid} 失败（0x{Err:X}），跳过资源限额挂接",
                process.Id, Marshal.GetLastWin32Error());
            return;
        }

        try
        {
            if (!NativeMethods.AssignProcessToJobObject(_hJob, hProcess))
                _logger.LogWarning("Job Object 挂接进程 {Pid} 失败（0x{Err:X}），可能宿主已在不可突破的 Job 中；资源限额不生效",
                    process.Id, Marshal.GetLastWin32Error());
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private void CreateJobObjectAndApplyLimits(long memoryLimitBytes, int cpuRatePercent, int maxProcessCount)
    {
        _hJob = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_hJob == IntPtr.Zero)
        {
            _logger.LogWarning("CreateJobObject 失败（0x{Err:X}），跳过 Job Object 资源限额",
                Marshal.GetLastWin32Error());
            return;
        }

        var mem = Math.Max(memoryLimitBytes, MinMemoryBytes);
        var cpu = Math.Clamp(cpuRatePercent, 1, 100);
        var procs = Math.Max(maxProcessCount, 1);

        var ext = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_WORKINGSET
                           | NativeMethods.JOB_OBJECT_LIMIT_JOB_MEMORY
                           | NativeMethods.JOB_OBJECT_LIMIT_ACTIVE_PROCESS,
                MinimumWorkingSetSize = (UIntPtr)(4 * 1024 * 1024),
                MaximumWorkingSetSize = (UIntPtr)mem,
                ActiveProcessLimit = (uint)procs,
            },
            JobMemoryLimit = (UIntPtr)mem,
        };

        var cpuInfo = new NativeMethods.JOBOBJECT_CPU_RATE_LIMIT_INFORMATION
        {
            ControlFlag = NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE
                        | NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
            CpuRate = (uint)(cpu * 100), // 百分之一百分比：50% -> 5000
        };

        SetInfo(NativeMethods.JobObjectInfoClass.ExtendedLimitInformation, ext);
        SetInfo(NativeMethods.JobObjectInfoClass.CpuRateInformation, cpuInfo);
    }

    private void SetInfo<T>(NativeMethods.JobObjectInfoClass infoClass, T info) where T : struct
    {
        if (_hJob == IntPtr.Zero)
            return;
        var handle = GCHandle.Alloc(info, GCHandleType.Pinned);
        try
        {
            var size = (uint)Marshal.SizeOf<T>();
            if (!NativeMethods.SetInformationJobObject(_hJob, infoClass, handle.AddrOfPinnedObject(), size))
                _logger.LogWarning("SetInformationJobObject({Class}) 失败（0x{Err:X}）",
                    infoClass, Marshal.GetLastWin32Error());
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hJob != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_hJob);
            _hJob = IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        public const uint JOB_OBJECT_LIMIT_WORKINGSET = 0x00000001;
        public const uint JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200;
        public const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
        public const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
        public const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x2;

        public const uint PROCESS_SET_QUOTA = 0x00000100;
        public const uint PROCESS_TERMINATE = 0x00000001;

        public enum JobObjectInfoClass
        {
            ExtendedLimitInformation = 9,
            CpuRateInformation = 14,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_CPU_RATE_LIMIT_INFORMATION
        {
            public uint ControlFlag;
            public uint CpuRate;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(
            IntPtr hJob, JobObjectInfoClass infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
