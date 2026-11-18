using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocketIOClient;

namespace Connect
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private const string SIGNAL_SERVER = "https://webrtc-69xt.onrender.com";

        private WebView2 webView = null!;
        private Label labelSession = null!;
        private Label labelStatus = null!;
        private Button btnConnect = null!;
        private Button btnDisconnect = null!;

        private SocketIOClient.SocketIO socket = null!;
        private string sessionId = string.Empty;
        private CancellationTokenSource? captureCts;

        public MainForm()
        {
            InitUI();
        }

        private void InitUI()
        {
            Text = "Connect - Screen Streamer";
            Size = new Size(480, 340);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(15, 15, 15);
            ForeColor = Color.FromArgb(241, 241, 241);

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32) };
            panel.BackColor = Color.FromArgb(15, 15, 15);

            var title = new Label
            {
                Text = "Connect",
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.FromArgb(99, 102, 241),
                AutoSize = true,
                Location = new Point(32, 32)
            };

            var subTitle = new Label
            {
                Text = "Трансляция экрана через WebRTC",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(136, 136, 136),
                AutoSize = true,
                Location = new Point(32, 72)
            };

            labelSession = new Label
            {
                Text = "Код сессии: —",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(241, 241, 241),
                AutoSize = true,
                Location = new Point(32, 120)
            };

            labelStatus = new Label
            {
                Text = "Статус: не подключено",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(136, 136, 136),
                AutoSize = true,
                Location = new Point(32, 155)
            };

            btnConnect = new Button
            {
                Text = "Начать трансляцию",
                Location = new Point(32, 200),
                Size = new Size(190, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(99, 102, 241),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += async (s, e) => await StartSession();

            btnDisconnect = new Button
            {
                Text = "Остановить",
                Location = new Point(238, 200),
                Size = new Size(140, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 42, 42),
                ForeColor = Color.FromArgb(241, 241, 241),
                Font = new Font("Segoe UI", 10),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnDisconnect.FlatAppearance.BorderSize = 0;
            btnDisconnect.Click += async (s, e) => await StopSession();

            var hintLabel = new Label
            {
                Text = "Открой сайт и введи код сессии на другом устройстве",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(80, 80, 80),
                AutoSize = true,
                Location = new Point(32, 258)
            };

            panel.Controls.AddRange(new Control[]
            {
                title, subTitle, labelSession, labelStatus,
                btnConnect, btnDisconnect, hintLabel
            });
            Controls.Add(panel);
        }

        private async Task StartSession()
        {
            SetStatus("Создание сессии...", Color.FromArgb(250, 204, 21));
            btnConnect.Enabled = false;

            try
            {
                // Create session on server
                using var http = new System.Net.Http.HttpClient();
                var resp = await http.PostAsync($"{SIGNAL_SERVER}/session/create", null);
                var json = await resp.Content.ReadAsStringAsync();
                dynamic? data = JsonConvert.DeserializeObject(json);
                sessionId = data?.sessionId ?? string.Empty;

                labelSession.Invoke(() =>
                    labelSession.Text = $"Код сессии:  {sessionId}");

                await ConnectSocket();
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка: {ex.Message}", Color.FromArgb(248, 113, 113));
                btnConnect.Enabled = true;
            }
        }

        private async Task ConnectSocket()
        {
            socket = new SocketIOClient.SocketIO(SIGNAL_SERVER, new SocketIOOptions
            {
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                ReconnectionAttempts = 5
            });

            socket.On("host-joined", _ =>
            {
                SetStatus("Ожидание зрителей...", Color.FromArgb(74, 222, 128));
                Invoke(() =>
                {
                    btnDisconnect.Enabled = true;
                    btnConnect.Enabled = false;
                });
            });

            socket.On("viewer-connected", async data =>
            {
                var viewerId = data.GetValue<dynamic>()?.viewerId?.ToString() ?? string.Empty;
                SetStatus("Зритель подключился, устанавливаем WebRTC...", Color.FromArgb(250, 204, 21));
                await StartWebRTC(viewerId);
            });

            socket.On("viewer-disconnected", _ =>
            {
                SetStatus("Зритель отключился. Ожидание...", Color.FromArgb(136, 136, 136));
            });

            socket.On("webrtc-answer", async data =>
            {
                var raw = data.GetValue<dynamic>();
                string sdp = raw?.answer?.sdp?.ToString() ?? string.Empty;
                string type = raw?.answer?.type?.ToString() ?? "answer";
                await WebRTCHandler.Instance.SetRemoteAnswer(sdp, type);
            });

            socket.On("webrtc-ice-candidate", async data =>
            {
                var raw = data.GetValue<dynamic>();
                string candidate = raw?.candidate?.candidate?.ToString() ?? string.Empty;
                string sdpMid = raw?.candidate?.sdpMid?.ToString() ?? "0";
                int sdpMLineIndex = (int)(raw?.candidate?.sdpMLineIndex ?? 0);
                await WebRTCHandler.Instance.AddIceCandidate(candidate, sdpMid, sdpMLineIndex);
            });

            await socket.ConnectAsync();
            await socket.EmitAsync("host-join", new { sessionId });
        }

        private async Task StartWebRTC(string viewerId)
        {
            await WebRTCHandler.Instance.Initialize(
                onIceCandidate: async (candidate, sdpMid, sdpMLineIndex) =>
                {
                    await socket.EmitAsync("webrtc-ice-candidate", new
                    {
                        targetId = viewerId,
                        candidate = new { candidate, sdpMid, sdpMLineIndex }
                    });
                },
                onOfferCreated: async (sdp, type) =>
                {
                    await socket.EmitAsync("webrtc-offer", new
                    {
                        targetId = viewerId,
                        offer = new { sdp, type }
                    });
                }
            );

            captureCts = new CancellationTokenSource();
            _ = Task.Run(() => CaptureLoop(captureCts.Token));
        }

        private async Task CaptureLoop(CancellationToken token)
        {
            // Capture screen at ~20fps and push frames into WebRTC video track
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var frame = ScreenCapture.CaptureScreen();
                    WebRTCHandler.Instance.PushFrame(frame);
                    frame.Dispose();
                    await Task.Delay(50, token); // 20fps
                }
                catch (OperationCanceledException) { break; }
                catch { /* continue */ }
            }
        }

        private async Task StopSession()
        {
            captureCts?.Cancel();
            WebRTCHandler.Instance.Dispose();
            if (socket != null) await socket.DisconnectAsync();

            SetStatus("Остановлено", Color.FromArgb(136, 136, 136));
            Invoke(() =>
            {
                labelSession.Text = "Код сессии: —";
                btnConnect.Enabled = true;
                btnDisconnect.Enabled = false;
            });
        }

        private void SetStatus(string text, Color color)
        {
            Invoke(() =>
            {
                labelStatus.Text = $"Статус: {text}";
                labelStatus.ForeColor = color;
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            captureCts?.Cancel();
            WebRTCHandler.Instance.Dispose();
            base.OnFormClosing(e);
        }
    }

    // -------------------------------------------------------------------------
    // Screen capture via GDI+
    // -------------------------------------------------------------------------
    public static class ScreenCapture
    {
        public static Bitmap CaptureScreen()
        {
            var bounds = SystemInformation.VirtualScreen;
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return bmp;
        }
    }

    // -------------------------------------------------------------------------
    // WebRTC handler — wraps WebView2 to run WebRTC inside a hidden browser
    // which gives us full native WebRTC with H.264/VP8 encoding for free.
    // -------------------------------------------------------------------------
    public class WebRTCHandler : IDisposable
    {
        public static readonly WebRTCHandler Instance = new WebRTCHandler();

        private Form? _hiddenForm;
        private WebView2? _webView;
        private bool _ready = false;

        private Func<string, string, int, Task>? _onIceCandidate;
        private Func<string, string, Task>? _onOfferCreated;

        public async Task Initialize(
            Func<string, string, int, Task> onIceCandidate,
            Func<string, string, Task> onOfferCreated)
        {
            _onIceCandidate = onIceCandidate;
            _onOfferCreated = onOfferCreated;

            var tcs = new TaskCompletionSource<bool>();

            var thread = new Thread(() =>
            {
                _hiddenForm = new Form
                {
                    Width = 1,
                    Height = 1,
                    ShowInTaskbar = false,
                    WindowState = FormWindowState.Minimized,
                    FormBorderStyle = FormBorderStyle.None
                };

                _webView = new WebView2 { Dock = DockStyle.Fill };
                _hiddenForm.Controls.Add(_webView);

                _hiddenForm.Load += async (s, e) =>
                {
                    var env = await CoreWebView2Environment.CreateAsync();
                    await _webView.EnsureCoreWebView2Async(env);
                    _webView.CoreWebView2.WebMessageReceived += OnWebMessage;
                    _webView.CoreWebView2.NavigateToString(GetWebRTCPage());
                    tcs.SetResult(true);
                };

                Application.Run(_hiddenForm);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            await tcs.Task;
            await Task.Delay(1000); // wait for page load
            _ready = true;
        }

        public void PushFrame(Bitmap bmp)
        {
            if (!_ready || _webView == null) return;

            // Convert bitmap to base64 JPEG and send to WebRTC page via postMessage
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            var b64 = Convert.ToBase64String(ms.ToArray());

            _webView?.Invoke(() =>
            {
                _webView.CoreWebView2?.PostWebMessageAsString(
                    JsonConvert.SerializeObject(new { type = "frame", data = b64 }));
            });
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                dynamic? msg = JsonConvert.DeserializeObject(e.TryGetWebMessageAsString());
                if (msg == null) return;
                string type = msg.type?.ToString() ?? string.Empty;

                switch (type)
                {
                    case "offer":
                        _onOfferCreated?.Invoke(
                            msg.sdp?.ToString() ?? string.Empty,
                            msg.offerType?.ToString() ?? "offer");
                        break;
                    case "ice":
                        _onIceCandidate?.Invoke(
                            msg.candidate?.ToString() ?? string.Empty,
                            msg.sdpMid?.ToString() ?? "0",
                            (int)(msg.sdpMLineIndex ?? 0));
                        break;
                }
            }
            catch { }
        }

        public async Task SetRemoteAnswer(string sdp, string type)
        {
            if (_webView == null) return;
            var json = JsonConvert.SerializeObject(new { type = "answer", sdp, answerType = type });
            _webView.Invoke(() =>
                _webView.CoreWebView2?.PostWebMessageAsString(json));
        }

        public async Task AddIceCandidate(string candidate, string sdpMid, int sdpMLineIndex)
        {
            if (_webView == null) return;
            var json = JsonConvert.SerializeObject(new
            {
                type = "ice-remote",
                candidate,
                sdpMid,
                sdpMLineIndex
            });
            _webView.Invoke(() =>
                _webView.CoreWebView2?.PostWebMessageAsString(json));
        }

        // Minimal HTML page that runs inside the hidden WebView2.
        // It acts as the WebRTC peer: receives JPEG frames via postMessage,
        // draws them on a canvas, then streams that canvas via captureStream().
        private static string GetWebRTCPage() => @"<!DOCTYPE html>
<html><body style='margin:0;background:#000'>
<canvas id='c'></canvas>
<script>
const canvas = document.getElementById('c');
const ctx = canvas.getContext('2d');
let pc = null;

const stream = canvas.captureStream(20);

window.chrome.webview.addEventListener('message', async (e) => {
  const msg = JSON.parse(e.data);

  if (msg.type === 'frame') {
    const img = new Image();
    img.onload = () => {
      canvas.width = img.width;
      canvas.height = img.height;
      ctx.drawImage(img, 0, 0);
    };
    img.src = 'data:image/jpeg;base64,' + msg.data;
    return;
  }

  if (msg.type === 'answer') {
    if (pc) {
      await pc.setRemoteDescription({ type: msg.answerType, sdp: msg.sdp });
    }
    return;
  }

  if (msg.type === 'ice-remote') {
    if (pc && msg.candidate) {
      await pc.addIceCandidate({
        candidate: msg.candidate,
        sdpMid: msg.sdpMid,
        sdpMLineIndex: msg.sdpMLineIndex
      });
    }
    return;
  }

  if (msg.type === 'create-offer') {
    pc = new RTCPeerConnection({
      iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' }
      ]
    });

    stream.getTracks().forEach(t => pc.addTrack(t, stream));

    pc.onicecandidate = (e) => {
      if (e.candidate) {
        window.chrome.webview.postMessage(JSON.stringify({
          type: 'ice',
          candidate: e.candidate.candidate,
          sdpMid: e.candidate.sdpMid,
          sdpMLineIndex: e.candidate.sdpMLineIndex
        }));
      }
    };

    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    window.chrome.webview.postMessage(JSON.stringify({
      type: 'offer',
      sdp: offer.sdp,
      offerType: offer.type
    }));
  }
});

// Signal ready
window.chrome.webview.postMessage(JSON.stringify({ type: 'ready' }));
</script>
</body></html>";

        public void Dispose()
        {
            _ready = false;
            _hiddenForm?.Invoke(() =>
            {
                _webView?.Dispose();
                _hiddenForm?.Close();
            });
        }
    }
}
