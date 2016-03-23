using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using AngleSharp.Parser.Html;
using Shadowsocks.Model;
using System.Diagnostics;

namespace Shadowsocks.Controller
{
    public class FreeSSServerUpdater
    {
        private List<Server> _onlineServerList;

        public FreeSSServerUpdater()
        {
            _onlineServerList = new List<Server>();
        }

        public List<Server> GetOnlineServers()
        {
            if (_onlineServerList.Count <= 0)
            {
                _onlineServerList.AddRange(GetServerFromIShadowsocks());
                _onlineServerList.AddRange(GetServerFromFreevpnss());
            }

            return _onlineServerList;
        }

        private List<Server> GetServerFromIShadowsocks()
        {
            const string IShadowsocks_URL = "http://www.ishadowsocks.net/";
            var serverList = new List<Server>();

            try
            {
                var htmlStr = string.Empty;
                using (WebClient http = new WebClient())
                {
                    http.Encoding = Encoding.UTF8;
                    //http.Proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
                    htmlStr = http.DownloadString(new Uri(IShadowsocks_URL));
                }

                var parser = new HtmlParser();

                var document = parser.Parse(htmlStr);

                var serverNodes = document.QuerySelectorAll("#free > div.container > div.row > div.col-lg-4,text-center");
                foreach (var serverNode in serverNodes)
                {
                    var infoNodes = serverNode.QuerySelectorAll("h4");
                    var serverName = infoNodes[0].TextContent.Split(new[] { ':' })[1].Trim();
                    var serverPort = int.Parse(infoNodes[1].TextContent.Split(new[] { ':' })[1].Trim());
                    var password = infoNodes[2].TextContent.Split(new[] { ':' })[1].Trim();
                    var encriptMethod = infoNodes[3].TextContent.Split(new[] { ':' })[1].Trim();
                    var serverStatus = infoNodes[4].TextContent.Split(new[] { ':' })[1].Trim();

                    serverList.Add(new Server() { auth = false, method = encriptMethod, password = password, remarks = "", server = serverName, server_port = serverPort });
                }
            }
            catch (Exception ex)
            {
                Debug.Write("get account from ishadowsocks failed.");
            }

            return serverList;
        }

        private List<Server> GetServerFromFreevpnss()
        {
            const string FreeVPNSSNet_URL = "https://www.freevpnss.net/";
            var serverList = new List<Server>();

            try
            {
                var htmlStr = string.Empty;
                using (WebClient http = new WebClient())
                {
                    http.Encoding = Encoding.UTF8;
                    //http.Proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
                    htmlStr = http.DownloadString(new Uri(FreeVPNSSNet_URL));
                }

                var parser = new HtmlParser();

                var document = parser.Parse(htmlStr);

                var serverNodes = document.QuerySelectorAll("div.panel-body");
                foreach (var serverNode in serverNodes)
                {
                    var infoNodes = serverNode.QuerySelectorAll("p");
                    if (infoNodes.Length >= 6)
                        continue; //ignore vpn

                    var serverName = Uri.EscapeUriString(infoNodes[0].TextContent).ToLower().Split(new string[] { "%ef%bc%9a" }, StringSplitOptions.None)[1].Trim();
                    var serverPort = int.Parse(Uri.EscapeUriString(infoNodes[1].TextContent).ToLower().Split(new string[] { "%ef%bc%9a" }, StringSplitOptions.None)[1].Trim());
                    var password = Uri.EscapeUriString(infoNodes[2].TextContent).ToLower().Split(new string[] { "%ef%bc%9a" }, StringSplitOptions.None)[1].Trim();
                    var encriptMethod = Uri.EscapeUriString(infoNodes[3].TextContent).ToLower().Split(new string[] { "%ef%bc%9a" }, StringSplitOptions.None)[1].Trim();

                    var serverStatus = infoNodes[4].QuerySelector("span").TextContent.Trim();

                    serverList.Add(new Server() { auth = false, method = encriptMethod, password = password, remarks = "", server = serverName, server_port = serverPort });
                }
            }
            catch (Exception)
            {
                Debug.Write("get account from freevpnss failed.");
            }

            return serverList;
        }
    }
}
