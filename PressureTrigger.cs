namespace BoardADSBridge;

internal sealed class PressureTrigger
{
    private readonly long[] _calibrationSums = new long[4];
    private int _calibrationSamples;
    private int[] _baseline = new int[4];
    private int _pressFrames;
    private int _releaseFrames;

    public bool IsCalibrated { get; private set; }
    public bool IsCalibrating { get; private set; }
    public bool IsPressed { get; private set; }
    public int Pressure { get; private set; }
    public int CalibrationSamples => _calibrationSamples;
    public int[] GetBaseline() => [.. _baseline];

    public bool LoadBaseline(int[]? baseline)
    {
        if (baseline is not { Length: 4 } || baseline.Any(value => value is < 0 or > ushort.MaxValue))
            return false;

        _baseline = [.. baseline];
        IsCalibrated = true;
        IsCalibrating = false;
        IsPressed = false;
        Pressure = 0;
        _pressFrames = 0;
        _releaseFrames = 0;
        return true;
    }

    public void Reset()
    {
        IsCalibrated = false;
        IsCalibrating = false;
        IsPressed = false;
        Pressure = 0;
        _pressFrames = 0;
        _releaseFrames = 0;
        _baseline = new int[4];
    }

    public void BeginCalibration()
    {
        Array.Clear(_calibrationSums);
        _calibrationSamples = 0;
        IsCalibrating = true;
    }

    public void AddCalibrationSample(SensorReading reading)
    {
        if (!IsCalibrating)
            return;

        _calibrationSums[0] += reading.TopRight;
        _calibrationSums[1] += reading.BottomRight;
        _calibrationSums[2] += reading.TopLeft;
        _calibrationSums[3] += reading.BottomLeft;
        _calibrationSamples++;
    }

    public bool CompleteCalibration()
    {
        IsCalibrating = false;
        if (_calibrationSamples == 0)
            return false;

        _baseline = _calibrationSums.Select(sum => (int)(sum / _calibrationSamples)).ToArray();
        IsCalibrated = true;
        IsPressed = false;
        _pressFrames = 0;
        _releaseFrames = 0;
        Pressure = 0;
        return true;
    }

    public bool Update(SensorReading reading, int threshold)
    {
        if (IsCalibrating)
        {
            AddCalibrationSample(reading);
            return IsPressed;
        }

        if (!IsCalibrated)
        {
            Pressure = 0;
            return IsPressed;
        }

        var corners = new[] { reading.TopRight, reading.BottomRight, reading.TopLeft, reading.BottomLeft };
        Pressure = 0;
        for (var i = 0; i < corners.Length; i++)
            Pressure += Math.Max(0, corners[i] - _baseline[i]);

        if (!IsPressed)
        {
            _pressFrames = Pressure >= threshold ? _pressFrames + 1 : 0;
            if (_pressFrames >= 3)
            {
                IsPressed = true;
                _releaseFrames = 0;
            }
        }
        else
        {
            var releaseThreshold = Math.Max(1, (int)(threshold * 0.65));
            _releaseFrames = Pressure <= releaseThreshold ? _releaseFrames + 1 : 0;
            if (_releaseFrames >= 3)
            {
                IsPressed = false;
                _pressFrames = 0;
            }
        }

        return IsPressed;
    }
}
