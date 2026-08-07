using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// Windows AppContainer P/Invoke 封装：创建无能力（无 internetClient）的 AppContainer profile 启动解释器，
/// 真实阻断出网。任何失败（权限 / 平台 / 解释器文件系统不可达）一律返回 <c>Success=false</c>，由调用方 fail-safe 回退。
/// AppContainer 默认禁止文件访问，故调用方以 stdin 管道把代码喂给解释器（<c>python -</c> / <c>node -</c>），
/// 避免依赖临时文件的可读性。
/// </summary>
internal sealed class WindowsAppContainer : IDisposable
{
    private const string ProfilePrefix = "AgentPlatformSandbox_";
    private const int HResultAlreadyExists = unchecked((int)0x800700B7); // 0x800700B7

    private readonly ILogger _logger;
    private readonly string _profileName;
    private IntPtr _sid = IntPtr.Zero;
    private bool _profileCreated;

    public WindowsAppContainer(ILogger logger, string? profileName = null)
    {
        _logger = logger;
        _profileName = profileName ?? (ProfilePrefix + Guid.NewGuid().ToString("N"));
    }

    /// <summary>创建 AppContainer profile（无网络/文件能力）。已存在则先删后建。返回是否成功。</summary>
    public bool TryCreateProfile()
    {
        if (_profileCreated)
            return _sid != IntPtr.Zero;

        try
        {
            var hr = NativeMethods.CreateAppContainerProfile(
                _profileName, _profileName, _profileName, Array.Empty<NativeMethods.SID_AND_ATTRIBUTES>(), 0, out _sid);
            if (hr == HResultAlreadyExists)
            {
                NativeMethods.DeleteAppContainerProfile(_profileName);
                hr = NativeMethods.CreateAppContainerProfile(
                    _profileName, _profileName, _profileName, Array.Empty<NativeMethods.SID_AND_ATTRIBUTES>(), 0, out _sid);
            }

            if (hr != 0 || _sid == IntPtr.Zero)
            {
                _logger.LogWarning("创建 AppContainer profile 失败（HRESULT=0x{HR:X}），网络隔离回退到环境标记缓解项",
                    unchecked((uint)hr));
                return false;
            }

            _profileCreated = true;
            return true;
        }
        catch (Exception ex)
        {
            // 含 EntryPointNotFoundException（本 Windows 构建未导出该 API）等任何 P/Invoke 异常：
            // 一律视为 AppContainer 不可用，透明回退到常规启动 + 环境标记缓解项，绝不阻断代码执行。
            _logger.LogWarning(ex, "AppContainer profile P/Invoke 失败（可能 OS 不支持），网络隔离回退到环境标记缓解项");
            return false;
        }
    }

    public void DeleteProfile()
    {
        if (!_profileCreated)
            return;
        try { NativeMethods.DeleteAppContainerProfile(_profileName); }
        catch (Exception ex) { _logger.LogWarning(ex, "删除 AppContainer profile 失败"); }
        finally { _profileCreated = false; _sid = IntPtr.Zero; }
    }

