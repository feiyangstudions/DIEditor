; DIEditor 安装包脚本
; 用法：ISCC.exe /DAppVersion=1.0.0 /DSourceDir=<publish目录> /DOutputDir=<输出目录> /DAppArch=x64 /DInnoArch=x64compatible installer.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef InnoArch
  #define InnoArch "x64compatible"
#endif
#ifndef AppVersion4
  #define AppVersion4 "1.0.0.0"
#endif

[Setup]
; 产品唯一标识，升级/卸载靠它识别。已发过版就不要随意改。
AppId={{7B2E4C9A-3F51-4D68-8A0E-1C5D93B7F2A6}
AppName=DIEditor
AppVersion={#AppVersion}
AppVerName=DIEditor {#AppVersion}
AppPublisher=YourName
AppPublisherURL=https://github.com/yourname/DIEditor
AppSupportURL=https://github.com/yourname/DIEditor/issues
DefaultDirName={autopf}\DIEditor
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DIEditor-{#AppVersion}-{#AppArch}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.17763
; 架构限定：x86 用 x86compatible，x64 用 x64compatible（含 ARM64 模拟），arm64 用 arm64
ArchitecturesAllowed={#InnoArch}
#if InnoArch != "x86compatible"
ArchitecturesInstallIn64BitMode={#InnoArch}
#endif
UninstallDisplayIcon={app}\DIEditor.exe
; 四段纯数字，不能用 1.2.0-beta 这类带后缀的版本
VersionInfoVersion={#AppVersion4}
VersionInfoCompany=个人开发者
VersionInfoDescription=DIEditor Setup
VersionInfoProductName=DIEditor
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 有图标就放开下面这行，路径相对于本 iss 文件
; SetupIconFile=..\DIEditor\Assets\app.ico

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Icons]
Name: "{autoprograms}\DIEditor"; Filename: "{app}\DIEditor.exe"
Name: "{autodesktop}\DIEditor"; Filename: "{app}\DIEditor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DIEditor.exe"; Description: "启动 DIEditor"; Flags: nowait postinstall skipifsilent
