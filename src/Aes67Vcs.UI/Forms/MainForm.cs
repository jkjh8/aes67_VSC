using System.Net;
using System.Runtime.Versioning;
using Aes67Vcs.Core.Models;
using Aes67Vcs.Core.Ptp;
using Aes67Vcs.Core.Sap;
using Aes67Vcs.Core.Scream;

namespace Aes67Vcs.UI.Forms;

[SupportedOSPlatform("windows")]
public class MainForm : Form
{
    // ── 서비스 ───────────────────────────────────────────────
    private Aes67Config  _cfg;
    private PtpClient?   _ptp;
    private SapAnnouncer? _sap;
    private ScreamManager _scream;
    private bool _running;

    // ── 컨트롤 ───────────────────────────────────────────────
    private GroupBox   _grpStream  = null!;
    private GroupBox   _grpPtp     = null!;
    private GroupBox   _grpScream  = null!;
    private GroupBox   _grpStatus  = null!;

    // 스트림 설정
    private ComboBox   _cmbChannels    = null!;
    private TextBox    _txtMulticast   = null!;
    private TextBox    _txtPort        = null!;
    private TextBox    _txtStreamName  = null!;
    private ComboBox   _cmbNic         = null!;

    // PTP
    private CheckBox   _chkPtp         = null!;
    private TextBox    _txtPtpDomain   = null!;
    private Label      _lblPtpState    = null!;
    private Label      _lblPtpOffset   = null!;
    private Label      _lblPtpMaster   = null!;

    // Scream
    private Label      _lblScreamStatus = null!;
    private Button     _btnInstall      = null!;
    private Button     _btnApply        = null!;

    // 시작/중지
    private Button     _btnStart  = null!;
    private Button     _btnStop   = null!;

    // 상태 로그
    private ListBox    _lstLog    = null!;

    // PTP 업데이트 타이머
    private System.Windows.Forms.Timer _uiTimer = null!;
    private PtpStatus _lastPtpStatus = new();

    public MainForm()
    {
        _cfg    = ConfigStore.Load();
        _scream = new ScreamManager(_cfg);

        InitializeComponent();
        LoadConfigToUi();
        CheckScreamStatus();
    }

    // ── UI 초기화 ────────────────────────────────────────────

    private void InitializeComponent()
    {
        Text            = "AES67 VCS";
        Size            = new Size(600, 680);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);

