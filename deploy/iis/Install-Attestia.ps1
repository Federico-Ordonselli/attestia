#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [string] $InstallRoot = 'C:\inetpub\attestia',
    [ValidatePattern('^[\w -]+$')][string] $SiteName = 'Attestia',
    [ValidatePattern('^[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?$')][string] $HostName = 'attestia.local',
    [ValidatePattern('^[\w.\\,():-]+$')][string] $SqlServer = '.',
    [ValidatePattern('^[\w -]+$')][string] $Database = 'Attestia',
    [SecureString] $SsoKey,
    # Metadata dell'IdP SAML (Keycloak: /realms/attestia/protocol/saml/descriptor). In produzione: HTTPS.
    [ValidatePattern('^https?://')][string] $IdpMetadataUrl = 'http://localhost:8081/realms/attestia/protocol/saml/descriptor',
    # SQL Server con certificato autofirmato (tipico in sviluppo): SqlClient e MSOLEDBSQL 19 cifrano per default
    # e rifiuterebbero il certificato. Mai in produzione con un certificato valido.
    [switch] $TrustSqlServerCertificate,
    [switch] $ConfigureSqlLogins,
    [switch] $TrustDevelopmentCertificate
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ValidatedKey {
    param([SecureString] $Secret)
    $encoded = $env:ATTESTIA_SSO_KEY
    if ($null -ne $Secret) {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret)
        try { $encoded = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    }
    try { $bytes = [Convert]::FromBase64String($encoded) }
    catch { throw 'ATTESTIA_SSO_KEY deve contenere una chiave in base64.' }
    try {
        if ($bytes.Length -lt 32) { throw 'La chiave SSO deve contenere almeno 32 byte.' }
        return [Convert]::ToBase64String($bytes)
    }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Set-IisValue {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Chiamata solo dentro blocchi gia protetti da $PSCmdlet.ShouldProcess')]
    param($Element, $Name, $Value)
    if ($Element.GetAttributeValue($Name) -cne $Value) {
        $Element.SetAttributeValue($Name, $Value)
        $script:configChanged = $true
    }
}

function Set-PoolEnvironment {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Chiamata solo dentro blocchi gia protetti da $PSCmdlet.ShouldProcess')]
    param($Pool, $Values)
    $variables = $Pool.GetCollection('environmentVariables')
    foreach ($name in $Values.Keys) {
        $entry = $variables | Where-Object { $_.GetAttributeValue('name') -eq $name } | Select-Object -First 1
        if ($null -eq $entry) {
            $entry = $variables.CreateElement('add')
            $entry.SetAttributeValue('name', $name)
            [void]$variables.Add($entry)
            $script:configChanged = $true
        }
        Set-IisValue $entry 'value' $Values[$name]
    }
}

function Set-FolderPermission {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Chiamata solo dentro blocchi gia protetti da $PSCmdlet.ShouldProcess')]
    param($Path, $PoolName)
    # Le ACL alla radice di ogni applicazione contengono solo i due amministratori
    # di sistema (SYSTEM e Administrators) e il suo pool in lettura/esecuzione.
    $acl = Get-Acl -LiteralPath $Path
    $original = $acl.Sddl
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { $acl.RemoveAccessRuleSpecific($rule) }
    $rules = [ordered]@{
        'S-1-5-18' = 'FullControl'
        'S-1-5-32-544' = 'FullControl'
    }
    $account = [System.Security.Principal.NTAccount]::new("IIS AppPool\$PoolName")
    $poolSid = $account.Translate([System.Security.Principal.SecurityIdentifier])
    $rules[$poolSid.Value] = 'ReadAndExecute'
    foreach ($sid in $rules.Keys) {
        $identity = [System.Security.Principal.SecurityIdentifier]::new($sid)
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity, $rules[$sid], 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    if ($acl.Sddl -ne $original) { Set-Acl -LiteralPath $Path -AclObject $acl }
    # Anche gli elementi preesistenti devono ereditare esclusivamente questi permessi.
    foreach ($child in Get-ChildItem -LiteralPath $Path -Force -Recurse) {
        $childAcl = Get-Acl -LiteralPath $child.FullName
        $explicit = @($childAcl.Access | Where-Object { -not $_.IsInherited })
        if ($childAcl.AreAccessRulesProtected -or $explicit.Count) {
            $childAcl.SetAccessRuleProtection($false, $false)
            foreach ($rule in $explicit) { $childAcl.RemoveAccessRuleSpecific($rule) }
            Set-Acl -LiteralPath $child.FullName -AclObject $childAcl
        }
    }
}

function Sync-PackageFolder($Source, $Destination, $ApplicationName) {
    $options = @('/MIR', '/XJ', '/COPY:DAT', '/R:10', '/W:2', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    # Il segnale di manutenzione rimane fino al completamento del mirror.
    if ($ApplicationName -eq 'api') { $options += @('/XF', 'app_offline.htm') }
    $offline = Join-Path $Destination 'app_offline.htm'
    $markerText = '<!-- Attestia --> Aggiornamento in corso.'
    $isApi = $ApplicationName -eq 'api'
    $managedMarker = $isApi -and (Test-Path -LiteralPath $offline) -and
        ([IO.File]::ReadAllText($offline) -ceq $markerText)
    if (Test-Path -LiteralPath $Destination) {
        & robocopy.exe $Source $Destination @options /L | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Verifica del pacchetto $ApplicationName fallita." }
        if ($LASTEXITCODE -eq 0) {
            if ($managedMarker) { Remove-Item -LiteralPath $offline }
            return
        }
    } else {
        [void][IO.Directory]::CreateDirectory($Destination)
    }
    if ($isApi -and !(Test-Path -LiteralPath $offline)) {
        [IO.File]::WriteAllText($offline, $markerText)
        $managedMarker = $true
    }
    & robocopy.exe $Source $Destination @options | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copia di $ApplicationName fallita. Ripetere l'installazione." }
    if ($managedMarker) { Remove-Item -LiteralPath $offline }
}

if (![Environment]::Is64BitProcess) { throw 'Avviare Windows PowerShell a 64 bit.' }
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).ProviderPath.TrimEnd('\')
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
if ($InstallRoot.Length -le 3 -or
    ($InstallRoot + '\').StartsWith($PackagePath + '\', 'OrdinalIgnoreCase') -or
    ($PackagePath + '\').StartsWith($InstallRoot + '\', 'OrdinalIgnoreCase')) {
    throw 'Scegliere due cartelle dedicate e disgiunte per pacchetto e destinazione.'
}
foreach ($file in 'api\web.config','api\Attestia.Api.dll','portale\web.config','portale\default.asp','admin\web.config') {
    if (!(Test-Path -LiteralPath (Join-Path $PackagePath $file) -PathType Leaf)) { throw "Pacchetto incompleto. Manca $file." }
}
if (!(Test-Path -LiteralPath (Join-Path $PackagePath 'admin\index.html'))) {
    Write-Warning 'Il pacchetto non contiene ancora la compilazione Angular.'
}
$keyValue = Get-ValidatedKey $SsoKey

$features = [ordered]@{
    'IIS-WebServerRole' = 'Web-Server'
    'IIS-WebServer' = 'Web-WebServer'
    'IIS-ASP' = 'Web-ASP'
    'IIS-ISAPIExtensions' = 'Web-ISAPI-Ext'
    'IIS-RequestFiltering' = 'Web-Filtering'
    'IIS-HttpLogging' = 'Web-Http-Logging'
    'IIS-StaticContent' = 'Web-Static-Content'
    'IIS-DefaultDocument' = 'Web-Default-Doc'
    'IIS-HttpErrors' = 'Web-Http-Errors'
    'IIS-ManagementConsole' = 'Web-Mgmt-Console'
}
$isServer = (Get-CimInstance Win32_OperatingSystem).ProductType -ne 1
if ($isServer) {
    $missing = @(Get-WindowsFeature -Name @($features.Values) | Where-Object { !$_.Installed } | ForEach-Object Name)
    if ($missing.Count -and $PSCmdlet.ShouldProcess(($missing -join ', '), 'Installare i ruoli IIS')) {
        $result = Install-WindowsFeature -Name $missing
        if (!$result.Success) { throw 'Installazione dei ruoli IIS non riuscita.' }
        if ($result.RestartNeeded -eq 'Yes') { throw 'Riavviare Windows, poi ripetere il comando.' }
    }
} else {
    $missing = @(Get-WindowsOptionalFeature -Online | Where-Object {
        $features.Contains($_.FeatureName) -and $_.State -ne 'Enabled'
    } | ForEach-Object FeatureName)
    if ($missing.Count -and $PSCmdlet.ShouldProcess(($missing -join ', '), 'Abilitare le funzionalità IIS')) {
        $result = Enable-WindowsOptionalFeature -Online -FeatureName $missing -All -NoRestart
        if ($result.RestartNeeded) { throw 'Riavviare Windows, poi ripetere il comando.' }
    }
}
$modulePath = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$hasRuntime = $dotnet -and ((& $dotnet.Source --list-runtimes) -match '^Microsoft\.AspNetCore\.App 10\.')
if (!(Test-Path -LiteralPath $modulePath) -or !$hasRuntime) {
    throw 'Installare o riparare il bundle di hosting ASP.NET Core 10 dopo IIS: https://dotnet.microsoft.com/download/dotnet/10.0'
}
if (!(Test-Path -LiteralPath 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite')) {
    throw 'Installare URL Rewrite 2.1 per IIS: https://www.iis.net/downloads/microsoft/url-rewrite'
}
if (!(Test-Path -LiteralPath 'Registry::HKEY_CLASSES_ROOT\MSOLEDBSQL19\CLSID')) {
    Write-Warning 'Provider MSOLEDBSQL 19 a 64 bit non rilevato. Installarlo prima di usare il portale.'
}

$certificateName = "Attestia sviluppo ($HostName)"
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
    $_.FriendlyName -eq $certificateName -and $_.Subject -eq "CN=$HostName" -and $_.Issuer -eq $_.Subject -and
    $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date)
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if (!$cert -and $PSCmdlet.ShouldProcess($HostName, 'Creare il certificato autofirmato di sviluppo')) {
    $cert = New-SelfSignedCertificate -DnsName $HostName -FriendlyName $certificateName -CertStoreLocation Cert:\LocalMachine\My
}
if ($TrustDevelopmentCertificate) {
    Write-Warning 'Solo per sviluppo: il certificato del sito sarà considerato attendibile da questa macchina.'
    if ((!$cert -or !(Test-Path "Cert:\LocalMachine\Root\$($cert.Thumbprint)")) -and
        $PSCmdlet.ShouldProcess('LocalMachine\Root', 'Aggiungere il certificato pubblico di sviluppo')) {
        if (!$cert) { throw 'Certificato di sviluppo assente.' }
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', 'LocalMachine')
        try {
            $store.Open('ReadWrite')
            $store.Add([System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cert.RawData))
        } finally { $store.Close() }
    }
}

$apps = [ordered]@{ api = 'AttestiaApi'; portale = 'AttestiaPortale'; admin = 'AttestiaAdmin' }
$apiDb = "Server=$SqlServer;Database=$Database;Integrated Security=True;Encrypt=True;"
# DataTypeCompatibility=80: le colonne DATE arrivano ad ADO come stringhe ISO (gestite da IsoDate in helpers.asp).
$portalDb = "Provider=MSOLEDBSQL19;Server=$SqlServer;Database=$Database;Integrated Security=SSPI;DataTypeCompatibility=80;"
if ($TrustSqlServerCertificate) {
    Write-Warning 'Solo per sviluppo: il certificato di SQL Server non sarà verificato.'
    $apiDb += 'TrustServerCertificate=True;'
    $portalDb += 'TrustServerCertificate=Yes;'
}
$poolValues = @{
    api = @{ ASPNETCORE_ENVIRONMENT = 'Production'; ConnectionStrings__Attestia = $apiDb
             ATTESTIA_SSO_KEY = $keyValue; Saml__BaseUrl = "https://$HostName"; Saml__IdpMetadataUrl = $IdpMetadataUrl }
    portale = @{ ATTESTIA_SSO_KEY = $keyValue; ATTESTIA_PORTAL_DB = $portalDb }
    admin = @{}
}
$assembly = Join-Path $env:windir 'System32\inetsrv\Microsoft.Web.Administration.dll'
if ($PSCmdlet.ShouldProcess(($apps.Values -join ', '), 'Configurare pool isolati e variabili di processo')) {
    Add-Type -Path $assembly
    $manager = [Microsoft.Web.Administration.ServerManager]::new()
    try {
        $script:configChanged = $false
        foreach ($app in $apps.Keys) {
            $pool = $manager.ApplicationPools[$apps[$app]]
            if ($null -eq $pool) {
                $pool = $manager.ApplicationPools.Add($apps[$app])
                $script:configChanged = $true
            }
            # Il portale usa via COM le classi .NET Framework (HMACSHA256, UTF8Encoding): il pool carica il CLR v4.
            # API e back-office non usano il CLR di .NET Framework (ASP.NET Core in-process e file statici).
            Set-IisValue $pool 'managedRuntimeVersion' $(if ($app -eq 'portale') { 'v4.0' } else { '' })
            Set-IisValue $pool 'enable32BitAppOnWin64' $false
            if ($pool.ProcessModel.IdentityType -ne 'ApplicationPoolIdentity') {
                $pool.ProcessModel.IdentityType = 'ApplicationPoolIdentity'
                $script:configChanged = $true
            }
            Set-PoolEnvironment $pool $poolValues[$app]
        }
        if ($script:configChanged) { $manager.CommitChanges() }
    } catch {
        throw 'Configurazione dei pool fallita. Verificare lo schema IIS 10 e i permessi amministrativi.'
    } finally { $manager.Dispose() }
}
foreach ($app in $apps.Keys) {
    $source = Join-Path $PackagePath $app
    $target = Join-Path $InstallRoot $app
    if ($PSCmdlet.ShouldProcess($target, 'Sincronizzare i file del pacchetto con robocopy /MIR')) {
        Sync-PackageFolder $source $target $app
    }
    if ($PSCmdlet.ShouldProcess($target, "Applicare le ACL di sola lettura per $($apps[$app])")) {
        Set-FolderPermission $target $apps[$app]
    }
}

if ($PSCmdlet.ShouldProcess($SiteName, "Configurare le applicazioni, ASP, HTTP e HTTPS con SNI per $HostName")) {
    if (!$cert) { throw 'Certificato per il binding HTTPS assente.' }
    Add-Type -Path $assembly
    $manager = [Microsoft.Web.Administration.ServerManager]::new()
    try {
        $script:configChanged = $false
        $apiPath = Join-Path $InstallRoot 'api'
        $httpInfo = '*:80:' + $HostName
        $httpsInfo = '*:443:' + $HostName
        $site = $manager.Sites[$SiteName]
        if ($null -eq $site) {
            $site = $manager.Sites.Add($SiteName, 'http', $httpInfo, $apiPath)
            $script:configChanged = $true
        }
        foreach ($app in $apps.Keys) {
            $url = if ($app -eq 'api') { '/' } else { "/$app" }
            $physicalPath = Join-Path $InstallRoot $app
            $application = $site.Applications[$url]
            if ($null -eq $application) {
                $application = $site.Applications.Add($url, $physicalPath)
                $script:configChanged = $true
            }
            Set-IisValue $application 'applicationPool' $apps[$app]
            Set-IisValue $application.VirtualDirectories['/'] 'physicalPath' $physicalPath
        }
        if (!($site.Bindings | Where-Object { $_.Protocol -eq 'http' -and $_.BindingInformation -eq $httpInfo })) {
            [void]$site.Bindings.Add($httpInfo, 'http')
            $script:configChanged = $true
        }
        $binding = $site.Bindings | Where-Object { $_.Protocol -eq 'https' -and $_.BindingInformation -eq $httpsInfo } | Select-Object -First 1
        if (!$binding) {
            [void]$site.Bindings.Add($httpsInfo, $cert.GetCertHash(), 'My', [Microsoft.Web.Administration.SslFlags]::Sni)
            $script:configChanged = $true
        } else {
            if ($binding.SslFlags -ne 'Sni' -or $binding.CertificateStoreName -ne 'My' -or
                [Convert]::ToBase64String([byte[]]@($binding.CertificateHash)) -ne [Convert]::ToBase64String($cert.GetCertHash())) {
                $binding.SslFlags = 'Sni'
                $binding.CertificateStoreName = 'My'
                $binding.CertificateHash = $cert.GetCertHash()
                $script:configChanged = $true
            }
        }
        $config = $manager.GetApplicationHostConfiguration()
        $anonymous = $config.GetSection('system.webServer/security/authentication/anonymousAuthentication', $SiteName)
        Set-IisValue $anonymous 'enabled' $true
        Set-IisValue $anonymous 'userName' ''
        $asp = $config.GetSection('system.webServer/asp', "$SiteName/portale")
        Set-IisValue $asp 'scriptErrorSentToBrowser' $false
        Set-IisValue $asp 'scriptErrorMessage' 'Si è verificato un errore. Riprova più tardi.'
        Set-IisValue $asp 'enableParentPaths' $false
        Set-IisValue $asp 'codePage' ([uint32]65001)
        Set-IisValue $asp 'bufferingOn' $true
        Set-IisValue ($asp.GetChildElement('session')) 'allowSessionState' $false
        if ($script:configChanged) { $manager.CommitChanges() }
    } finally { $manager.Dispose() }
}

if ($ConfigureSqlLogins) {
    $sql = @"
USE [$Database];
SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID('attestia_api') IS NULL OR DATABASE_PRINCIPAL_ID('attestia_portal') IS NULL
    THROW 50000, 'Applicare prima le migrazioni con un account proprietario.', 1;
BEGIN TRANSACTION;
"@
    foreach ($item in @{ AttestiaApi = 'attestia_api'; AttestiaPortale = 'attestia_portal' }.GetEnumerator()) {
        $login = 'IIS APPPOOL\' + $item.Key
        $role = $item.Value
        $sql += @"

IF SUSER_ID(N'$login') IS NULL CREATE LOGIN [$login] FROM WINDOWS;
IF DATABASE_PRINCIPAL_ID(N'$login') IS NULL CREATE USER [$login] FOR LOGIN [$login];
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members
               WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'$role')
                 AND member_principal_id = DATABASE_PRINCIPAL_ID(N'$login'))
    ALTER ROLE [$role] ADD MEMBER [$login];
"@
    }
    $sql += [Environment]::NewLine + 'COMMIT;'
    if (Get-Module -ListAvailable -Name SqlServer) {
        if ($PSCmdlet.ShouldProcess("$SqlServer / $Database", 'Associare le identità IIS ai ruoli SQL')) {
            SqlServer\Invoke-Sqlcmd -ServerInstance $SqlServer -Database master -Query $sql -AbortOnError -DisableVariables
        }
    } else {
        $sqlFile = Join-Path $PSScriptRoot 'Configure-SqlLogins.sql'
        if ($PSCmdlet.ShouldProcess($sqlFile, 'Scrivere lo script SQL dei permessi')) {
            [IO.File]::WriteAllText($sqlFile, $sql, [Text.UTF8Encoding]::new($true))
            Write-Host "Eseguire $sqlFile con un amministratore di SQL Server dopo le migrazioni."
        }
    }
}
if ($WhatIfPreference) { Write-Host 'Simulazione terminata.' }
else { Write-Host "Sito $SiteName configurato su https://$HostName/ con API, portale e back-office in pool distinti." }
Write-Host 'Applicare le migrazioni una volta con un account proprietario: dalla cartella api eseguire dotnet Attestia.Api.dll migrate.'
