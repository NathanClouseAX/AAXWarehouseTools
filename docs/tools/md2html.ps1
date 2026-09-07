$ErrorActionPreference = 'Stop'

$vsIde   = 'C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE'
$depDir  = "$vsIde\CommonExtensions\Microsoft\Asal\TokenService"
$facades = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Facades'
$markdigPath = "$vsIde\PrivateAssemblies\Markdig.Signed.dll"

$bridgeSource = @'
using System;
using System.IO;
using System.Reflection;

public static class MdBridge
{
    private static string[] _probeDirs;
    private static object _pipeline;

    public static void Init(string[] probeDirs)
    {
        _probeDirs = probeDirs;
        AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
    }

    private static Assembly OnResolve(object sender, ResolveEventArgs e)
    {
        string want = new AssemblyName(e.Name).Name;
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name == want) { return a; }
        }
        foreach (string dir in _probeDirs)
        {
            string p = Path.Combine(dir, want + ".dll");
            if (File.Exists(p)) { return Assembly.LoadFrom(p); }
        }
        return null;
    }

    public static string ToHtml(string markdown)
    {
        if (_pipeline == null)
        {
            object builder = Activator.CreateInstance(Type.GetType("Markdig.MarkdownPipelineBuilder, Markdig.Signed", true));
            Type ext = Type.GetType("Markdig.MarkdownExtensions, Markdig.Signed", true);
            builder = ext.GetMethod("UseAdvancedExtensions").Invoke(null, new object[] { builder });
            _pipeline = builder.GetType().GetMethod("Build").Invoke(builder, null);
        }
        Type md = Type.GetType("Markdig.Markdown, Markdig.Signed", true);
        foreach (MethodInfo m in md.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != "ToHtml" || m.ReturnType != typeof(string)) { continue; }
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length >= 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.Name == "MarkdownPipeline")
            {
                object[] args = new object[ps.Length];
                args[0] = markdown;
                args[1] = _pipeline;
                for (int i = 2; i < ps.Length; i++) { args[i] = null; }
                return (string)m.Invoke(null, args);
            }
        }
        throw new InvalidOperationException("No suitable Markdig ToHtml overload found.");
    }
}
'@
Add-Type -TypeDefinition $bridgeSource -Language CSharp

[MdBridge]::Init(@("$vsIde\PrivateAssemblies", $depDir, $facades))
[void][System.Reflection.Assembly]::LoadFrom($markdigPath)

$css = @'
:root { color-scheme: light; }
body { margin: 0 auto; padding: 2rem 2.5rem 4rem; max-width: 920px;
  font-family: "Segoe UI", -apple-system, Roboto, Helvetica, Arial, sans-serif;
  font-size: 16px; line-height: 1.55; color: #1f2328; background: #ffffff; }
h1, h2, h3, h4 { font-weight: 600; line-height: 1.25; margin: 1.6em 0 0.6em; }
h1 { font-size: 1.9em; border-bottom: 1px solid #d1d9e0; padding-bottom: 0.3em; margin-top: 0.4em; }
h2 { font-size: 1.45em; border-bottom: 1px solid #d1d9e0; padding-bottom: 0.3em; }
h3 { font-size: 1.2em; }
p, ul, ol { margin: 0.5em 0 1em; }
li { margin: 0.25em 0; }
a { color: #0969da; text-decoration: none; }
a:hover { text-decoration: underline; }
code { font-family: Consolas, "Cascadia Mono", "Courier New", monospace; font-size: 0.9em;
  background: #f0f2f5; border-radius: 4px; padding: 0.15em 0.4em; }
pre { background: #f6f8fa; border: 1px solid #d1d9e0; border-radius: 6px;
  padding: 12px 16px; overflow-x: auto; line-height: 1.45; }
pre code { background: none; padding: 0; font-size: 0.88em; }
table { border-collapse: collapse; margin: 1em 0 1.4em; width: 100%; display: block; overflow-x: auto; }
th, td { border: 1px solid #d1d9e0; padding: 7px 12px; text-align: left; vertical-align: top; }
th { background: #f6f8fa; font-weight: 600; }
tr:nth-child(even) td { background: #fafbfc; }
blockquote { margin: 1em 0; padding: 0.2em 1em; border-left: 4px solid #d1d9e0; color: #59636e; }
hr { border: none; border-top: 1px solid #d1d9e0; margin: 2em 0; }
img { max-width: 100%; }
@media print { body { max-width: none; padding: 0; font-size: 12px; } }
'@

function Convert-Doc([string]$inPath, [string]$outPath) {
    $md = [System.IO.File]::ReadAllText($inPath, [System.Text.Encoding]::UTF8)

    # Point cross-document links at the .html copies
    $md = $md -replace '\.md\)', '.html)'
    $md = $md -replace '\.md#', '.html#'

    # Title from the first level-1 heading, else the file name
    $title = [System.IO.Path]::GetFileNameWithoutExtension($inPath)
    $m = [regex]::Match($md, '(?m)^#\s+(.+?)\s*$')
    if ($m.Success) { $title = $m.Groups[1].Value }

    $body = [MdBridge]::ToHtml($md)

    $html = "<!DOCTYPE html>`r`n<html lang=`"en`">`r`n<head>`r`n<meta charset=`"utf-8`" />`r`n" +
            "<meta name=`"viewport`" content=`"width=device-width, initial-scale=1`" />`r`n" +
            "<title>" + [System.Net.WebUtility]::HtmlEncode($title) + "</title>`r`n" +
            "<style>`r`n" + $css + "`r`n</style>`r`n</head>`r`n<body>`r`n" +
            $body + "`r`n</body>`r`n</html>`r`n"

    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($outPath, $html, $enc)
    Write-Output ("{0} -> {1} ({2:n0} bytes)" -f (Split-Path $inPath -Leaf), (Split-Path $outPath -Leaf), (Get-Item $outPath).Length)
}

$repo = 'C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools'
$docs = @(
    @{ in = "$repo\README.md";                       out = "$repo\README.html" },
    @{ in = "$repo\THIRD-PARTY-NOTICES.md";          out = "$repo\THIRD-PARTY-NOTICES.html" },
    @{ in = "$repo\docs\UserGuide.md";               out = "$repo\docs\UserGuide.html" },
    @{ in = "$repo\docs\ItemIdentitySetup.md";       out = "$repo\docs\ItemIdentitySetup.html" },
    @{ in = "$repo\docs\ItemIdentityUserGuide.md";   out = "$repo\docs\ItemIdentityUserGuide.html" },
    @{ in = "$repo\docs\ItemIdentityScenarios.md";   out = "$repo\docs\ItemIdentityScenarios.html" },
    @{ in = "$repo\docs\ItemIdentitySmokeTest.md";   out = "$repo\docs\ItemIdentitySmokeTest.html" },
    @{ in = "$repo\docs\BuildPipeline.md";           out = "$repo\docs\BuildPipeline.html" }
)

foreach ($d in $docs) { Convert-Doc $d.in $d.out }
