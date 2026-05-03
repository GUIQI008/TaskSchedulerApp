[Setup]
; 绝对不能变！这是软件覆盖安装的唯一标识
AppId={{2c05a59e-111c-49cd-86c9-11c978e04a34}}
AppName=TaskSchedulerApp
; 这里使用了 {#AppVersion}，它将由 GitHub Actions 在打包时自动注入当前标签版本号
AppVersion={#AppVersion}
AppPublisher=YourName
DefaultDirName={autopf}\TaskSchedulerApp
DefaultGroupName=TaskSchedulerApp
OutputDir=Output
OutputBaseFilename=TaskSchedulerApp_Installer
Compression=lzma
SolidCompression=yes
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 这里的源路径设为 publish\*，是因为我们等下会让 GitHub Actions 把编译结果放在这个名字的文件夹里
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TaskSchedulerApp"; Filename: "{app}\TaskSchedulerApp.exe"
Name: "{autodesktop}\TaskSchedulerApp"; Filename: "{app}\TaskSchedulerApp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TaskSchedulerApp.exe"; Description: "启动 TaskSchedulerApp"; Flags: nowait postinstall skipifsilent