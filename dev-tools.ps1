# XNotInclude / Unity 6 协作指令集
# 所有相对路径均基于 Get-DevRoot 返回的项目根目录。

function Get-DevRoot {
    $root = $global:XNotIncludeDevRoot
    if (-not $root) { $root = 'E:\wsp\XNotInclude' }
    $full = [System.IO.Path]::GetFullPath($root)
    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        throw "项目根目录不存在: $full"
    }
    return $full
}

function Format-DevRel {
    param([string]$Full)
    $root = Get-DevRoot
    $prefix = $root.TrimEnd('\', '/') + '\'
    if ($Full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Full.Substring($prefix.Length).Replace('\', '/')
    }
    return $Full.Replace('\', '/')
}

function Resolve-DevPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $p = $Path.Trim().Replace('/', '\')
    if ([System.IO.Path]::IsPathRooted($p)) {
        throw "只接受项目根目录下的相对路径: $Path"
    }
    $root = Get-DevRoot
    $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($root, $p))
    $rootTrim = $root.TrimEnd('\', '/')
    $prefix = $rootTrim + '\'
    if (-not ($full.Equals($rootTrim, [System.StringComparison]::OrdinalIgnoreCase) -or
              $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase))) {
        throw "路径越界，拒绝访问项目外文件: $Path"
    }
    return $full
}

function RUN {
    param([Parameter(Mandatory = $true)][scriptblock]$Block)

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    $output = New-Object System.Collections.ArrayList

    try {
        & $Block *>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                [void]$output.Add($_.ToString())
            }
            else {
                [void]$output.Add([string]$_)
            }
        }
    }
    catch {
        [void]$output.Add("[ERROR] $($_.Exception.Message)")
    }
    finally {
        $ErrorActionPreference = $prev
    }

    $text = $output -join "`n"
    Set-Clipboard -Value $text
    $bytes = [System.Text.Encoding]::UTF8.GetByteCount($text)
    Write-Output ("[OK] 已复制 {0} 行 / {1:N1} KB" -f $output.Count, ($bytes / 1KB))
}

function TREE {
    param([string]$Path = '.')

    $full = Resolve-DevPath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        throw "不是目录: $Path"
    }

    $excludedDirs = @('Library', 'Temp', 'obj', 'Logs', '.git', '.idea', 'UserSettings')
    $items = Get-ChildItem -LiteralPath $full -Recurse -File | Sort-Object FullName

    foreach ($item in $items) {
        $rel = Format-DevRel $item.FullName
        $segments = $rel.Split('/')
        $skip = $false
        foreach ($seg in $segments) {
            if ($excludedDirs -contains $seg) { $skip = $true; break }
        }
        if ($skip -or $item.Extension -ieq '.meta') { continue }

        try {
            $lines = [System.IO.File]::ReadAllLines($item.FullName).Count
            "{0} ({1} lines)" -f $rel, $lines
        }
        catch {
            "{0} ({1} bytes)" -f $rel, $item.Length
        }
    }
}

function READ {
    param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Specs)

    foreach ($spec in $Specs) {
        $match = [regex]::Match($spec, '^(.+?)(?::(\d+)(?:-(\d+))?)?$')
        if (-not $match.Success) { throw "无法解析 READ 参数: $spec" }

        $rawPath = $match.Groups[1].Value
        $full = Resolve-DevPath $rawPath
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "文件不存在: $rawPath" }

        $lines = [System.IO.File]::ReadAllLines($full)
        $total = $lines.Count
        $start = 1
        $end = $total
        if ($match.Groups[2].Success) { $start = [int]$match.Groups[2].Value }
        if ($match.Groups[3].Success) { $end = [int]$match.Groups[3].Value }
        if ($start -lt 1) { $start = 1 }
        if ($end -gt $total) { $end = $total }
        if ($start -gt $end) { throw "行号范围无效: $spec" }

        $rel = Format-DevRel $full
        "// === $rel ($start-$end / $total) ==="
        for ($i = $start; $i -le $end; $i++) {
            "{0,4}: {1}" -f $i, $lines[$i - 1]
        }
        ""
    }
}

