using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using Shadowsocks.Model;

namespace Shadowsocks.Controller
{
    public class Listener
    {
        private Configuration _config;
        private bool _shareOverLAN;
        private Socket _tcpSocket;
        private Socket _udpSocket;
        private IList<IService> _services;

        public Listener(IList<IService> services)
        {
            this._services = services;
        }

        private bool CheckIfPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] ipEndPoints = ipProperties.GetActiveTcpListeners();

            foreach (IPEndPoint endPoint in ipEndPoints)
            {
                if (endPoint.Port == port)
                {
                    return true;
                }
            }
            return false;
        }

        public void Start(Configuration config)
        {
            this._config = config;
            this._shareOverLAN = config.shareOverLan;

            if (CheckIfPortInUse(_config.localPort))
                throw new Exception(I18N.GetString("Port already in use"));

            try
            {
                // Create a TCP/IP socket.
                _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                IPEndPoint localEndPoint = null;
                if (_shareOverLAN)
                {
                    localEndPoint = new IPEndPoint(IPAddress.Any, _config.localPort);
                }
                else
                {
                    localEndPoint = new IPEndPoint(IPAddress.Loopback, _config.localPort);
                }

                // Bind the socket to the local endpoint and listen for incoming connections.
                _tcpSocket.Bind(localEndPoint);
                _udpSocket.Bind(localEndPoint);
                _tcpSocket.Listen(1024);

                // Start an asynchronous socket to listen for connections.
                Logging.Info("Shadowsocks started");
                _tcpSocket.BeginAccept(new AsyncCallback(TCPAcceptCallback), _tcpSocket);

                UDPState udpState = new UDPState();
                _udpSocket.BeginReceiveFrom(udpState.Buffer, 0, udpState.Buffer.Length, 0, ref udpState.RemoteEndPoint, new AsyncCallback(UDPRecvFromCallback), udpState);
            }
            catch (SocketException)
            {
                _tcpSocket.Close();
                throw;
            }
        }

        public void Stop()
        {
            if (_tcpSocket != null)
            {
                _tcpSocket.Close();
                _tcpSocket = null;
            }
            if (_udpSocket != null)
            {
                _udpSocket.Close();
                _udpSocket = null;
            }
        }

        private void UDPRecvFromCallback(IAsyncResult ar)
        {
            UDPState state = (UDPState)ar.AsyncState;
            try
            {
                int bytesRead = _udpSocket.EndReceiveFrom(ar, ref state.RemoteEndPoint);
                foreach (IService service in _services)
                {
                    if (service.Handle(state.Buffer, bytesRead, _udpSocket, state))
                    {
                        break;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    _udpSocket.BeginReceiveFrom(state.Buffer, 0, state.Buffer.Length, 0, ref state.RemoteEndPoint, new AsyncCallback(UDPRecvFromCallback), state);
                }
                catch (ObjectDisposedException)
                {
                    // do nothing
                }
                catch (Exception)
                {
                }
            }
        }

        private void TCPAcceptCallback(IAsyncResult ar)
        {
            Socket listener = (Socket)ar.AsyncState;
            try
            {
                Socket conn = listener.EndAccept(ar);

                byte[] buf = new byte[4096];
                object[] state = new object[] {
                    conn,
                    buf
                };

                conn.BeginReceive(buf, 0, buf.Length, 0, new AsyncCallback(TCPReceiveCallback), state);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
            finally
            {
                try
                {
                    listener.BeginAccept(new AsyncCallback(TCPAcceptCallback), listener);
                }
                catch (ObjectDisposedException)
                {
                    // do nothing
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }
        }

        private void TCPReceiveCallback(IAsyncResult ar)
        {
            object[] state = (object[])ar.AsyncState;

            Socket conn = (Socket)state[0];
            byte[] buf = (byte[])state[1];

            try
            {
                int bytesRead = conn.EndReceive(ar);
                foreach (IService service in _services)
                {
                    if (service.Handle(buf, bytesRead, conn, null))
                    {
                        return;
                    }
                }

                // no service found for this
                if (conn.ProtocolType == ProtocolType.Tcp)
                {
                    //todo: log issue
                    conn.Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                conn.Close();
            }
        }
    }
}
