# Community Knowledge Pack: PowerShell 5.1 & .NET Async Interop - ETS Exception-Swallowing & HttpClient Pooling

> **Battle-Tested AI Knowledge Pack for Google Antigravity CLI (gy) & AI Pair Programmers**
> 
> Curated patterns, bug workarounds, and undocument framework edge cases.
> Free to use, adapt, and share under the MIT License.
>
> 💡 *How to use:* Copy this file into ~/.gemini/antigravity-cli/skills/knowledge-base/custom-knowledge/
> or directly into your project's .gemini/rules/ or docs/knowledge.md to equip your AI assistant with these solutions.

---
# Windows Performance Tuning & Process Priority Automation

- **Windows Energiesparplan auf Höchstleistung setzen**:
  ```powershell
  powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
  powercfg /s SCHEME_MIN
  ```
- **Prozesspriorität für GUI, CLI und Compilier-Prozesse anheben**:
  ```powershell
  Get-Process | Where-Object { $_.ProcessName -in @("Antigravity CLI Dashboard", "agy", "cargo", "rustc", "link") } | ForEach-Object {
      $_.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High
  }
  ```
- **Stumme 1-Klick Desktop-Verknüpfung via WScript.Shell**:
  ```powershell
  $WshShell = New-Object -ComObject WScript.Shell
  $Shortcut = $WshShell.CreateShortcut("$([Environment]::GetFolderPath('Desktop'))\AGY Performance Booster.lnk")
  $Shortcut.TargetPath = "powershell.exe"
  $Shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""C:\Path\To\Script.ps1"""
  $Shortcut.WorkingDirectory = "C:\Path\To"
  $Shortcut.IconLocation = "%SystemRoot%\System32\shell32.dll, 238"
  $Shortcut.Save()
  ```
- **High-DPI & Dark-Mode Nativer WPF-Bestätigungsdialog (`PresentationFramework`)**:
  ```powershell
  Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

  $wpfXml = @"
  <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          Title="AGY Performance Booster" Height="200" Width="400"
          WindowStyle="None" AllowsTransparency="True" Background="Transparent"
          WindowStartupLocation="CenterScreen" Topmost="True" ShowInTaskbar="True">
      <Border Background="#1E293B" BorderBrush="#3B82F6" BorderThickness="2" CornerRadius="14" Padding="20">
          <Grid>
              <Grid.RowDefinitions>
                  <RowDefinition Height="Auto"/>
                  <RowDefinition Height="Auto"/>
                  <RowDefinition Height="Auto"/>
                  <RowDefinition Height="*"/>
              </Grid.RowDefinitions>
              
              <TextBlock Grid.Row="0" Text="🚀 AGY Performance Booster" Foreground="#F8FAFC" FontSize="16" FontWeight="Bold" Margin="0,0,0,12"/>
              <TextBlock Grid.Row="1" Text="⚡ Energiesparplan: Höchstleistung" Foreground="#38BDF8" FontSize="13" Margin="0,0,0,6"/>
              <TextBlock Grid.Row="2" Text="🎯 Priorität: HOCH ($boostedCount Prozesse optimiert)" Foreground="#38BDF8" FontSize="13" Margin="0,0,0,16"/>
              
              <Button Name="OkBtn" Grid.Row="3" Content="OK" Width="110" Height="34" HorizontalAlignment="Right" VerticalAlignment="Bottom"
                      Background="#3B82F6" Foreground="#FFFFFF" FontSize="13" FontWeight="Bold" BorderThickness="0" Cursor="Hand"/>
          </Grid>
      </Border>
  </Window>
"@

  $reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($wpfXml))
  $window = [System.Windows.Markup.XamlReader]::Load($reader)
  $window.add_MouseLeftButtonDown({ $window.DragMove() })
  $okBtn = $window.FindName("OkBtn")
  if ($okBtn) { $okBtn.add_Click({ $window.Close() }) }

- **Windows Non-Admin Git-Installation (MinGit / PortableGit)**:
  ```powershell
  $target = "$env:LOCALAPPDATA\Programs\Git"
  $url = "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.3/MinGit-2.55.0.3-64-bit.zip"
  $zip = "$env:TEMP\MinGit.zip"
  Invoke-WebRequest -Uri $url -OutFile $zip
  Expand-Archive -Path $zip -DestinationPath $target -Force
  Remove-Item $zip -Force
  $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
  if ($userPath -notlike "*Git\cmd*") {
      [Environment]::SetEnvironmentVariable("Path", "$userPath;$target\cmd", "User")
  }
  ```


## Update August 2026
* **Windows Dev Drive (ReFS):** Essenziell für 2026. Quellcode, Caches und Registrys müssen auf ein Dev Drive gelegt werden, um I/O-Latenz und Defender-Scans zu minimieren.
* **HAGS:** Hardware-Accelerated GPU Scheduling sollte in Windows 11 zwingend aktiviert werden.
* **Defender & VBS:** Build-Verzeichnisse als Defender-Ausnahme eintragen. Auf reinen Dev-Maschinen kann das Deaktivieren von VBS / Memory Integrity die Performance massiv steigern.
* **Stiller Windows-Konsolenhost (ConHost Delegation):** Um das kurzzeitige Aufblitzen von Terminalfenstern bei CLI-Tools zu verhindern, in `HKCU\Console\%%Startup` die Werte `DelegationConsole` und `DelegationTerminal` auf `{B23D10C0-E52E-411E-9D5B-C09FDF709C7D}` setzen. Zurücksetzen auf Standard erfolgt mit `{00000000-0000-0000-0000-000000000000}`.

## Update September 2026
* **NTFS Cold-Cache vs. Standby-RAM I/O Skalierung**: Das synchrone oder sequentielle Durchsuchen von Hunderten kleiner Dateien (z. B. 500+ `transcript.jsonl` Dateien in tief verschachtelten Verzeichnissen) führt bei kaltem Windows-Dateisystem-Cache (nach Neustart oder hohem RAM-Druck) zu extremen Latenzen (15–30+ Sekunden), während ein warmer Standby-RAM-Cache dieselbe Abfrage in < 1 Sekunde beantwortet.
  * **Lösung**: Persistenter Metadaten-Delta-Cache (Speichern von `LastWriteTimeUtc.Ticks` und Dateilänge in einer kompakten JSON-Datei), kombiniert mit parallelem Delta-Scanning (`Parallel.ForEachAsync`) und Single-Pass Streaming Parsing. Dadurch sinken Kaltstart-Latenzen deterministisch von >15s auf unter 200 ms.

## Update Oktober 2026
* **PowerShell 5.1 & .NET Async Interop & Subprozess-Vermeidung**:
  * *Subprozess-Overhead*: Das Starten lokaler Helfer via `& powershell.exe -NoProfile -File ...` erzeugt auf Windows 600–800 ms Prozessinitialisierung. Lokale RPC-Endpunkte (z. B. HTTP Port 9223) sollten immer direkt per `HttpClient` angesprochen werden (~50 ms).
  * *Parallele Async-Tasks*: Web- und API-Requests parallel mit `Add-Type -AssemblyName System.Net.Http`, `HttpClient` und `[System.Threading.Tasks.Task]::WaitAll($tasks, timeoutMs)` abwickeln (10x Tempo-Steigerung).
  * *ETS Exception-Swallowing*: In PowerShell 5.1 fängt das Extended Type System Exceptions beim Getter `.Result` ab und liefert `$null`. Stattdessen immer `$task.GetAwaiter().GetResult()` nutzen, um echte Exceptions sauber in `catch`-Blöcken zu fangen.
