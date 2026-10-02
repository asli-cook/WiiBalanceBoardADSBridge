using System.Runtime.InteropServices;

namespace BoardADSBridge;

internal static class BalanceBoardBluetooth
{
    private const uint ServiceEnable = 0x01;
    private const uint ErrorInvalidParameter = 87;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;

    private static readonly Guid HidService = new("00001124-0000-1000-8000-00805F9B34FB");

    public sealed class Radio : IDisposable
    {
        internal Radio(IntPtr handle, string name, ulong address)
        {
            Handle = handle;
            Name = name;
            Address = address;
        }

        internal IntPtr Handle { get; }
        public string Name { get; }
        public ulong Address { get; }

        public override string ToString() => $"{Name}  ({Address:X12})";

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                CloseHandle(Handle);
        }
    }

    public static List<Radio> EnumerateRadios()
    {
        var radios = new List<Radio>();
        var findParams = new BluetoothFindRadioParams { Size = (uint)Marshal.SizeOf<BluetoothFindRadioParams>() };
        var findHandle = BluetoothFindFirstRadio(ref findParams, out var radioHandle);
        if (findHandle == IntPtr.Zero || findHandle == new IntPtr(-1))
            return radios;

        try
        {
            do
            {
                var info = new BluetoothRadioInfo { Size = (uint)Marshal.SizeOf<BluetoothRadioInfo>() };
                var result = BluetoothGetRadioInfo(radioHandle, ref info);
                if (result == 0)
                    radios.Add(new Radio(radioHandle, info.Name ?? "Bluetooth Radio", info.Address));
                else
                    CloseHandle(radioHandle);
            }
            while (BluetoothFindNextRadio(findHandle, out radioHandle));
        }
        finally
        {
            BluetoothFindRadioClose(findHandle);
        }

        return radios;
    }

    public static async Task<ulong> PairOrWakeBoardAsync(
        Radio radio,
        ulong? knownBoardAddress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log(knownBoardAddress is null
            ? $"عم استخدم {radio.Name}. إذا البورد مقترن من قبل اضغط زره الأمامي؛ SYNC الأحمر مطلوب لأول اقتران فقط."
            : $"عم استخدم {radio.Name}. اضغط زر البورد الأمامي لتشغيله؛ ما في داعي لزر SYNC داخل البطاريات بعد الاقتران الأول.");
        var boardSeen = false;
        var lastStatusLog = DateTime.UtcNow.AddSeconds(10);
        ulong lastLoggedCandidate = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var discovered in DiscoverBoardCandidates(radio.Handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var device = discovered;
                boardSeen = true;
                if (lastLoggedCandidate != device.Address)
                {
                    log($"لقيت جهاز Wii: {device.Name} ({device.Address:X12}).");
                    lastLoggedCandidate = device.Address;
                }

                if (device.Authenticated || device.Connected || device.Remembered)
                    log("ويندوز حافظ الاقتران؛ عم حاول أوصل بالبورد من الزر الأمامي بدون إعادة SYNC.");
                else
                    log("هذا أول اقتران على ويندوز؛ اضغط SYNC الأحمر داخل غطاء البطاريات الآن.");

                if (!device.Authenticated && !device.Connected && !device.Remembered)
                {
                    log("عم جرّب اقتران SYNC بمفتاح Wii المشتق من عنوان محوّل البلوتوث...");
                    var authStatus = AuthenticateWithSyncPasskey(radio, ref device);
                    if (authStatus != 0 && authStatus != ErrorNoMoreItems)
                    {
                        log($"ويندوز رفض مفتاح اقتران SYNC (رمز {authStatus}).");
                        continue;
                    }

                    if (authStatus == 0)
                        log("اقتران SYNC الموثّق نجح.");
                    else
                        log("ويندوز يقول إن الجهاز موثّق من قبل؛ رح أتابع بتفعيل HID.");

                    uint installedServiceCount = 0;
                    var enumerateStatus = BluetoothEnumerateInstalledServices(
                        radio.Handle, ref device, ref installedServiceCount, IntPtr.Zero);
                    if (enumerateStatus != 0 && enumerateStatus != ErrorMoreData)
                    {
                        log($"تعذّر تثبيت سجلّ الاقتران في ويندوز (رمز {enumerateStatus}).");
                        continue;
                    }
                }

                log("عم فعّل Bluetooth HID...");
                var service = HidService;
                var status = BluetoothSetServiceState(radio.Handle, ref device, ref service, ServiceEnable);
                // Windows can report ERROR_INVALID_PARAMETER when the HID service is already active.
                if (status == 0 || status == ErrorInvalidParameter || status == 0x80070057)
                {
                    log(status == 0
                        ? "تفعيل HID نجح. ناطر ويندوز يجهّز جهاز الحساسات..."
                        : "خدمة HID موجودة أو مفعّلة مسبقًا؛ عم جرّب أفتح جهاز الحساسات...");
                    return device.Address;
                }

                log($"ويندوز ما فعّل HID (رمز {status}); رح جرّب دورة اكتشاف ثانية.");
            }

            if (DateTime.UtcNow >= lastStatusLog)
            {
                log(boardSeen
                    ? "لسا عم حاول أكمّل اتصال Wii Balance Board؛ رح تابع البحث تلقائيًا…"
                    : "ما ظهر البورد بعد؛ شغّله من الزر الأمامي، ورح ضل عم دور…");
                lastStatusLog = DateTime.UtcNow.AddSeconds(10);
                lastLoggedCandidate = 0;
            }
            await Task.Delay(700, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<BluetoothDeviceInfo> DiscoverBoardCandidates(IntPtr radio)
    {
        var search = new BluetoothDeviceSearchParams
        {
            Size = (uint)Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = true,
            ReturnConnected = true,
            IssueInquiry = true,
            TimeoutMultiplier = 2,
            Radio = radio
        };

        var info = new BluetoothDeviceInfo { Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>() };
        var searchHandle = BluetoothFindFirstDevice(ref search, ref info);
        if (searchHandle == IntPtr.Zero || searchHandle == new IntPtr(-1))
            yield break;

        try
        {
            do
            {
                var name = info.Name ?? string.Empty;
                if (name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("RVL-WBC", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("RVL-A-BC", StringComparison.OrdinalIgnoreCase))
                {
                    yield return info;
                }

                info = new BluetoothDeviceInfo { Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>() };
            }
            while (BluetoothFindNextDevice(searchHandle, ref info));
        }
        finally
        {
            BluetoothFindDeviceClose(searchHandle);
        }
    }

    private static uint AuthenticateWithSyncPasskey(Radio radio, ref BluetoothDeviceInfo device)
    {
        // With the board's SYNC button, Nintendo's pairing passkey is the host radio's
        // six raw Bluetooth address bytes in native order (not printable hex digits).
        var passkey = Marshal.AllocHGlobal(14);
        try
        {
            for (var i = 0; i < 6; i++)
            {
                var addressByte = (short)((radio.Address >> (8 * i)) & 0xFF);
                Marshal.WriteInt16(passkey, i * 2, addressByte);
            }

            Marshal.WriteInt16(passkey, 12, 0);
            return BluetoothAuthenticateDevice(IntPtr.Zero, radio.Handle, ref device, passkey, 6);
        }
        finally
        {
            Marshal.FreeHGlobal(passkey);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public uint Size;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool IssueInquiry;
        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public uint Size;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothRadioInfo
    {
        public uint Size;
        public ulong Address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string? Name;
        public uint ClassOfDevice;
        public ushort LmpSubversion;
        public ushort Manufacturer;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string? Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BluetoothDeviceSearchParams searchParams, ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr findHandle, ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr findHandle);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParams findParams, out IntPtr radioHandle);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr findHandle, out IntPtr radioHandle);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr findHandle);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetRadioInfo(IntPtr radioHandle, ref BluetoothRadioInfo radioInfo);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr radio, ref BluetoothDeviceInfo deviceInfo, ref Guid serviceGuid, uint flags);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothAuthenticateDevice(IntPtr parentWindow, IntPtr radio, ref BluetoothDeviceInfo deviceInfo, IntPtr passkey, uint passkeyLength);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothEnumerateInstalledServices(IntPtr radio, ref BluetoothDeviceInfo deviceInfo, ref uint serviceCount, IntPtr services);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothRemoveDevice(ref ulong address);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
