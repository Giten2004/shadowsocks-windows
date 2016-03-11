using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Shadowsocks.Controller
{
    public class UDPState
    {
        public byte[] Buffer;
        public EndPoint RemoteEndPoint;

        public UDPState()
        {
            Buffer = new byte[4096];
            RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }
    }
}
