param(
    [Parameter(Mandatory=$true)]
    [string]$AboutXmlPath,
    
    [Parameter(Mandatory=$true)]
    [string]$Version
)

# Load the XML document
[xml]$xmlDoc = Get-Content $AboutXmlPath

# Update the version
$xmlDoc.ModMetadata.Version = $Version

# Save with proper formatting (preserve indentation and encoding)
$xmlWriterSettings = New-Object System.Xml.XmlWriterSettings
$xmlWriterSettings.Indent = $true
$xmlWriterSettings.IndentChars = "    "
$xmlWriterSettings.NewLineChars = [System.Environment]::NewLine
$xmlWriterSettings.Encoding = [System.Text.Encoding]::UTF8

$xmlWriter = [System.Xml.XmlWriter]::Create($AboutXmlPath, $xmlWriterSettings)
$xmlDoc.Save($xmlWriter)
$xmlWriter.Close()

Write-Host "Updated About.xml version to: $Version"