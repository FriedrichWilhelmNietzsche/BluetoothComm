using System.Collections.ObjectModel;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Extensions;

namespace BluetoothComm;

public partial class MainPage : ContentPage
{
    private readonly IAdapter _adapter;
    private readonly IBluetoothLE _ble;

    private readonly ObservableCollection<DeviceItem> _devices = new();
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _connectCts;
    private DeviceItem? _connectedItem;
    private DeviceItem? _lastConnectedItem; // 断开后保留的设备信息，用于一键重连

    // BLE GATT：串口透传特征（写 + 通知）
    private ICharacteristic? _writeCharacteristic;
    private ICharacteristic? _notifyCharacteristic;

    // 颜色常量（与 XAML 中主题色保持一致）
    private static readonly Color PrimaryBlue = Color.FromArgb("#3B82F6");
    private static readonly Color ConnectedGreen = Color.FromArgb("#22C55E");
    private static readonly Color GrayEllipse = Color.FromArgb("#C5CBD7");
    private static readonly Color GrayButton = Color.FromArgb("#94A3B8");

    public MainPage(IAdapter adapter, IBluetoothLE ble)
    {
        InitializeComponent();
        _adapter = adapter;
        _ble = ble;

        DeviceListView.ItemsSource = _devices;
        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _adapter.ScanTimeoutElapsed += OnScanTimeout;
        _adapter.DeviceDisconnected += OnDeviceDisconnected;
        _adapter.DeviceConnectionLost += OnDeviceDisconnected;
    }

    #region 上半部分：蓝牙通讯（界面占位，功能后续补充）

    private bool _isInputMode = true;

    /// <summary>点击「输入」按钮：切换到输入模式并立即发送 0x10 写总电量帧</summary>
    private async void OnInputTabClicked(object? sender, EventArgs e)
    {
        SwitchMode(true);
        await SendInputFrameAsync();
    }

    /// <summary>点击「输出」按钮：切换到输出模式并立即发送 0x04 读电量帧</summary>
    private async void OnOutputTabClicked(object? sender, EventArgs e)
    {
        SwitchMode(false);
        await SendOutputFrameAsync();
    }

    /// <summary>切换 输入/输出 模式（仅切换按钮选中样式）</summary>
    private void SwitchMode(bool isInput)
    {
        _isInputMode = isInput;
        if (isInput)
        {
            InputTab.BackgroundColor = PrimaryBlue;
            InputTab.TextColor = Colors.White;
            InputTab.BorderWidth = 0;
            OutputTab.BackgroundColor = Colors.White;
            OutputTab.TextColor = PrimaryBlue;
            OutputTab.BorderColor = PrimaryBlue;
            OutputTab.BorderWidth = 1;
        }
        else
        {
            OutputTab.BackgroundColor = PrimaryBlue;
            OutputTab.TextColor = Colors.White;
            OutputTab.BorderWidth = 0;
            InputTab.BackgroundColor = Colors.White;
            InputTab.TextColor = PrimaryBlue;
            InputTab.BorderColor = PrimaryBlue;
            InputTab.BorderWidth = 1;
        }
    }

    #endregion

    #region 下半部分：设备扫描与连接

    /// <summary>扫描 / 停止按钮</summary>
    private async void OnScanClicked(object? sender, EventArgs e)
    {
        try
        {
            // 扫描中 → 点击为停止
            if (_adapter.IsScanning)
            {
                await _adapter.StopScanningForDevicesAsync();
                UpdateScanButton(false);
                return;
            }

            // 蓝牙未开启
            if (_ble.State != BluetoothState.On)
            {
                await DisplayAlertAsync("蓝牙未开启", "请先打开系统蓝牙，再开始扫描。", "确定");
                return;
            }

            // 运行时权限：被拒绝则中止扫描
            if (!await EnsurePermissionsAsync())
            {
                await DisplayAlertAsync("权限不足", "需要「附近设备」或定位权限才能扫描蓝牙，请在系统设置中授予后重试。", "确定");
                return;
            }

            _devices.Clear();
            UpdateScanButton(true);

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            await _adapter.StartScanningForDevicesAsync(_scanCts.Token);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("OnScanClicked", ex);
            UpdateScanButton(false);
            await DisplayAlertAsync("扫描失败", ex.Message, "确定");
        }
    }

