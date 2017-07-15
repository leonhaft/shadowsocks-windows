using Newtonsoft.Json;
using Shadowsocks.Controller;
using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ZXing;
using System.Net.NetworkInformation;

namespace Shadowsocks.Free
{
    public class FreeConfiguration
    {
        private static string CONFIG_FILE = "free-config.json";
        public List<FreeServer> Configs { get; set; }
        public List<string> UpdateTimes { get; set; }
        /// <summary>
        /// 获取网页中的SS地址
        /// </summary>
        public List<string> Urls { get; set; }

        [JsonIgnore]
        public List<DateTime> ServerUpdateTimes { get; set; }

        public static FreeConfiguration Load()
        {
            var configurationJsonContent = File.ReadAllText(CONFIG_FILE);
            FreeConfiguration configuration = null;
            try
            {
                configuration = JsonConvert.DeserializeObject<FreeConfiguration>(configurationJsonContent);
                //if (configuration.UpdateTimes.Any())
                //{
                //    configuration.ServerUpdateTimes = configuration.UpdateTimes.Select(ts => DateTime.Parse(ts)).ToList();

                //}
                return configuration;
            }
            catch (Exception ex)
            {
                if (!(ex is FileNotFoundException))
                    Logging.LogUsefulException(ex);
                return new FreeConfiguration
                {
                    Configs = new List<FreeServer> { },
                    Urls = new List<string>()
                };
            }
        }

        public static List<Server> UpdateFromConfig(FreeConfiguration configuration)
        {
            if (configuration.Configs == null)
            {
                return null;
            }
            var servers = new List<Server>();
            foreach (var server in configuration.Configs)
            {
                var qrCodeImage = DownloadQRCode(server.Url);

                if (qrCodeImage != null)
                {
                    var loadServers = DecodeQRCode(qrCodeImage);
                    if (loadServers == null || loadServers.Any() == false)
                    {
                        Logging.Info($"{server.Url}没有获取到SS地址");
                    }
                    else
                    {
                        loadServers.ForEach(s => s.remarks = $"{ server.Name}({PingReply(s.server)})");
                        servers.AddRange(loadServers);
                        Logging.Info($"{server.Url}获取成功");
                    }
                }
                else
                {
                    Logging.Info($"{server.Url}获取失败");
                }
            }

            return servers;
        }

        private static Stream DownloadQRCode(string url)
        {
            var httpClient = new HttpClient();
            var qrCodeImage = httpClient.GetStreamAsync(url);
            try
            {
                qrCodeImage.Wait();
                return qrCodeImage.Result;
            }
            catch (Exception ex)
            {
                Logging.LogUsefulException(ex);
                return null;
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        /// <summary>
        /// 从URL的代码中找到SS地址
        /// </summary>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static List<Server> UpdateFromUrl(FreeConfiguration configuration)
        {
            if (configuration.Urls == null)
            {
                return null;
            }
            var servers = new List<Server>();
            foreach (var url in configuration.Urls)
            {
                var urlSource = DownloadSource(url);
                if (string.IsNullOrEmpty(urlSource))
                {
                    continue;
                }
                servers.AddRange(FindSSUrlAndServers(urlSource));
            }

            return servers;
        }

        private static List<Server> FindSSUrlAndServers(string urlSource)
        {
            var serverUrls = new List<Server>();
            var ssMatchs = Server.UrlFinder.Match(urlSource);
            while (ssMatchs.Success)
            {
                serverUrls.AddRange(Server.GetServers(ssMatchs.Value));
                ssMatchs = ssMatchs.NextMatch();
            }
            return serverUrls;
        }

        private static string DownloadSource(string url)
        {
            var httpClient = new HttpClient();
            var webSource = httpClient.GetStringAsync(url);
            try
            {
                webSource.Wait();
                return webSource.Result;
            }
            catch (Exception ex)
            {
                Logging.LogUsefulException(ex);
                return null;
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        public static List<Server> DecodeQRCode(Stream qrCodeStream)
        {
            var reader = new BarcodeReader();
            var ssConfig = new List<Server>();
            Bitmap bitmap = null;
            try
            {
                bitmap = (Bitmap)Bitmap.FromStream(qrCodeStream);
                var result = reader.Decode(bitmap);
                if (result != null)
                {
                    var findServers = Server.GetServers(result.Text);
                    if (findServers != null)
                    {
                        ssConfig.AddRange(findServers);
                    }
                }
            }
            catch (Exception)
            {
                Logging.Info("非有效QRCode图片");
            }

            return ssConfig;
        }
        private static long PingReply(string ip)
        {
            using (var ping = new Ping())
            {
                var reply = ping.Send(ip, 120);
                if (reply.Status == IPStatus.Success)
                {
                    return reply.RoundtripTime;
                }
                else
                {
                    return 0;
                }
            }
        }
    }
}
