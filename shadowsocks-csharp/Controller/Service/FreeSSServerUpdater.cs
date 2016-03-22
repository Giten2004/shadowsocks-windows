using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using AngleSharp.Parser.Html;
using Shadowsocks.Model;

namespace Shadowsocks.Controller
{
    public class FreeSSServerUpdater
    {
        private const string IShadowsocks_URL = "http://www.ishadowsocks.net/";

        //todo: use https://fizzlerex.codeplex.com/ to pause html and get the content

        private string _htmlStr;
        public FreeSSServerUpdater()
        {
            WebClient http = new WebClient();
            http.Encoding = Encoding.UTF8;
            //http.Proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
            http.DownloadStringCompleted += http_DownloadStringCompleted;
            _htmlStr = http.DownloadString(new Uri(IShadowsocks_URL));
        }

        private void http_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                //"#free > div.container > div.row > div.col-lg-4.text-center"];
            }
            catch (Exception ex)
            {

            }
        }

        public List<Server> GetOnlineServers()
        {
            var serverList = new List<Server>();
            var parser = new HtmlParser();
            var document = parser.Parse(_htmlStr);

            var serverNodes = document.QuerySelectorAll("#free > div.container > div.row > div.col-lg-4,text-center");
            foreach (var serverNode in serverNodes)
            {
                var infoNodes = serverNode.QuerySelectorAll("h4");
                var serverName = infoNodes[0].TextContent.Split(new[] { ':' })[1];
                var serverPort = int.Parse(infoNodes[1].TextContent.Split(new[] { ':' })[1]);
                var password = infoNodes[2].TextContent.Split(new[] { ':' })[1];
                var encriptMethod = infoNodes[3].TextContent.Split(new[] { ':' })[1];
                var serverStatus = infoNodes[4].TextContent.Split(new[] { ':' })[1];

                serverList.Add(new Server() { auth = false, method = encriptMethod, password = password, remarks = "", server = serverName, server_port = serverPort });
            }

            return serverList;
        }
    }
}
