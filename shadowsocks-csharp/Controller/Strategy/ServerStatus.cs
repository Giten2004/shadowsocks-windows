using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shadowsocks.Controller
{
    public class ServerStatus
    {
        // time interval between SYN and SYN+ACK
        public TimeSpan Latency { get; set; }
        public DateTime LastTimeDetectLatency { get; set; }

        // last time anything received
        public DateTime LastRead { get; set; }

        // last time anything sent
        public DateTime LastWrite { get; set; }

        // connection refused or closed before anything received
        public DateTime LastFailure { get; set; }

        public Server Server { get; set; }

        public double Score { get; set; }
    }
}
