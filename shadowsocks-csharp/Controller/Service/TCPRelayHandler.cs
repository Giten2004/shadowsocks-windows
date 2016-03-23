using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Timers;

using Shadowsocks.Controller.Strategy;
using Shadowsocks.Encryption;
using Shadowsocks.Model;
using System.Text;

namespace Shadowsocks.Controller
{
    class TCPRelayHandler
    {
        private class ServerTimer : Timer
        {
            public Server Server { get; private set; }

            public ServerTimer(double interval, Server server) : base(interval)
            {
                Server = server;
            }
        }

        private const int MaxRetry = 4;
        // Size of receive buffer.
        private const int RecvSize = 8192;
        private const int RecvReserveSize = IVEncryptor.ONETIMEAUTH_BYTES + IVEncryptor.AUTH_BYTES; // reserve for one-time auth
        private const int BufferSize = RecvSize + RecvReserveSize + 32;

        private IEncryptor _encryptor;
        private Server _server;
        // Client  socket.
        private Socket _ssServerSocket;
        private Socket _connection;
        private ShadowsocksController _ssController;

        private int _retryCount = 0;
        private bool _connected;

        private byte _command;
        private byte[] _firstPacket;
        private int _firstPacketLength;

        private int _totalRead = 0;
        private int _totalWrite = 0;

        private byte[] _ssServerRecvBuffer = new byte[BufferSize];
        private byte[] _ssServerSend2UserBuffer = new byte[BufferSize];
        private byte[] _connetionRecvBuffer = new byte[BufferSize];
        private byte[] _connetionSend2SSServerBuffer = new byte[BufferSize];

        private bool _connectionShutdown = false;
        private bool _remoteShutdown = false;
        private bool _closed = false;

        private object _encryptionLock = new object();
        private object _decryptionLock = new object();

        private DateTime _startConnectTime;
        private DateTime _startReceivingTime;
        private DateTime _startSendingTime;
        private int _bytesToSend;
        private TCPRelay _tcprelay;  // TODO: tcprelay ?= relay

        public DateTime LastActivity { get; private set; }

        public TCPRelayHandler(TCPRelay tcprelay, Socket connection, ShadowsocksController controller)
        {
            this._tcprelay = tcprelay;
            this._connection = connection;
            this._ssController = controller;
        }

        public void Start(byte[] firstPacket, int firstPacketLength)
        {
            _firstPacket = firstPacket;
            _firstPacketLength = firstPacketLength;

            HandshakeReceive();

            LastActivity = DateTime.Now;
        }

        private void CheckClose()
        {
            if (_connectionShutdown && _remoteShutdown)
            {
                Close();
            }
        }

