using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

using Shadowsocks.Controller.Strategy;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;

namespace Shadowsocks.Controller
{
    public class PathEventArgs : EventArgs
    {
        public string Path { get; private set; }

        public PathEventArgs(string path)
        {
            Path = path;
        }
    }

    // controller:
    // handle user actions
    // manipulates UI
    // interacts with low level logic
    public class ShadowsocksController
    {
        private Thread _ramThread;

        private Listener _listener;
        private PACServer _pacServer;
        public Configuration Configuration { get; private set; }
        private StrategyManager _strategyManager;
        private PolipoRunner polipoRunner;
        private GFWListUpdater gfwListUpdater;

        public AvailabilityStatistics availabilityStatistics = AvailabilityStatistics.Instance;
        public StatisticsStrategyConfiguration StatisticsConfiguration { get; private set; }

        public long inboundCounter = 0;
        public long outboundCounter = 0;

        private bool stopped = false;
        private bool _systemProxyIsDirty = false;      

        public event EventHandler ConfigChanged;
        public event EventHandler SystemProxyStatusChanged;
        public event EventHandler EnableGlobalChanged;
        public event EventHandler ShareOverLANStatusChanged;

        // when user clicked Edit PAC, and PAC file has already created
        public event EventHandler<PathEventArgs> PACFileReadyToOpen;
        public event EventHandler<PathEventArgs> UserRuleFileReadyToOpen;
        public event EventHandler<ResultEventArgs> UpdatePACFromGFWListCompleted;
        public event ErrorEventHandler UpdatePACFromGFWListError;
        public event ErrorEventHandler Errored;

        public ShadowsocksController()
        {
            ConfigurationManager.SingleTon.LoadConfig();
            Configuration = ConfigurationManager.SingleTon.Configuration;

            StatisticsConfiguration = StatisticsStrategyConfiguration.Load();

            _strategyManager = new StrategyManager(this);

            StartReleasingMemory();
        }

        public void Start()
        {
            Reload();
        }

        // always return copy
        //todo: stupid!!!!!! reafactor
        public Configuration GetConfigurationCopy()
        {
            return ConfigurationManager.SingleTon.CloneConfiguration();
        }

        public IList<IStrategy> GetStrategies()
        {
            return _strategyManager.GetStrategies();
        }

        public IStrategy GetCurrentStrategy()
        {
            foreach (var strategy in _strategyManager.GetStrategies())
            {
                if (strategy.ID == this.Configuration.strategy)
                {
                    return strategy;
                }
            }
            return null;
        }

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint)
        {
            IStrategy strategy = GetCurrentStrategy();
            if (strategy != null)
            {
                return strategy.GetAServer(type, localIPEndPoint);
            }
            if (Configuration.index < 0)
            {
                Configuration.index = 0;
            }
            return Configuration.GetCurrentServer();
        }

        public void SaveServers(List<Server> servers, int localPort)
        {
            Configuration.configs = servers;
            Configuration.localPort = localPort;

            ConfigurationManager.SingleTon.Save(Configuration);
        }

        public void SaveStrategyConfigurations(StatisticsStrategyConfiguration configuration)
        {
            StatisticsConfiguration = configuration;
            StatisticsStrategyConfiguration.Save(configuration);
        }

        public bool AddServerBySSURL(string ssURL)
        {
            try
            {
                var server = new Server(ssURL);
                Configuration.configs.Add(server);
                Configuration.index = Configuration.configs.Count - 1;
                SaveConfig(Configuration);
                return true;
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                return false;
            }
        }

        public void ToggleSystemProxyEnable(bool enabled)
        {
            Configuration.enabled = enabled;
            UpdateSystemProxy();
            SaveConfig(Configuration);

            if (SystemProxyStatusChanged != null)
            {
                SystemProxyStatusChanged(this, new EventArgs());
            }
        }

        public void ToggleGlobal(bool global)
        {
            Configuration.global = global;
            UpdateSystemProxy();
            SaveConfig(Configuration);
            if (EnableGlobalChanged != null)
            {
                EnableGlobalChanged(this, new EventArgs());
            }
        }

        public void ToggleShareOverLAN(bool enabled)
        {
            Configuration.shareOverLan = enabled;
            SaveConfig(Configuration);
            if (ShareOverLANStatusChanged != null)
            {
                ShareOverLANStatusChanged(this, new EventArgs());
            }
        }

        public void SelectServerIndex(int index)
        {
            Configuration.index = index;
            Configuration.strategy = null;
            SaveConfig(Configuration);
        }

        public void SelectStrategy(string strategyID)
        {
            Configuration.index = -1;
            Configuration.strategy = strategyID;
            SaveConfig(Configuration);
        }

