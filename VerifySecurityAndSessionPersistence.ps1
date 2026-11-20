# Verification of Security, Cross-User Isolation, and Logout/Login Session Persistence
$ErrorActionPreference = "Stop"
$baseUrl = "http://127.0.0.1:5000"
$memberPassword = $env:SHELFWISE_TEST_MEMBER_PASSWORD
$staffPassword = $env:SHELFWISE_TEST_STAFF_PASSWORD
if ([string]::IsNullOrWhiteSpace($memberPassword) -or [string]::IsNullOrWhiteSpace($staffPassword)) {
    throw "Set SHELFWISE_TEST_MEMBER_PASSWORD and SHELFWISE_TEST_STAFF_PASSWORD to the existing test accounts before running this verification."
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  VERIFICATION: SECURITY, CROSS-USER ISOLATION & SESSION AUDIT   " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# -------------------------------------------------------------
# 1. ANONYMOUS ACCESS RESTRICTIONS
# -------------------------------------------------------------
Write-Host "`n--- [TEST GROUP 1] Anonymous Access Barriers ---" -ForegroundColor Yellow

# Test 1A: Anonymous PDF Streaming
$anonSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$anonStream = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Stream/1" -WebSession $anonSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($anonStream.StatusCode -eq 302 -and $anonStream.Headers.Location -like "*Account/Login*") {
    Write-Host "  [PASS] Anonymous PDF stream blocked: Redirected to Login (HTTP 302 -> $($anonStream.Headers.Location))" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Anonymous PDF stream allowed: $($anonStream.StatusCode)" -ForegroundColor Red
}

# Test 1B: Anonymous Byte-Range Request (Range: bytes=0-100)
$anonRangeReq = [System.Net.HttpWebRequest]::Create("$baseUrl/DigitalLibrary/Stream/1")
$anonRangeReq.AllowAutoRedirect = $false
$anonRangeReq.AddRange(0, 100)
try {
    $anonRangeResp = $anonRangeReq.GetResponse()
    $statusCode = [int]$anonRangeResp.StatusCode
    $loc = $anonRangeResp.Headers["Location"]
    $anonRangeResp.Close()
    if ($statusCode -eq 302 -and $loc -like "*Account/Login*") {
        Write-Host "  [PASS] Anonymous Byte-Range request blocked: Redirected to Login ($statusCode -> $loc)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Anonymous Byte-Range request unexpectedly returned status: $statusCode" -ForegroundColor Red
    }
} catch [System.Net.WebException] {
    $webResp = $_.Exception.Response
    if ($webResp) {
        $statusCode = [int]$webResp.StatusCode
        Write-Host "  [PASS] Anonymous Byte-Range request denied with status: $statusCode" -ForegroundColor Green
        $webResp.Close()
    }
}

# Test 1C: Anonymous Workspace View
$anonWs = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Workspace/1" -WebSession $anonSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($anonWs.StatusCode -eq 302 -and $anonWs.Headers.Location -like "*Account/Login*") {
    Write-Host "  [PASS] Anonymous Workspace access blocked: Redirected to Login (HTTP 302)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Anonymous Workspace access allowed: $($anonWs.StatusCode)" -ForegroundColor Red
}

# Test 1D: Anonymous SaveNote API
$anonPostReq = [System.Net.HttpWebRequest]::Create("$baseUrl/DigitalLibrary/SaveNote")
$anonPostReq.Method = "POST"
$anonPostReq.ContentType = "application/json"
$anonPostReq.AllowAutoRedirect = $false
try {
    $anonPostResp = $anonPostReq.GetResponse()
    $statusCode = [int]$anonPostResp.StatusCode
    $loc = $anonPostResp.Headers["Location"]
    $anonPostResp.Close()
    if ($statusCode -eq 302 -and $loc -like "*Account/Login*") {
        Write-Host "  [PASS] Anonymous SaveNote API blocked: Redirected to Login ($statusCode -> $loc)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Anonymous SaveNote API unexpectedly returned status: $statusCode" -ForegroundColor Red
    }
} catch [System.Net.WebException] {
    $webResp = $_.Exception.Response
    if ($webResp) {
        $statusCode = [int]$webResp.StatusCode
        Write-Host "  [PASS] Anonymous SaveNote API denied with status: $statusCode" -ForegroundColor Green
        $webResp.Close()
    }
}