        public void Close()
        {
            lock (_tcprelay.Handlers)
            {
                _tcprelay.Handlers.Remove(this);
            }

            lock (this)
            {
                if (_closed)
                {
                    return;
                }
                _closed = true;
            }

            if (_connection != null)
            {
                try
                {
                    _connection.Shutdown(SocketShutdown.Both);
                    _connection.Close();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }

            if (_ssServerSocket != null)
            {
                try
                {
                    _ssServerSocket.Shutdown(SocketShutdown.Both);
                    _ssServerSocket.Close();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }

            lock (_encryptionLock)
            {
                lock (_decryptionLock)
                {
                    if (_encryptor != null)
                    {
                        ((IDisposable)_encryptor).Dispose();
                    }
                }
            }
        }

        private void HandshakeReceive()
        {
            if (_closed)
            {
                return;
            }

            try
            {
                int bytesRead = _firstPacketLength;

                if (bytesRead > 1)
                {
                    //+-----+--------+
                    //| VER | METHOD |
                    //+-----+--------+
                    //| 1   | 1      |
                    //+-----+--------+
                    // X'00' NO AUTHENTICATION REQUIRED
                    byte[] response = { 5, 0 };
                    if (_firstPacket[0] != 5)
                    {
                        // reject socks 4
                        response = new byte[] { 0, 91 };
                        Logging.Error("socks 5 protocol error");
                    }

                    _connection.BeginSend(response, 0, response.Length, SocketFlags.None, new AsyncCallback(HandshakeSendCallback), null);
                }
                else
                {
                    Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void HandshakeSendCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                _connection.EndSend(ar);

                // +-----+-----+-------+------+----------+----------+
                // | VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
                // +-----+-----+-------+------+----------+----------+
                // |  1  |  1  | X'00' |  1   | Variable |    2     |
                // +-----+-----+-------+------+----------+----------+

                // TODO validate
                //receive the request details of the client sent
                // only read first 3 bytes
                _connection.BeginReceive(_connetionRecvBuffer, 0, 3, SocketFlags.None, new AsyncCallback(handshakeReceive2Callback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void handshakeReceive2Callback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                // +-----+-----+-------+------+----------+----------+
                // | VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
                // +-----+-----+-------+------+----------+----------+
                // |  1  |  1  | X'00' |  1   | Variable |    2     |
                // +-----+-----+-------+------+----------+----------+
                // only read the first 3 bytes, why?
                int bytesRead = _connection.EndReceive(ar);

                //o VER    protocol version: X'05'
                //o CMD
                //   o CONNECT X'01'
                //   o BIND X'02'
                //   o UDP ASSOCIATE X'03'
                //o RSV    RESERVED
                if (bytesRead >= 3)
                {
                    _command = _connetionRecvBuffer[1];
                    if (_command == 1)
                    {
                        //+-----+-----+-------+------+----------+----------+
                        //| VER | REP | RSV   | ATYP | BND.ADDR | BND.PORT |
                        //+-----+-----+-------+------+----------+----------+
                        //| 1   | 1   | X'00' | 1    | Variable | 2        |
                        //+-----+-----+-------+------+----------+----------+
                        // o VER    protocol version: X'05'
                        // o REP    Reply field:
                        //    o X'00' succeeded
                        //    o X'01' general SOCKS server failure
                        //    o X'02' connection not allowed by ruleset
                        //    o X'03' Network unreachable
                        //    o X'04' Host unreachable
                        //    o X'05' Connection refused
                        //    o X'06' TTL expired
                        //    o X'07' Command not supported
                        //    o X'08' Address type not supported
                        //    o X'09' to X'FF' unassigned
                        // o  RSV RESERVED
                        // o ATYP   address type of following address
                        //    o IP V4 address: X'01'
                        //    o DOMAINNAME: X'03'
                        //    o IP V6 address: X'04'
                        // o BND.ADDR server bound address
                        // o BND.PORT server bound port in network octet order
                        byte[] response = { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
                        _connection.BeginSend(response, 0, response.Length, SocketFlags.None, new AsyncCallback(ResponseCallback), null);
                    }
                    else if (_command == 3)
                    {
                        HandleUDPAssociate();
                    }
                }
                else
                {
                    Logging.Debug("failed to recv data in Shadowsocks.Controller.TCPHandler.handshakeReceive2Callback()");
                    Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void HandleUDPAssociate()
        {
            IPEndPoint endPoint = (IPEndPoint)_connection.LocalEndPoint;
            byte[] address = endPoint.Address.GetAddressBytes();
            int port = endPoint.Port;
            byte[] response = new byte[4 + address.Length + 2];
            response[0] = 5;
            if (endPoint.AddressFamily == AddressFamily.InterNetwork)
            {
                response[3] = 1;
            }
            else if (endPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                response[3] = 4;
            }
            address.CopyTo(response, 4);
            response[response.Length - 1] = (byte)(port & 0xFF);
            response[response.Length - 2] = (byte)((port >> 8) & 0xFF);
            _connection.BeginSend(response, 0, response.Length, SocketFlags.None, new AsyncCallback(ReadAll), true);
        }

        private void ReadAll(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                if (ar.AsyncState != null)
                {
                    _connection.EndSend(ar);
                    Logging.Debug(_ssServerSocket, RecvSize, "TCP Relay");
                    _connection.BeginReceive(_connetionRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(ReadAll), null);
                }
                else
                {
                    int bytesRead = _connection.EndReceive(ar);
                    if (bytesRead > 0)
                    {
                        Logging.Debug(_ssServerSocket, RecvSize, "TCP Relay");
                        _connection.BeginReceive(_connetionRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(ReadAll), null);
                    }
                    else
                    {
                        Close();
                    }
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void ResponseCallback(IAsyncResult ar)
        {
            try
            {
                _connection.EndSend(ar);

                StartConnect();
            }

            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void ChooseServer()
        {
            Server server = _ssController.GetAServer(StrategyCallerType.TCP, (IPEndPoint)_connection.RemoteEndPoint);
            if (server == null || server.server == "")
            {
                throw new ArgumentException("No server configured");
            }
            this._server = server;
            _encryptor = EncryptorFactory.GetEncryptor(server.method, server.password, server.auth, false);
        }

        private void StartConnect()
        {
            try
            {
                ChooseServer();

                // TODO async resolving
                IPAddress ipAddress;
                bool parsed = IPAddress.TryParse(_server.server, out ipAddress);
                if (!parsed)
                {
                    IPHostEntry ipHostInfo = Dns.GetHostEntry(_server.server);
                    ipAddress = ipHostInfo.AddressList[0];
                }
                IPEndPoint ssServerEndPoint = new IPEndPoint(ipAddress, _server.server_port);

                _ssServerSocket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _ssServerSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                _startConnectTime = DateTime.Now;
                ServerTimer connectTimer = new ServerTimer(3000, _server);
                connectTimer.AutoReset = false;
                connectTimer.Elapsed += connectTimer_Elapsed;
                connectTimer.Enabled = true;

                _connected = false;
                // Connect to the remote endpoint.
                _ssServerSocket.BeginConnect(ssServerEndPoint, new AsyncCallback(SSServerConnectCallback), connectTimer);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void connectTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_connected)
            {
                return;
            }

            Server server = ((ServerTimer)sender).Server;
            IStrategy strategy = _ssController.GetCurrentStrategy();
            if (strategy != null)
            {
                strategy.SetFailure(server);
            }

            Logging.Info($"{server.FriendlyName()} timed out");

            _ssServerSocket.Close();

            RetryConnect();
        }

        private void RetryConnect()
        {
            if (_retryCount < MaxRetry)
            {
                Logging.Debug($"Connection failed, retry ({_retryCount})");
                StartConnect();
                _retryCount++;
            }
            else
            {
                Close();
            }
        }

        private void SSServerConnectCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            Server server = null;

            try
            {
                ServerTimer timer = (ServerTimer)ar.AsyncState;
                timer.Elapsed -= connectTimer_Elapsed;
                timer.Enabled = false;
                timer.Dispose();

                server = timer.Server;

                // Complete the connection.
                _ssServerSocket.EndConnect(ar);

                _connected = true;

                Logging.Debug($"Socket connected to {_ssServerSocket.RemoteEndPoint}");

                var latency = DateTime.Now - _startConnectTime;

                IStrategy strategy = _ssController.GetCurrentStrategy();
                strategy?.UpdateLatency(server, latency);

                //_tcprelay.UpdateLatency(server, latency);
                _ssController.UpdateLatency(server, latency);

                StartPipe();
            }
            catch (ArgumentException)
            {
            }
            catch (Exception e)
            {
                if (server != null)
                {
                    IStrategy strategy = _ssController.GetCurrentStrategy();
                    if (strategy != null)
                    {
                        strategy.SetFailure(server);
                    }
                }
                Logging.LogUsefulException(e);
                RetryConnect();
            }
        }

        private void StartPipe()
        {
            if (_closed)
            {
                return;
            }

            try
            {
                _startReceivingTime = DateTime.Now;
                _ssServerSocket.BeginReceive(_ssServerRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeRemoteReceiveCallback), null);
                _connection.BeginReceive(_connetionRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeConnectionReceiveCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeRemoteReceiveCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                int bytesRead = _ssServerSocket.EndReceive(ar);
                _totalRead += bytesRead;
                _ssController.UpdateInboundCounter(_server, bytesRead);

                if (bytesRead > 0)
                {
                    LastActivity = DateTime.Now;
                    int bytesToSend;

                    lock (_decryptionLock)
                    {
                        if (_closed)
                        {
                            return;
                        }
                        _encryptor.Decrypt(_ssServerRecvBuffer, bytesRead, _ssServerSend2UserBuffer, out bytesToSend);
                    }

                    Logging.Debug(_ssServerSocket, bytesToSend, "TCP Relay", "@PipeRemoteReceiveCallback() (download)");

                    _connection.BeginSend(_ssServerSend2UserBuffer, 0, bytesToSend, SocketFlags.None, new AsyncCallback(PipeConnectionSendCallback), null);

                    //[[ test
                    //string request = Encoding.UTF8.GetString(_ssServerSend2UserBuffer, 0, bytesToSend);
                    //Logging.Debug("From SS:" + request);
                    //]]
                    IStrategy strategy = _ssController.GetCurrentStrategy();
                    if (strategy != null)
                    {
                        strategy.UpdateLastRead(_server);
                    }
                }
                else
                {
                    _connection.Shutdown(SocketShutdown.Send);
                    _connectionShutdown = true;
                    CheckClose();

                    //if (totalRead == 0)
                    //{
                    //    // closed before anything received, reports as failure
                    //    // disable this feature
                    //    controller.GetCurrentStrategy().SetFailure(this.server);
                    //}
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeConnectionReceiveCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                int bytesRead = _connection.EndReceive(ar);
                _totalWrite += bytesRead;

                if (bytesRead > 0)
                {
                    int bytesToSend;
                    lock (_encryptionLock)
                    {
                        if (_closed)
                        {
                            return;
                        }
                        //[[ test
                        //string request = Encoding.UTF8.GetString(_connetionRecvBuffer, 0, bytesRead);
                        //Logging.Debug("From User:" + request);
                        //]]

                        _encryptor.Encrypt(_connetionRecvBuffer, bytesRead, _connetionSend2SSServerBuffer, out bytesToSend);
                    }

                    Logging.Debug(_ssServerSocket, bytesToSend, "TCP Relay", "@PipeConnectionReceiveCallback() (upload)");
                    //_tcprelay.UpdateOutboundCounter(_server, bytesToSend);
                    _ssController.UpdateOutboundCounter(_server, bytesToSend);

                    _startSendingTime = DateTime.Now;
                    _bytesToSend = bytesToSend;
                    _ssServerSocket.BeginSend(_connetionSend2SSServerBuffer, 0, bytesToSend, SocketFlags.None, new AsyncCallback(PipeRemoteSendCallback), null);

                    IStrategy strategy = _ssController.GetCurrentStrategy();
                    strategy?.UpdateLastWrite(_server);
                }
                else
                {
                    _ssServerSocket.Shutdown(SocketShutdown.Send);
                    _remoteShutdown = true;
                    CheckClose();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeRemoteSendCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                _ssServerSocket.EndSend(ar);
                _connection.BeginReceive(_connetionRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeConnectionReceiveCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeConnectionSendCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                _connection.EndSend(ar);

                _ssServerSocket.BeginReceive(_ssServerRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeRemoteReceiveCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }
    }
}
