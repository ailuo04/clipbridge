#requires -Version 7.0
[CmdletBinding()]
param([string]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\ClipBridge-Setup.iss'))

$ErrorActionPreference = 'Stop'
$text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
$checks = @(
    @{ Pattern = '(?m)^PrivilegesRequired=admin\s*$'; Message = '安装器必须要求管理员权限。' },
    @{ Pattern = '(?m)^DefaultDirName=\{code:GetDefaultInstallDir\}\s*$'; Message = '安装器必须使用动态默认安装目录。' },
    @{ Pattern = '(?m)^DisableDirPage=no\s*$'; Message = '安装器必须显示安装目录页面。' },
    @{ Pattern = '(?m)^Name: "\{app\}"; Permissions: users-modify\s*$'; Message = '安装目录必须允许普通用户修改，以保持自动更新。' },
    @{ Pattern = '(?s)function GetDefaultInstallDir\(Param: String\): String;.*?D:\\own\\app.*?ClipBridge'; Message = 'D:\own\app\ClipBridge 必须是存在 D: 盘时的默认目录。' }
)
foreach ($check in $checks) {
    if ($text -notmatch $check.Pattern) { throw $check.Message }
}
Write-Output 'PASS installer privilege, default path, directory page and update ACL policy'
