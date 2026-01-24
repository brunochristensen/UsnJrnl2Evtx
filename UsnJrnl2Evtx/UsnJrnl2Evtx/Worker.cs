using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace UsnJrnl2Evtx
{
    public class Worker(ILogger<Worker> logger) : BackgroundService
    {
        // For CreateFile
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        // For DeviceIoControl
        private const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr InBuffer,
            uint nInBufferSize,
            IntPtr OutBuffer,
            uint nOutBufferSize,
            out uint pBytesReturned,
            IntPtr lpOverlapped);

        [StructLayout(LayoutKind.Sequential)]
        record struct USN_JOURNAL_DATA_V0 
        (
            ulong UsnJournalID,
            long FirstUsn,
            long NextUsn,
            long LowestValidUsn,
            long MaxUsn,
            ulong MaximumSize,
            ulong AllocationDelta
        );

        [StructLayout(LayoutKind.Sequential)]
        record struct READ_USN_JOURNAL_DATA_V0
        (
            long StartUsn,
            uint ReasonMask,
            uint ReturnOnlyOnClose,
            ulong Timeout,
            ulong BytesToWaitFor,
            ulong UsnJournalID
        );

        [StructLayout(LayoutKind.Sequential)]
        record struct USN_RECORD_V2
        (
            uint RecordLength,
            ushort MajorVersion,
            ushort MinorVersion,
            ulong FileReferenceNumber,
            ulong ParentFileReferenceNumber,
            long Usn,
            long TimeStamp,
            uint Reason,
            uint SourceInfo,
            uint SecurityId,
            uint FileAttributes,
            ushort FileNameLength,
            ushort FileNameOffset,
            char FileName
        );

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var journalInfo = GetJournalInfo('C');
            if( journalInfo != null)
            {
                logger.LogInformation("Journal ID: {id}, NextUSN: {usn}",
                    journalInfo.Value.UsnJournalID,
                    journalInfo.Value.NextUsn);
            } 
            else
            {
                logger.LogError("Failed to query USN journal. Try running as Admin?");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }
                await Task.Delay(1000, stoppingToken);
            }
        }

        private USN_JOURNAL_DATA_V0? GetJournalInfo(char driveLetter)
        {
            String volumePath = "\\\\.\\" + driveLetter + ":";

            IntPtr handle = CreateFile(
                volumePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == new IntPtr(-1)) 
            {
                return null;
            }

            int size = Marshal.SizeOf<USN_JOURNAL_DATA_V0>();
            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                bool success = DeviceIoControl(
                    handle,
                    FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero,
                    0,
                    buffer,
                    (uint)size,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!success) {
                    return null;
                }

                return Marshal.PtrToStructure<USN_JOURNAL_DATA_V0>(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                CloseHandle(handle);
            }
        }
    }
}
