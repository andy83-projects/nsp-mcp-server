using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nsp.Mcp.Server;

/// <summary>Kill the owned worker if the supervisor dies, including an ungraceful task stop.</summary>
internal sealed class WorkerJob : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Basic { public long ProcessTime, JobTime; public uint Flags; public UIntPtr Min, Max; public uint Processes; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct Counters { public ulong A, B, C, D, E, F; }
    [StructLayout(LayoutKind.Sequential)] private struct Limits { public Basic Basic; public Counters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref Limits limits, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    private readonly SafeFileHandle handle = CreateJobObject(IntPtr.Zero, null);
    public WorkerJob(Process worker)
    {
        var limits = new Limits { Basic = new Basic { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (handle.IsInvalid || !SetInformationJobObject(handle, 9, ref limits, Marshal.SizeOf<Limits>()) || !AssignProcessToJobObject(handle, worker.Handle))
        { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, "Cannot establish owned-worker lifetime containment."); }
    }
    public void Dispose() => handle.Dispose();
}
