using System;
using static NetworkManager.Protocol;

namespace NetworkManager
{
  public class InfoNode
  {
    private readonly Node _base;
    private StandardAnswer _connectionStatus = StandardAnswer.Disconnected;

    public InfoNode(Node Base) => _base = Base;

    public string Address => _base.Address;
    public string MachineName => _base.MachineName;
    public string PublicKey => _base.PublicKey;
    public uint Ip => _base.Ip;
    public int Connections { get; private set; }
    public StandardAnswer ConnectionStatus
    {
      get => _connectionStatus;
      internal set
      {
        _connectionStatus = value;
        OnConnectionStatusChanged?.Invoke(EventArgs.Empty, ConnectionStatus);
      }
    }
    public event EventHandler<StandardAnswer> OnConnectionStatusChanged;
    internal void RaiseEventOnNodeListChanged(int count) { Connections = count; OnNodeListChanged?.Invoke(EventArgs.Empty, count); }
    public event EventHandler<int> OnNodeListChanged;
  }
}
