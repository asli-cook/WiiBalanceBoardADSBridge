using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace BoardADSBridge;

internal readonly record struct SensorReading(ushort TopRight, ushort BottomRight, ushort TopLeft, ushort BottomLeft);

internal sealed class BalanceBoardHid : IDisposable
{
    private const ushort NintendoVendorId = 0x057E;
    private const ushort BalanceBoardProductId = 0x0306;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;

    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private readonly int _inputReportLength;
    private readonly int _outputReportLength;
    private bool _disposed;

    private BalanceBoardHid(SafeFileHandle handle, int inputReportLength, int outputReportLength)
    {
        _handle = handle;
        _stream = new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: true);
        _inputReportLength = inputReportLength;
        _outputReportLength = outputReportLength;
    }

    public static BalanceBoardHid? TryOpen(out string message)
    {
        HidDGetHidGuid(out var hidGuid);
        var infoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == new IntPtr(-1))
        {
            message = $"تعذّر فحص HID: {Marshal.GetLastWin32Error()}";
            return null;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    break;

                _ = SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (requiredSize <= 0)
                    continue;

                var detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                        continue;

                    var path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var handle = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                    if (handle.IsInvalid)
                    {
                        handle.Dispose();
                        continue;
                    }

                    var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
                    if (!HidDGetAttributes(handle, ref attributes) ||
                        attributes.VendorId != NintendoVendorId ||
                        attributes.ProductId != BalanceBoardProductId)
                    {
                        handle.Dispose();
                        continue;
                    }

                    if (!HidDGetPreparsedData(handle, out var preparsedData))
                    {
                        handle.Dispose();
                        message = "ويندوز لقى البورد لكن ما قدر يقرأ وصف HID.";
                        return null;
                    }

                    try
                    {
                        var caps = new HidCapabilities { Reserved = new ushort[17] };
                        var status = HidPGetCaps(preparsedData, ref caps);
                        if (status < 0 || caps.InputReportByteLength < 11 || caps.OutputReportByteLength < 7)
                        {
                            handle.Dispose();
                            message = $"وصف تقارير HID غير متوقع (0x{status:X8}).";
                            return null;
                        }

                        message = "اتصل Wii Balance Board عبر HID.";
                        return new BalanceBoardHid(handle, caps.InputReportByteLength, caps.OutputReportByteLength);
                    }
                    finally
                    {
                        HidDFreePreparsedData(preparsedData);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }

        message = "ما لقيت Nintendo Wii Balance Board (VID 057E / PID 0306) كجهاز HID.";
        return null;
    }

    public async Task RunAsync(Action<SensorReading> onReading, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var readTask = ReadReportsAsync(onReading, linked.Token);
        var keepAliveTask = KeepAliveAsync(linked.Token);
        var first = await Task.WhenAny(readTask, keepAliveTask).ConfigureAwait(false);
        linked.Cancel();
        try
        {
            await first.ConfigureAwait(false);
        }
        finally
        {
            try { await Task.WhenAll(readTask, keepAliveTask).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SendReportAsync(0x15, [0x00], cancellationToken).ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        await SendReportAsync(0x16, [0x04, 0xA4, 0x00, 0xF0, 0x01, 0x55], cancellationToken).ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        await SendReportAsync(0x16, [0x04, 0xA4, 0x00, 0xFB, 0x01, 0x00], cancellationToken).ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        await SendReportAsync(0x12, [0x04, 0x32], cancellationToken).ConfigureAwait(false);
    }

    private async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            await SendReportAsync(0x15, [0x00], cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadReportsAsync(Action<SensorReading> onReading, CancellationToken cancellationToken)
    {
        var report = new byte[_inputReportLength];
        while (!cancellationToken.IsCancellationRequested)
        {
            var count = await _stream.ReadAsync(report, cancellationToken).ConfigureAwait(false);
            if (count < 11 || report[0] != 0x32)
                continue;

            onReading(new SensorReading(
                ReadBigEndian(report, 3),
                ReadBigEndian(report, 5),
                ReadBigEndian(report, 7),
                ReadBigEndian(report, 9)));
        }
    }

    private static ushort ReadBigEndian(byte[] bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private async Task SendReportAsync(byte reportId, byte[] data, CancellationToken cancellationToken)
    {
        var report = new byte[_outputReportLength];
        report[0] = reportId;
        Array.Copy(data, 0, report, 1, Math.Min(data.Length, report.Length - 1));
        await _stream.WriteAsync(report, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stream.Dispose();
        _handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCapabilities
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
    }

    [DllImport("hid.dll", EntryPoint = "HidD_GetHidGuid")]
    private static extern void HidDGetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", EntryPoint = "HidD_GetAttributes", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidDGetAttributes(SafeFileHandle handle, ref HidAttributes attributes);

    [DllImport("hid.dll", EntryPoint = "HidD_GetPreparsedData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidDGetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);

    [DllImport("hid.dll", EntryPoint = "HidD_FreePreparsedData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidDFreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", EntryPoint = "HidP_GetCaps")]
    private static extern int HidPGetCaps(IntPtr preparsedData, ref HidCapabilities capabilities);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref DeviceInterfaceData interfaceData, IntPtr detailData, int detailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
