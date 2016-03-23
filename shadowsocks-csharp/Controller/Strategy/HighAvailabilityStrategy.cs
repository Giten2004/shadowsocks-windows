using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shadowsocks.Controller.Strategy
{
    class HighAvailabilityStrategy : IStrategy
    {
        private ServerStatus _currentServer;
        private Dictionary<Server, ServerStatus> _serverStatus;
        private ShadowsocksController _controller;
        private Random _random;

        public HighAvailabilityStrategy(ShadowsocksController controller)
        {
            _controller = controller;
            _random = new Random();
            _serverStatus = new Dictionary<Server, ServerStatus>();
        }

        #region Implement methods of interface IStrategy

        public string Name
        {
            get { return I18N.GetString("High Availability"); }
        }

        public string ID
        {
            get { return "com.shadowsocks.strategy.ha"; }
        }

        public void ReloadServers()
        {
            // make a copy to avoid locking
            var serverStatusDic = new Dictionary<Server, ServerStatus>(_serverStatus);

            foreach (var server in _controller.Configuration.configs)
            {
                if (!serverStatusDic.ContainsKey(server))
                {
                    var status = new ServerStatus();
                    status.Server = server;
                    status.LastFailure = DateTime.MinValue;
                    status.LastRead = DateTime.Now;
                    status.LastWrite = DateTime.Now;
                    status.Latency = new TimeSpan(0, 0, 0, 0, 10);
                    status.LastTimeDetectLatency = DateTime.Now;

                    serverStatusDic[server] = status;
                }
                else
                {
                    // update settings for existing server
                    serverStatusDic[server].Server = server;
                }
            }
            _serverStatus = serverStatusDic;

            ChooseNewServer();
        }

        public Server GetAServer(StrategyCallerType type, System.Net.IPEndPoint localIPEndPoint)
        {
            if (type == StrategyCallerType.TCP)
            {
                ChooseNewServer();
            }

            if (_currentServer == null)
            {
                return null;
            }

            return _currentServer.Server;
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            Logging.Debug($"latency: {server.FriendlyName()} {latency}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
            {
                status.Latency = latency;
                status.LastTimeDetectLatency = DateTime.Now;
            }
        }

        public void UpdateLastRead(Model.Server server)
        {
            Logging.Debug($"last read: {server.FriendlyName()}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
            {
                status.LastRead = DateTime.Now;
            }
        }

        public void UpdateLastWrite(Model.Server server)
        {
            Logging.Debug($"last write: {server.FriendlyName()}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
            {
                status.LastWrite = DateTime.Now;
            }
        }

        public void SetFailure(Model.Server server)
        {
            Logging.Debug($"failure: {server.FriendlyName()}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
            {
                status.LastFailure = DateTime.Now;
            }
        }

        #endregion

        /**
       * once failed, try after 5 min
       * and (last write - last read) < 5s
       * and (now - last read) <  5s  // means not stuck
       * and latency < 200ms, try after 30s
       */
        private void ChooseNewServer()
        {
            ServerStatus oldServer = _currentServer;
            List<ServerStatus> servers = new List<ServerStatus>(_serverStatus.Values);
            DateTime now = DateTime.Now;
            foreach (var status in servers)
            {
                // all of failure, latency, (lastread - lastwrite) normalized to 1000, then
                // 100 * failure - 2 * latency - 0.5 * (lastread - lastwrite)
                status.Score =
                    100 * 1000 * Math.Min(5 * 60, (now - status.LastFailure).TotalSeconds)
                    - 2 * 5 * (Math.Min(2000, status.Latency.TotalMilliseconds) / (1 + (now - status.LastTimeDetectLatency).TotalSeconds / 30 / 10) +
                    -0.5 * 200 * Math.Min(5, (status.LastRead - status.LastWrite).TotalSeconds));

                Logging.Debug(String.Format("server: {0} latency:{1} score: {2}", status.Server.FriendlyName(), status.Latency, status.Score));
            }
            ServerStatus max = null;
            foreach (var status in servers)
            {
                if (max == null)
                {
                    max = status;
                }
                else
                {
                    if (status.Score >= max.Score)
                    {
                        max = status;
                    }
                }
            }
            if (max != null)
            {
                if (_currentServer == null || max.Score - _currentServer.Score > 200)
                {
                    _currentServer = max;
                    Logging.Info($"HA switching to server: {_currentServer.Server.FriendlyName()}");
                }
            }
        }
    }
}
