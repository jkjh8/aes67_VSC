#Requires -RunAsAdministrator
param(
    [ValidateSet("Add","Remove")]
    [string]$Action = "Add"
)

$rules = @(
    @{ Name="AES67-VCS PTP Event (In)";    Port=319;  Dir="Inbound"  },
    @{ Name="AES67-VCS PTP General (In)";  Port=320;  Dir="Inbound"  },
    @{ Name="AES67-VCS RTP Audio (In)";    Port=5004; Dir="Inbound"  },
    @{ Name="AES67-VCS SAP Announce (In)"; Port=9875; Dir="Inbound"  },
    @{ Name="AES67-VCS RTP Audio (Out)";   Port=4010; Dir="Outbound" },
    @{ Name="AES67-VCS RTP AES67 (Out)";   Port=5004; Dir="Outbound" },
    @{ Name="AES67-VCS SAP Announce (Out)";Port=9875; Dir="Outbound" }
)

foreach ($r in $rules) {
    Remove-NetFirewallRule -DisplayName $r.Name -ErrorAction SilentlyContinue
    if ($Action -eq "Add") {
        New-NetFirewallRule `
            -DisplayName $r.Name `
            -Direction   $r.Dir `
            -Protocol    UDP `
            -LocalPort   $r.Port `
            -Action      Allow `
            -Profile     Any `
            -ErrorAction SilentlyContinue | Out-Null
    }
}