        public void Stop()
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            if (_listener != null)
            {
                _listener.Stop();
            }

            if (polipoRunner != null)
            {
                polipoRunner.Stop();
            }

            if (Configuration.enabled)
            {
                SystemProxy.Update(Configuration, true);
            }
        }

        public void TouchPACFile()
        {
            string pacFilename = _pacServer.TouchPACFile();
            if (PACFileReadyToOpen != null)
            {
                PACFileReadyToOpen(this, new PathEventArgs(pacFilename) );
            }
        }

        public void TouchUserRuleFile()
        {
            string userRuleFilename = _pacServer.TouchUserRuleFile();
            if (UserRuleFileReadyToOpen != null)
            {
                UserRuleFileReadyToOpen(this, new PathEventArgs(userRuleFilename));
            }
        }

        public string GetQRCodeForCurrentServer()
        {
            Server server = Configuration.GetCurrentServer();
            return GetQRCode(server);
        }

        public static string GetQRCode(Server server)
        {
            string parts = server.method + ":" + server.password + "@" + server.server + ":" + server.server_port;
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(parts));
            return "ss://" + base64;
        }

        public void UpdatePACFromGFWList()
        {
            if (gfwListUpdater != null)
            {
                gfwListUpdater.UpdatePACFromGFWList(Configuration);
            }
        }

        public void UpdateStatisticsConfiguration(bool enabled)
        {
            if (availabilityStatistics == null) return;
            availabilityStatistics.UpdateConfiguration(this);
            Configuration.availabilityStatistics = enabled;
            SaveConfig(Configuration);
        }

        public void SavePACUrl(string pacUrl)
        {
            Configuration.pacUrl = pacUrl;
            UpdateSystemProxy();
            SaveConfig(Configuration);
            if (ConfigChanged != null)
            {
                ConfigChanged(this, new EventArgs());
            }
        }

        public void UseOnlinePAC(bool useOnlinePac)
        {
            Configuration.useOnlinePac = useOnlinePac;
            UpdateSystemProxy();
            SaveConfig(Configuration);
            if (ConfigChanged != null)
            {
                ConfigChanged(this, new EventArgs());
            }
        }

        public void ToggleCheckingUpdate(bool enabled)
        {
            Configuration.autoCheckUpdate = enabled;
            ConfigurationManager.SingleTon.Save(Configuration);
        }

        public void SaveLogViewerConfig(LogViewerConfig newConfig)
        {
            Configuration.logViewer = newConfig;
            ConfigurationManager.SingleTon.Save(Configuration);
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            if (Configuration.availabilityStatistics)
            {
                new Task(() => availabilityStatistics.UpdateLatency(server, (int) latency.TotalMilliseconds)).Start();
            }
        }

        public void UpdateInboundCounter(Server server, long n)
        {
            Interlocked.Add(ref inboundCounter, n);
            if (Configuration.availabilityStatistics)
            {
                new Task(() => availabilityStatistics.UpdateInboundCounter(server, n)).Start();
            }
        }

        public void UpdateOutboundCounter(Server server, long n)
        {
            Interlocked.Add(ref outboundCounter, n);
            if (Configuration.availabilityStatistics)
            {
                new Task(() => availabilityStatistics.UpdateOutboundCounter(server, n)).Start();
            }
        }

        private void Reload()
        {
            // some logic in configuration updated the config when saving, we need to read it again
            Configuration = ConfigurationManager.SingleTon.Configuration;

            StatisticsConfiguration = StatisticsStrategyConfiguration.Load();

            if (polipoRunner == null)
            {
                polipoRunner = new PolipoRunner();
            }

            if (_pacServer == null)
            {
                _pacServer = new PACServer();
                _pacServer.PACFileChanged += pacServer_PACFileChanged;
                _pacServer.UserRuleFileChanged += pacServer_UserRuleFileChanged;
            }

            _pacServer.UpdateConfiguration(Configuration);

            if (gfwListUpdater == null)
            {
                gfwListUpdater = new GFWListUpdater();
                gfwListUpdater.UpdateCompleted += pacServer_PACUpdateCompleted;
                gfwListUpdater.Error += pacServer_PACUpdateError;
            }

            availabilityStatistics.UpdateConfiguration(this);

            if (_listener != null)
            {
                _listener.Stop();
            }
            // don't put polipoRunner.Start() before pacServer.Stop()
            // or bind will fail when switching bind address from 0.0.0.0 to 127.0.0.1
            // though UseShellExecute is set to true now
            // http://stackoverflow.com/questions/10235093/socket-doesnt-close-after-application-exits-if-a-launched-process-is-open
            polipoRunner.Stop();

            try
            {
                var strategy = GetCurrentStrategy();
                if (strategy != null)
                {
                    strategy.ReloadServers();
                }

                polipoRunner.Start(Configuration);

                TCPRelay tcpRelay = new TCPRelay(this);
                UDPRelay udpRelay = new UDPRelay(this);

                List<IService> services = new List<IService>();
                services.Add(tcpRelay);
                services.Add(udpRelay);

                services.Add(_pacServer);
                services.Add(new PortForwarder(polipoRunner.RunningPort));

                _listener = new Listener(services);
                _listener.Start(Configuration);
            }
            catch (Exception e)
            {
                // translate Microsoft language into human language
                // i.e. An attempt was made to access a socket in a way forbidden by its access permissions => Port already in use
                if (e is SocketException)
                {
                    SocketException se = (SocketException)e;
                    if (se.SocketErrorCode == SocketError.AccessDenied)
                    {
                        e = new Exception(I18N.GetString("Port already in use"), e);
                    }
                }
                Logging.LogUsefulException(e);
                ReportError(e);
            }

            if (ConfigChanged != null)
            {
                ConfigChanged(this, new EventArgs());
            }

            UpdateSystemProxy();

            Utils.ReleaseMemory(true);
        }

        private void SaveConfig(Configuration newConfig)
        {
            ConfigurationManager.SingleTon.Save(newConfig);

            Reload();
        }

        private void UpdateSystemProxy()
        {
            if (Configuration.enabled)
            {
                SystemProxy.Update(Configuration, false);
                _systemProxyIsDirty = true;
            }
            else
            {
                // only switch it off if we have switched it on
                if (_systemProxyIsDirty)
                {
                    SystemProxy.Update(Configuration, false);
                    _systemProxyIsDirty = false;
                }
            }
        }

        private void pacServer_PACFileChanged(object sender, EventArgs e)
        {
            UpdateSystemProxy();
        }

        private void pacServer_PACUpdateCompleted(object sender, ResultEventArgs e)
        {
            if (UpdatePACFromGFWListCompleted != null)
                UpdatePACFromGFWListCompleted(this, e);
        }

        private void pacServer_PACUpdateError(object sender, ErrorEventArgs e)
        {
            if (UpdatePACFromGFWListError != null)
                UpdatePACFromGFWListError(this, e);
        }

        private static readonly IEnumerable<char> IgnoredLineBegins = new[] { '!', '[' };
        private void pacServer_UserRuleFileChanged(object sender, EventArgs e)
        {
            // TODO: this is a dirty hack. (from code GListUpdater.http_DownloadStringCompleted())
            if (!File.Exists(Utils.GetTempPath("gfwlist.txt")))
            {
                UpdatePACFromGFWList();
                return;
            }
            List<string> lines = GFWListUpdater.ParseBase64String(File.ReadAllText(Utils.GetTempPath("gfwlist.txt")));
            if (File.Exists(PACServer.USER_RULE_FILE))
            {
                string local = File.ReadAllText(PACServer.USER_RULE_FILE, Encoding.UTF8);
                using (var sr = new StringReader(local))
                {
                    foreach (var rule in sr.NonWhiteSpaceLines())
                    {
                        if (rule.BeginWithAny(IgnoredLineBegins))
                            continue;
                        lines.Add(rule);
                    }
                }
            }
            string abpContent;
            if (File.Exists(PACServer.USER_ABP_FILE))
            {
                abpContent = File.ReadAllText(PACServer.USER_ABP_FILE, Encoding.UTF8);
            }
            else
            {
                abpContent = Utils.UnGzip(Resources.abp_js);
            }
            abpContent = abpContent.Replace("__RULES__", JsonConvert.SerializeObject(lines, Formatting.Indented));
            if (File.Exists(PACServer.PAC_FILE))
            {
                string original = File.ReadAllText(PACServer.PAC_FILE, Encoding.UTF8);
                if (original == abpContent)
                {
                    return;
                }
            }
            File.WriteAllText(PACServer.PAC_FILE, abpContent, Encoding.UTF8);
        }

        private void StartReleasingMemory()
        {
            _ramThread = new Thread(new ThreadStart(ReleaseMemory));
            _ramThread.IsBackground = true;
            _ramThread.Start();
        }

        private void ReleaseMemory()
        {
            while (true)
            {
                Utils.ReleaseMemory(false);
                Thread.Sleep(30 * 1000);
            }
        }

        private void ReportError(Exception e)
        {
            if (Errored != null)
            {
                Errored(this, new ErrorEventArgs(e));
            }
        }
    }
}
