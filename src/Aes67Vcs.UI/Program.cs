using System.Runtime.Versioning;
using Aes67Vcs.UI;

[assembly: SupportedOSPlatform("windows")]

ApplicationConfiguration.Initialize();

// 중복 실행 방지
bool createdNew;
using var mutex = new System.Threading.Mutex(true, "Aes67VcsMutex", out createdNew);
if (!createdNew)
{
    MessageBox.Show("AES67 VCS가 이미 실행 중입니다.", "AES67 VCS",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

Application.Run(new TrayApp());