function GREP {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Pattern,
        [Parameter(Position = 1)][string]$Path = 'Assets/Scripts',
        [Alias('C')][int]$Context = 0
    )

    $full = Resolve-DevPath $Path
    if (-not (Test-Path -LiteralPath $full)) { throw "路径不存在: $Path" }

    $extensions = @('.cs', '.shader', '.hlsl', '.asmdef', '.json')
    if ((Get-Item -LiteralPath $full).PSIsContainer) {
        $files = Get-ChildItem -LiteralPath $full -Recurse -File | Where-Object {
            $extensions -contains $_.Extension.ToLowerInvariant()
        }
    }
    else {
        $files = @(Get-Item -LiteralPath $full)
    }

    $hits = foreach ($file in $files) {
        Select-String -LiteralPath $file.FullName -Pattern $Pattern -Context $Context, $Context
    }

    $limit = 100
    $total = @($hits).Count
    if ($total -gt $limit) {
        "[GREP] 共 $total 条，仅显示前 $limit 条"
        $hits = $hits | Select-Object -First $limit
    }

    foreach ($hit in $hits) {
        $rel = Format-DevRel $hit.Path
        if ($Context -gt 0) {
            $beforeStart = $hit.LineNumber - $Context
            for ($k = 0; $k -lt $hit.Context.PreContext.Count; $k++) {
                "{0}:{1}- {2}" -f $rel, ($beforeStart + $k), $hit.Context.PreContext[$k]
            }
        }
        "{0}:{1}: {2}" -f $rel, $hit.LineNumber, $hit.Line
        if ($Context -gt 0) {
            for ($k = 0; $k -lt $hit.Context.PostContext.Count; $k++) {
                "{0}:{1}+ {2}" -f $rel, ($hit.LineNumber + 1 + $k), $hit.Context.PostContext[$k]
            }
        }
    }

    if ($total -eq 0) { "[GREP] 无匹配: $Pattern" }
}

function WRITE {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Path,
        [Parameter(Mandatory = $true, Position = 1)][string]$Content
    )

    $full = Resolve-DevPath $Path
    $dir = Split-Path -Parent $full
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $text = $Content.TrimStart("`r", "`n")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($full, $text, $utf8NoBom)

    $lineCount = ($text -split "\r?\n").Count
    "[WRITE OK] $(Format-DevRel $full) ($lineCount lines)"
}

function PATCH {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Path,
        [Parameter(Mandatory = $true, Position = 1)][string]$Old,
        [Parameter(Mandatory = $true, Position = 2)][string]$New
    )

    $full = Resolve-DevPath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "文件不存在: $Path" }

    $bytes = [System.IO.File]::ReadAllBytes($full)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)

    $reader = New-Object System.IO.StreamReader($full, [System.Text.Encoding]::UTF8, $true)
    try { $original = $reader.ReadToEnd() }
    finally { $reader.Dispose() }

    $newline = if ($original.Contains("`r`n")) { "`r`n" } else { "`n" }
    $normalized = $original.Replace("`r`n", "`n")
    $oldNorm = $Old.Replace("`r`n", "`n")
    $newNorm = $New.Replace("`r`n", "`n")

    if ([string]::IsNullOrEmpty($oldNorm)) { throw "旧片段不能为空" }

    $count = [regex]::Matches($normalized, [regex]::Escape($oldNorm)).Count
    if ($count -ne 1) {
        throw "PATCH 中止: 旧片段在 $(Format-DevRel $full) 中命中 $count 次（要求精确 1 次）"
    }

    $patched = $normalized.Replace($oldNorm, $newNorm).Replace("`n", $newline)
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    [System.IO.File]::WriteAllText($full, $patched, $encoding)

    $lineCount = ($patched -split "\r?\n").Count
    "[PATCH OK] $(Format-DevRel $full) ($lineCount lines)"
}

