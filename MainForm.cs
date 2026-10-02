using System.Drawing;
using System.Security.Principal;
using System.Windows.Forms;

namespace BoardADSBridge;

internal sealed class MainForm : Form
{
    private readonly ComboBox _radioPicker = new();
    private readonly Button _refreshRadios = new();
    private readonly Button _connectButton = new();
    private readonly Button _calibrateButton = new();
    private readonly CheckBox _enableOutput = new();
    private readonly CheckBox _keyboardNumpad1Output = new();
    private readonly TrackBar _thresholdSlider = new();
    private readonly Label _thresholdLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _pressureLabel = new();
    private readonly Label _calibrationLabel = new();
    private readonly ProgressBar _pressureBar = new();
    private readonly TextBox _log = new();
    private readonly List<BalanceBoardBluetooth.Radio> _radios = [];
    private readonly PressureTrigger _trigger = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly object _stateLock = new();

    private CancellationTokenSource? _connectionCancellation;
    private BalanceBoardHid? _connection;
    private DateTime _calibrationEndsAt;
    private int _threshold = 100;
    private bool _outputHeld;
    private bool _outputEnabled;
    private bool _calibrationShouldEnableOutput;
    private bool _startupHandled;

    public MainForm()
    {
        _threshold = _settings.Threshold;
        Text = "Balance Board ADS Bridge";
        MinimumSize = new Size(620, 560);
        Size = new Size(720, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 248, 250);
        Font = new Font("Segoe UI", 10F);

        BuildLayout();
        LoadRadios();
        Shown += OnShown;
        FormClosing += OnClosing;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(18),
            BackColor = BackColor,
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BackColor,
            Margin = Padding.Empty
        };
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var title = new Label
        {
            Text = "Wii Balance Board → ADS",
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Color.FromArgb(27, 43, 61),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var brand = new Label
        {
            Text = "✿  Made by ASLI  ✿",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(214, 24, 56),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        heading.Controls.Add(title, 0, 0);
        heading.Controls.Add(brand, 0, 1);
        root.Controls.Add(heading, 0, 0);

        var radioRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 8, 0, 0) };
        radioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        radioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        radioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        radioRow.Controls.Add(new Label { Text = "Bluetooth adapter", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _radioPicker.Dock = DockStyle.Fill;
        _radioPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        radioRow.Controls.Add(_radioPicker, 1, 0);
        _refreshRadios.Text = "Refresh list";
        _refreshRadios.Dock = DockStyle.Fill;
        _refreshRadios.Click += (_, _) => LoadRadios();
        radioRow.Controls.Add(_refreshRadios, 2, 0);
        root.Controls.Add(radioRow, 0, 1);

        var connectionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 4, 0, 4) };
        connectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        connectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _connectButton.Text = "Connect board";
        _connectButton.Dock = DockStyle.Fill;
        _connectButton.Click += ConnectButtonClicked;
        connectionRow.Controls.Add(_connectButton, 0, 0);
        _statusLabel.Text = "Starting the board connection automatically…";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        connectionRow.Controls.Add(_statusLabel, 1, 0);
        root.Controls.Add(connectionRow, 0, 2);

