using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kitopia.DeviceCommunication.Discovery;

public partial class DiscoveredDevice : ObservableObject
{
    public DiscoveredDevice() { }

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _customName = string.Empty;

    [ObservableProperty]
    private string _operatingSystem = string.Empty;

    [ObservableProperty]
    private IPAddress _ipv4Address = IPAddress.None;

    [ObservableProperty]
    private IPAddress _ipv6Address = IPAddress.None;

    [ObservableProperty]
    private int _tcpPort;

    [ObservableProperty]
    private DateTime _lastSeen;

    public bool HasIpv4 => Ipv4Address != IPAddress.None;

    public bool HasIpv6 => Ipv6Address != IPAddress.None;

    public IPAddress PreferredTransportAddress => Ipv6Address != IPAddress.None
        ? Ipv6Address
        : Ipv4Address;

    public string ComputerName => string.IsNullOrWhiteSpace(Name) ? "未知设备" : Name;

    public string OperatingSystemDisplayName => string.IsNullOrWhiteSpace(OperatingSystem)
        ? "未知系统"
        : OperatingSystem;

    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? ComputerName : $"{CustomName} ({ComputerName})";

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(ComputerName));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnCustomNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnOperatingSystemChanged(string value)
    {
        OnPropertyChanged(nameof(OperatingSystemDisplayName));
    }

    partial void OnIpv4AddressChanged(IPAddress value)
    {
        OnPropertyChanged(nameof(HasIpv4));
        OnPropertyChanged(nameof(PreferredTransportAddress));
    }

    partial void OnIpv6AddressChanged(IPAddress value)
    {
        OnPropertyChanged(nameof(HasIpv6));
        OnPropertyChanged(nameof(PreferredTransportAddress));
    }

    public override string ToString() =>
        $"{DisplayName} ({PreferredTransportAddress}:{TcpPort})";

    public bool Equals(DiscoveredDevice? other)
    {
        if (other is null) return false;

        if (!string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(other.Id))
            return string.Equals(Id, other.Id, StringComparison.Ordinal);

        return TcpPort == other.TcpPort &&
               string.Equals(PreferredTransportAddress.ToString(), other.PreferredTransportAddress.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
