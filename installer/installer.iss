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
; 版本资源写在 exe 文件属性里，无法随安装语言切换，所有用户看到的是同一串文字
VersionInfoCompany=个人开发者
VersionInfoDescription=DIEditor Setup
VersionInfoProductName=DIEditor
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 有图标就放开下面这行，路径相对于本 iss 文件
; SetupIconFile=..\DIEditor\Assets\app.ico

[Languages]
; 一次性生成全部官方语言（含英文）的完整 [Languages] 段，不要另写段头。
; 展开结果见 IDE 的 Preprocessor Output 标签页；语言内部名由 .isl 文件名小写生成，
; 例如 ChineseSimplified.isl -> chinesesimplified，German.isl -> german。
#call EmitLanguagesSection

[CustomMessages]
; 不带语言前缀 = 所有语言的默认值（英文）；再用“语言名.”前缀覆盖个别语言。
; 注意：前缀必须与 [Languages] 中的 Name 完全一致，写错会导致编译直接报错。
AdditionalTasks=Additional tasks:
chinesesimplified.AdditionalTasks=附加任务：
chinesetraditional.AdditionalTasks=附加工作：
japanese.AdditionalTasks=追加タスク：
korean.AdditionalTasks=추가 작업:
german.AdditionalTasks=Zusätzliche Aufgaben:
french.AdditionalTasks=Tâches supplémentaires :
spanish.AdditionalTasks=Tareas adicionales:
russian.AdditionalTasks=Дополнительные задачи:
portuguese.AdditionalTasks=Tarefas adicionais:
brazilianportuguese.AdditionalTasks=Tarefas adicionais:
italian.AdditionalTasks=Attività aggiuntive:
dutch.AdditionalTasks=Extra taken:
polish.AdditionalTasks=Dodatkowe zadania:
turkish.AdditionalTasks=Ek görevler:
ukrainian.AdditionalTasks=Додаткові завдання:

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
; CreateDesktopIcon 是 Inno Setup 内置消息，官方 .isl 已为每种语言翻译好，不必自己写
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Icons]
Name: "{autoprograms}\DIEditor"; Filename: "{app}\DIEditor.exe"
Name: "{autodesktop}\DIEditor"; Filename: "{app}\DIEditor.exe"; Tasks: desktopicon

[Run]
; LaunchProgram 同样是内置消息，值为 "Launch %1" 及其各语言译文
Filename: "{app}\DIEditor.exe"; Description: "{cm:LaunchProgram,DIEditor}"; Flags: nowait postinstall skipifsilent