        var sensitivity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 4, 0, 0) };
        sensitivity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        sensitivity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sensitivity.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        sensitivity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sensitivity.Controls.Add(new Label { Text = "Pressure threshold", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _thresholdLabel.Text = $"{_settings.Threshold:N0} raw units";
        _thresholdLabel.Dock = DockStyle.Fill;
        _thresholdLabel.TextAlign = ContentAlignment.MiddleRight;
        sensitivity.Controls.Add(_thresholdLabel, 1, 0);
        _thresholdSlider.Minimum = 100;
        _thresholdSlider.Maximum = 12000;
        _thresholdSlider.Value = _settings.Threshold;
        _thresholdSlider.TickFrequency = 1000;
        _thresholdSlider.SmallChange = 100;
        _thresholdSlider.LargeChange = 500;
        _thresholdSlider.Dock = DockStyle.Fill;
        _thresholdSlider.ValueChanged += (_, _) =>
        {
            lock (_stateLock)
                _threshold = _thresholdSlider.Value;
            _thresholdLabel.Text = $"{_threshold:N0} raw units";
            _settings.Threshold = _threshold;
            _settings.Save();
        };
        sensitivity.Controls.Add(_thresholdSlider, 1, 1);
        root.Controls.Add(sensitivity, 0, 3);

        var pressureRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        pressureRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        pressureRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pressureRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        pressureRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _calibrateButton.Text = "Calibrate empty board";
        _calibrateButton.Dock = DockStyle.Fill;
        _calibrateButton.Enabled = false;
        _calibrateButton.Click += (_, _) => BeginCalibration();
        pressureRow.Controls.Add(_calibrateButton, 0, 0);
        _calibrationLabel.Text = "Remove all weight before calibration.";
        _calibrationLabel.Dock = DockStyle.Fill;
        _calibrationLabel.TextAlign = ContentAlignment.MiddleLeft;
        pressureRow.Controls.Add(_calibrationLabel, 1, 0);
        pressureRow.Controls.Add(new Label { Text = "Live pressure", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        var barRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 5, 0, 5) };
        barRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        barRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        _pressureBar.Minimum = 0;
        _pressureBar.Maximum = 12000;
        _pressureBar.Dock = DockStyle.Fill;
        barRow.Controls.Add(_pressureBar, 0, 0);
        _pressureLabel.Text = "0";
        _pressureLabel.Dock = DockStyle.Fill;
        _pressureLabel.TextAlign = ContentAlignment.MiddleRight;
        barRow.Controls.Add(_pressureLabel, 1, 0);
        pressureRow.Controls.Add(barRow, 1, 1);
        root.Controls.Add(pressureRow, 0, 4);

        var outputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0, 3, 0, 0) };
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _enableOutput.Text = "Enable selected outputs while standing on board";
        _enableOutput.Dock = DockStyle.Fill;
        _enableOutput.Enabled = false;
        _enableOutput.Checked = _settings.OutputEnabled;
        _enableOutput.CheckedChanged += OutputEnabledChanged;
        outputRow.Controls.Add(_enableOutput, 0, 0);
        root.Controls.Add(outputRow, 0, 5);

        var outputChoices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = false,
            Padding = new Padding(0, 4, 0, 0)
        };
        ConfigureOutputChoice(_keyboardNumpad1Output, "Numpad 1 key", _settings.KeyboardNumpad1Enabled, outputChoices);
        root.Controls.Add(outputChoices, 0, 6);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.BackColor = Color.White;
        _log.Font = new Font("Consolas", 9F);
        root.Controls.Add(_log, 0, 7);

        Controls.Add(root);
    }

    private void ConfigureOutputChoice(CheckBox checkBox, string label, bool selected, FlowLayoutPanel panel)
    {
        checkBox.AutoSize = true;
        checkBox.Text = label;
        checkBox.Checked = selected;
        checkBox.Margin = new Padding(0, 5, 18, 0);
        checkBox.CheckedChanged += OutputSelectionChanged;
        panel.Controls.Add(checkBox);
    }

    private void LoadRadios(bool silent = false)
    {
        var selectedAddress = (_radioPicker.SelectedItem as BalanceBoardBluetooth.Radio)?.Address ?? _settings.BluetoothAddress;
        foreach (var radio in _radios)
            radio.Dispose();
        _radios.Clear();
        _radioPicker.Items.Clear();
        _radios.AddRange(BalanceBoardBluetooth.EnumerateRadios());
        foreach (var radio in _radios)
            _radioPicker.Items.Add(radio);

        var index = _radios.FindIndex(radio => radio.Address == selectedAddress);
        if (index < 0 && _radios.Count > 0)
            index = 0;
        if (index >= 0)
            _radioPicker.SelectedIndex = index;

        _radioPicker.SelectedIndexChanged -= RadioPickerChanged;
        _radioPicker.SelectedIndexChanged += RadioPickerChanged;
        if (_radioPicker.SelectedItem is BalanceBoardBluetooth.Radio selectedRadio)
        {
            _settings.BluetoothAddress = selectedRadio.Address;
            _settings.Save();
        }

        if (_radios.Count == 0)
        {
            _statusLabel.Text = "Windows ما شايف Bluetooth radio.";
            if (!silent)
                AppendLog("ما لقيت محوّل بلوتوث عبر Windows Bluetooth API.");
        }
        else
        {
            _statusLabel.Text = $"Found {_radios.Count} Bluetooth adapter(s).";
            if (!silent)
                AppendLog("Adapters: " + string.Join("; ", _radios.Select(radio => radio.Name)));
        }
    }

    private void RadioPickerChanged(object? sender, EventArgs e)
    {
        if (_radioPicker.SelectedItem is BalanceBoardBluetooth.Radio radio)
        {
            _settings.BluetoothAddress = radio.Address;
            _settings.Save();
        }
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (_startupHandled)
            return;
        _startupHandled = true;
        AppendLog("Keyboard-only mode: no vJoy or mouse output is initialized.");
        ConnectButtonClicked(this, EventArgs.Empty);
    }

    private async void ConnectButtonClicked(object? sender, EventArgs e)
    {
        if (_connectionCancellation is not null)
        {
            DisconnectBoard("Connection stopped.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _connectionCancellation = cancellation;
        _connectButton.Text = "Stop search";
        _radioPicker.Enabled = false;
        _refreshRadios.Enabled = false;
        _statusLabel.Text = "Searching for the board…";
        var isElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        AppendLog($"The bridge is running as administrator: {isElevated}.");
        AppendLog("رح ضل دور على البورد، وبعيد المحاولة تلقائيًا إذا فصل.");

        try
        {
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                var radio = _radioPicker.SelectedItem as BalanceBoardBluetooth.Radio;
                if (radio is null)
                {
                    LoadRadios(silent: true);
                    radio = _radioPicker.SelectedItem as BalanceBoardBluetooth.Radio;
                    if (radio is null)
                    {
                        _statusLabel.Text = "ما لقيت محوّل بلوتوث؛ عم جرّب من جديد…";
                        await Task.Delay(3000, cancellation.Token);
                        continue;
                    }
                }

                try
                {
                    AppendLog($"رح أستخدم الراديو المحدد فقط: {radio.Name}.");
                    var boardAddress = await Task.Run(
                        () => BalanceBoardBluetooth.PairOrWakeBoardAsync(radio, _settings.BoardBluetoothAddress, AppendLog, cancellation.Token),
                        cancellation.Token);
                    _settings.BoardBluetoothAddress = boardAddress;
                    _settings.Save();

                    BalanceBoardHid? connection = null;
                    DateTime nextHidLog = DateTime.UtcNow;
                    while (connection is null)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        string message = string.Empty;
                        await Task.Run(() => connection = BalanceBoardHid.TryOpen(out message), cancellation.Token);
                        if (connection is null)
                        {
                            _statusLabel.Text = message;
                            if (DateTime.UtcNow >= nextHidLog)
                            {
                                AppendLog("ناطر ويندوز يجهّز HID؛ رح ضل عم جرّب لحد ما يفتح جهاز البورد.");
                                nextHidLog = DateTime.UtcNow.AddSeconds(10);
                            }
                            await Task.Delay(500, cancellation.Token);
                        }
                    }

                    _connection = connection;
                    _connectButton.Text = "Disconnect";
                    _calibrateButton.Enabled = true;
                    if (_trigger.LoadBaseline(_settings.CalibrationBaseline))
                    {
                        _statusLabel.Text = "Connected. Using your saved calibration.";
                        _calibrationLabel.Text = "Saved calibration loaded. Recalibrate only when needed.";
                        AppendLog("تم تحميل المعايرة المحفوظة؛ ما في داعي تعيدها.");
                        _enableOutput.Enabled = true;
                        ApplySavedOutputPreference();
                    }
                    else
                    {
                        _statusLabel.Text = "Connected. Keep the board empty for its first calibration.";
                        AppendLog("أول معايرة: خلي البورد فاضي خمس ثواني؛ بعدها بيحفظها وبيفعّل ADS تلقائيًا.");
                        BeginCalibration();
                    }
                    _ = RunBoardAsync(connection, cancellation.Token);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppendLog("Connection attempt failed; رح جرّب من جديد: " + ex.Message);
                    _statusLabel.Text = "Connection issue; continuing to search…";
                    foreach (var oldRadio in _radios)
                        oldRadio.Dispose();
                    _radios.Clear();
                    _radioPicker.Items.Clear();
                    await Task.Delay(3000, cancellation.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            DisconnectBoard("Connection cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            DisconnectBoard("Connection stopped. Press Connect to search again.");
        }
    }

    private async Task RunBoardAsync(BalanceBoardHid connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.RunAsync(OnSensorReading, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendLog("HID disconnected: " + ex.Message);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(() => _ = ReconnectAfterDisconnectAsync());
        }
    }

    private async Task ReconnectAfterDisconnectAsync()
    {
        DisconnectBoard("Board disconnected; searching again automatically…");
        try
        {
            await Task.Delay(2000);
            if (!IsDisposed && !Disposing && _connectionCancellation is null)
                ConnectButtonClicked(this, EventArgs.Empty);
        }
        catch (ObjectDisposedException) { }
    }

    private void BeginCalibration()
    {
        lock (_stateLock)
        {
            _calibrationShouldEnableOutput = _settings.OutputEnabled;
            _outputEnabled = false;
            _trigger.BeginCalibration();
            _calibrationEndsAt = DateTime.UtcNow.AddSeconds(5);
            if (_outputHeld)
            {
                SendBoardOutput(false);
                _outputHeld = false;
            }
        }

        _enableOutput.Enabled = false;
        _calibrationLabel.Text = "Sampling for 5 seconds — nobody on the board.";
        AppendLog("Calibration started; board must be empty.");
    }

    private void OnSensorReading(SensorReading reading)
    {
        int pressure;
        bool pressed;
        bool calibratedNow = false;
        lock (_stateLock)
        {
            pressed = _trigger.Update(reading, _threshold);
            if (_trigger.IsCalibrating && DateTime.UtcNow >= _calibrationEndsAt)
                calibratedNow = _trigger.CompleteCalibration();
            pressure = _trigger.Pressure;

            if (_outputEnabled && pressed != _outputHeld)
            {
                SendBoardOutput(pressed);
                _outputHeld = pressed;
            }

        }

        if (!IsDisposed && IsHandleCreated)
        {
            BeginInvoke(() =>
            {
                _pressureLabel.Text = pressure.ToString("N0");
                _pressureBar.Value = Math.Clamp(pressure, _pressureBar.Minimum, _pressureBar.Maximum);
                if (calibratedNow)
                {
                    _calibrationLabel.Text = $"Calibrated ({_trigger.CalibrationSamples} samples), saved for next time.";
                    _settings.CalibrationBaseline = _trigger.GetBaseline();
                    _settings.Save();
                    _enableOutput.Enabled = true;
                    AppendLog($"Calibration complete: {_trigger.CalibrationSamples} samples.");
                    if (_calibrationShouldEnableOutput)
                        ApplySavedOutputPreference();
                }
                if (_trigger.IsCalibrating)
                    _calibrationLabel.Text = $"Calibrating… {_trigger.CalibrationSamples} samples; keep board empty.";
            });
        }
    }

    private void OutputEnabledChanged(object? sender, EventArgs e)
    {
        _settings.OutputEnabled = _enableOutput.Checked;
        _settings.Save();

        lock (_stateLock)
        {
            _outputEnabled = _enableOutput.Checked;
            if (!_outputEnabled)
            {
                SendBoardOutput(false);
                _outputHeld = false;
            }
            else if (_trigger.IsPressed && !_outputHeld)
            {
                SendBoardOutput(true);
                _outputHeld = true;
            }
        }

        AppendLog(_enableOutput.Checked ? "Selected outputs enabled." : "Selected outputs disabled; all inputs released.");
    }

    private void OutputSelectionChanged(object? sender, EventArgs e)
    {
        lock (_stateLock)
        {
            if (_outputEnabled && _outputHeld)
                SendBoardOutput(false);

            _settings.MouseClickEnabled = false;
            _settings.TriggerEnabled = false;
            _settings.AButtonEnabled = false;
            _settings.BButtonEnabled = false;
            _settings.XButtonEnabled = false;
            _settings.YButtonEnabled = false;
            _settings.KeyboardNumpad1Enabled = _keyboardNumpad1Output.Checked;
            _settings.Save();

            if (_outputEnabled && _trigger.IsPressed)
                SendBoardOutput(true);
        }
        AppendLog("Output choices saved.");
    }

    private void DisconnectBoard(string status)
    {
        _connectionCancellation?.Cancel();
        _connectionCancellation?.Dispose();
        _connectionCancellation = null;
        _connection?.Dispose();
        _connection = null;

        lock (_stateLock)
        {
            SendBoardOutput(false);
            _outputHeld = false;
            _outputEnabled = false;
            _trigger.Reset();
        }

        _enableOutput.CheckedChanged -= OutputEnabledChanged;
        _enableOutput.Checked = _settings.OutputEnabled;
        _enableOutput.CheckedChanged += OutputEnabledChanged;
        _enableOutput.Enabled = false;
        _calibrateButton.Enabled = false;
        _connectButton.Text = "Connect board";
        _radioPicker.Enabled = true;
        _refreshRadios.Enabled = true;
        _statusLabel.Text = status;
        _calibrationLabel.Text = "Remove all weight before calibration.";
        _pressureBar.Value = 0;
        _pressureLabel.Text = "0";
    }

    private void AppendLog(string message)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void ApplySavedOutputPreference()
    {
        if (!_settings.OutputEnabled)
        {
            lock (_stateLock)
                _outputEnabled = false;
            return;
        }

        lock (_stateLock)
        {
            _outputEnabled = true;
            if (_trigger.IsPressed && !_outputHeld)
            {
                SendBoardOutput(true);
                _outputHeld = true;
            }
        }

        _enableOutput.CheckedChanged -= OutputEnabledChanged;
        _enableOutput.Checked = true;
        _enableOutput.CheckedChanged += OutputEnabledChanged;
        AppendLog("ADS output is on and saved for next launch.");
    }

    private void SendBoardOutput(bool held)
    {
        if (!held)
        {
            _ = WindowsInputOutput.SetNumpad1Held(false);
            return;
        }

        if (_keyboardNumpad1Output.Checked)
        {
            var result = WindowsInputOutput.SetNumpad1Held(true);
            if (result.InsertedEvents == 1)
                AppendLog("Windows accepted Numpad 1 key down.");
            else
                AppendLog($"SendInput failed for Numpad 1 key down (inserted {result.InsertedEvents}; Win32 error {result.LastError}).");
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        _settings.Save();
        DisconnectBoard("Closed.");
        foreach (var radio in _radios)
            radio.Dispose();
        _radios.Clear();
    }
}
