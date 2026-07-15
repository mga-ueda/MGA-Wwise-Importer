param([string[]]$Paths)

function Dump-Wav($path) {
    $fs = [System.IO.File]::OpenRead($path)
    $br = New-Object System.IO.BinaryReader($fs)
    [void]$br.ReadBytes(12)
    Write-Output "== $([System.IO.Path]::GetFileName($path)) (len=$($fs.Length))"
    while ($fs.Position + 8 -le $fs.Length) {
        $id = [System.Text.Encoding]::ASCII.GetString($br.ReadBytes(4))
        $csize = $br.ReadUInt32()
        $start = $fs.Position
        if ($id -eq "cue ") {
            $n = $br.ReadUInt32()
            for ($i = 0; $i -lt $n; $i++) {
                $cid = $br.ReadUInt32()
                $pos = $br.ReadUInt32()
                [void]$br.ReadBytes(16)
                Write-Output "   cue#$cid pos=$pos"
            }
        }
        elseif ($id -eq "LIST") {
            $listType = [System.Text.Encoding]::ASCII.GetString($br.ReadBytes(4))
            $end = $start + $csize
            while ($fs.Position + 8 -le $end) {
                $sid = [System.Text.Encoding]::ASCII.GetString($br.ReadBytes(4))
                $ssize = $br.ReadUInt32()
                $sstart = $fs.Position
                if ($sid -eq "labl" -or $sid -eq "note") {
                    $cid = $br.ReadUInt32()
                    $txt = [System.Text.Encoding]::GetEncoding(932).GetString($br.ReadBytes([int]$ssize - 4)).TrimEnd([char]0)
                    Write-Output "   $sid id=$cid `"$txt`""
                }
                elseif ($sid -eq "ltxt") {
                    $cid = $br.ReadUInt32()
                    $slen = $br.ReadUInt32()
                    [void]$br.ReadBytes(12)
                    $txt = [System.Text.Encoding]::GetEncoding(932).GetString($br.ReadBytes([int]$ssize - 20)).TrimEnd([char]0)
                    Write-Output "   ltxt id=$cid len=$slen `"$txt`""
                }
                $fs.Position = $sstart + $ssize + ($ssize % 2)
            }
        }
        elseif ($id -eq "data") {
            Write-Output "   data size=$csize frames=$($csize / 15)"
        }
        $fs.Position = $start + $csize + ($csize % 2)
    }
    $br.Close()
    $fs.Close()
}

foreach ($p in $Paths) { Dump-Wav $p }
