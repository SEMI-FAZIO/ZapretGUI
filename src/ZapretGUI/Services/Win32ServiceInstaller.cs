using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace ZapretGUI.Services;

/// <summary>
/// Installs / configures a Windows service via the native advapi32 SCM API.
/// Avoids the brittle sc.exe quoting that breaks for binPath values containing spaces and quotes.
/// </summary>
public static class Win32ServiceInstaller
{
    // SCM access rights
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    private const uint SC_MANAGER_CONNECT        = 0x0001;

    // Service access rights
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint DELETE = 0x00010000;

    // Service type / start
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_AUTO_START = 0x00000002;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;

    // ChangeServiceConfig2 levels
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    private const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    // Failure action types
    private const int SC_ACTION_NONE = 0;
    private const int SC_ACTION_RESTART = 1;
    private const int SC_ACTION_REBOOT = 2;
    private const int SC_ACTION_RUN_COMMAND = 3;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint dwAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(
        IntPtr hSCManager,
        string lpServiceName,
        string lpDisplayName,
        uint dwDesiredAccess,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ChangeServiceConfig2(IntPtr hService, uint dwInfoLevel, IntPtr lpInfo);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, [In] string[]? lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_DESCRIPTION
    {
        public IntPtr lpDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION
    {
        public int Type;
        public int Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_FAILURE_ACTIONS
    {
        public int dwResetPeriod;
        public IntPtr lpRebootMsg;
        public IntPtr lpCommand;
        public int cActions;
        public IntPtr lpsaActions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_DELAYED_AUTO_START_INFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fDelayedAutostart;
    }

    public sealed class ServiceInstallResult
    {
        public bool Created { get; init; }
        public bool Started { get; init; }
        public string Message { get; init; } = "";
        public int? StartErrorCode { get; init; }
    }

    public static ServiceInstallResult Install(string serviceName, string displayName, string description,
        string binPathWithArgs, bool autoStart = true, bool startNow = true)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
        if (scm == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed.");

        try
        {
            // If the service already exists, open it and reconfigure binPath rather than fail.
            IntPtr svc = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            bool created = false;

            if (svc == IntPtr.Zero)
            {
                svc = CreateService(
                    scm,
                    serviceName,
                    displayName,
                    SERVICE_ALL_ACCESS,
                    SERVICE_WIN32_OWN_PROCESS,
                    autoStart ? SERVICE_AUTO_START : SERVICE_DEMAND_START,
                    SERVICE_ERROR_NORMAL,
                    binPathWithArgs,
                    null, IntPtr.Zero, null, null, null);

                if (svc == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateService failed.");
                created = true;
            }

            try
            {
                SetDescription(svc, description);
                SetFailureActions(svc, resetPeriodSec: 86400, restartDelayMs: 60_000, attempts: 3);
                if (autoStart) SetDelayedAutoStart(svc, true);

                int? startErr = null;
                bool started = false;
                if (startNow)
                {
                    if (!StartService(svc, 0, null))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == 1056) // ERROR_SERVICE_ALREADY_RUNNING
                            started = true;
                        else
                            startErr = err;
                    }
                    else
                    {
                        started = true;
                    }
                }

                return new ServiceInstallResult
                {
                    Created = created,
                    Started = started,
                    StartErrorCode = startErr,
                    Message = (created ? "Service created" : "Service reconfigured") +
                              (started ? " and started" : startErr is null ? "" : $" (start failed, code {startErr})"),
                };
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static void Uninstall(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return;
        try
        {
            IntPtr svc = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero) return;
            try
            {
                try
                {
                    using var sc = new ServiceController(serviceName);
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                    }
                }
                catch { }
                DeleteService(svc);
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    private static void SetDescription(IntPtr svc, string description)
    {
        IntPtr descPtr = Marshal.StringToHGlobalUni(description);
        try
        {
            var desc = new SERVICE_DESCRIPTION { lpDescription = descPtr };
            IntPtr buf = Marshal.AllocHGlobal(Marshal.SizeOf<SERVICE_DESCRIPTION>());
            try
            {
                Marshal.StructureToPtr(desc, buf, false);
                ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { Marshal.FreeHGlobal(descPtr); }
    }

    private static void SetFailureActions(IntPtr svc, int resetPeriodSec, int restartDelayMs, int attempts)
    {
        var actions = new SC_ACTION[attempts];
        for (int i = 0; i < attempts; i++)
            actions[i] = new SC_ACTION { Type = SC_ACTION_RESTART, Delay = restartDelayMs };

        int actionSize = Marshal.SizeOf<SC_ACTION>();
        IntPtr actionsPtr = Marshal.AllocHGlobal(actionSize * attempts);
        try
        {
            for (int i = 0; i < attempts; i++)
                Marshal.StructureToPtr(actions[i], actionsPtr + i * actionSize, false);

            var fa = new SERVICE_FAILURE_ACTIONS
            {
                dwResetPeriod = resetPeriodSec,
                lpRebootMsg = IntPtr.Zero,
                lpCommand = IntPtr.Zero,
                cActions = attempts,
                lpsaActions = actionsPtr,
            };

            IntPtr faBuf = Marshal.AllocHGlobal(Marshal.SizeOf<SERVICE_FAILURE_ACTIONS>());
            try
            {
                Marshal.StructureToPtr(fa, faBuf, false);
                ChangeServiceConfig2(svc, SERVICE_CONFIG_FAILURE_ACTIONS, faBuf);
            }
            finally { Marshal.FreeHGlobal(faBuf); }
        }
        finally { Marshal.FreeHGlobal(actionsPtr); }
    }

    private static void SetDelayedAutoStart(IntPtr svc, bool delayed)
    {
        var s = new SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = delayed };
        IntPtr buf = Marshal.AllocHGlobal(Marshal.SizeOf<SERVICE_DELAYED_AUTO_START_INFO>());
        try
        {
            Marshal.StructureToPtr(s, buf, false);
            ChangeServiceConfig2(svc, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, buf);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
