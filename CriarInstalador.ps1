param([string]$CompiladorInno = 'ISCC.exe')

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $compiler = Get-Command $CompiladorInno -ErrorAction SilentlyContinue
    if (!$compiler -and $CompiladorInno -eq 'ISCC.exe') {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate) {
                $compiler = Get-Command $candidate
                break
            }
        }
    }
    if (!$compiler) { throw 'Inno Setup nao encontrado. Informe o caminho de ISCC.exe em -CompiladorInno.' }
    # O proprio .iss publica a Release e interrompe a compilacao se ela falhar.
    & $compiler.Source (Join-Path $PSScriptRoot 'InstaladorControleFerias.iss')
    if ($LASTEXITCODE -ne 0) { throw 'A compilação do instalador falhou.' }
}
finally {
    Pop-Location
}
