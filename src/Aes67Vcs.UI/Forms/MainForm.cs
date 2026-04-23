using System.Net;
using System.Runtime.Versioning;
using Aes67Vcs.Core.Models;
using Aes67Vcs.Core.Ptp;
using Aes67Vcs.Core.Rtp;
using Aes67Vcs.Core.Sap;
using Aes67Vcs.Core.Scream;

namespace Aes67Vcs.UI.Forms;

[SupportedOSPlatform("windows")]
public class MainForm : Form
{
    // ── 서비스 ───────────────────────────────────────────────
    private Aes67Config   _cfg;
    private PtpMonitor?   _ptp;
    private SapAnnouncer? _sap;
    private ScreamManager _scream;
    private bool          _running;

    // C++ 통계 콜백 — GC 방지를 위해 필드로 유지
    private Aes67StatsCallback? _statsCallback;

    // 최근 통계
    private volatile int   _lastPkts;
    private volatile float _lastJitter;
    private volatile int   _lastChannels;

    // ── 컨트롤 ───────────────────────────────────────────────
    private GroupBox _grpStream = null!;
    private GroupBox _grpPtp    = null!;
    private GroupBox _grpScream = null!;
    private GroupBox _grpRtp    = null!;

    // 스트림 설정
    private ComboBox _cmbChannels   = null!;
    private TextBox  _txtMulticast  = null!;
    private TextBox  _txtPort       = null!;
    private TextBox  _txtStreamName = null!;
    private ComboBox _cmbNic        = null!;

    // PTP
    private CheckBox _chkPtp       = null!;
    private TextBox  _txtPtpDomain = null!;
    private Label    _lblPtpState  = null!;
    private Label    _lblPtpOffset = null!;
    private Label    _lblPtpMaster = null!;

    // Scream
    private Label  _lblScreamStatus = null!;
    private Button _btnInstall      = null!;
    private Button _btnApply        = null!;

    // RTP 스트리머 상태
    private Label _lblRtpDevice = null!;
    private Label _lblRtpPkts   = null!;
    private Label _lblRtpJitter = null!;

    // 시작/중지
    private Button  _btnStart = null!;
    private Button  _btnStop  = null!;
    private ListBox _lstLog   = null!;

    // UI 타이머
    private System.Windows.Forms.Timer _uiTimer = null!;
    private PtpStatus _lastPtpStatus = new();

    public MainForm()
    {
        _cfg    = ConfigStore.Load();
        _scream = new ScreamManager(_cfg);
        InitializeComponent();
        LoadConfigToUi();
        CheckScreamStatus();
        RefreshDeviceLabel();
    }

    // ── UI 구성 ──────────────────────────────────────────────

    private void InitializeComponent()
    {
        Text            = "AES67 VCS";
        Size            = new Size(610, 760);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);

