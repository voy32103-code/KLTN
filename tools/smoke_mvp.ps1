param(
    [string]$ApiBaseUrl = "http://localhost:5206",
    [string]$AiBaseUrl = "http://localhost:8000",
    [string]$Email = "",
    [string]$Password = "SmokeTest123!",
    [string]$Name = "MVP Smoke Student",
    [string]$ScenarioTitle = "University Course Registration System",
    [switch]$RunAiFlow
)

$ErrorActionPreference = "Stop"

if (-not $Email) {
    $Email = "mvp-smoke-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())@example.com"
}

$ApiBaseUrl = $ApiBaseUrl.TrimEnd("/")
$AiBaseUrl = $AiBaseUrl.TrimEnd("/")

$scenarioRequirements = @{
    "University Course Registration System" = 10
    "Hospital Appointment System" = 12
    "Small Business Inventory Management" = 9
}

$scenarioQuestions = @{
    "University Course Registration System" = @(
        "What is the main purpose of this registration system?",
        "Before a student registers, are there prerequisite or eligibility rules?",
        "Can unpaid fees block a student from registration?"
    )
    "Hospital Appointment System" = @(
        "What is the main goal of the appointment system?",
        "Should patients see available doctor time slots before confirming?",
        "What happens if a patient has urgent symptoms instead of a routine case?"
    )
    "Small Business Inventory Management" = @(
        "What is the main purpose of the inventory system?",
        "Should staff record stock-in and stock-out transactions?",
        "What happens if internet goes down temporarily during daily operations?"
    )
}

function Write-Step($Text) {
    Write-Host "[smoke] $Text"
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

Assert-True -Condition ($scenarioRequirements.ContainsKey($ScenarioTitle)) -Message "unsupported scenario title: $ScenarioTitle"
$expectedRequirementCount = $scenarioRequirements[$ScenarioTitle]

Write-Step "API base: $ApiBaseUrl"
Write-Step "Register smoke user $Email"
Invoke-ApiJson -Method "POST" -Path "/api/Auth/register" -Body @{
    name = $Name
    email = $Email
    password = $Password
} | Out-Null

Write-Step "Login smoke user"
$login = Invoke-ApiJson -Method "POST" -Path "/api/Auth/login" -Body @{
    email = $Email
    password = $Password
}
Assert-True -Condition ([string]::IsNullOrWhiteSpace($login.token) -eq $false) -Message "login should return JWT token"
$token = $login.token

Write-Step "Load active scenarios"
$scenarios = Invoke-ApiJson -Method "GET" -Path "/api/Scenarios" -Token $token
Assert-True -Condition ($scenarios.Count -gt 0) -Message "at least one active scenario should exist; run backend with SeedData__Enabled=true if empty"
Assert-True -Condition (@($scenarios | Where-Object { $_.title -eq "Hospital Appointment System" }).Count -ge 1) -Message "Hospital Appointment System scenario should exist"
Assert-True -Condition (@($scenarios | Where-Object { $_.title -eq "Small Business Inventory Management" }).Count -ge 1) -Message "Small Business Inventory Management scenario should exist"

$scenario = $scenarios | Where-Object { $_.title -eq $ScenarioTitle } | Select-Object -First 1
Assert-True -Condition ($null -ne $scenario) -Message "$ScenarioTitle scenario should exist"
Assert-True -Condition ($scenario.personaCount -ge 1) -Message "scenario should have at least one persona"
Assert-True -Condition ($scenario.requirementCount -ge $expectedRequirementCount) -Message "scenario should have at least $expectedRequirementCount hidden requirements"

Write-Step "Load scenario detail"
$detail = Invoke-ApiJson -Method "GET" -Path "/api/Scenarios/$($scenario.id)" -Token $token
Assert-True -Condition (@($detail.personas).Count -gt 0) -Message "scenario detail should include personas"
Assert-True -Condition ($detail.personaCount -ge 1) -Message "scenario detail should include personaCount"
Assert-True -Condition ($detail.requirementCount -ge $expectedRequirementCount) -Message "scenario detail should include requirementCount"

$persona = $detail.personas | Select-Object -First 1
Write-Step "Create session for scenario/persona"
$session = Invoke-ApiJson -Method "POST" -Path "/api/Sessions" -Token $token -Body @{
    scenarioId = $scenario.id
    personaId = $persona.id
}
Assert-True -Condition ([string]::IsNullOrWhiteSpace($session.id) -eq $false) -Message "create session should return session id"

if ($RunAiFlow) {
    Write-Step "Check AI service health at $AiBaseUrl/health"
    $health = Invoke-RestMethod -Method "GET" -Uri "$AiBaseUrl/health"
    Assert-True -Condition ($health.status -eq "ok") -Message "AI service health should be ok"

    $questions = $scenarioQuestions[$ScenarioTitle]

    foreach ($question in $questions) {
        Write-Step "Send chat message: $question"
        $chat = Invoke-ApiJson -Method "POST" -Path "/api/Sessions/$($session.id)/messages" -Token $token -Body @{
            content = $question
        }
        Assert-True -Condition ([string]::IsNullOrWhiteSpace($chat.reply) -eq $false) -Message "chat should return stakeholder reply"
        if ($null -ne $chat.stateUpdate -and $null -ne $chat.stateUpdate.newlyRevealed) {
            Assert-True -Condition ($chat.stateUpdate.newlyRevealed.Count -le 1) -Message "chat should reveal at most one new requirement per turn"
        }
    }

    Write-Step "End session and evaluate coverage"
    $evaluation = Invoke-ApiJson -Method "POST" -Path "/api/Sessions/$($session.id)/end" -Token $token
    Assert-True -Condition ($null -ne $evaluation.coverageScore) -Message "evaluation should include coverageScore"
    Assert-True -Condition ($evaluation.extractedCount -ge 0) -Message "evaluation should include extractedCount"
    Assert-True -Condition ($null -ne $evaluation.matches) -Message "evaluation should include requirement-level matches"
    Assert-True -Condition ($evaluation.matches.Count -ge $expectedRequirementCount) -Message "evaluation should include one report row per hidden requirement"
    Assert-True -Condition (($evaluation.matches | Where-Object { $_.reason }).Count -ge $expectedRequirementCount) -Message "evaluation match report should include reasons"
    Assert-True -Condition ($null -ne $evaluation.scoringPolicy) -Message "evaluation should include scoringPolicy for reproducibility"
    Assert-True -Condition ([string]::IsNullOrWhiteSpace($evaluation.scoringPolicy.preset) -eq $false) -Message "scoringPolicy should include preset"
    Write-Step "Coverage: $($evaluation.coverageScore)%"
} else {
    Write-Step "Skipped AI chat/evaluation flow. Re-run with -RunAiFlow when AI service and API keys are configured."
}

Write-Step "MVP smoke completed successfully."
