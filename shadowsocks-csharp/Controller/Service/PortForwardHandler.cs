using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shadowsocks.Controller
{
    /// <summary>
    /// Forward http proxy on local running port 
    /// to
    /// Privoxy's http proxy port
    /// </summary>
    class PortForwardHandler
    {
        public const int RecvSize = 16384;

        private byte[] _firstPacket;
        private int _firstPacketLength;
        private Socket _local;
        private Socket _remote;
        private bool _closed = false;
        private bool _localShutdown = false;
        private bool _remoteShutdown = false;
        // remote receive buffer
        private byte[] remoteRecvBuffer = new byte[RecvSize];
        // connection receive buffer
        private byte[] connetionRecvBuffer = new byte[RecvSize];

        public void Start(byte[] firstPacket, int length, Socket socket, int targetPort)
        {
            this._firstPacket = firstPacket;
            this._firstPacketLength = length;
            this._local = socket;

            try
            {
                var tt = Encoding.UTF8.GetString(firstPacket, 0, length);
                // TODO async resolving
                IPAddress ipAddress;
                bool parsed = IPAddress.TryParse("127.0.0.1", out ipAddress);
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, targetPort);

                _remote = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _remote.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                // Connect to the Privoxy's running port.
                _remote.BeginConnect(remoteEP, new AsyncCallback(RemoteConnectCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        /// <summary>
        /// The remove is Privoxy's running port
        /// </summary>
        /// <param name="ar"></param>
        private void RemoteConnectCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                _remote.EndConnect(ar);

                ForwardRequestData();
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        private void ForwardRequestData()
        {
            if (_closed)
            {
                return;
            }

            try
            {
                //transfer the data received from local shadowsocks proxy port 
                //to 
                //Privoxy's running port
                _remote.BeginSend(_firstPacket, 0, _firstPacketLength, SocketFlags.None, new AsyncCallback(StartPipe), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        private void StartPipe(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                _remote.EndSend(ar);

                _remote.BeginReceive(remoteRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeRemoteReceiveCallback), null);
                _local.BeginReceive(connetionRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeConnectionReceiveCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
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
                int bytesRead = _remote.EndReceive(ar);

                if (bytesRead > 0)
                {
                    _local.BeginSend(remoteRecvBuffer, 0, bytesRead, SocketFlags.None, new AsyncCallback(PipeConnectionSendCallback), null);
                }
                else
                {
                    _local.Shutdown(SocketShutdown.Send);
                    _localShutdown = true;
                    CheckClose();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
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
                int bytesRead = _local.EndReceive(ar);

                if (bytesRead > 0)
                {
                    _remote.BeginSend(connetionRecvBuffer, 0, bytesRead, SocketFlags.None, new AsyncCallback(PipeRemoteSendCallback), null);
                }
                else
                {
                    _remote.Shutdown(SocketShutdown.Send);
                    _remoteShutdown = true;
                    CheckClose();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
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
                _remote.EndSend(ar);
                _local.BeginReceive(this.connetionRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeConnectionReceiveCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
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
                _local.EndSend(ar);

                _remote.BeginReceive(this.remoteRecvBuffer, 0, RecvSize, SocketFlags.None, new AsyncCallback(PipeRemoteReceiveCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        private void CheckClose()
        {
            if (_localShutdown && _remoteShutdown)
            {
                this.Close();
            }
        }

        private void Close()
        {
            lock (this)
            {
                if (_closed)
                {
                    return;
                }
                _closed = true;
            }

            if (_local != null)
            {
                try
                {
                    _local.Shutdown(SocketShutdown.Both);
                    _local.Close();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }

            if (_remote != null)
            {
                try
                {
                    _remote.Shutdown(SocketShutdown.Both);
                    _remote.Close();
                }
                catch (SocketException e)
                {
                    Logging.LogUsefulException(e);
                }
            }
        }
    }
}