    /// <summary>
    /// 在 AppContainer 内启动进程并接入 stdin/stdout/stderr 管道。
    /// 成功返回 <see cref="AppContainerLaunchResult"/>（Success=true，已关闭父侧多余句柄，仅保留子进程写 stdin 的句柄供调用方写代码）；
    /// 任何失败返回 Success=false 的结果。
    /// </summary>
    public AppContainerLaunchResult Launch(string exePath, string commandLine)
    {
        var result = new AppContainerLaunchResult();
        if (!_profileCreated || _sid == IntPtr.Zero)
            return result;

        try
        {
            var saInherit = MakeInheritableSa();
            var saNoInherit = MakeNonInheritableSa();

            // stdout / stderr：父读、子写（写端可继承）
            if (!NativeMethods.CreatePipe(out var hStdoutRead, out var hStdoutWrite, ref saInherit, 0) ||
                !NativeMethods.CreatePipe(out var hStderrRead, out var hStderrWrite, ref saInherit, 0) ||
                !NativeMethods.CreatePipe(out var hStdinRead, out var hStdinWrite, ref saInherit, 0))
            {
                _logger.LogWarning("AppContainer 管道创建失败（0x{Err:X}）", Marshal.GetLastWin32Error());
                return result;
            }

            // 读端不可继承，避免子进程继承
            NativeMethods.SetHandleInformation(hStdoutRead, NativeMethods.HANDLE_FLAG_INHERIT, 0);
            NativeMethods.SetHandleInformation(hStderrRead, NativeMethods.HANDLE_FLAG_INHERIT, 0);
            NativeMethods.SetHandleInformation(hStdinRead, NativeMethods.HANDLE_FLAG_INHERIT, 0);

            var si = new NativeMethods.STARTUPINFOEX
            {
                StartupInfo = new NativeMethods.STARTUPINFO
                {
                    cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                    dwFlags = NativeMethods.STARTF_USESTDHANDLES,
                    hStdInput = hStdinRead,
                    hStdOutput = hStdoutWrite,
                    hStdError = hStderrWrite,
                },
            };

            // 属性列表：列出子进程需继承的 3 个句柄
            var handles = new[] { hStdinRead, hStdoutWrite, hStderrWrite };
            if (!BuildAttributeList(handles, out si.lpAttributeList, out var attrListMem))
            {
                CloseAll(hStdoutRead, hStdoutWrite, hStderrRead, hStderrWrite, hStdinRead, hStdinWrite);
                return result;
            }

            var pi = new NativeMethods.PROCESS_INFORMATION();
            var cmdLineBuilder = new StringBuilder(commandLine);
            var ok = NativeMethods.CreateProcessInAppContainer(
                exePath, cmdLineBuilder, IntPtr.Zero, IntPtr.Zero, true,
                NativeMethods.CREATE_NO_WINDOW, IntPtr.Zero, null, ref si, _sid, out pi);

            NativeMethods.DeleteProcThreadAttributeList(attrListMem);
            Marshal.FreeHGlobal(attrListMem);

            // 关闭父侧多余句柄：子进程已继承副本
            NativeMethods.CloseHandle(hStdoutWrite);
            NativeMethods.CloseHandle(hStderrWrite);
            NativeMethods.CloseHandle(hStdinRead);

            if (!ok || pi.hProcess == IntPtr.Zero)
            {
                _logger.LogWarning("CreateProcessInAppContainer 失败（0x{Err:X}），回退常规启动",
                    Marshal.GetLastWin32Error());
                NativeMethods.CloseHandle(hStdoutRead);
                NativeMethods.CloseHandle(hStderrRead);
                NativeMethods.CloseHandle(hStdinWrite);
                NativeMethods.CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero)
                    NativeMethods.CloseHandle(pi.hProcess);
                return result;
            }

            NativeMethods.CloseHandle(pi.hThread);

            var process = Process.GetProcessById(pi.dwProcessId);
            // CreateProcessInAppContainer 返回的 hProcess 是独立句柄，Process.GetProcessById 已开新句柄，
            // 此处必须关闭原始句柄，否则句柄泄漏。
            if (pi.hProcess != IntPtr.Zero)
                NativeMethods.CloseHandle(pi.hProcess);
            result.Success = true;
            result.Process = process;
            result.Stdout = new FileStream(new SafeFileHandle(hStdoutRead, ownsHandle: true), FileAccess.Read);
            result.Stderr = new FileStream(new SafeFileHandle(hStderrRead, ownsHandle: true), FileAccess.Read);
            result.StdinWrite = new SafeFileHandle(hStdinWrite, ownsHandle: true);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AppContainer 启动异常，回退常规启动");
            return result;
        }
    }

    private static bool BuildAttributeList(IntPtr[] handles, out IntPtr lpAttributeList, out IntPtr mem)
    {
        lpAttributeList = IntPtr.Zero;
        mem = IntPtr.Zero;
        if (!NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, out var size) && size == IntPtr.Zero)
            return false;
        mem = Marshal.AllocHGlobal(size);
        if (!NativeMethods.InitializeProcThreadAttributeList(mem, 1, 0, out size))
        {
            Marshal.FreeHGlobal(mem);
            return false;
        }
        var handleArray = GCHandle.Alloc(handles, GCHandleType.Pinned);
        try
        {
            var ok = NativeMethods.UpdateProcThreadAttribute(
                mem, 0, NativeMethods.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                handleArray.AddrOfPinnedObject(), (IntPtr)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero);
            lpAttributeList = mem;
            return ok;
        }
        finally
        {
            handleArray.Free();
        }
    }

    private static NativeMethods.SECURITY_ATTRIBUTES MakeInheritableSa()
        => new() { nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(), bInheritHandle = true };

    private static NativeMethods.SECURITY_ATTRIBUTES MakeNonInheritableSa()
        => new() { nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(), bInheritHandle = false };

    private static void CloseAll(params IntPtr[] handles)
    {
        foreach (var h in handles)
            if (h != IntPtr.Zero) NativeMethods.CloseHandle(h);
    }

    public void Dispose() => DeleteProfile();

    internal sealed class AppContainerLaunchResult : IDisposable
    {
        public bool Success { get; set; }
        public Process? Process { get; set; }
        public Stream? Stdout { get; set; }
        public Stream? Stderr { get; set; }
        public SafeFileHandle? StdinWrite { get; set; }

        public void Dispose()
        {
            try { StdinWrite?.Dispose(); } catch { }
            try { Stdout?.Dispose(); } catch { }
            try { Stderr?.Dispose(); } catch { }
            try { Process?.Dispose(); } catch { }
        }
    }

    private static class NativeMethods
    {
        public const uint STARTF_USESTDHANDLES = 0x00000100;
        public const uint CREATE_NO_WINDOW = 0x08000000;
        public const uint HANDLE_FLAG_INHERIT = 0x00000001;
        public const int PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;

        [StructLayout(LayoutKind.Sequential)]
        public struct SID_AND_ATTRIBUTES
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize;
            public int dwXCountChars, dwYCountChars, dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public bool bInheritHandle;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int CreateAppContainerProfile(
            string appContainerName, string displayName, string description,
            [MarshalAs(UnmanagedType.LPArray)] SID_AND_ATTRIBUTES[] capabilities,
            uint capabilityCount, out IntPtr appContainerSid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int DeleteAppContainerProfile(string appContainerName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessInAppContainer(
            string lpApplicationName, StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
            uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo, IntPtr lpProcessAppContainerSid,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(
            out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, out IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList, int dwFlags, int dwAttribute, IntPtr lpValue, IntPtr cbSize,
            IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeleteProcThreadAttributeList(IntPtr lpAttributeList);
    }
}
