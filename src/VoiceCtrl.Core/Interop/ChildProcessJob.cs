using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoiceCtrl.Core.Interop;

/// <summary>
/// A Windows job object that kills every process put in it when VoiceCtrl exits, however it exits.
/// Without it a crashed or force-quit VoiceCtrl would leave its <c>llama-server</c> running, holding
/// the language model's memory until the next reboot.
/// </summary>
internal sealed class ChildProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly nint _job;

    public ChildProcessJob()
    {
        _job = CreateJobObject(0, null);
        if (_job == 0)
        {
            throw new InvalidOperationException("Could not create a job object.");
        }

        ExtendedLimitInformation info = default;
        info.BasicLimitInformation.LimitFlags = KillOnJobClose;

        int size = Marshal.SizeOf<ExtendedLimitInformation>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
            if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                throw new InvalidOperationException("Could not set the job object's limits.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Puts <paramref name="process"/> in the job. A failure is ignored: the process then
    /// simply is not tied to ours, which is what happens without a job at all.</summary>
    public void Add(Process process) => AssignProcessToJobObject(_job, process.Handle);

    public void Dispose() => CloseHandle(_job);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int informationClass, nint information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformationStruct
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformationStruct BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
