using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shadowsocks.Controller;
using Newtonsoft.Json;

namespace Shadowsocks.Model
{
    [Serializable]
    public class Configuration
    {
        public List<Server> configs;

        // when strategy is set, index is ignored
        public string strategy;
        public int index;
        public bool global;
        public bool enabled;
        public bool shareOverLan;
        public bool isDefault;
        public int localPort;
        public string pacUrl;
        public bool useOnlinePac;
        public bool availabilityStatistics;
        public bool autoCheckUpdate;

        public LogViewerConfig logViewer;

        public Server GetCurrentServer()
        {
            if (index >= 0 && index < configs.Count)
                return configs[index];
            else
                return CreateNewServer();
        }

        public static void CheckServer(Server server)
        {
            CheckPort(server.server_port);
            CheckPassword(server.password);
            CheckServer(server.server);
        }       

        public static Server CreateNewServer()
        {
            return new Server();
        }     

        private static void Assert(bool condition)
        {
            if (!condition)
                throw new Exception(I18N.GetString("assertion failure"));
        }

        public static void CheckPort(int port)
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentException(I18N.GetString("Port out of range"));
        }

        public static void CheckLocalPort(int port)
        {
            CheckPort(port);
            if (port == 8123)
                throw new ArgumentException(I18N.GetString("Port can't be 8123"));
        }

        private static void CheckPassword(string password)
        {
            if (password.IsNullOrEmpty())
                throw new ArgumentException(I18N.GetString("Password can not be blank"));
        }

        private static void CheckServer(string server)
        {
            if (server.IsNullOrEmpty())
                throw new ArgumentException(I18N.GetString("Server IP can not be blank"));
        }
    }
}
