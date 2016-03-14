using System;
using System.Net;
using System.Net.Sockets;

namespace Shadowsocks.Controller
{
    class PortForwarder : IService
    {
        int _targetPort;

        public PortForwarder(int targetPort)
        {
            this._targetPort = targetPort;
        }

        public bool Handle(byte[] firstPacket, int length, Socket socket, object state)
        {
            if (socket.ProtocolType != ProtocolType.Tcp)
            {
                return false;
            }

            new PortForwardHandler().Start(firstPacket, length, socket, this._targetPort);

            return true;
        }

      
    }
}
