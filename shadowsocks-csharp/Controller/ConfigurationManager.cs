using Newtonsoft.Json;
using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace Shadowsocks.Controller
{
    public class ConfigurationManager
    {
        private const string CONFIG_FILE = "gui-config.json";
        public Configuration Configuration { get; private set; }

        private ConfigurationManager()
        {
            //todo: start one timeer to check the published resource if them are updated.
            //
        }

        static ConfigurationManager()
        {
            SingleTon = new ConfigurationManager();
        }

        public static ConfigurationManager SingleTon { get; private set; }

        public void LoadConfig()
        {
            if (Configuration != null)
                return;

            try
            {
                string configContent = File.ReadAllText(CONFIG_FILE);
                Configuration config = JsonConvert.DeserializeObject<Configuration>(configContent);
                config.isDefault = false;

                var onlineServers = GetServerFromInternet();
                foreach (var onlineServer in onlineServers)
                {
                    var savedServer = config.configs.FirstOrDefault(x => x.server == onlineServer.server);
                    if (savedServer != null)
                    {
                        //update
                        savedServer.server_port = onlineServer.server_port;
                        savedServer.method = onlineServer.method;
                        savedServer.password = onlineServer.password;
                    }
                    else
                    {
                        config.configs.Add(onlineServer);
                    }
                }

                if (config.localPort == 0)
                    config.localPort = 1080;

                if (config.index == -1 && config.strategy == null)
                    config.index = 0;

                Configuration = config;
            }
            catch (Exception e)
            {
                if (!(e is FileNotFoundException))
                    Logging.LogUsefulException(e);

                try
                {
                    var tempconfig = new Configuration()
                    {
                        index = 0,
                        isDefault = false,
                        localPort = 1080,
                        autoCheckUpdate = true,
                        configs = new List<Server>()
                    };
                    var onlineServers = GetServerFromInternet();
                    tempconfig.configs.AddRange(onlineServers);

                    Configuration = tempconfig;
                }
                catch (Exception onlineEx)
                {
                    if (!(onlineEx is FileNotFoundException))
                        Logging.LogUsefulException(onlineEx);

                    Configuration = new Configuration
                    {
                        index = 0,
                        isDefault = true,
                        localPort = 1080,
                        autoCheckUpdate = true,
                        configs = new List<Server>()
                        {
                            Configuration.CreateNewServer()
                        }
                    };
                }
            }
        }

        public void Save(Configuration newConfig)
        {
            if (newConfig.index >= newConfig.configs.Count)
                newConfig.index = newConfig.configs.Count - 1;

            if (newConfig.index < -1)
                newConfig.index = -1;

            if (newConfig.index == -1 && newConfig.strategy == null)
                newConfig.index = 0;

            newConfig.isDefault = false;

            try
            {
                using (StreamWriter sw = new StreamWriter(File.Open(CONFIG_FILE, FileMode.Create)))
                {
                    string jsonString = JsonConvert.SerializeObject(newConfig, Formatting.Indented);
                    sw.Write(jsonString);
                    sw.Flush();
                }

                Configuration = newConfig;
            }
            catch (IOException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        public Configuration CloneConfiguration()
        {
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, Configuration);
                ms.Position = 0;

                return (Configuration)formatter.Deserialize(ms);
            }
        }

        private IList<Server> GetServerFromInternet()
        {
            return new FreeSSServerUpdater().GetOnlineServers();
        }
    }
}
