; Simplified Chinese messages for the FireflyVPN installer.
; Encoding: UTF-8
; This compact local language definition covers every page and dialog exposed
; by this installer. It intentionally ships with the project so build machines
; do not need an extra Inno Setup language-pack installation.

[LangOptions]
LanguageName=简体中文
LanguageID=$0804
LanguageCodePage=936

[Messages]
SetupAppTitle=安装
SetupWindowTitle=安装 - %1
UninstallAppTitle=卸载
UninstallAppFullTitle=%1 卸载
InformationTitle=信息
ConfirmTitle=确认
ErrorTitle=错误

SetupLdrStartupMessage=现在将安装 %1。您想要继续吗？
LdrCannotCreateTemp=无法创建临时文件。安装程序已中止
LdrCannotExecTemp=无法执行临时目录中的文件。安装程序已中止
LastErrorMessage=%1。%n%n错误 %2: %3
SetupFileMissing=安装目录中缺少文件 %1。请获取程序的新副本。
SetupFileCorrupt=安装文件已损坏。请获取程序的新副本。
SetupAlreadyRunning=安装程序已在运行。
WindowsVersionNotSupported=此程序不支持当前计算机运行的 Windows 版本。
OnlyOnTheseArchitectures=此程序只能安装到下列处理器架构的 Windows 版本中：%n%n%1
AdminPrivilegesRequired=安装此程序需要管理员权限。
SetupAppRunningError=安装程序检测到 %1 正在运行。%n%n请先关闭该程序，然后点击“确定”继续，或点击“取消”退出。
UninstallAppRunningError=卸载程序检测到 %1 正在运行。%n%n请先关闭该程序，然后点击“确定”继续，或点击“取消”退出。

ExitSetupTitle=退出安装程序
ExitSetupMessage=安装程序尚未完成。如果现在退出，将不会安装该程序。%n%n您之后可以再次运行安装程序完成安装。%n%n现在退出安装程序吗？

ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonOK=确定
ButtonCancel=取消
ButtonYes=是(&Y)
ButtonNo=否(&N)
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&B)...
ButtonWizardBrowse=浏览(&R)...
ButtonNewFolder=新建文件夹(&M)

ClickNext=点击“下一步”继续，或点击“取消”退出安装程序。
BrowseDialogTitle=浏览文件夹
BrowseDialogLabel=在下方列表中选择文件夹，然后点击“确定”。
NewFolderName=新建文件夹

WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=即将在您的计算机上安装 [name/ver]。%n%n建议您在继续安装前关闭所有其他应用程序。

WizardLicense=许可协议
LicenseLabel=请在继续安装前阅读以下重要信息。
LicenseLabel3=请阅读下列许可协议。在继续安装前您必须同意这些协议条款。
LicenseAccepted=我同意此协议(&A)
LicenseNotAccepted=我不同意此协议(&D)

WizardSelectDir=选择目标位置
SelectDirDesc=您想将 [name] 安装在哪里？
SelectDirLabel3=安装程序将安装 [name] 到下列文件夹中。
SelectDirBrowseLabel=点击“下一步”继续。如果您想选择其他文件夹，请点击“浏览”。
DiskSpaceGBLabel=至少需要 [gb] GB 可用磁盘空间。
DiskSpaceMBLabel=至少需要 [mb] MB 可用磁盘空间。
InvalidPath=您必须输入带有驱动器盘符的完整路径，例如：%n%nC:\App%n%n或 UNC 路径：%n%n\\server\share
InvalidDrive=您选择的驱动器或 UNC 共享不存在或无法访问。请选择其他位置。
DiskSpaceWarningTitle=磁盘空间不足
DiskSpaceWarning=安装程序至少需要 %1 KB 可用空间，但所选驱动器仅有 %2 KB。%n%n仍要继续吗？
DirNameTooLong=文件夹名称或路径太长。
InvalidDirName=文件夹名称无效。
DirExistsTitle=文件夹已存在
DirExists=文件夹：%n%n%1%n%n已存在。确定要安装到此文件夹吗？
DirDoesntExistTitle=文件夹不存在
DirDoesntExist=文件夹：%n%n%1%n%n不存在。要创建此文件夹吗？

WizardSelectTasks=选择附加任务
SelectTasksDesc=您想让安装程序执行哪些附加任务？
SelectTasksLabel2=选择安装 [name] 时要执行的附加任务，然后点击“下一步”。

WizardReady=准备安装
ReadyLabel1=安装程序已准备好将 [name] 安装到您的计算机。
ReadyLabel2a=点击“安装”继续。如果您想查看或修改任意设置，请点击“上一步”。
ReadyLabel2b=点击“安装”继续。
ReadyMemoDir=目标位置：
ReadyMemoTasks=附加任务：

WizardPreparing=正在准备安装
PreparingDesc=安装程序正在准备将 [name] 安装到您的计算机。
ApplicationsFound=下列应用程序正在使用安装程序需要更新的文件。建议允许安装程序自动关闭这些应用程序。
CloseApplications=自动关闭应用程序(&A)
DontCloseApplications=不要关闭应用程序(&D)

WizardInstalling=正在安装
InstallingLabel=安装程序正在将 [name] 安装到您的计算机，请稍候。

FinishedHeadingLabel=完成 [name] 安装向导
FinishedLabelNoIcons=安装程序已在您的计算机中安装 [name]。
FinishedLabel=安装程序已在您的计算机中安装 [name]。您可以通过已安装的快捷方式启动应用程序。
ClickFinish=点击“完成”退出安装程序。
RunEntryExec=运行 %1

SetupAborted=安装程序未完成安装。%n%n请修正问题后重新运行安装程序。
AbortRetryIgnoreRetry=重试(&T)
AbortRetryIgnoreIgnore=忽略错误并继续(&I)
AbortRetryIgnoreCancel=取消安装
RetryCancelRetry=重试(&T)
RetryCancelCancel=取消
StatusClosingApplications=正在关闭应用程序...
StatusCreateDirs=正在创建目录...
StatusExtractFiles=正在提取文件...
StatusCreateIcons=正在创建快捷方式...
StatusSavingUninstall=正在保存卸载信息...
StatusRunProgram=正在完成安装...
StatusRollback=正在撤销更改...

ErrorCreatingDir=安装程序无法创建目录“%1”。
ErrorInternal2=内部错误：%1。
ErrorFunctionFailedNoCode=%1 失败。
ErrorFunctionFailed=%1 失败；错误代码 %2。
ErrorExecutingProgram=无法执行文件：%n%1
SourceIsCorrupted=源文件已损坏。
SourceDoesntExist=源文件“%1”不存在。
ExistingFileReadOnly2=无法替换已存在的只读文件。
ErrorReadingSource=读取源文件时出错：
ErrorCopying=复制文件时出错：
ErrorExtracting=提取压缩包时出错：

UninstallNotFound=文件“%1”不存在。无法卸载。
UninstallOpenError=文件“%1”无法打开。无法卸载。
ConfirmUninstall=确定要完全移除 %1 及其所有组件吗？
OnlyAdminCanUninstall=只有具有管理员权限的用户才能卸载此程序。
UninstallStatusLabel=正在从您的计算机中移除 %1，请稍候。
UninstalledAll=已成功从您的计算机中移除 %1。
UninstalledMost=%1 已卸载。%n%n有部分内容未能删除，您可以手动删除它们。
WizardUninstalling=卸载状态
StatusUninstalling=正在卸载 %1...

[CustomMessages]
AdditionalIcons=附加快捷方式：
CreateDesktopIcon=创建桌面快捷方式(&D)
LaunchProgram=启动 %1
