using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Overlayer.Module.StrictJudge
{
    /// <summary>
    /// 只给热点网段看的只读报告服务：绑定 192.168.137.1:8765，
    /// 只返回桌面上的 严判失败记录.html。热点没开就完全不启动。
    /// </summary>
    public static class ReportServer
    {
        public static string BindIp = "192.168.137.1";
        public static int Port = 8765;
        public static bool Enabled = true;

        public class Config
        {
            public string bindIp { get; set; }
            public int? port { get; set; }
        }

        /// <summary>读取游戏根目录的 严判配置.json；文件不存在或字段为空则用默认值。</summary>
        public static void LoadConfig()
        {
            try
            {
                var path = System.IO.Path.Combine(State.OutDir, "严判配置.json");
                if (!System.IO.File.Exists(path)) return;
                var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(System.IO.File.ReadAllText(path, Encoding.UTF8));
                if (cfg == null) return;
                if (cfg.bindIp != null)
                {
                    BindIp = cfg.bindIp.Trim();
                    if (BindIp.Length == 0) Enabled = false;      // 留空 = 关闭热点分享
                }
                if (cfg.port.HasValue && cfg.port.Value > 0 && cfg.port.Value < 65536)
                    Port = cfg.port.Value;
                UnityEngine.Debug.Log($"[StrictJudge] 配置：Enabled={Enabled} bind={BindIp}:{Port}");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[StrictJudge] 配置读取失败: " + e.Message);
            }
        }

        private static TcpListener _listener;
        private static Thread _acceptThread;
        private static Thread _watchThread;
        private static volatile bool _running;

        public static bool HotspotPresent()
        {
            try
            {
                foreach (var ni in NetworkInterfaceSafe())
                    if (ni == BindIp) return true;
            }
            catch { }
            return false;
        }

        private static IEnumerable<string> NetworkInterfaceSafe()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily == AddressFamily.InterNetwork) list.Add(ip.ToString());
                    }
                }
            }
            catch { }
            return list;
        }

        public static void StartIfHotspot()
        {
            if (_running) return;
            LoadConfig();
            if (!Enabled)
            {
                Debug.Log("[StrictJudge] 配置里未填写热点地址，热点分享不启动");
                return;
            }
            if (!HotspotPresent())
            {
                Debug.Log("[StrictJudge] 热点未开启（无 " + BindIp + "），报告服务不启动");
                return;
            }
            Start();
        }

        public static void Start()
        {
            if (_running) return;
            try
            {
                _listener = new TcpListener(IPAddress.Parse(BindIp), Port);
                _listener.Start();
                _running = true;
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
                _acceptThread.Start();
                Debug.Log($"[StrictJudge] 报告服务已启动 http://{BindIp}:{Port}/");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StrictJudge] 报告服务启动失败: " + e.Message);
                _running = false;
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listener = null;
            Debug.Log("[StrictJudge] 报告服务已停止");
        }

        /// <summary>热点后开的情况：每 30 秒检查一次，出现就启动。</summary>
        public static void WatchHotspot()
        {
            if (_watchThread != null) return;
            _watchThread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(30000);
                    if (_running) continue;
                    if (!Enabled) continue;
                    if (HotspotPresent()) Start();
                }
            }) { IsBackground = true };
            _watchThread.Start();
        }

        private static void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    var c = client;
                    ThreadPool.QueueUserWorkItem(_ => Handle(c));
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(200);
                }
            }
        }

        private static string HtmlPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "严判失败记录.html");

        private static void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = 3000;
                    var buffer = new byte[2048];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) return;

                    var request = Encoding.ASCII.GetString(buffer, 0, read);
                    var firstLine = request.Split('\n')[0];
                    bool isRoot = firstLine.StartsWith("GET / ") || firstLine.StartsWith("GET /?")
                                  || firstLine.StartsWith("GET /report") || firstLine.StartsWith("GET /index");

                    byte[] body;
                    string status;
                    if (isRoot)
                    {
                        try { body = Encoding.UTF8.GetBytes(File.ReadAllText(HtmlPath, Encoding.UTF8)); }
                        catch (Exception e) { body = Encoding.UTF8.GetBytes("读取失败: " + e.Message); }
                        status = "200 OK";
                    }
                    else
                    {
                        body = Encoding.UTF8.GetBytes("not found");
                        status = "404 Not Found";
                    }

                    var head = "HTTP/1.1 " + status + "\r\n"
                             + "Content-Type: text/html; charset=utf-8\r\n"
                             + "Cache-Control: no-store, max-age=0\r\n"
                             + "Content-Length: " + body.Length + "\r\n"
                             + "Connection: close\r\n\r\n";
                    var headBytes = Encoding.ASCII.GetBytes(head);
                    stream.Write(headBytes, 0, headBytes.Length);
                    stream.Write(body, 0, body.Length);
                }
            }
            catch { }
        }
    }
}
