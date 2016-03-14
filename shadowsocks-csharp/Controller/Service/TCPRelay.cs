using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Timers;

using Shadowsocks.Controller.Strategy;
using Shadowsocks.Encryption;
using Shadowsocks.Model;

namespace Shadowsocks.Controller
{
    class TCPRelay : IService
    {
        private ShadowsocksController _controller;
        private DateTime _lastSweepTime;

        public ISet<TCPRelayHandler> Handlers
        {
            get; set;
        }

        public TCPRelay(ShadowsocksController controller)
        {
            _controller = controller;
            Handlers = new HashSet<TCPRelayHandler>();
            _lastSweepTime = DateTime.Now;
        }

        public bool Handle(byte[] firstPacket, int length, Socket socket, object state)
        {
            if (socket.ProtocolType != ProtocolType.Tcp)
            {
                return false;
            }

            //ref: Socks5 protocol 
            if (length < 2 || firstPacket[0] != 5)
            {
                return false;
            }

            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            TCPRelayHandler handler = new TCPRelayHandler(this);
            handler.connection = socket;
            handler.controller = _controller;
            handler.relay = this;

            handler.Start(firstPacket, length);
            IList<TCPRelayHandler> handlersToClose = new List<TCPRelayHandler>();

            lock (Handlers)
            {
                Handlers.Add(handler);
                DateTime now = DateTime.Now;
                if (now - _lastSweepTime > TimeSpan.FromSeconds(1))
                {
                    _lastSweepTime = now;
                    foreach (TCPRelayHandler handler1 in Handlers)
                    {
                        if (now - handler1.lastActivity > TimeSpan.FromSeconds(900))
                        {
                            handlersToClose.Add(handler1);
                        }
                    }
                }
            }

            foreach (TCPRelayHandler handler1 in handlersToClose)
            {
                Logging.Debug("Closing timed out TCP connection.");
                handler1.Close();
            }

            return true;
        }

        public void UpdateInboundCounter(Server server, long n)
        {
            _controller.UpdateInboundCounter(server, n);
        }

        public void UpdateOutboundCounter(Server server, long n)
        {
            _controller.UpdateOutboundCounter(server, n);
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            _controller.UpdateLatency(server, latency);
        }
    }

   
}
