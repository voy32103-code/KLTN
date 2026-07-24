[Diagnostics.CodeAnalysis.SuppressMessageAttribute("PSAvoidUsingPlainTextForPassword", "")]
param(
    [string]$ApiBaseUrl = "http://localhost:5206",
    [string]$Email = "admin-smoke@example.com",
    [string]$Password = "AdminPassword123!",
    [string]$Name = "Smoke Admin"
)

$ErrorActionPreference = "Stop"
$ApiBaseUrl = $ApiBaseUrl.TrimEnd("/")

function Write-Step($Text) {
    Write-Host "[smoke-admin] $Text" -ForegroundColor Cyan
}

function Invoke-ApiJson {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = ""
    )

    $headers = @{ Accept = "application/json" }
    if ($Token) {
        $headers.Authorization = "Bearer $Token"
    }

    $params = @{
        Method = $Method
        Uri = "$ApiBaseUrl$Path"
        Headers = $headers
    }

    if ($null -ne $Body) {
        $headers["Content-Type"] = "application/json"
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }

    Invoke-RestMethod @params
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

Write-Step "1. Registering test admin user..."
try {
    $register = Invoke-ApiJson -Method "POST" -Path "/api/Auth/register-admin" -Body @{
        name = $Name
        email = $Email
        password = $Password
    }
    Write-Step "Registered successfully! ID: $($register.id), Role: $($register.role)"
} catch {
    if ($_.Exception.Message -like "*409*" -or $_.Exception.Message -like "*Conflict*") {
        Write-Step "Admin user already exists. Proceeding to login..."
    } else {
        throw $_
    }
}

Write-Step "2. Logging in to get JWT Token..."
$login = Invoke-ApiJson -Method "POST" -Path "/api/Auth/login" -Body @{
    email = $Email
    password = $Password
}
Assert-True -Condition ([string]::IsNullOrWhiteSpace($login.token) -eq $false) -Message "Login failed, token not returned."
$token = $login.token
Write-Step "Login successful!"

Write-Step "3. Triggering Crawl Scenario API from local test-spec..."
$crawlBody = @{
    url = "http://localhost:8000/api/test-spec"
    selectedModel = "gemini-2.5-flash"
}
$crawlRes = Invoke-ApiJson -Method "POST" -Path "/api/AdminScenarios/crawl" -Token $token -Body $crawlBody

Write-Step "Crawl Result Message: $($crawlRes.message)"
Assert-True -Condition ($crawlRes.scenarioId -ne [Guid]::Empty) -Message "Crawl did not return a valid ScenarioId."
Write-Step "New Scenario ID: $($crawlRes.scenarioId)"
Write-Step "Extracted hidden requirements count: $($crawlRes.requirementsCount)"

Write-Step "4. Verifying new scenario is active in DB..."
$scenarios = Invoke-ApiJson -Method "GET" -Path "/api/Scenarios" -Token $token
$newScenario = $scenarios | Where-Object { $_.id -eq $crawlRes.scenarioId }

Assert-True -Condition ($null -ne $newScenario) -Message "New scenario not found in active list."
Write-Step "Found scenario in DB: $($newScenario.title) (Requirements: $($newScenario.requirementCount))"

Write-Step "SMOKE TEST ADMIN FLOW COMPLETED SUCCESSFULLY!"
