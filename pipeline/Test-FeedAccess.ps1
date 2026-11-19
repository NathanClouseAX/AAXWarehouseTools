<#
.SYNOPSIS
    Checks that the build identity can read the package feed named in nuget.config.
.DESCRIPTION
    Requests the feed's service index with the job access token before the package restore runs.
    When the request is refused, names the build identity and the Azure DevOps permission it is
    missing, then fails the step.
.PARAMETER NugetConfigPath
    The nuget.config file that names the feed.
.PARAMETER AccessToken
    The job access token. Defaults to the SYSTEM_ACCESSTOKEN environment variable.
.PARAMETER CollectionUri
    The organization URL. Defaults to the SYSTEM_COLLECTIONURI environment variable.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $NugetConfigPath,

    [string] $AccessToken = $env:SYSTEM_ACCESSTOKEN,

    [string] $CollectionUri = $env:SYSTEM_COLLECTIONURI
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

# Read the feed from nuget.config
[xml] $config = Get-Content -LiteralPath $NugetConfigPath -Raw
$source = @($config.configuration.packageSources.add) | Where-Object { $_.value -match '^https?://' } | Select-Object -First 1

if (-not $source)
{
    Write-PipelineError "No package source URL is listed in $NugetConfigPath."
    exit 1
}

$feedUrl = $source.value
$parts = [regex]::Match($feedUrl, '^https://(?:pkgs\.dev\.azure\.com/[^/]+|[^./]+\.pkgs\.visualstudio\.com)(?:/(?<project>[^/]+))?/_packaging/(?<feed>[^/@]+)')
$feedName = if ($parts.Success) { [uri]::UnescapeDataString($parts.Groups['feed'].Value) } else { $feedUrl }
$projectName = if ($parts.Success -and $parts.Groups['project'].Success) { [uri]::UnescapeDataString($parts.Groups['project'].Value) } else { '' }

if (-not $AccessToken)
{
    Write-PipelineError 'No job access token reached the script. Map SYSTEM_ACCESSTOKEN to $(System.AccessToken) in the step env.'
    exit 1
}

$headers = @{ Authorization = "Bearer $AccessToken" }

# Name the identity the job runs as, so the message says whom to grant access
$identity = $null

if ($CollectionUri)
{
    try
    {
        $connection = Invoke-RestMethod -Uri ($CollectionUri.TrimEnd('/') + '/_apis/connectionData') -Headers $headers -UseBasicParsing -MaximumRedirection 0
        $identity = $connection.authenticatedUser.providerDisplayName
    }
    catch
    {
        $identity = $null
    }
}

$who = if ($identity) { "'$identity'" } else { "The pipeline's build identity" }
$whom = if ($identity) { "'$identity'" } else { "the pipeline's build identity" }
Write-Host "Feed: $feedUrl"
Write-Host "Build identity: $(if ($identity) { $identity } else { 'unknown' })"

# Request the service index the restore starts with
$status = 0
$detail = ''

try
{
    $response = Invoke-WebRequest -Uri $feedUrl -Headers $headers -UseBasicParsing -MaximumRedirection 0
    $status = [int] $response.StatusCode
}
catch
{
    $failure = $_
    $errorResponse = $failure.Exception.Response

    if ($errorResponse)
    {
        $status = [int] $errorResponse.StatusCode
    }

    if ($failure.ErrorDetails -and $failure.ErrorDetails.Message)
    {
        $detail = $failure.ErrorDetails.Message
    }
    elseif ($errorResponse)
    {
        # Read the error body when PowerShell didn't keep it
        try
        {
            $reader = New-Object System.IO.StreamReader($errorResponse.GetResponseStream())
            $detail = $reader.ReadToEnd()
            $reader.Dispose()
        }
        catch
        {
            $detail = ''
        }
    }

    if (-not $detail)
    {
        $detail = $failure.Exception.Message
    }
}

if ($status -eq 200)
{
    Write-Host "$who can read feed '$feedName'."
    exit 0
}

if ($detail)
{
    Write-Host "Response: $detail"
}

if ($status -eq 404 -and $detail -match 'VS800075')
{
    $projectRef = if ($projectName) { "project '$projectName'" } else { 'that project' }
    Write-PipelineError "$who can't see the Azure DevOps project that hosts feed '$feedName'. In $projectRef, open Project settings > Permissions > Readers > Members and add $whom. Then open the feed's Feed settings > Permissions and add the same identity as Feed Reader."
}
elseif ($status -eq 401 -or $status -eq 403)
{
    Write-PipelineError "$who can't read feed '$feedName'. Open the feed's Feed settings > Permissions > Add users/groups and add $whom as Feed Reader."
}
elseif ($status -eq 404)
{
    Write-PipelineError "Feed '$feedName' wasn't found, or $whom can't see it. Check the feed URL in nuget.config and the feed's permissions."
}
else
{
    Write-PipelineError "Feed '$feedName' answered with HTTP status $status. The package restore is likely to fail."
}

exit 1
