param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$xmlPath = Join-Path $ProjectRoot "ProjectConfig\SK\BCM-125\BCM-125_autoTest.xml"
if (-not (Test-Path -LiteralPath $xmlPath)) {
    throw "BCM-125 test plan not found: $xmlPath"
}

[xml]$document = Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8
$root = $document.TestItems
$items = @($root.TestItem)
if ($items.Count -eq 957) {
    foreach ($cleanupName in @(
        "CLEANUP Release fixture press relays and start fixture rise",
        "CLEANUP Verify fixture left pressed position (digital input 7)"
    )) {
        $item = $document.CreateElement("TestItem")
        $cleanupFields = [ordered]@{
            Enabled = "true"
            Name = $cleanupName
            UpperLimit = "1"
            LowerLimit = "1"
            Unit = "state"
        }
        foreach ($field in $cleanupFields.GetEnumerator()) {
            $node = $document.CreateElement($field.Key)
            $node.InnerText = $field.Value
            [void]$item.AppendChild($node)
        }
        [void]$root.AppendChild($item)
    }
    $items = @($root.TestItem)
}
if ($items.Count -ne 959) {
    throw "Expected 957 legacy or 959 migrated BCM-125 items, actual count: $($items.Count)"
}

function Set-ItemField {
    param(
        [System.Xml.XmlElement]$Item,
        [string]$Name,
        [string]$Value
    )
    $node = $Item.SelectSingleNode($Name)
    if ($null -eq $node) {
        $node = $document.CreateElement($Name)
        $enabled = $Item.SelectSingleNode("Enabled")
        if ($null -ne $enabled) {
            [void]$Item.InsertBefore($node, $enabled)
        }
        else {
            [void]$Item.PrependChild($node)
        }
    }
    $node.InnerText = $Value
}

function Get-GroupId {
    param([int]$Index)
    $ends = @(7, 10, 24, 53, 61, 69, 96, 105, 117, 126, 161, 207,
              266, 344, 415, 452, 513, 630, 740, 840, 917, 953, 958)
    $ids = @(
        "BCM125.PRECHECK", "BCM125.CH07", "BCM125.CH08", "BCM125.CH09",
        "BCM125.CH10", "BCM125.CH11", "BCM125.CH12", "BCM125.CH13",
        "BCM125.CH14", "BCM125.CH15", "BCM125.CH16", "BCM125.CH17",
        "BCM125.CH18", "BCM125.CH19", "BCM125.CH20", "BCM125.CH21",
        "BCM125.CH22", "BCM125.CH23", "BCM125.CH24", "BCM125.CH25",
        "BCM125.CH26", "BCM125.CH27", "BCM125.CLEANUP"
    )
    for ($i = 0; $i -lt $ends.Count; $i++) {
        if ($Index -le $ends[$i]) { return $ids[$i] }
    }
    throw "No GroupId mapping for item index $Index"
}

$groupNames = [ordered]@{
    "BCM125.PRECHECK" = "PRECHECK"
    "BCM125.CH07" = "CH7 INSTRUMENT INITIALIZATION"
    "BCM125.CH08" = "CH8 FIRST START-UP"
    "BCM125.CH09" = "CH9 PROGRAMMING"
    "BCM125.CH10" = "CH10 CAN BUS"
    "BCM125.CH11" = "CH11 FIRMWARE VERSION"
    "BCM125.CH12" = "CH12 RESET FROM FLAT CABLE"
    "BCM125.CH13" = "CH13 VREF VOLTAGE"
    "BCM125.CH14" = "CH14 POWER SUPPLY VOLTAGE"
    "BCM125.CH15" = "CH15 HEAT SINK TEMPERATURE"
    "BCM125.CH16" = "CH16 VBATT_SCAL_IN CALIBRATION"
    "BCM125.CH17" = "CH17 WESTINGHOUSE LINE"
    "BCM125.CH18" = "CH18 PRECHARGE RELAY"
    "BCM125.CH19" = "CH19 VMID CALIBRATION"
    "BCM125.CH20" = "CH20 DISCHARGE CURRENT"
    "BCM125.CH21" = "CH21 DISCHARGE MOSFET"
    "BCM125.CH22" = "CH22 SHORT PROTECTION"
    "BCM125.CH23" = "CH23 CHARGING/VSTR_NEG CALIBRATION"
    "BCM125.CH24" = "CH24 CALIBRATION VERIFICATION"
    "BCM125.CH25" = "CH25 CCB FUNCTIONAL CHECK"
    "BCM125.CH26" = "CH26 STRING TEST"
    "BCM125.CH27" = "CH27 WRITING INFO FIELDS"
    "BCM125.CLEANUP" = "CLEANUP"
}

