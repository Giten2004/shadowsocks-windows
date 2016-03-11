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
    class UDPRelay : IService
    {
        private ShadowsocksController _controller;
        private LRUCache<IPEndPoint, UDPRelayHandler> _cache;

        //public long outbound = 0;
        //public long inbound = 0;

        public UDPRelay(ShadowsocksController controller)
        {
            this._controller = controller;
            this._cache = new LRUCache<IPEndPoint, UDPRelayHandler>(512);  // todo: choose a smart number
        }

        public bool Handle(byte[] firstPacket, int length, Socket socket, object state)
        {
            if (socket.ProtocolType != ProtocolType.Udp)
            {
                return false;
            }

            if (length < 4)
            {
                return false;
            }

            UDPState udpState = (UDPState)state;
            IPEndPoint remoteEndPoint = (IPEndPoint)udpState.RemoteEndPoint;

            UDPRelayHandler handler = _cache.get(remoteEndPoint);
            if (handler == null)
            {
                handler = new UDPRelayHandler(socket, _controller.GetAServer(IStrategyCallerType.UDP, remoteEndPoint), remoteEndPoint);
                _cache.add(remoteEndPoint, handler);
            }

            handler.Send(firstPacket, length);
            handler.Receive();

            return true;
        }
    }
}