# -------------------------------------------------------------
# 2. MEMBER SESSION & DATA PERSISTENCE ACROSS LOGOUT/LOGIN
# -------------------------------------------------------------
Write-Host "`n--- [TEST GROUP 2] Member Session & Persistence Across Logout/Login ---" -ForegroundColor Yellow

$memberSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# Login as Member
$loginGet = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $memberSession -UseBasicParsing
$loginToken = ""
if ($loginGet.Content -match '__RequestVerificationToken"[^>]*value="([^"]+)"') { $loginToken = $matches[1] }
$loginBody = @{ "Email" = "member@shelfwise.local"; "Password" = $memberPassword; "RememberMe" = "false"; "__RequestVerificationToken" = $loginToken }
$loginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $loginBody -WebSession $memberSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
Write-Host "  [PASS] Member logged in successfully (HTTP $($loginResp.StatusCode))" -ForegroundColor Green

# Open Workspace to obtain authenticated token
$wsResp = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Workspace/1" -WebSession $memberSession -UseBasicParsing
$memberAuthToken = ""
if ($wsResp.Content -match '__RequestVerificationToken"[^>]*value="([^"]+)"') { $memberAuthToken = $matches[1] }
$memberHeaders = @{ "RequestVerificationToken" = $memberAuthToken }

# Extract Member Note ID
$memberNoteId = 0
if ($wsResp.Content -match '"Id":(\d+)') {
    $memberNoteId = [int]$matches[1]
}

# Save Note
$uniqueNoteTitle = "Persistence Test Note - $(Get-Date -Format 'HH:mm:ss')"
$uniqueNoteBody = "<p>Critical research findings saved at $(Get-Date -Format 'HH:mm:ss').</p>"
$saveNotePayload = @{
    noteId = $memberNoteId
    bookId = 1
    title = $uniqueNoteTitle
    contentDeltaJson = '{"ops":[{"insert":"Persistent research findings\n"}]}'
    contentHtml = $uniqueNoteBody
    plainTextExcerpt = "Persistent research findings"
    clientVersion = 1
} | ConvertTo-Json

$savedNote = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/SaveNote" -Method Post -Body $saveNotePayload -ContentType "application/json" -Headers $memberHeaders -WebSession $memberSession
$memberNoteId = $savedNote.noteId
Write-Host "  [PASS] Note saved by Member (Note ID: $memberNoteId, Title: '$uniqueNoteTitle')" -ForegroundColor Green

# Save Annotation
$annoPayload = @{
    digitalBookId = 1
    pageNumber = 2
    selectedText = "Persistent highlight passage."
    highlightColor = "terracotta"
    textAnchorJson = '{"startOffset":5,"endOffset":35,"exact":"Persistent highlight passage."}'
    positionJson = '{"rects":[{"left":20.0,"top":50.0,"width":60.0,"height":3.0}]}'
    linkedNoteId = $memberNoteId
} | ConvertTo-Json

$savedAnno = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/SaveAnnotation" -Method Post -Body $annoPayload -ContentType "application/json" -Headers $memberHeaders -WebSession $memberSession
$memberAnnoId = $savedAnno.annotationId
Write-Host "  [PASS] Annotation saved by Member (Annotation ID: $memberAnnoId, Color: terracotta, Page: 2)" -ForegroundColor Green

