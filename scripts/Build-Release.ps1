param([switch]$SkipPackage)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solution = Join-Path $repo 'NativeHub.sln'
$project = Join-Path $repo 'NativeHub\NativeHub.csproj'
$buildRoot = [IO.Path]::GetFullPath((Join-Path $repo 'build'))
$payload = [IO.Path]::GetFullPath((Join-Path $buildRoot 'payload'))
$package = [IO.Path]::GetFullPath((Join-Path $buildRoot 'package'))

if (-not $buildRoot.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved build directory escaped the repository.'
}
foreach ($target in @($payload, $package)) {
    if (-not $target.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe build target: $target"
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}

dotnet restore $solution --runtime win-x64 --property:Platform=x64 --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
dotnet build $solution --configuration Release --no-restore --property:Platform=x64 --warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
dotnet test (Join-Path $repo 'NativeHub.Tests\NativeHub.Tests.csproj') --configuration Release --no-build --no-restore --property:Platform=x64 --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
dotnet publish $project --configuration Release --no-restore --runtime win-x64 --warnaserror --property:Platform=x64 --property:GenerateAppxPackageOnBuild=false --property:WindowsPackageType=None --property:WindowsAppSDKSelfContained=true --property:AppxPackage=false --property:PublishDir="$payload\" --property:SelfContained=false
if ($LASTEXITCODE -ne 0) { throw 'Payload publish failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $payload 'NativeHub.exe'))) { throw 'Published payload is missing NativeHub.exe.' }

if (-not $SkipPackage) {
    $pfx = Join-Path ([IO.Path]::GetTempPath()) ("NativeHub-Development-{0}.pfx" -f [Guid]::NewGuid().ToString('N'))
    $cer = Join-Path $package 'NativeHub.cer'
    $passwordText = [Guid]::NewGuid().ToString('N')
    $rsa = [System.Security.Cryptography.RSA]::Create(3072)
    $certificate = $null
    try {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=NativeHub Development', $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $usage = [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($usage, $true))
        $oids = [System.Security.Cryptography.OidCollection]::new()
        [void]$oids.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3', 'Code Signing'))
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $true))
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddYears(2))
        # Prefer legacy PKCS#12 PBE for compatibility with Windows signing tools.
        # Windows PowerShell 5 lacks ExportPkcs12, so use its legacy Export implementation.
        $exportPkcs12 = [System.Security.Cryptography.X509Certificates.X509Certificate2].GetMethods() |
            Where-Object { $_.Name -eq 'ExportPkcs12' -and $_.GetParameters()[0].ParameterType.IsEnum } |
            Select-Object -First 1
        if ($null -ne $exportPkcs12) {
            $pbeType = $exportPkcs12.GetParameters()[0].ParameterType
            $legacyPbe = [Enum]::Parse($pbeType, 'Pkcs12TripleDesSha1')
            $pfxBytes = $exportPkcs12.Invoke($certificate, @($legacyPbe, $passwordText))
        }
        else {
            $pfxBytes = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $passwordText)
        }
        [IO.File]::WriteAllBytes($pfx, $pfxBytes)
        [IO.File]::WriteAllBytes($cer, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

        dotnet publish $project --configuration Release --no-restore --runtime win-x64 --warnaserror --property:Platform=x64 --property:GenerateAppxPackageOnBuild=true --property:AppxBundle=Never --property:UapAppxPackageBuildMode=SideloadOnly --property:AppxPackageDir="$package\" --property:AppxPackageSigningEnabled=false --property:AppxSymbolPackageEnabled=false --property:DebugSymbols=false --property:DebugType=None
        if ($LASTEXITCODE -ne 0) { throw 'MSIX package build failed.' }

        $builtMsix = Get-ChildItem -LiteralPath $package -Recurse -File -Filter '*.msix' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -eq $builtMsix) { throw 'Packaging completed without producing an MSIX.' }
        $signTool = Get-ChildItem -Path (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\*\x64\signtool.exe') -File |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($null -eq $signTool) { throw 'Windows SDK SignTool was not found.' }
        & $signTool.FullName sign /fd SHA256 /f $pfx /p $passwordText $builtMsix.FullName
        if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }

        $signature = Get-AuthenticodeSignature -LiteralPath $builtMsix.FullName
        if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw 'MSIX signature validation failed.'
        }
    }
    finally {
        if ($null -ne $certificate) { $certificate.Dispose() }
        $rsa.Dispose()
        if (Test-Path -LiteralPath $pfx) { Remove-Item -LiteralPath $pfx -Force }
    }

    $msix = Get-ChildItem -LiteralPath $package -Recurse -File -Filter '*.msix' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $normalizedMsix = Join-Path $package 'NativeHub.msix'
    if ($msix.FullName -ne $normalizedMsix) { Copy-Item -LiteralPath $msix.FullName -Destination $normalizedMsix -Force }
}

Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination (Join-Path $package 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $repo 'scripts\Install-NativeHub.ps1') -Destination (Join-Path $package 'Install.ps1') -Force
Get-ChildItem -LiteralPath $package -File | Where-Object Extension -in @('.msix', '.cer') | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding ascii
Write-Host "NativeHub build ready at $buildRoot"
