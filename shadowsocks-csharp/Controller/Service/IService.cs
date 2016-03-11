using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Shadowsocks.Controller
{
    public interface IService
    {
        bool Handle(byte[] firstPacket, int length, Socket socket, object state);
    }
}