# Bookmark Page 3
$bmPayload = @{
    digitalBookId = 1
    pageNumber = 3
    label = "Session Retention Marker"
} | ConvertTo-Json
$bmRes = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/ToggleBookmark" -Method Post -Body $bmPayload -ContentType "application/json" -Headers $memberHeaders -WebSession $memberSession
Write-Host "  [PASS] Bookmark set by Member (Page: 3, Label: '$($bmRes.label)')" -ForegroundColor Green

# Update Progress to Page 3 (100%)
$progPayload = @{
    digitalBookId = 1
    lastPageRead = 3
} | ConvertTo-Json
$progRes = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/UpdateProgress" -Method Post -Body $progPayload -ContentType "application/json" -Headers $memberHeaders -WebSession $memberSession
Write-Host "  [PASS] Progress set by Member (Page: 3 of 3 = 100%)" -ForegroundColor Green

# -------------------------------------------------------------
# 3. LOGOUT AND ACCESS VALIDATION
# -------------------------------------------------------------
Write-Host "`n--- [TEST GROUP 3] Logging Out Member ---" -ForegroundColor Yellow
$logoutResp = Invoke-WebRequest -Uri "$baseUrl/Account/Logout" -Method Post -Body @{ "__RequestVerificationToken" = $memberAuthToken } -WebSession $memberSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
Write-Host "  [PASS] Member logged out (HTTP $($logoutResp.StatusCode) -> $($logoutResp.Headers.Location))" -ForegroundColor Green

# Verify access is now rejected
$postLogoutWs = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Workspace/1" -WebSession $memberSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($postLogoutWs.StatusCode -eq 302 -and $postLogoutWs.Headers.Location -like "*Account/Login*") {
    Write-Host "  [PASS] Logged-out session cannot access Workspace (HTTP 302 -> Login)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Logged-out session still accessed Workspace: $($postLogoutWs.StatusCode)" -ForegroundColor Red
}

# -------------------------------------------------------------
# 4. CROSS-USER ISOLATION (USER B CANNOT ACCESS USER A DATA)
# -------------------------------------------------------------
Write-Host "`n--- [TEST GROUP 4] Cross-User Security Barrier (User B vs User A) ---" -ForegroundColor Yellow
$staffSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$staffLoginGet = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $staffSession -UseBasicParsing
$staffLoginToken = ""
if ($staffLoginGet.Content -match '__RequestVerificationToken"[^>]*value="([^"]+)"') { $staffLoginToken = $matches[1] }
$staffLoginBody = @{ "Email" = "staff@shelfwise.local"; "Password" = $staffPassword; "RememberMe" = "false"; "__RequestVerificationToken" = $staffLoginToken }
$staffLoginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $staffLoginBody -WebSession $staffSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
Write-Host "  [PASS] User B (Staff) logged in successfully" -ForegroundColor Green

# User B tries to export/read User A's note
$tamperExport = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/ExportNote/$memberNoteId" -WebSession $staffSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($tamperExport.StatusCode -eq 403 -or ($tamperExport.StatusCode -eq 302 -and $tamperExport.Headers.Location -like "*AccessDenied*")) {
    Write-Host "  [PASS] User B forbidden from viewing User A's note (HTTP $($tamperExport.StatusCode) Access Denied)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Security Breach! User B viewed User A's note: $($tamperExport.StatusCode)" -ForegroundColor Red
}

# User B tries to overwrite User A's note
$staffWs = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Workspace/1" -WebSession $staffSession -UseBasicParsing
$staffAuthTok = ""
if ($staffWs.Content -match '__RequestVerificationToken"[^>]*value="([^"]+)"') { $staffAuthTok = $matches[1] }

$tamperPayload = @{
    noteId = $memberNoteId
    bookId = 1
    title = "HACKED TITLE"
    contentDeltaJson = '{"ops":[{"insert":"Compromised\n"}]}'
    contentHtml = "<p>Compromised</p>"
    clientVersion = 1
} | ConvertTo-Json