foreach ($nodeName in @("PlanMetadata", "Groups")) {
    $oldNode = $root.SelectSingleNode($nodeName)
    if ($null -ne $oldNode) {
        [void]$root.RemoveChild($oldNode)
    }
}

$metadata = $document.CreateElement("PlanMetadata")
$metadataFields = [ordered]@{
    BoardType = "BCM-125"
    PlanVersion = "6.1"
    BaselineId = "BCM125-PRODUCTION-20260724"
}
foreach ($entry in $metadataFields.GetEnumerator()) {
    $node = $document.CreateElement($entry.Key)
    $node.InnerText = $entry.Value
    [void]$metadata.AppendChild($node)
}
[void]$root.PrependChild($metadata)

$groupsNode = $document.CreateElement("Groups")
$groupSequence = 0
foreach ($entry in $groupNames.GetEnumerator()) {
    $groupSequence++
    $group = $document.CreateElement("Group")
    $isMandatory = ($entry.Key -eq "BCM125.PRECHECK") -or
                   ($entry.Key -eq "BCM125.CH07") -or
                   ($entry.Key -eq "BCM125.CLEANUP")
    $groupFields = [ordered]@{
        GroupId = $entry.Key
        Name = $entry.Value
        SequenceOrder = $groupSequence.ToString()
        Mandatory = $isMandatory.ToString().ToLowerInvariant()
        DefaultEnabled = "true"
        DependsOn = ""
    }
    foreach ($field in $groupFields.GetEnumerator()) {
        $node = $document.CreateElement($field.Key)
        $node.InnerText = $field.Value
        [void]$group.AppendChild($node)
    }
    [void]$groupsNode.AppendChild($group)
}
[void]$root.InsertAfter($groupsNode, $metadata)

$groupCounters = @{}
for ($index = 0; $index -lt $items.Count; $index++) {
    $item = [System.Xml.XmlElement]$items[$index]
    $groupId = Get-GroupId -Index $index
    if (-not $groupCounters.ContainsKey($groupId)) {
        $groupCounters[$groupId] = 0
    }
    $groupCounters[$groupId]++

    $name = [string]$item.Name
    $match = [regex]::Match($name, '(?<![A-Z0-9])([B-U]\d{2})(?!\d)', 'IgnoreCase')
    $suffix = if ($match.Success) { $match.Groups[1].Value.ToUpperInvariant() } else { "ACTION" }
    $generatedStepId = "{0}.S{1:D3}.{2}" -f $groupId, $groupCounters[$groupId], $suffix
    $stepId = if ([string]::IsNullOrWhiteSpace([string]$item.StepId)) {
        $generatedStepId
    }
    else {
        [string]$item.StepId
    }
    $mandatory = ($groupId -eq "BCM125.PRECHECK") -or
                 ($groupId -eq "BCM125.CH07") -or
                 ($groupId -eq "BCM125.CLEANUP")
    $alwaysRun = $groupId -eq "BCM125.CLEANUP"
    $enabledText = [string]$item.Enabled
    $defaultEnabled = if ([string]::IsNullOrWhiteSpace($enabledText)) {
        "true"
    }
    else {
        $enabledText.ToLowerInvariant()
    }

    Set-ItemField $item "StepId" $stepId
    Set-ItemField $item "GroupId" $groupId
    Set-ItemField $item "SequenceOrder" ($index + 1).ToString()
    Set-ItemField $item "DefaultEnabled" $defaultEnabled
    Set-ItemField $item "Mandatory" $mandatory.ToString().ToLowerInvariant()
    Set-ItemField $item "AlwaysRun" $alwaysRun.ToString().ToLowerInvariant()
    Set-ItemField $item "DependsOn" ""
}

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.IndentChars = "  "
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$settings.NewLineChars = "`r`n"
$settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
$writer = [System.Xml.XmlWriter]::Create($xmlPath, $settings)
try {
    $document.Save($writer)
}
finally {
    $writer.Dispose()
}

Write-Output "Migrated BCM-125 test plan: $xmlPath"
Write-Output "Items=$($items.Count); Groups=$($groupNames.Count); Dependencies=0"
