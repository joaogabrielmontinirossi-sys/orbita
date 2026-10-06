// Órbita para Windows: serve o app em 127.0.0.1, abre-o numa janela própria (Edge em modo app)
// e oferece a API de sincronização que grava orbita-sync.json numa pasta do Google Drive.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Orbita
{
    const int Port = 47821;
    const string SyncFile = "orbita-sync.json";
    const int MaxBody = 20 * 1024 * 1024;
    static readonly object Gate = new object();
    static string dataDir, cfgPath, folder = "";
    static long lastRequest = DateTime.UtcNow.Ticks;

    [STAThread]
    static void Main()
    {
        dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbita");
        Directory.CreateDirectory(dataDir);
        cfgPath = Path.Combine(dataDir, "sync-folder.txt");
        try { if (File.Exists(cfgPath)) folder = File.ReadAllText(cfgPath, Encoding.UTF8).Trim(); } catch { }

        TcpListener listener = null;
        try { listener = new TcpListener(IPAddress.Loopback, Port); listener.Start(); }
        catch (SocketException) { listener = null; } // já existe um Órbita aberto servindo nesta porta

        if (listener != null)
        {
            TcpListener l = listener;
            Thread t = new Thread(delegate() { AcceptLoop(l); });
            t.IsBackground = true;
            t.Start();
        }

        Process window = OpenWindow();
        if (listener == null) return;
        if (window != null) { try { window.WaitForExit(); } catch { } }
        // A página chama a API a cada 30 s; sem chamadas por um tempo, a janela foi fechada.
        while ((DateTime.UtcNow.Ticks - Interlocked.Read(ref lastRequest)) < TimeSpan.FromSeconds(150).Ticks) Thread.Sleep(5000);
    }

    static Process OpenWindow()
    {
        string url = "http://127.0.0.1:" + Port + "/";
        string[] roots = {
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        string[] rel = { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" };
        foreach (string r in rel)
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string exe = Path.Combine(root, r);
                if (!File.Exists(exe)) continue;
                string profile = Path.Combine(dataDir, "janela");
                string args = "--app=" + url + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check --window-size=1240,900";
                try { return Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false }); } catch { }
            }
        try { Process.Start(url); } catch { }
        return null;
    }

    static void AcceptLoop(TcpListener l)
    {
        while (true)
        {
            TcpClient c;
            try { c = l.AcceptTcpClient(); } catch { return; }
            ThreadPool.QueueUserWorkItem(delegate(object o) { Handle((TcpClient)o); }, c);
        }
    }

    static void Handle(TcpClient client)
    {
        try
        {
            client.ReceiveTimeout = 15000;
            client.SendTimeout = 15000;
            NetworkStream s = client.GetStream();
            MemoryStream buf = new MemoryStream();
            byte[] chunk = new byte[16384];
            int headEnd = -1;
            while (headEnd < 0)
            {
                int n = s.Read(chunk, 0, chunk.Length);
                if (n <= 0) return;
                buf.Write(chunk, 0, n);
                headEnd = FindHeadEnd(buf.GetBuffer(), (int)buf.Length);
                if (headEnd < 0 && buf.Length > 65536) return;
            }
            string head = Encoding.ASCII.GetString(buf.GetBuffer(), 0, headEnd);
            string[] lines = head.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] rq = lines[0].Split(' ');
            if (rq.Length < 2) return;
            string method = rq[0], path = rq[1];
            int q = path.IndexOf('?');
            if (q >= 0) path = path.Substring(0, q);
            Dictionary<string, string> h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) h[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }

            // Só atende a própria janela: outro site não consegue se passar por este endereço nem enviar o cabeçalho.
            string host; h.TryGetValue("Host", out host);
            if (host != "127.0.0.1:" + Port && host != "localhost:" + Port) { Send(s, 403, "text/plain", "proibido"); return; }

            int len = 0; string cl;
            if (h.TryGetValue("Content-Length", out cl)) int.TryParse(cl, out len);
            if (len < 0 || len > MaxBody) { Send(s, 413, "text/plain", "grande demais"); return; }
            byte[] body = new byte[len];
            int have = Math.Min(len, (int)buf.Length - (headEnd + 4));
            if (have > 0) Buffer.BlockCopy(buf.GetBuffer(), headEnd + 4, body, 0, have);
            while (have < len)
            {
                int n = s.Read(body, have, len - have);
                if (n <= 0) return;
                have += n;
            }

            Interlocked.Exchange(ref lastRequest, DateTime.UtcNow.Ticks);

            if (path.StartsWith("/api/"))
            {
                string token; h.TryGetValue("X-Orbita", out token);
                if (token != "1") { Send(s, 403, "text/plain", "proibido"); return; }
                try { Api(s, method, path.Substring(5), body); }
                catch (Exception ex) { Send(s, 500, "text/plain", ex.Message); }
                return;
            }
            if (method != "GET") { Send(s, 405, "text/plain", "método não permitido"); return; }
            string name = path == "/" ? "index.html" : path.Substring(path.LastIndexOf('/') + 1);
            byte[] res = Resource(name);
            if (res == null) { Send(s, 404, "text/plain", "não encontrado"); return; }
            Send(s, 200, Mime(name), res);
        }
        catch { }
        finally { try { client.Close(); } catch { } }
    }

    static void Api(Stream s, string method, string route, byte[] body)
    {
        lock (Gate)
        {
            if (route == "ping") { Send(s, 204, "text/plain", new byte[0]); return; }
            if (route == "sync/info" && method == "GET") { Send(s, 200, "application/json", Info()); return; }
            if (route == "sync" && method == "GET")
            {
                if (folder == "") { Send(s, 409, "text/plain", "sincronização desativada"); return; }
                string p = Path.Combine(folder, SyncFile);
                if (!File.Exists(p)) { Send(s, 204, "text/plain", new byte[0]); return; }
                Send(s, 200, "application/json", File.ReadAllBytes(p));
                return;
            }
            if (route == "sync" && method == "POST")
            {
                if (folder == "") { Send(s, 409, "text/plain", "sincronização desativada"); return; }
                if (body.Length < 2 || body[0] != (byte)'{') { Send(s, 400, "text/plain", "conteúdo inválido"); return; }
                string p = Path.Combine(folder, SyncFile), tmp = p + ".tmp";
                try
                {
                    File.WriteAllBytes(tmp, body);
                    if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
                }
                catch
                {
                    File.WriteAllBytes(p, body); // algumas pastas virtuais não aceitam a troca atômica
                    try { File.Delete(tmp); } catch { }
                }
                Send(s, 200, "text/plain", "ok");
                return;
            }
            if (route == "sync/config" && method == "POST")
            {
                string v = Encoding.UTF8.GetString(body).Trim();
                if (v == "off") SetFolder("");
                else if (v != "" && Directory.Exists(v)) SetFolder(v);
                Send(s, 200, "application/json", Info());
                return;
            }
            if (route == "sync/choose" && method == "POST")
            {
                string chosen = null;
                Thread t = new Thread(delegate()
                {
                    using (Form owner = new Form())
                    using (FolderBrowserDialog d = new FolderBrowserDialog())
                    {
                        owner.TopMost = true; owner.ShowInTaskbar = false; owner.Opacity = 0; owner.StartPosition = FormStartPosition.CenterScreen;
                        owner.Show();
                        d.Description = "Escolha a pasta sincronizada onde o Órbita vai gravar o arquivo " + SyncFile;
                        if (d.ShowDialog(owner) == DialogResult.OK) chosen = d.SelectedPath;
                    }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                if (chosen != null && Directory.Exists(chosen)) SetFolder(chosen);
                Send(s, 200, "application/json", Info());
                return;
            }
            Send(s, 404, "text/plain", "não encontrado");
        }
    }

    static void SetFolder(string f)
    {
        folder = f;
        try { File.WriteAllText(cfgPath, f, Encoding.UTF8); } catch { }
    }

    static string Info()
    {
        if (folder != "" && !Directory.Exists(folder)) folder = "";
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"app\":\"orbita\",\"enabled\":").Append(folder != "" ? "true" : "false");
        sb.Append(",\"folder\":").Append(Json(folder)).Append(",\"drives\":[");
        bool first = true;
        foreach (DriveInfo d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                foreach (string n in new string[] { "Meu Drive", "My Drive" })
                {
                    string p = Path.Combine(d.RootDirectory.FullName, n);
                    if (!Directory.Exists(p)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"path\":").Append(Json(p)).Append(",\"label\":").Append(Json(d.VolumeLabel ?? "")).Append('}');
                    break;
                }
            }
            catch { }
        }
        sb.Append("]}");
        return sb.ToString();
    }

    static string Json(string v)
    {
        StringBuilder sb = new StringBuilder("\"");
        foreach (char c in v)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static int FindHeadEnd(byte[] b, int n)
    {
        for (int i = 0; i + 3 < n; i++)
            if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
        return -1;
    }

    static byte[] Resource(string name)
    {
        using (Stream r = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (r == null) return null;
            byte[] b = new byte[r.Length];
            int off = 0;
            while (off < b.Length) { int n = r.Read(b, off, b.Length - off); if (n <= 0) break; off += n; }
            return b;
        }
    }

    static string Mime(string name)
    {
        if (name.EndsWith(".html")) return "text/html; charset=utf-8";
        if (name.EndsWith(".png")) return "image/png";
        if (name.EndsWith(".webmanifest")) return "application/manifest+json";
        if (name.EndsWith(".js")) return "text/javascript; charset=utf-8";
        return "application/octet-stream";
    }

    static void Send(Stream s, int code, string type, string text) { Send(s, code, type, Encoding.UTF8.GetBytes(text)); }

    static void Send(Stream s, int code, string type, byte[] body)
    {
        if (type.StartsWith("text/plain") || type == "application/json") type += "; charset=utf-8";
        string head = "HTTP/1.1 " + code + " OK\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
            "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        byte[] hb = Encoding.ASCII.GetBytes(head);
        s.Write(hb, 0, hb.Length);
        if (body.Length > 0 && code != 204) s.Write(body, 0, body.Length);
        s.Flush();
    }
}
