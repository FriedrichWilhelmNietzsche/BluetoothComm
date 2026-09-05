using System.ComponentModel;
using System.Runtime.CompilerServices;
using Plugin.BLE.Abstractions.Contracts;

namespace BluetoothComm;

/// <summary>
/// 扫描结果列表中的单个设备项（带属性通知，连接状态变化时界面自动刷新）
/// </summary>
public class DeviceItem : INotifyPropertyChanged
{
    /// <summary>设备名称（广播名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>设备标识（Plugin.BLE 中为 Guid）</summary>
    public Guid Id { get; set; }

    /// <summary>设备标识显示文本（MAC / UUID）</summary>
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>Plugin.BLE 原生设备对象</summary>
    public IDevice? NativeDevice { get; set; }

    private int _rssi;
    /// <summary>信号强度</summary>
    public int Rssi
    {
        get => _rssi;
        set
        {
            if (_rssi != value)
            {
                _rssi = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RssiText));
            }
        }
    }

    /// <summary>RSSI 显示文本</summary>
    public string RssiText => $"{_rssi} dBm";

    private bool _isConnected;
    /// <summary>是否已连接（已连接=椭圆重叠，断开=椭圆分开）</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isConnecting;
    /// <summary>是否正在连接</summary>
    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (_isConnecting != value)
            {
                _isConnecting = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