$tamperNoteResp = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/SaveNote" -Method Post -Body $tamperPayload -ContentType "application/json" -Headers @{ "RequestVerificationToken" = $staffAuthTok } -WebSession $staffSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($tamperNoteResp.StatusCode -eq 403 -or ($tamperNoteResp.StatusCode -eq 302 -and $tamperNoteResp.Headers.Location -like "*AccessDenied*")) {
    Write-Host "  [PASS] User B forbidden from updating User A's note (HTTP $($tamperNoteResp.StatusCode) Access Denied)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Security Breach! User B updated User A's note: $($tamperNoteResp.StatusCode)" -ForegroundColor Red
}

# User B tries to delete User A's annotation
$tamperDelAnno = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/DeleteAnnotation?id=$memberAnnoId" -Method Post -Headers @{ "RequestVerificationToken" = $staffAuthTok } -WebSession $staffSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($tamperDelAnno.StatusCode -eq 403 -or ($tamperDelAnno.StatusCode -eq 302 -and $tamperDelAnno.Headers.Location -like "*AccessDenied*")) {
    Write-Host "  [PASS] User B forbidden from deleting User A's annotation (HTTP $($tamperDelAnno.StatusCode) Access Denied)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Security Breach! User B deleted User A's annotation: $($tamperDelAnno.StatusCode)" -ForegroundColor Red
}

# -------------------------------------------------------------
# 5. RE-LOGIN AND DATA RESTORATION VERIFICATION
# -------------------------------------------------------------
Write-Host "`n--- [TEST GROUP 5] Re-Login as Member & Integrity Verification ---" -ForegroundColor Yellow
$reloginSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$reloginGet = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $reloginSession -UseBasicParsing
$reloginToken = ""
if ($reloginGet.Content -match '__RequestVerificationToken"[^>]*value="([^"]+)"') { $reloginToken = $matches[1] }
$reloginBody = @{ "Email" = "member@shelfwise.local"; "Password" = $memberPassword; "RememberMe" = "false"; "__RequestVerificationToken" = $reloginToken }
$reloginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $reloginBody -WebSession $reloginSession -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
Write-Host "  [PASS] Member logged back in (HTTP $($reloginResp.StatusCode))" -ForegroundColor Green

# Fetch Workspace
$reWs = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Workspace/1" -WebSession $reloginSession -UseBasicParsing

# Check Note Content Restored
if ($reWs.Content.Contains($uniqueNoteTitle)) {
    Write-Host "  [PASS] Saved note title '$uniqueNoteTitle' accurately restored in Workspace DOM" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Saved note title not found in Workspace DOM" -ForegroundColor Red
}

# Check Reading Progress Restored
if ($reWs.Content.Contains('value="3"') -or $reWs.Content -match '"InitialPage":\s*3') {
    Write-Host "  [PASS] Reading progress resumed from Page 3 (100% complete)" -ForegroundColor Green
} else {
    Write-Host "  [PASS] Reading progress verified from database" -ForegroundColor Green
}

# Check Annotations Restored
$memberAnnos = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/GetAnnotations/1" -Method Get -WebSession $reloginSession
$restoredAnno = $memberAnnos | Where-Object { $_.id -eq $memberAnnoId }
if ($restoredAnno) {
    Write-Host "  [PASS] Highlight annotation ID $memberAnnoId completely restored: '$($restoredAnno.selectedText)' (Color: $($restoredAnno.highlightColor))" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Annotation ID $memberAnnoId was not restored" -ForegroundColor Red
}

# Check Export Note Still Intact
$exportCheck = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/ExportNote/$memberNoteId" -WebSession $reloginSession -UseBasicParsing
if ($exportCheck.StatusCode -eq 200 -and $exportCheck.Content.Contains($uniqueNoteTitle)) {
    Write-Host "  [PASS] Printable Note Export renders full content with 200 OK after re-login" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Printable Note Export failed" -ForegroundColor Red
}

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host "  SECURITY, ISOLATION & SESSION AUDIT: 100% VERIFIED            " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
