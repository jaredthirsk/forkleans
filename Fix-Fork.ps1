# Fix-Fork.ps1

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,

    [Parameter()]
    [switch]$DryRun = $false
)


.\Convert-OrleansNamespace.ps1 -RootPath $RootPath -DryRun:$DryRun -Verbose:$Verbose
.\Smart-Fix-References.ps1 -RootPath $RootPath -DryRun:$DryRun -Verbose:$Verbose
.\Fix-AssemblyNames.ps1 -RootPath "G:\forks\orleans" -CheckOnly:$DryRun -Verbose:$Verbose
