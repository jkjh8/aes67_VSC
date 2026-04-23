using System.Runtime.Versioning;
using Aes67Vcs.UI.Forms;

namespace Aes67Vcs.UI;

/// <summary>
/// 트레이 아이콘 컨텍스트.
/// 앱을 닫아도 트레이에 상주, 더블클릭으로 설정 창 열기.
/// </summary>
[SupportedOSPlatform("windows")]
public class TrayApp : ApplicationContext
{
    private NotifyIcon  _tray    = null!;
    private MainForm?   _mainForm;
    private ContextMenuStrip _menu = null!;

    public TrayApp()
    {
        BuildTray();
        ShowMainForm();
    }

    private void BuildTray()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("설정 열기",   null, (_, _) => ShowMainForm());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("종료",        null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Text    = "AES67 VCS",
            Icon    = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainForm();
    }

    private void ShowMainForm()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm();
            _mainForm.FormClosed += (_, _) => _mainForm = null;
        }
        _mainForm.Show();
        _mainForm.BringToFront();
        _mainForm.Activate();
    }

    private void ExitApp()
    {
        _mainForm?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }
}
