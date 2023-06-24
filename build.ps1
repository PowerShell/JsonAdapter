[CmdletBinding()]
param (
    [switch]$NoBuild, # by default we build
    [switch]$Release,
    [switch]$Test,
    [switch]$UseSignedFiles,
    [switch]$Package,
    [switch]$Clean # remove out,staging,src/bin,src/obj
)

function Test-PSVersion {
    # Be sure we're running on 7.4 - otherwise we cannot publish
    # running on a preview is sufficient
    if (($PSVersionTable.PSVersion -as [Version]) -lt "7.4") {
        $ex = [invalidoperationexception]::new("PowerShell version must be at least 7.4 to run this script.")
        $er = [System.Management.Automation.ErrorRecord]::new($ex, "invalidpsversion", "InvalidOperation", $PSVersionTable.PSVersion)	
        $PSCmdlet.ThrowTerminatingError($er)
    }
}

# this takes the files for the module and publishes them to a created, local repository
# so the nupkg can be used to publish to the PSGallery
function Export-Module
{
    [CmdletBinding()]
    [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("PSAvoidUsingWriteHost", "")]
    param(
        [Parameter(Mandatory=$true,Position=0)][string]$packageRoot,
        [Parameter(Mandatory=$true,Position=1)][string]$exportRoot
        )

    if ( -not (test-path $packageRoot)) {
        throw "'$packageRoot' does not exist"
    }
    # now construct a nupkg by registering a local repository and calling publish module
    $repoName = [guid]::newGuid().ToString("N")
    try {
        Register-PSRepository -Name $repoName -SourceLocation ${exportRoot} -InstallationPolicy Trusted
        Publish-Module -Path $packageRoot -Repository $repoName -Force
    }
    catch {
        throw $_
    }
    finally {
        if (Get-PackageSource -Name $repoName) {
            Unregister-PSRepository -Name $repoName
        }
    }
    Get-ChildItem -Recurse -Name $packageRoot | Write-Verbose -Verbose

    # construct the package path and publish it
    $nupkgName = "{0}.{1}" -f $moduleName,$moduleInfo.ModuleVersion
    $pre = $moduleInfo.PrivateData.PSData.Prerelease
    if ($pre) { $nupkgName += "-${pre}" }
    $nupkgName += ".nupkg"
    $nupkgPath = Join-Path $exportRoot $nupkgName
    if ($env:TF_BUILD) {
        # In Azure DevOps
        Write-Host "##vso[artifact.upload containerfolder=$nupkgName;artifactname=$nupkgName;]$nupkgPath"
    }
    else {
        Write-Verbose -Verbose "package path: ${nupkgPath} (exists:$(Test-Path $nupkgPath))"
    }
}

$moduleName = "Microsoft.PowerShell.JsonAdapter"
$moduleManifest = "${ModuleName}.psd1"
try {
    $moduleManifestPath = "${PSScriptRoot}/src/${ModuleManifest}"
    $moduleInfo = Import-PowerShellDatafile "${moduleManifestPath}" -ErrorAction Stop
}
catch {
    throw "file not found '${moduleInfoPath}'"
}

$moduleVersion = $moduleInfo.ModuleVersion
$outDirectory = "${PSScriptRoot}/out/${moduleName}/${moduleVersion}"
$srcDirectory = "${PSScriptRoot}/src"
$stagingDirectory = "${PSScriptRoot}/staging/${moduleName}/${moduleVersion}"
$signDirectory = "${PSScriptRoot}/signed"
Write-Progress "Starting Build"

if ($clean) {
	Write-Progress "Cleaning"
    Remove-Item -Rec -Force "${PSScriptRoot}/out" -Verbose -ErrorAction Ignore
    Remove-Item -Rec -Force "${PSScriptRoot}/staging" -Verbose -ErrorAction Ignore
    Remove-Item -Rec -Force "${srcDirectory}/bin" -Verbose -ErrorAction Ignore
    Remove-Item -Rec -Force "${srcDirectory}/obj" -Verbose -ErrorAction Ignore
    Remove-Item -Force "${PSScriptRoot}/*.nupkg" -Verbose
}

$moduleFiles = @(
    @{ Sign = $true ; File = "${ModuleManifest}" }
    @{ Sign = $true ; File = "${ModuleName}.dll" }
)

$config = $Release.ToBool() ? "Release" : "Debug"
# build (unless not requested)
if (!$NoBuild) {
	Write-Progress "Starting Build"
    try {
        Push-Location "$PSScriptRoot/src"
        if (!(test-Path ${outDirectory})) {
            $null = New-Item -Type Directory -Path ${outDirectory} -Force
        }
        $output = dotnet publish --configuration ${config} -o ${outDirectory}
        if ($LASTEXITCODE -ne 0) {
            write-error $output
            return
        }
        copy-item ${moduleManifest} ${outDirectory}
    }
    finally {
        Pop-Location
    }
}

# create a module nupkg
if ($package) {
    Test-PSVersion
    Write-Progress -Verbose "Starting Packaging"
    try {
        Push-Location ${PSScriptRoot}
        if (!(Test-Path ${stagingDirectory})) {
            $null = New-Item -Type Directory ${stagingDirectory}
        }
        foreach ($file in $moduleFiles) {
            $fmt = "${outDirectory}/{0}"
            if ($file.Sign -and $UseSignedFiles) {
                $fmt = "${signDirectory}/{0}"
            }
            $src = "${fmt}" -f $file.File
			Write-Progress "Copying $src"
            if (Test-Path $src) {
                copy-item $src ${stagingDirectory}
            }
            else {
                Write-Error "file not found: ${src}"
            }
        }
		Write-Progress "Exporting Package"
        Export-Module -packageRoot $stagingDirectory -exportRoot $PSScriptRoot
    }
    finally {
        Pop-Location
    }
}

if ($Test) {
    $sb = [scriptblock]::Create("
        Set-Location $PSScriptRoot
        import-module $PSScriptRoot/out/Microsoft.PowerShell.JsonAdapter
        Set-Location $PSScriptRoot/test
        import-module -Name Pester -Max 4.99
        Invoke-Pester
    ")
    pwsh-preview -nopro -c $sb
}