        BuildStreamGroup();
        BuildPtpGroup();
        BuildScreamGroup();
        BuildButtonBar();
        BuildLogBox();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += (_, _) => UpdatePtpUi();
        _uiTimer.Start();
    }

    private void BuildStreamGroup()
    {
        _grpStream = new GroupBox
        {
            Text     = "스트림 설정",
            Location = new Point(10, 10),
            Size     = new Size(570, 160),
        };

        int y = 22;

        // 채널 수
        AddLabel(_grpStream, "채널:", 10, y);
        _cmbChannels = new ComboBox
        {
            Location      = new Point(110, y),
            Size          = new Size(100, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cmbChannels.Items.AddRange(new object[] { "2ch", "8ch" });
        _grpStream.Controls.Add(_cmbChannels);

        // 포맷 표시 (고정)
        AddLabel(_grpStream, "포맷: L24 / 48kHz  (고정)", 230, y, 300);

        y += 32;

        // 멀티캐스트 주소
        AddLabel(_grpStream, "멀티캐스트 IP:", 10, y);
        _txtMulticast = new TextBox { Location = new Point(110, y), Size = new Size(140, 24) };
        _grpStream.Controls.Add(_txtMulticast);

        AddLabel(_grpStream, "포트:", 270, y);
        _txtPort = new TextBox { Location = new Point(320, y), Size = new Size(70, 24) };
        _grpStream.Controls.Add(_txtPort);

        y += 32;

        // 스트림 이름
        AddLabel(_grpStream, "스트림 이름:", 10, y);
        _txtStreamName = new TextBox { Location = new Point(110, y), Size = new Size(200, 24) };
        _grpStream.Controls.Add(_txtStreamName);

        y += 32;

        // NIC 선택
        AddLabel(_grpStream, "네트워크 인터페이스:", 10, y);
        _cmbNic = new ComboBox
        {
            Location      = new Point(150, y),
            Size          = new Size(380, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        PopulateNicList();
        _grpStream.Controls.Add(_cmbNic);

        Controls.Add(_grpStream);
    }

    private void BuildPtpGroup()
    {
        _grpPtp = new GroupBox
        {
            Text     = "PTP 동기화 (IEEE 1588v2)",
            Location = new Point(10, 180),
            Size     = new Size(570, 140),
        };

        int y = 22;

        _chkPtp = new CheckBox
        {
            Text     = "PTP 동기화 활성화",
            Location = new Point(10, y),
            Size     = new Size(200, 24),
        };
        _chkPtp.CheckedChanged += (_, _) => _cfg.PtpEnabled = _chkPtp.Checked;
        _grpPtp.Controls.Add(_chkPtp);

        AddLabel(_grpPtp, "도메인:", 220, y);
        _txtPtpDomain = new TextBox
        {
            Location = new Point(280, y),
            Size     = new Size(50, 24),
            Text     = "0",
        };
        _grpPtp.Controls.Add(_txtPtpDomain);

        y += 36;

        // 상태 표시
        _lblPtpState = new Label
        {
            Location  = new Point(10, y),
            Size      = new Size(540, 22),
            Text      = "상태: 비활성",
            ForeColor = Color.Gray,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _grpPtp.Controls.Add(_lblPtpState);

        y += 24;

        _lblPtpOffset = new Label
        {
            Location = new Point(10, y),
            Size     = new Size(280, 20),
            Text     = "오프셋: -",
            ForeColor = Color.DimGray,
        };
        _grpPtp.Controls.Add(_lblPtpOffset);

        _lblPtpMaster = new Label
        {
            Location = new Point(300, y),
            Size     = new Size(260, 20),
            Text     = "마스터: -",
            ForeColor = Color.DimGray,
        };
        _grpPtp.Controls.Add(_lblPtpMaster);

        Controls.Add(_grpPtp);
    }

    private void BuildScreamGroup()
    {
        _grpScream = new GroupBox
        {
            Text     = "Scream 드라이버",
            Location = new Point(10, 330),
            Size     = new Size(570, 80),
        };

        _lblScreamStatus = new Label
        {
            Location  = new Point(10, 25),
            Size      = new Size(360, 22),
            Text      = "드라이버 상태: 확인 중...",
            ForeColor = Color.DimGray,
        };
        _grpScream.Controls.Add(_lblScreamStatus);

        _btnInstall = new Button
        {
            Text     = "드라이버 설치",
            Location = new Point(380, 22),
            Size     = new Size(90, 28),
        };
        _btnInstall.Click += OnInstallDriver;
        _grpScream.Controls.Add(_btnInstall);

        _btnApply = new Button
        {
            Text     = "설정 적용",
            Location = new Point(475, 22),
            Size     = new Size(80, 28),
        };
        _btnApply.Click += OnApplyScreamConfig;
        _grpScream.Controls.Add(_btnApply);

        Controls.Add(_grpScream);
    }

    private void BuildButtonBar()
    {
        var panel = new Panel
        {
            Location = new Point(10, 420),
            Size     = new Size(570, 44),
        };

        _btnStart = new Button
        {
            Text      = "시작",
            Location  = new Point(0, 6),
            Size      = new Size(100, 32),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _btnStart.Click += OnStart;
        panel.Controls.Add(_btnStart);

        _btnStop = new Button
        {
            Text      = "중지",
            Location  = new Point(110, 6),
            Size      = new Size(100, 32),
            BackColor = Color.FromArgb(200, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled   = false,
        };
        _btnStop.Click += OnStop;
        panel.Controls.Add(_btnStop);

        Controls.Add(panel);
    }

    private void BuildLogBox()
    {
        var lbl = new Label
        {
            Text     = "이벤트 로그:",
            Location = new Point(10, 472),
            Size     = new Size(100, 20),
        };
        Controls.Add(lbl);

        _lstLog = new ListBox
        {
            Location       = new Point(10, 492),
            Size           = new Size(570, 140),
            HorizontalScrollbar = true,
        };
        Controls.Add(_lstLog);
    }

    // ── UI ↔ 설정 연동 ───────────────────────────────────────

    private void LoadConfigToUi()
    {
        _cmbChannels.SelectedIndex = _cfg.Channels == ChannelCount.Eight ? 1 : 0;
        _txtMulticast.Text  = _cfg.MulticastAddress;
        _txtPort.Text       = _cfg.RtpPort.ToString();
        _txtStreamName.Text = _cfg.StreamName;
        _chkPtp.Checked     = _cfg.PtpEnabled;
        _txtPtpDomain.Text  = _cfg.PtpDomain.ToString();

        // NIC 선택
        if (!string.IsNullOrEmpty(_cfg.LocalInterface))
        {
            for (int i = 0; i < _cmbNic.Items.Count; i++)
            {
                if (_cmbNic.Items[i] is NicItem n && n.IpAddress == _cfg.LocalInterface)
                {
                    _cmbNic.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void SaveUiToConfig()
    {
        _cfg.Channels         = _cmbChannels.SelectedIndex == 1 ? ChannelCount.Eight : ChannelCount.Two;
        _cfg.MulticastAddress = _txtMulticast.Text.Trim();
        _cfg.StreamName       = _txtStreamName.Text.Trim();
        _cfg.PtpEnabled       = _chkPtp.Checked;

        if (int.TryParse(_txtPort.Text, out int port))       _cfg.RtpPort   = port;
        if (int.TryParse(_txtPtpDomain.Text, out int domain)) _cfg.PtpDomain = domain;

        if (_cmbNic.SelectedItem is NicItem n)
            _cfg.LocalInterface = n.IpAddress;

        // IP 유효성 검사
        if (!IPAddress.TryParse(_cfg.MulticastAddress, out _))
            throw new FormatException("멀티캐스트 주소가 올바르지 않습니다.");

        ConfigStore.Save(_cfg);
    }

    // ── 이벤트 핸들러 ────────────────────────────────────────

    private void OnStart(object? sender, EventArgs e)
    {
        try
        {
            SaveUiToConfig();
        }
        catch (FormatException ex)
        {
            MessageBox.Show(ex.Message, "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // PTP 시작
        if (_cfg.PtpEnabled)
        {
            _ptp = new PtpClient(_cfg);
            _ptp.StatusChanged += OnPtpStatus;
            _ptp.Start();
            Log("PTP 클라이언트 시작됨");
        }

        // SAP 시작
        _sap = new SapAnnouncer(_cfg);
        _sap.Start();
        Log($"SAP 어나운서 시작됨 → {_cfg.MulticastAddress}:{_cfg.RtpPort}");

        // Scream 설정 적용
        try
        {
            _scream.ApplyConfig();
            Log("Scream 드라이버 설정 적용됨");
        }
        catch (Exception ex)
        {
            Log($"[경고] Scream 설정 실패: {ex.Message}");
        }

        _running = true;
        _btnStart.Enabled = false;
        _btnStop.Enabled  = true;
        SetGroupsEnabled(false);

        Log($"AES67 VCS 시작 — 채널: {(int)_cfg.Channels}ch, 포맷: L24/48kHz");
    }

    private void OnStop(object? sender, EventArgs e)
    {
        _ptp?.Stop();
        _ptp?.Dispose();
        _ptp = null;

        _sap?.Stop();
        _sap?.Dispose();
        _sap = null;

        _running = false;
        _btnStart.Enabled = true;
        _btnStop.Enabled  = false;
        SetGroupsEnabled(true);

        _lblPtpState.Text      = "상태: 비활성";
        _lblPtpState.ForeColor = Color.Gray;
        _lblPtpOffset.Text     = "오프셋: -";
        _lblPtpMaster.Text     = "마스터: -";

        Log("AES67 VCS 중지됨");
    }

    private void OnInstallDriver(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title  = "Scream INF 파일 선택",
            Filter = "INF 파일 (*.inf)|*.inf",
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
        }
        catch (Exception ex)
        {
            Log($"[오류] 드라이버 설치 실패: {ex.Message}");
            MessageBox.Show(ex.Message, "드라이버 설치 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnInstall.Enabled = true;
        }
    }

    private void OnApplyScreamConfig(object? sender, EventArgs e)
    {
        try
        {
            SaveUiToConfig();
            _scream.ApplyConfig();
            Log("Scream 설정 적용됨 (드라이버 재시작 필요할 수 있음)");
        }
        catch (Exception ex)
        {
            Log($"[오류] {ex.Message}");
            MessageBox.Show(ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── PTP 상태 업데이트 ────────────────────────────────────

    private void OnPtpStatus(object? sender, PtpStatus status)
    {
        _lastPtpStatus = status;
    }

    private void UpdatePtpUi()
    {
        if (!_running) return;
        var s = _lastPtpStatus;

        _lblPtpState.Text = $"상태: {s.StateDescription}";
        _lblPtpState.ForeColor = s.State switch
        {
            PtpState.Locked   => Color.Green,
            PtpState.Syncing  => Color.DarkOrange,
            PtpState.Error    => Color.Red,
            _                 => Color.Gray,
        };

        _lblPtpOffset.Text = s.IsLocked
            ? $"오프셋: {s.OffsetNs:+#;-#;0} ns  /  지연: {s.MeanPathDelayNs} ns"
            : "오프셋: -";

        _lblPtpMaster.Text = string.IsNullOrEmpty(s.MasterClockId)
            ? "마스터: 탐색 중..."
            : $"마스터: {FormatClockId(s.MasterClockId)}";
    }

    // ── 유틸리티 ─────────────────────────────────────────────

    private void CheckScreamStatus()
    {
        bool installed = _scream.IsDriverInstalled();
        _lblScreamStatus.Text      = installed ? "드라이버 상태: 설치됨 ✓" : "드라이버 상태: 미설치";
        _lblScreamStatus.ForeColor = installed ? Color.Green : Color.OrangeRed;
        _btnApply.Enabled          = installed;
    }

    private void PopulateNicList()
    {
        _cmbNic.Items.Clear();
        _cmbNic.Items.Add(new NicItem("(자동)", ""));
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    _cmbNic.Items.Add(new NicItem($"{nic.Name}  ({ua.Address})", ua.Address.ToString()));
                }
            }
        }
        _cmbNic.SelectedIndex = 0;
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

    private static string FormatClockId(string id) =>
        id.Length >= 16
            ? $"{id[..2]}-{id[2..4]}-{id[4..6]}-{id[6..8]}-{id[8..10]}-{id[10..12]}-{id[12..14]}-{id[14..16]}"
            : id;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running) OnStop(null, EventArgs.Empty);
        base.OnFormClosing(e);
    }

    private static Label AddLabel(Control parent, string text, int x, int y, int width = 110)
    {
        var lbl = new Label
        {
            Text     = text,
            Location = new Point(x, y + 3),
            Size     = new Size(width, 20),
        };
        parent.Controls.Add(lbl);
        return lbl;
    }
}

/// <summary>NIC 콤보박스 아이템</summary>
file class NicItem
{
    public string Display   { get; }
    public string IpAddress { get; }
    public NicItem(string display, string ip) { Display = display; IpAddress = ip; }
    public override string ToString() => Display;
}
