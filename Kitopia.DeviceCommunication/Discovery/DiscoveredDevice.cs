using System.ComponentModel;
using System.Net;

namespace Kitopia.DeviceCommunication.Discovery;

public sealed class DiscoveredDevice : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _customName = string.Empty;
    private IPAddress _ipv4Address = IPAddress.None;
    private IPAddress _ipv6Address = IPAddress.None;
    private int _tcpPort;
    private DateTime _lastSeen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value, nameof(Id));
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value, nameof(Name)))
            {
                OnPropertyChanged(nameof(ComputerName));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string CustomName
    {
        get => _customName;
        set
        {
            if (SetField(ref _customName, value, nameof(CustomName)))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public IPAddress Ipv4Address
    {
        get => _ipv4Address;
        set
        {
            if (SetField(ref _ipv4Address, value, nameof(Ipv4Address)))
            {
                OnPropertyChanged(nameof(HasIpv4));
                OnPropertyChanged(nameof(PreferredTransportAddress));
            }
        }
    }

    public IPAddress Ipv6Address
    {
        get => _ipv6Address;
        set
        {
            if (SetField(ref _ipv6Address, value, nameof(Ipv6Address)))
            {
                OnPropertyChanged(nameof(HasIpv6));
                OnPropertyChanged(nameof(PreferredTransportAddress));
            }
        }
    }

    public int TcpPort
    {
        get => _tcpPort;
        set => SetField(ref _tcpPort, value, nameof(TcpPort));
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set => SetField(ref _lastSeen, value, nameof(LastSeen));
    }

    public bool HasIpv4 => Ipv4Address != IPAddress.None;
    public bool HasIpv6 => Ipv6Address != IPAddress.None;
    public IPAddress PreferredTransportAddress => Ipv6Address != IPAddress.None ? Ipv6Address : Ipv4Address;
    public string ComputerName => string.IsNullOrWhiteSpace(Name) ? "未知设备" : Name;
    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? ComputerName : $"{CustomName} ({ComputerName})";

    private bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