    /// <summary>点击列表项：已连接则断开，未连接则连接</summary>
    private async void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is not DeviceItem item)
                return;

            // 清除选中态，避免重复触发
            DeviceListView.SelectedItem = null;

            // 连接前先停止扫描
            if (_adapter.IsScanning)
            {
                await _adapter.StopScanningForDevicesAsync();
                UpdateScanButton(false);
            }

            if (item.IsConnected)
            {
                await DisconnectDeviceAsync(item);
                return;
            }

            await ConnectDeviceAsync(item);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("OnDeviceSelected", ex);
            await DisplayAlertAsync("操作失败", ex.Message, "确定");
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        try
        {
            var device = e.Device;
            // 过滤无广播名的设备
            if (string.IsNullOrEmpty(device.Name))
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var existing = _devices.FirstOrDefault(d => d.Id == device.Id);
                    if (existing is not null)
                    {
                        // 已存在 → 刷新信号强度
                        existing.Rssi = device.Rssi;
                    }
                    else
                    {
                        _devices.Add(new DeviceItem
                        {
                            Id = device.Id,
                            Name = device.Name,
                            MacAddress = device.Id.ToString(),
                            Rssi = device.Rssi,
                            NativeDevice = device
                        });
                    }
                }
                catch (Exception inner)
                {
                    CrashLogger.Log("OnDeviceDiscovered-UI", inner);
                }
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log("OnDeviceDiscovered", ex);
        }
    }

    private void OnScanTimeout(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(() => UpdateScanButton(false));

    /// <summary>设备断开 / 连接丢失时刷新界面状态</summary>
    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var item = _devices.FirstOrDefault(d => d.Id == e.Device.Id);
                    if (item is not null)
                        item.IsConnected = false;

                    if (_connectedItem is not null && e.Device.Id == _connectedItem.Id)
                    {
                        _lastConnectedItem = _connectedItem; // 保留设备信息
                        _connectedItem = null;
                        StatusLabel.Text = "未连接";
                        StatusDetailLabel.Text = "设备已断开，点击可重连";
                        SetStatusIcon(false);
                        UpdateCommStatus(item, false);
                    }
                }
                catch (Exception inner)
                {
                    CrashLogger.Log("OnDeviceDisconnected-UI", inner);
                }
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log("OnDeviceDisconnected", ex);
        }
    }

    private async Task ConnectDeviceAsync(DeviceItem item)
    {
        item.IsConnecting = true;
        StatusLabel.Text = $"正在连接 {item.Name}…";
        StatusDetailLabel.Text = item.MacAddress;
        SetStatusIcon(false);

        try
        {
            _connectCts?.Cancel();
            _connectCts = new CancellationTokenSource();

            // 强制走 BLE 传输，不自动重连
            await _adapter.ConnectToDeviceAsync(
                item.NativeDevice!,
                new ConnectParameters(autoConnect: false, forceBleTransport: true),
                _connectCts.Token);

            // 连接成功：发现 GATT 服务与特征，更新列表与顶部状态
            _connectedItem = item;
            foreach (var d in _devices)
                d.IsConnected = d == item;
            item.IsConnecting = false;

            StatusLabel.Text = $"{item.Name} 已连接";
            StatusDetailLabel.Text = item.MacAddress;
            SetStatusIcon(true);
            UpdateCommStatus(item);

            try
            {
                await SetupGattAsync(item.NativeDevice!);
            }
            catch (Exception gattEx)
            {
                CrashLogger.Log("SetupGatt", gattEx);
                await DisplayAlertAsync("提示", "已连接，但 GATT 服务发现失败：" + gattEx.Message, "确定");
            }
            await DisplayAlertAsync("连接成功", $"已连接 {item.Name}", "确定");
        }
        catch (Exception ex)
        {
            item.IsConnecting = false;
            StatusLabel.Text = "未连接";
            StatusDetailLabel.Text = "连接失败，请重试";
            SetStatusIcon(false);
            await DisplayAlertAsync("连接失败", ex.Message, "确定");
        }
    }

    private async Task DisconnectDeviceAsync(DeviceItem item)
    {
        try
        {
            // 停止通知订阅
            if (_notifyCharacteristic is not null)
            {
                _notifyCharacteristic.ValueUpdated -= OnValueUpdated;
                await _notifyCharacteristic.StopUpdatesAsync(CancellationToken.None);
            }
        }
        catch
        {
            // 忽略
        }
        _writeCharacteristic = null;
        _notifyCharacteristic = null;

        try
        {
            await _adapter.DisconnectDeviceAsync(item.NativeDevice!, CancellationToken.None);
        }
        catch
        {
            // 设备可能已掉线，忽略
        }

        item.IsConnected = false;
        _lastConnectedItem = item; // 保留设备信息，便于一键重连
        _connectedItem = null;
        _rxBuffer.Clear();

        StatusLabel.Text = "未连接";
        StatusDetailLabel.Text = "点击设备区可重连";
        SetStatusIcon(false);
        UpdateCommStatus(item, false);
    }

    /// <summary>
    /// 顶部连接状态图标：isConnected=true 时两个椭圆重叠（已连接），否则分开（未连接）
    /// </summary>
    private void SetStatusIcon(bool isConnected)
    {
        if (isConnected)
        {
            StatusEllipse1.BackgroundColor = ConnectedGreen;
            StatusEllipse2.BackgroundColor = ConnectedGreen;
            StatusEllipse2.Margin = new Thickness(-14, 0, 0, 0); // 左移覆盖到第一个椭圆 → 重叠
        }
        else
        {
            StatusEllipse1.BackgroundColor = GrayEllipse;
            StatusEllipse2.BackgroundColor = GrayEllipse;
            StatusEllipse2.Margin = new Thickness(0);            // 恢复间距 → 分开
        }
    }

    private void UpdateScanButton(bool scanning)
    {
        ScanButton.Text = scanning ? "停止" : "扫描";
        ScanButton.BackgroundColor = scanning ? GrayButton : PrimaryBlue;
    }

    /// <summary>
    /// 点击上半部分已连接设备显示区：
    /// 已连接 → 断开（保留设备信息）；
    /// 已断开但有保留设备 → 一键重连；
    /// 无保留设备 → 开始扫描。
    /// </summary>
    private async void OnCommStatusTapped(object? sender, TappedEventArgs e)
    {
        if (_connectedItem is not null)
        {
            await DisconnectDeviceAsync(_connectedItem);
        }
        else if (_lastConnectedItem is not null)
        {
            await ReconnectLastDeviceAsync();
        }
        else
        {
            OnScanClicked(sender, e);
        }
    }

    /// <summary>用保留的设备 Guid 直接重连，无需重新扫描</summary>
    private async Task ReconnectLastDeviceAsync()
    {
        if (_lastConnectedItem is null)
            return;

        try
        {
            _connectCts?.Cancel();
            _connectCts = new CancellationTokenSource();

            StatusLabel.Text = $"正在重连 {_lastConnectedItem.Name}…";
            StatusDetailLabel.Text = _lastConnectedItem.MacAddress;

            var device = await _adapter.ConnectToKnownDeviceAsync(
                _lastConnectedItem.Id,
                new ConnectParameters(autoConnect: false, forceBleTransport: true),
                _connectCts.Token);

            // 重连成功：更新保留设备的原生引用与状态
            _lastConnectedItem.NativeDevice = device;
            _lastConnectedItem.IsConnected = true;
            _connectedItem = _lastConnectedItem;

            var existing = _devices.FirstOrDefault(d => d.Id == device.Id);
            if (existing is not null)
                existing.IsConnected = true;
            else
                _devices.Add(_lastConnectedItem);

            StatusLabel.Text = $"{_lastConnectedItem.Name} 已连接";
            StatusDetailLabel.Text = _lastConnectedItem.MacAddress;
            SetStatusIcon(true);
            UpdateCommStatus(_lastConnectedItem, true);

            await SetupGattAsync(device);
            await DisplayAlertAsync("重连成功", $"已连接 {_lastConnectedItem.Name}", "确定");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ReconnectLastDevice", ex);
            StatusLabel.Text = "未连接";
            StatusDetailLabel.Text = "重连失败，请重新扫描";
            SetStatusIcon(false);
            UpdateCommStatus(_lastConnectedItem, false);
            await DisplayAlertAsync("重连失败", ex.Message, "确定");
        }
    }

    /// <summary>
    /// 更新上半部分「蓝牙通讯」卡片中的设备显示：
    /// connected=true  → 绿色重叠椭圆 + 设备名 + MAC（已连接）
    /// connected=false 且 item 非空 → 灰色分开椭圆 + 设备名 + "已断开，点击重连"
    /// item 为 null      → 灰色分开椭圆 + "未连接" + "点击此处开始扫描"
    /// </summary>
    private void UpdateCommStatus(DeviceItem? item, bool connected = true)
    {
        if (item is not null && connected)
        {
            CommStatusNameLabel.Text = item.Name;
            CommStatusMacLabel.Text = item.MacAddress;
            CommStatusEllipse1.BackgroundColor = ConnectedGreen;
            CommStatusEllipse2.BackgroundColor = ConnectedGreen;
            CommStatusEllipse2.Margin = new Thickness(-10, 0, 0, 0);
        }
        else if (item is not null && !connected)
        {
            CommStatusNameLabel.Text = item.Name;
            CommStatusMacLabel.Text = "已断开，点击重连";
            CommStatusEllipse1.BackgroundColor = GrayEllipse;
            CommStatusEllipse2.BackgroundColor = GrayEllipse;
            CommStatusEllipse2.Margin = new Thickness(0);
        }
        else
        {
            CommStatusNameLabel.Text = "未连接";
            CommStatusMacLabel.Text = "点击此处开始扫描";
            CommStatusEllipse1.BackgroundColor = GrayEllipse;
            CommStatusEllipse2.BackgroundColor = GrayEllipse;
            CommStatusEllipse2.Margin = new Thickness(0);
        }
    }

    /// <summary>
    /// 请求运行时权限并返回是否全部已授权：
    /// Android 12+ 只需「附近设备」(BLUETOOTH_SCAN/CONNECT)；
    /// Android 11 及以下需要定位权限。
    /// </summary>
    private async Task<bool> EnsurePermissionsAsync()
    {
        try
        {
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                var bt = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
                if (bt != PermissionStatus.Granted)
                    bt = await Permissions.RequestAsync<Permissions.Bluetooth>();
                return bt == PermissionStatus.Granted;
            }
            else
            {
                var loc = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (loc != PermissionStatus.Granted)
                    loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                return loc == PermissionStatus.Granted;
            }
#else
            return true;
#endif
        }
        catch (Exception ex)
        {
            CrashLogger.Log("EnsurePermissionsAsync", ex);
            return false;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 页面退出时停止扫描，避免后台耗电
        if (_adapter.IsScanning)
        {
            _adapter.StopScanningForDevicesAsync();
            MainThread.BeginInvokeOnMainThread(() => UpdateScanButton(false));
        }
    }

    #endregion

    #region 蓝牙通讯：Modbus RTU 指令收发

    /// <summary>
    /// 连接成功后枚举 GATT 服务，找出可写特征与可通知特征：
    /// 优先匹配串口透传 UUID（...FFE1），否则取第一个可写 / 可通知特征。
    /// </summary>
    private async Task SetupGattAsync(IDevice device)
    {
        _writeCharacteristic = null;
        _notifyCharacteristic = null;

        // 请求更大 MTU，确保 21 字节写帧不被拆包
        try
        {
            var mtu = await device.RequestMtuAsync(247);
            AppendLogText($"GATT: MTU = {mtu}");
        }
        catch (Exception ex)
        {
            AppendLogText($"GATT: MTU 请求失败: {ex.Message}");
        }

        var services = await device.GetServicesAsync();
        ICharacteristic? bestWrite = null;
        ICharacteristic? bestNotify = null;
        ICharacteristic? bothWriteNotify = null; // 同时可写+可通知，优先级最高

        foreach (var service in services)
        {
            var characteristics = await service.GetCharacteristicsAsync();
            foreach (var ch in characteristics)
            {
                var uuid = ch.Id.ToString().ToLower();
                var props = new List<string>();
                if (ch.CanRead) props.Add("Read");
                if (ch.CanWrite) props.Add("Write");
                if (ch.CanUpdate) props.Add("Notify");
                AppendLogText($"GATT: {uuid} [{string.Join(",", props)}]");

                bool isSerial = uuid.Contains("ffe1") || uuid.Contains("ffe2")
                              || uuid.Contains("ffb2") || uuid.Contains("ffe0")
                              || uuid.Contains("ffe3") || uuid.Contains("ffb1");

                if (ch.CanWrite && ch.CanUpdate && bothWriteNotify is null)
                    bothWriteNotify = ch;

                if (isSerial)
                {
                    if (ch.CanWrite) bestWrite ??= ch;
                    if (ch.CanUpdate) bestNotify ??= ch;
                }

                bestWrite ??= ch.CanWrite ? ch : null;
                bestNotify ??= ch.CanUpdate ? ch : null;
            }
        }

        // 优先用同时可写+可通知的特征（典型串口透传模块）
        if (bothWriteNotify is not null)
        {
            _writeCharacteristic = bothWriteNotify;
            _notifyCharacteristic = bothWriteNotify;
        }
        else
        {
            _writeCharacteristic = bestWrite;
            _notifyCharacteristic = bestNotify;
        }

        if (_notifyCharacteristic is not null)
        {
            _notifyCharacteristic.ValueUpdated += OnValueUpdated;
            await _notifyCharacteristic.StartUpdatesAsync(CancellationToken.None);
            AppendLogText($"GATT: 已订阅通知 {_notifyCharacteristic.Id}");
        }

        if (_writeCharacteristic is null)
            throw new InvalidOperationException("未找到可写的 GATT 特征");

        AppendLogText($"GATT: 写特征={_writeCharacteristic.Id}, 通知特征={_notifyCharacteristic?.Id.ToString() ?? "无"}");
    }

    /// <summary>发送 0x10 写总电量帧（输入数值 → BCD → CRC）</summary>
    private async Task SendInputFrameAsync()
    {
        try
        {
            if (!await CheckConnectedAsync())
                return;

            var frame = BuildEnergyWriteFrame(InputEntry.Text ?? string.Empty);
            if (frame is null)
            {
                await DisplayAlertAsync("格式错误", "请输入例如：334.12 或 500（整数 0-65535，小数 0-99）", "确定");
                return;
            }

            await WriteFrameAsync(frame, "[TX 写总电量]");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("SendInputFrame", ex);
            await DisplayAlertAsync("发送失败", ex.Message, "确定");
        }
    }

    /// <summary>发送 0x04 读电量帧：01 04 00 1D 00 02 + CRC</summary>
    private async Task SendOutputFrameAsync()
    {
        try
        {
            if (!await CheckConnectedAsync())
                return;

            byte[] cmd = { 0x01, 0x04, 0x00, 0x1D, 0x00, 0x02 };
            var crc = CalcModbusCrc(cmd, 0, cmd.Length);
            var frame = cmd.Concat(crc).ToArray();

            await WriteFrameAsync(frame, "[TX 读电量]");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("SendOutputFrame", ex);
            await DisplayAlertAsync("发送失败", ex.Message, "确定");
        }
    }

    /// <summary>检查是否已连接并找到可写特征</summary>
    private async Task<bool> CheckConnectedAsync()
    {
        if (_connectedItem is null || _writeCharacteristic is null)
        {
            await DisplayAlertAsync("未连接", "请先在下方「设备扫描」中连接设备。", "确定");
            return false;
        }
        return true;
    }

    /// <summary>写入 BLE 特征并记录 TX 日志，发送后主动读一次特征作为接收兜底</summary>
    private async Task WriteFrameAsync(byte[] frame, string label)
    {
        // Plugin.BLE 内部自动根据特征属性选择 WriteWithoutResponse / Write
        await _writeCharacteristic!.WriteAsync(frame, CancellationToken.None);
        AppendLog(label, frame);

        // 主动读一次：部分设备不发通知，需主动读才能拿到响应
        try
        {
            if (_notifyCharacteristic is not null && _notifyCharacteristic.CanRead)
            {
                var readResult = await _notifyCharacteristic.ReadAsync(CancellationToken.None);
                if (readResult.data is not null && readResult.data.Length > 0)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _rxBuffer.AddRange(readResult.data);
                        TryExtractFrames();
                    });
                }
            }
        }
        catch
        {
            // 主动读失败忽略，等待通知回调
        }
    }

    /// <summary>BLE 特征通知回调：数据追加到接收缓冲区，尝试提取完整 Modbus 帧</summary>
    private void OnValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var data = e.Characteristic.Value;
        if (data is null || data.Length == 0)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                _rxBuffer.AddRange(data);
                TryExtractFrames();
            }
            catch (Exception inner)
            {
                CrashLogger.Log("OnValueUpdated", inner);
            }
        });
    }

    /// <summary>
    /// 从接收缓冲区中提取完整 Modbus 帧（处理 BLE 分包）：
    /// 0x04 读响应、0x10 写响应、异常响应，CRC 校验后显示并解析。
    /// </summary>
    private void TryExtractFrames()
    {
        while (_rxBuffer.Count >= 5)
        {
            // 找帧头（从站地址 0x01）
            int idx = _rxBuffer.IndexOf(0x01);
            if (idx < 0)
            {
                _rxBuffer.Clear();
                return;
            }
            if (idx > 0)
                _rxBuffer.RemoveRange(0, idx);

            if (_rxBuffer.Count < 3)
                return;

            byte func = _rxBuffer[1];
            int expectedLen;

            if (func == 0x04)
            {
                byte byteCount = _rxBuffer[2];
                expectedLen = 3 + byteCount + 2; // 地址+功能码+字节数+数据+CRC
            }
            else if (func == 0x10)
            {
                expectedLen = 8; // 01 10 起始地址×2 寄存器数×2 CRC×2
            }
            else if ((func & 0x80) != 0)
            {
                expectedLen = 5; // 异常响应：01 8x 异常码 CRC×2
            }
            else
            {
                // 未知功能码，丢弃首字节继续找
                _rxBuffer.RemoveAt(0);
                continue;
            }

            if (_rxBuffer.Count < expectedLen)
                return; // 分包未到齐，等下一包

            var frame = _rxBuffer.GetRange(0, expectedLen).ToArray();
            _rxBuffer.RemoveRange(0, expectedLen);

            // CRC 校验
            var crc = CalcModbusCrc(frame, 0, expectedLen - 2);
            if (frame[expectedLen - 2] != crc[0] || frame[expectedLen - 1] != crc[1])
            {
                AppendLog("[RX CRC错误]", frame);
                continue;
            }

            // 显示原始帧
            AppendLog("[RX]", frame);

            // 按功能码解析
            if (func == 0x04)
            {
                var parsed = TryParseEnergyResponse(frame);
                if (parsed is not null)
                {
                    OutputLabel.Text = parsed;
                    OutputLabel.TextColor = Color.FromArgb("#1F2937");
                    AppendLogText($"  → 解析电量: {parsed}");
                }
            }
            else if (func == 0x10)
            {
                ushort addr = (ushort)((frame[2] << 8) | frame[3]);
                ushort count = (ushort)((frame[4] << 8) | frame[5]);
                AppendLogText($"  → 写成功: 起始寄存器 0x{addr:X4}, 数量 {count}");
            }
            else if ((func & 0x80) != 0)
            {
                byte exCode = frame[2];
                AppendLogText($"  → 异常响应: 功能码 0x{func:X2}, 异常码 0x{exCode:X2}");
            }
        }
    }

    /// <summary>追加一行文本日志（用于解析结果等非 hex 内容）</summary>
    private void AppendLogText(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {text}";
        _logLines.Add(line);
        while (_logLines.Count > 40)
            _logLines.RemoveAt(0);
        CommLogLabel.Text = string.Join("\n", _logLines);
    }

    /// <summary>追加一行通讯日志（保留最近 40 行）</summary>
    private void AppendLog(string label, byte[] data)
    {
        var hex = string.Join(" ", data.Select(b => b.ToString("X2")));
        var line = $"{DateTime.Now:HH:mm:ss} {label} {hex}";

        _logLines.Add(line);
        while (_logLines.Count > 40)
            _logLines.RemoveAt(0);

        CommLogLabel.Text = string.Join("\n", _logLines);
    }

    private readonly List<string> _logLines = new();
    private readonly List<byte> _rxBuffer = new(); // BLE 接收缓冲区，用于拼接分包的 Modbus 帧

    /// <summary>Modbus CRC16（多项式 0xA001，低字节在前）</summary>
    private static byte[] CalcModbusCrc(byte[] data, int offset, int len)
    {
        int crc = 0xFFFF;
        for (int i = 0; i < len; i++)
        {
            crc ^= data[offset + i] & 0xFF;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc >>= 1;
                    crc ^= 0xA001;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return new byte[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) };
    }

    /// <summary>解析定标值字符串（"334.12" / "500"）→ (整数, 小数)</summary>
    private static (long IntPart, long FracPart)? ParseFixedPoint100(string s)
    {
        s = s.Trim();
        int dot = s.IndexOf('.');
        long intPart = 0, fracPart;

        if (dot < 0)
        {
            if (!long.TryParse(s, out intPart))
                return null;
            fracPart = 0;
        }
        else
        {
            string intStr = s[..dot];
            if (intStr.Length > 0 && !long.TryParse(intStr, out intPart))
                return null;

            string frac = s[(dot + 1)..];
            if (frac.Length >= 2)
            {
                if (!long.TryParse(frac[..2], out fracPart))
                    return null;
            }
            else if (frac.Length == 1)
            {
                if (!long.TryParse(frac, out fracPart))
                    return null;
                fracPart *= 10;
            }
            else
            {
                fracPart = 0;
            }
        }

        return (intPart, fracPart);
    }

    /// <summary>BCD 编码为 4 字节（value = intPart*100 + fracPart）</summary>
    private static byte[] ToBcd4Bytes(long intPart, long fracPart)
    {
        long value = intPart * 100 + fracPart;
        var result = new byte[4];
        for (int i = 3; i >= 0; i--)
        {
            int low = (int)(value % 10);
            value /= 10;
            int high = (int)(value % 10);
            value /= 10;
            result[i] = (byte)((high << 4) | low);
        }
        return result;
    }

    /// <summary>组 0x10 写总电量帧（21 字节，含 CRC）</summary>
    private static byte[]? BuildEnergyWriteFrame(string text)
    {
        var parts = ParseFixedPoint100(text);
        if (parts is null)
            return null;

        var (totalH, totalL) = parts.Value;
        if (totalH < 0 || totalH > 0xFFFF || totalL < 0 || totalL > 99)
            return null;

        var frame = new byte[21];
        frame[0] = 0x01;   // 从站地址
        frame[1] = 0x10;   // 写多个寄存器
        frame[2] = 0x03;   // 起始寄存器高
        frame[3] = 0x00;   // 起始寄存器低
        frame[4] = 0x00;   // 寄存器数高
        frame[5] = 0x06;   // 寄存器数低
        frame[6] = 0x0C;   // 字节数

        var bcd = ToBcd4Bytes(totalH, totalL);
        frame[7] = bcd[0]; frame[8] = bcd[1]; frame[9] = bcd[2]; frame[10] = bcd[3];
        frame[11] = bcd[0]; frame[12] = bcd[1]; frame[13] = bcd[2]; frame[14] = bcd[3];
        frame[15] = 0x00; frame[16] = 0x00; frame[17] = 0x00; frame[18] = 0x00;

        var crc = CalcModbusCrc(frame, 0, 19);
        frame[19] = crc[0];
        frame[20] = crc[1];
        return frame;
    }

    /// <summary>解析 0x04 读电量响应：01 04 04 [4字节十六进制整数大端] CRC×2 → 值/100</summary>
    private static string? TryParseEnergyResponse(byte[] data)
    {
        if (data.Length < 7 || data[0] != 0x01 || data[1] != 0x04 || data[2] != 0x04)
            return null;

        // 设备返回 4 字节十六进制整数（大端），例如 00 00 34 58 → 0x3458=13400 → 134.00
        long value = ((long)data[3] << 24) | ((long)data[4] << 16) | ((long)data[5] << 8) | data[6];
        return $"{value / 100}.{value % 100:D2}";
    }

    #endregion
}
