<#
test Powershell action script
#>
param(
    [string]$File,
    [string]$Json
)

if ($File) {
    [PSCustomObject]@{
        File    = $File
        Version = $PSVersionTable.PSVersion
    } | ConvertTo-Json | Set-Content $PSScriptRoot/output_file.json
    return $File
}
if ($Json) {
    $incoming = $Json | ConvertFrom-Json
    $incoming | ConvertTo-Json -ErrorAction Ignore -WarningAction Ignore | Set-Content $PSScriptRoot/output_json.json
    return $incoming
}
