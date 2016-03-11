using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shadowsocks.Controller
{
    public class ServerStatus
    {
        // time interval between SYN and SYN+ACK
        public TimeSpan latency;
        public DateTime lastTimeDetectLatency;

        // last time anything received
        public DateTime lastRead;

        // last time anything sent
        public DateTime lastWrite;

        // connection refused or closed before anything received
        public DateTime lastFailure;

        public Server server;

        public double score;
    }
}
