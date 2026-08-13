using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class UdpConnect : IDisposable
{
    private UdpClient _udpClient;
    private string _serverIp;
    private int _serverPort;
    private IPEndPoint _serverEP;
    private bool _isOpen = false;

    public bool IsOpen => _isOpen;

    public UdpConnect(string ipAddress, int port)
    {
        _serverIp = ipAddress;
        _serverPort = port;
    }

    public void DoConnect()
    {
        if (_udpClient != null)
        {
            Debug.LogWarning("UDP client already connected.");
            return;
        }

        try
        {
            _serverEP = new IPEndPoint(IPAddress.Parse(_serverIp), _serverPort);
            _udpClient = new UdpClient();
            _udpClient.Connect(_serverEP);

            // Optional: Send a handshake message
            //byte[] data = Encoding.ASCII.GetBytes("Clover Drone");
            //_udpClient.Send(data, data.Length);

            _isOpen = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"UDP Connect error: {ex}");
            _isOpen = false;
            Dispose();
        }
    }

    public void DoDisconnect()
    {
        try
        {
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient.Dispose();
                _udpClient = null;
            }
            _isOpen = false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error when disconnecting from UAV: {ex}");
        }
    }

    public int SendData(byte[] data)
    {
        if (_udpClient == null || !_isOpen)
        {
            Debug.LogWarning("UDP client is not connected.");
            return 0;
        }

        try
        {
            return _udpClient.Send(data, data.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError($"UDP SendData error: {ex}");
            return 0;
        }
    }

    public int ReceiveData(out byte[] data)
    {
        data = null;
        if (_udpClient == null || !_isOpen)
        {
            Debug.LogWarning("UDP client is not connected.");
            return 0;
        }

        try
        {
            data = _udpClient.Receive(ref _serverEP);
            return data?.Length ?? 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"UDP ReceiveData error: {ex}");
            return 0;
        }
    }

    public void Dispose()
    {
        DoDisconnect();
        GC.SuppressFinalize(this);
    }
}
