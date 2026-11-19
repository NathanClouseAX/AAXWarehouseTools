<#
.SYNOPSIS
    Checks that every model of the package was compiled into the build output.
.DESCRIPTION
    Reads each model descriptor in the package's Descriptor folder and looks for the model in the
    compiled <package>_ModelInfo.md metadata. A descriptor that places its model in another package,
    or a model missing from the compiled metadata, fails the step, so a deployable package never
    ships without one of its models.
.PARAMETER DescriptorPath
    The package's Descriptor folder.
.PARAMETER BinPath
    The compiled package bin folder.
.PARAMETER PackageName
    The package (module) the pipeline builds.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DescriptorPath,

    [Parameter(Mandatory = $true)]
    [string] $BinPath,

    [Parameter(Mandatory = $true)]
    [string] $PackageName
)

$ErrorActionPreference = 'Stop'

function Write-PipelineError([string] $Message)
{
    if ($env:TF_BUILD)
    {
        Write-Host "##vso[task.logissue type=error]$Message"
    }
    else
    {
        Write-Host "ERROR: $Message"
    }
}

$modelInfoPath = Join-Path $BinPath "$($PackageName)_ModelInfo.md"

if (-not (Test-Path -LiteralPath $modelInfoPath))
{
    Write-PipelineError "The compiled package metadata $modelInfoPath doesn't exist."
    exit 1
}

# The model names are stored as plain text inside the binary metadata file
$modelInfo = [System.IO.File]::ReadAllText($modelInfoPath, [System.Text.Encoding]::GetEncoding(28591))
$compiled = @()
$failed = $false

foreach ($file in Get-ChildItem -LiteralPath $DescriptorPath -Filter '*.xml' | Sort-Object Name)
{
    [xml] $descriptor = Get-Content -LiteralPath $file.FullName -Raw
    $modelName = $descriptor.AxModelInfo.Name
    $modelPackage = $descriptor.AxModelInfo.ModelModule

    if ($modelPackage -ne $PackageName)
    {
        Write-PipelineError "Descriptor $($file.Name) places model $modelName in package $modelPackage, but this pipeline builds package $PackageName."
        $failed = $true
        continue
    }

    $nameText = [System.Text.Encoding]::GetEncoding(28591).GetString([System.Text.Encoding]::UTF8.GetBytes($modelName))

    if (-not $modelInfo.Contains($nameText))
    {
        Write-PipelineError "Model $modelName is missing from the compiled package $PackageName."
        $failed = $true
        continue
    }

    $compiled += $modelName
}

if (-not $compiled -and -not $failed)
{
    Write-PipelineError "No model descriptors were found in $DescriptorPath."
    exit 1
}

Write-Host "Package $PackageName contains models: $($compiled -join ', ')"

if ($failed)
{
    exit 1
}

exit 0