function MOVE {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$OldPath,
        [Parameter(Mandatory = $true, Position = 1)][string]$NewPath
    )

    $oldFull = Resolve-DevPath $OldPath
    $newFull = Resolve-DevPath $NewPath
    if (-not (Test-Path -LiteralPath $oldFull)) { throw "源文件不存在: $OldPath" }
    if (Test-Path -LiteralPath $newFull) { throw "目标已存在，拒绝覆盖: $NewPath" }

    $dir = Split-Path -Parent $newFull
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    Move-Item -LiteralPath $oldFull -Destination $newFull
    $metaNote = ''
    $oldMeta = $oldFull + '.meta'
    if (Test-Path -LiteralPath $oldMeta) {
        Move-Item -LiteralPath $oldMeta -Destination ($newFull + '.meta')
        $metaNote = ' (+meta)'
    }

    "[MOVE OK] $(Format-DevRel $oldFull) -> $(Format-DevRel $newFull)$metaNote"
}

function DELETE {
    param([Parameter(Mandatory = $true, Position = 0)][string]$Path)

    $full = Resolve-DevPath $Path
    $meta = $full + '.meta'
    $note = ''

    if (Test-Path -LiteralPath $full -PathType Container) {
        Remove-Item -LiteralPath $full -Recurse
        $note = ' (dir)'
    }
    elseif (Test-Path -LiteralPath $full -PathType Leaf) {
        Remove-Item -LiteralPath $full
    }
    else {
        throw "路径不存在: $Path"
    }

    if (Test-Path -LiteralPath $meta) { Remove-Item -LiteralPath $meta; $note += ' (+meta)' }
    "[DELETE OK] $(Format-DevRel $full)$note"
}

function GIT {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)

    if (-not $GitArgs -or $GitArgs.Count -eq 0) { $GitArgs = @('status') }
    $root = Get-DevRoot
    $cmd = $GitArgs[0].ToLowerInvariant()

    switch ($cmd) {
        'status' {
            & git.exe -C $root status -s
        }
        'diff' {
            if ($GitArgs.Count -ge 2) {
                $rel = (Format-DevRel (Resolve-DevPath $GitArgs[1])).Replace('\', '/')
                & git.exe -C $root --no-pager diff -- $rel
            }
            else {
                & git.exe -C $root --no-pager diff
            }
        }
        'restore' {
            if ($GitArgs.Count -lt 2) { throw 'GIT restore 需要指定路径' }
            $rel = (Format-DevRel (Resolve-DevPath $GitArgs[1])).Replace('\', '/')
            & git.exe -C $root restore -- $rel
            "[GIT OK] restored $rel"
        }
        default {
            throw "GIT 仅支持 status / diff / restore，收到: $($GitArgs[0])"
        }
    }
}

function DIAG {
    param([ValidateSet('unity', 'build')][string]$Mode = 'unity')

    $root = Get-DevRoot
    if ($Mode -eq 'unity') {
        $log = Join-Path $env:LOCALAPPDATA 'Unity\Editor\Editor.log'
        if (-not (Test-Path -LiteralPath $log)) { throw "未找到 Editor.log: $log" }
        $tail = Get-Content -LiteralPath $log -Tail 800
        $hits = $tail | Select-String -Pattern 'error CS\d{4}', 'Exception', 'BuildFailedException', 'Compilation failed'
        if ($hits) {
            $hits | Select-Object -Last 80 | ForEach-Object { $_.Line }
        }
        else {
            '[DIAG] Editor.log 尾部未发现编译错误或异常'
        }
    }
    else {
        $csproj = Join-Path $root 'Assembly-CSharp.csproj'
        if (-not (Test-Path -LiteralPath $csproj)) { throw "未找到 $csproj" }
        & dotnet build $csproj --no-restore --nologo -v q 2>&1
    }
}
