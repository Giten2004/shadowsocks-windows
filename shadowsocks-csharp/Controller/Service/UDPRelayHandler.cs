using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

using Shadowsocks.Controller.Strategy;
using Shadowsocks.Encryption;
using Shadowsocks.Model;


namespace Shadowsocks.Controller
{
    public class UDPRelayHandler
    {
        private Socket _local;
        private Socket _remote;

        private Server _server;
        private byte[] _buffer = new byte[1500];

        private IPEndPoint _localEndPoint;
        private IPEndPoint _remoteEndPoint;

        public UDPRelayHandler(Socket local, Server server, IPEndPoint localEndPoint)
        {
            _local = local;
            _server = server;
            _localEndPoint = localEndPoint;

            // TODO async resolving
            IPAddress ipAddress;
            bool parsed = IPAddress.TryParse(server.server, out ipAddress);
            if (!parsed)
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(server.server);
                ipAddress = ipHostInfo.AddressList[0];
            }
            _remoteEndPoint = new IPEndPoint(ipAddress, server.server_port);
            _remote = new Socket(_remoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        }

        public void Send(byte[] data, int length)
        {
            IEncryptor encryptor = EncryptorFactory.GetEncryptor(_server.method, _server.password, _server.auth, true);
            byte[] dataIn = new byte[length - 3 + IVEncryptor.ONETIMEAUTH_BYTES];
            Array.Copy(data, 3, dataIn, 0, length - 3);
            byte[] dataOut = new byte[length - 3 + 16 + IVEncryptor.ONETIMEAUTH_BYTES];
            int outlen;
            encryptor.Encrypt(dataIn, length - 3, dataOut, out outlen);
            Logging.Debug(_localEndPoint, _remoteEndPoint, outlen, "UDP Relay");
            _remote.SendTo(dataOut, outlen, SocketFlags.None, _remoteEndPoint);
        }

        public void Receive()
        {
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            Logging.Debug($"++++++Receive Server Port, size:" + _buffer.Length);
            _remote.BeginReceiveFrom(_buffer, 0, _buffer.Length, 0, ref remoteEndPoint, new AsyncCallback(RecvFromCallback), null);
        }

        public void RecvFromCallback(IAsyncResult ar)
        {
            try
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int bytesRead = _remote.EndReceiveFrom(ar, ref remoteEndPoint);

                byte[] dataOut = new byte[bytesRead];
                int outlen;

                IEncryptor encryptor = EncryptorFactory.GetEncryptor(_server.method, _server.password, _server.auth, true);
                encryptor.Decrypt(_buffer, bytesRead, dataOut, out outlen);

                byte[] sendBuf = new byte[outlen + 3];
                Array.Copy(dataOut, 0, sendBuf, 3, outlen);

                Logging.Debug(_localEndPoint, _remoteEndPoint, outlen, "UDP Relay");
                _local.SendTo(sendBuf, outlen + 3, 0, _localEndPoint);
                Receive();
            }
            catch (ObjectDisposedException)
            {
                // TODO: handle the ObjectDisposedException
            }
            catch (Exception)
            {
                // TODO: need more think about handle other Exceptions, or should remove this catch().
            }
            finally
            {
            }
        }

        public void Close()
        {
            try
            {
                _remote.Close();
            }
            catch (ObjectDisposedException)
            {
                // TODO: handle the ObjectDisposedException
            }
            catch (Exception)
            {
                // TODO: need more think about handle other Exceptions, or should remove this catch().
            }
            finally
            {
            }
        }
    }
}