        BuildStreamGroup();
        BuildPtpGroup();
        BuildScreamGroup();
        BuildRtpGroup();
        BuildButtonBar();
        BuildLogBox();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += (_, _) => { UpdatePtpUi(); UpdateRtpUi(); };
        _uiTimer.Start();
    }

    private void BuildStreamGroup()
    {
        _grpStream = new GroupBox
        {
            Text = "스트림 설정", Location = new Point(10, 10), Size = new Size(580, 160)
        };
        int y = 22;

        AddLabel(_grpStream, "채널:", 10, y);
        _cmbChannels = new ComboBox
        {
            Location = new Point(90, y), Size = new Size(90, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbChannels.Items.AddRange(new object[] { "2ch", "8ch" });
        _grpStream.Controls.Add(_cmbChannels);

        AddLabel(_grpStream, "포맷: L24 / 48kHz  (고정)", 200, y, 320);
        y += 32;

        AddLabel(_grpStream, "멀티캐스트 IP:", 10, y);
        _txtMulticast = new TextBox { Location = new Point(100, y), Size = new Size(135, 24) };
        _grpStream.Controls.Add(_txtMulticast);

        AddLabel(_grpStream, "포트:", 250, y);
        _txtPort = new TextBox { Location = new Point(285, y), Size = new Size(65, 24) };
        _grpStream.Controls.Add(_txtPort);
        y += 32;

        AddLabel(_grpStream, "스트림 이름:", 10, y);
        _txtStreamName = new TextBox { Location = new Point(100, y), Size = new Size(200, 24) };
        _grpStream.Controls.Add(_txtStreamName);
        y += 32;

        AddLabel(_grpStream, "네트워크 인터페이스:", 10, y);
        _cmbNic = new ComboBox
        {
            Location = new Point(145, y), Size = new Size(390, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _grpStream.Controls.Add(_cmbNic);

        Controls.Add(_grpStream);
    }

    private void BuildPtpGroup()
    {
        _grpPtp = new GroupBox
        {
            Text = "PTP 동기화 (IEEE 1588v2 / W32TM)",
            Location = new Point(10, 180), Size = new Size(580, 140)
        };
        int y = 22;

        _chkPtp = new CheckBox
        {
            Text = "W32TM PTP 모니터링", Location = new Point(10, y), Size = new Size(190, 24)
        };
        _grpPtp.Controls.Add(_chkPtp);

        AddLabel(_grpPtp, "도메인:", 210, y);
        _txtPtpDomain = new TextBox { Location = new Point(265, y), Size = new Size(45, 24) };
        _grpPtp.Controls.Add(_txtPtpDomain);
        y += 34;

        _lblPtpState = new Label
        {
            Location = new Point(10, y), Size = new Size(555, 22),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Text = "상태: 비활성", ForeColor = Color.Gray
        };
        _grpPtp.Controls.Add(_lblPtpState);
        y += 24;

        _lblPtpOffset = new Label
        {
            Location = new Point(10, y), Size = new Size(270, 20),
            Text = "오프셋: -", ForeColor = Color.DimGray
        };
        _grpPtp.Controls.Add(_lblPtpOffset);

        _lblPtpMaster = new Label
        {
            Location = new Point(290, y), Size = new Size(270, 20),
            Text = "마스터: -", ForeColor = Color.DimGray
        };
        _grpPtp.Controls.Add(_lblPtpMaster);

        Controls.Add(_grpPtp);
    }

    private void BuildScreamGroup()
    {
        _grpScream = new GroupBox
        {
            Text = "Scream 드라이버",
            Location = new Point(10, 330), Size = new Size(580, 80)
        };
        _lblScreamStatus = new Label
        {
            Location = new Point(10, 25), Size = new Size(370, 22), ForeColor = Color.DimGray
        };
        _grpScream.Controls.Add(_lblScreamStatus);

        _btnInstall = new Button
        {
            Text = "드라이버 설치", Location = new Point(385, 22), Size = new Size(90, 28)
        };
        _btnInstall.Click += OnInstallDriver;
        _grpScream.Controls.Add(_btnInstall);

        _btnApply = new Button
        {
            Text = "설정 적용", Location = new Point(480, 22), Size = new Size(80, 28)
        };
        _btnApply.Click += OnApplyScreamConfig;
        _grpScream.Controls.Add(_btnApply);

        Controls.Add(_grpScream);
    }

    private void BuildRtpGroup()
    {
        _grpRtp = new GroupBox
        {
            Text = "AES67 RTP 엔진 (C++)",
            Location = new Point(10, 420), Size = new Size(580, 90)
        };

        _lblRtpDevice = new Label
        {
            Location = new Point(10, 22), Size = new Size(555, 20),
            Text = "소스 디바이스: -", ForeColor = Color.DimGray
        };
        _grpRtp.Controls.Add(_lblRtpDevice);

        _lblRtpPkts = new Label
        {
            Location = new Point(10, 44), Size = new Size(270, 20),
            Text = "패킷: -", ForeColor = Color.DimGray
        };
        _grpRtp.Controls.Add(_lblRtpPkts);

        _lblRtpJitter = new Label
        {
            Location = new Point(290, 44), Size = new Size(270, 20),
            Text = "지터: -", ForeColor = Color.DimGray
        };
        _grpRtp.Controls.Add(_lblRtpJitter);

        Controls.Add(_grpRtp);
    }

    private void BuildButtonBar()
    {
        var panel = new Panel { Location = new Point(10, 520), Size = new Size(580, 44) };

        _btnStart = new Button
        {
            Text = "시작", Location = new Point(0, 6), Size = new Size(100, 32),
            BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnStart.Click += OnStart;
        panel.Controls.Add(_btnStart);

        _btnStop = new Button
        {
            Text = "중지", Location = new Point(110, 6), Size = new Size(100, 32),
            BackColor = Color.FromArgb(200, 60, 60), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Enabled = false
        };
        _btnStop.Click += OnStop;
        panel.Controls.Add(_btnStop);

        Controls.Add(panel);
    }

    private void BuildLogBox()
    {
        Controls.Add(new Label
        {
            Text = "이벤트 로그:", Location = new Point(10, 572), Size = new Size(100, 20)
        });
        _lstLog = new ListBox
        {
            Location = new Point(10, 592), Size = new Size(580, 120),
            HorizontalScrollbar = true
        };
        Controls.Add(_lstLog);
    }

    // ── 설정 ↔ UI ────────────────────────────────────────────

    private void LoadConfigToUi()
    {
        _cmbChannels.SelectedIndex = _cfg.Channels == ChannelCount.Eight ? 1 : 0;
        _txtMulticast.Text         = _cfg.MulticastAddress;
        _txtPort.Text              = _cfg.RtpPort.ToString();
        _txtStreamName.Text        = _cfg.StreamName;
        _chkPtp.Checked            = _cfg.PtpEnabled;
        _txtPtpDomain.Text         = _cfg.PtpDomain.ToString();
    }

    private void SaveUiToConfig()
    {
        _cfg.Channels         = _cmbChannels.SelectedIndex == 1 ? ChannelCount.Eight : ChannelCount.Two;
        _cfg.MulticastAddress = _txtMulticast.Text.Trim();
        _cfg.StreamName       = _txtStreamName.Text.Trim();
        _cfg.PtpEnabled       = _chkPtp.Checked;

        if (int.TryParse(_txtPort.Text, out int port))        _cfg.RtpPort   = port;
        if (int.TryParse(_txtPtpDomain.Text, out int domain)) _cfg.PtpDomain = domain;

        if (_cmbNic.SelectedItem is NicItem n)
            _cfg.LocalInterface = n.IpAddress;

        if (!IPAddress.TryParse(_cfg.MulticastAddress, out _))
            throw new FormatException("멀티캐스트 주소가 올바르지 않습니다.");

        ConfigStore.Save(_cfg);
    }

    // ── 이벤트 핸들러 ────────────────────────────────────────

    private void OnStart(object? sender, EventArgs e)
    {
        try { SaveUiToConfig(); }
        catch (FormatException ex)
        {
            MessageBox.Show(ex.Message, "입력 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 1. C++ 엔진 초기화
        string srcIp = _cfg.LocalInterface ?? "";
        if (!Aes67EngineInterop.Aes67Engine_Init(
                _cfg.MulticastAddress, _cfg.RtpPort,
                (int)_cfg.Channels, srcIp))
        {
            Log("[오류] 엔진 초기화 실패");
            return;
        }

        // 2. 통계 콜백 등록 (필드로 보관 → GC 방지)
        _statsCallback = (pkts, jitter, dropped, ch) =>
        {
            _lastPkts     = pkts;
            _lastJitter   = jitter;
            _lastChannels = ch;
        };
        Aes67EngineInterop.Aes67Engine_SetStatsCallback(_statsCallback);

        // 3. 엔진 시작
        if (!Aes67EngineInterop.Aes67Engine_Start())
        {
            Log("[오류] 엔진 시작 실패 — Scream 디바이스를 찾을 수 없거나 소켓 오류");
            return;
        }
        Log($"AES67 엔진 시작 (C++) — {(int)_cfg.Channels}ch / L24 / 48kHz / 4ms ptime");
        _lblRtpDevice.Text      = "소스 디바이스: Scream (WDM) ← WASAPI Loopback";
        _lblRtpDevice.ForeColor = Color.DarkGreen;

        // 4. PTP 모니터 (W32TM)
        if (_cfg.PtpEnabled)
        {
            _ptp = new PtpMonitor();
            _ptp.StatusChanged += OnPtpStatus;
            _ptp.Start();
            Log("PTP 모니터 시작 (W32TM)");
        }

        // 5. SAP
        _sap = new SapAnnouncer(_cfg);
        _sap.Start();
        Log($"SAP 어나운서 시작 → {_cfg.MulticastAddress}:{_cfg.RtpPort}");

        // 6. Scream 레지스트리 설정
        try   { _scream.ApplyConfig(); Log("Scream 레지스트리 설정 적용"); }
        catch (Exception ex) { Log($"[경고] Scream: {ex.Message}"); }

        _running = true;
        _btnStart.Enabled = false;
        _btnStop.Enabled  = true;
        SetGroupsEnabled(false);
    }

    private void OnStop(object? sender, EventArgs e)
    {
        // C++ 엔진 중지
        Aes67EngineInterop.Aes67Engine_SetStatsCallback(null);
        Aes67EngineInterop.Aes67Engine_Stop();
        _statsCallback = null;

        _ptp?.Stop(); _ptp?.Dispose(); _ptp = null;
        _sap?.Stop(); _sap?.Dispose(); _sap = null;

        _running = false;
        _btnStart.Enabled = true;
        _btnStop.Enabled  = false;
        SetGroupsEnabled(true);

        _lblPtpState.Text       = "상태: 비활성";
        _lblPtpState.ForeColor  = Color.Gray;
        _lblPtpOffset.Text      = "오프셋: -";
        _lblPtpMaster.Text      = "마스터: -";
        _lblRtpDevice.Text      = "소스 디바이스: -";
        _lblRtpDevice.ForeColor = Color.DimGray;
        _lblRtpPkts.Text        = "패킷: -";
        _lblRtpJitter.Text      = "지터: -";

        Log("중지됨");
    }

    private void OnInstallDriver(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Scream INF 파일 선택", Filter = "INF 파일 (*.inf)|*.inf"
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        _ = InstallDriverAsync(ofd.FileName);
    }

    private async Task InstallDriverAsync(string infPath)
    {
        try
        {
            _btnInstall.Enabled = false;
            Log("Scream 드라이버 설치 중...");
            await _scream.InstallDriverAsync(infPath);
            Log("드라이버 설치 완료");
            CheckScreamStatus();
            RefreshDeviceLabel();
        }
        catch (Exception ex)
        {
            Log($"[오류] 드라이버 설치 실패: {ex.Message}");
        }
        finally { _btnInstall.Enabled = true; }
    }

    private void OnApplyScreamConfig(object? sender, EventArgs e)
    {
        try
        {
            SaveUiToConfig();
            _scream.ApplyConfig();
            Log("Scream 설정 적용됨");
        }
        catch (Exception ex)
        {
            Log($"[오류] {ex.Message}");
        }
    }

    // ── 상태 업데이트 ────────────────────────────────────────

    private void OnPtpStatus(PtpStatus s)
    {
        _lastPtpStatus = s;
        // PTP 오프셋을 C++ 엔진에 전달
        if (s.State == PtpState.Locked || s.State == PtpState.Syncing)
        {
            try { Aes67EngineInterop.Aes67Engine_SetPtpOffsetNs(s.OffsetNs); }
            catch { /* DLL 미로드 시 무시 */ }
        }
    }

    private void UpdatePtpUi()
    {
        if (!_running) return;
        var s = _lastPtpStatus;
        _lblPtpState.Text = $"상태: {s.StateDescription}";
        _lblPtpState.ForeColor = s.State switch
        {
            PtpState.Locked  => Color.Green,
            PtpState.Syncing => Color.DarkOrange,
            PtpState.Error   => Color.Red,
            _                => Color.Gray
        };
        _lblPtpOffset.Text = s.IsLocked
            ? $"오프셋: {s.OffsetNs:+#;-#;0} ns"
            : "오프셋: -";
        _lblPtpMaster.Text = string.IsNullOrEmpty(s.MasterClockId)
            ? "마스터: 탐색 중..." : $"마스터: {s.MasterClockId}";
    }

    private void UpdateRtpUi()
    {
        if (!_running) return;
        int   pkts    = _lastPkts;
        float jitter  = _lastJitter;
        int   ch      = _lastChannels;

        _lblRtpPkts.Text   = pkts > 0
            ? $"패킷: {pkts} pkt/s  ({ch}ch)"
            : "패킷: 대기 중...";
        _lblRtpJitter.Text = pkts > 0
            ? $"지터: {jitter:F1} μs"
            : "지터: -";
    }

    // ── 유틸리티 ─────────────────────────────────────────────

    private void CheckScreamStatus()
    {
        bool ok = _scream.IsDriverInstalled();
        _lblScreamStatus.Text      = ok ? "드라이버: 설치됨 ✓" : "드라이버: 미설치";
        _lblScreamStatus.ForeColor = ok ? Color.Green : Color.OrangeRed;
        _btnApply.Enabled          = ok;
    }

    private void RefreshDeviceLabel()
    {
        try
        {
            string[] devices = Aes67EngineInterop.GetDeviceNames();
            bool hasScream = devices.Any(d =>
                d.Contains("Scream", StringComparison.OrdinalIgnoreCase));
            _lblRtpDevice.Text      = hasScream
                ? "소스 디바이스: Scream 감지됨 ✓"
                : "소스 디바이스: Scream 없음 (드라이버 설치 필요)";
            _lblRtpDevice.ForeColor = hasScream ? Color.DarkGreen : Color.OrangeRed;
        }
        catch (DllNotFoundException)
        {
            _lblRtpDevice.Text      = "소스 디바이스: Aes67Engine.dll 없음";
            _lblRtpDevice.ForeColor = Color.Red;
        }
        catch { }
    }

    private void PopulateNicList()
    {
        _cmbNic.Items.Clear();
        _cmbNic.Items.Add(new NicItem("(자동)", ""));
        foreach (var nic in System.Net.NetworkInformation
            .NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus !=
                System.Net.NetworkInformation.OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType ==
                System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetwork)
                    _cmbNic.Items.Add(new NicItem(
                        $"{nic.Name}  ({ua.Address})", ua.Address.ToString()));
            }
        }
        _cmbNic.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(_cfg.LocalInterface))
        {
            for (int i = 0; i < _cmbNic.Items.Count; i++)
            {
                if (_cmbNic.Items[i] is NicItem n &&
                    n.IpAddress == _cfg.LocalInterface)
                {
                    _cmbNic.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void SetGroupsEnabled(bool enabled)
    {
        _grpStream.Enabled = enabled;
        _grpPtp.Enabled    = enabled;
    }

    private void Log(string msg)
    {
        if (InvokeRequired) { Invoke(() => Log(msg)); return; }
        _lstLog.Items.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
        _lstLog.TopIndex = _lstLog.Items.Count - 1;
    }

    private static Label AddLabel(Control parent, string text,
        int x, int y, int width = 90)
    {
        var lbl = new Label
        {
            Text = text, Location = new Point(x, y + 3),
            Size = new Size(width, 20)
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PopulateNicList();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running) OnStop(null, EventArgs.Empty);
        _uiTimer.Stop();
        base.OnFormClosing(e);
    }
}

file class NicItem
{
    public string Display   { get; }
    public string IpAddress { get; }
    public NicItem(string d, string ip) { Display = d; IpAddress = ip; }
    public override string ToString() => Display;
}
