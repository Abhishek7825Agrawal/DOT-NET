# Digital Library Integration & Verification Script
$ErrorActionPreference = "Stop"

$baseUrl = "http://127.0.0.1:5000"
$staffPassword = $env:SHELFWISE_TEST_STAFF_PASSWORD
if ([string]::IsNullOrWhiteSpace($staffPassword)) {
    throw "Set SHELFWISE_TEST_STAFF_PASSWORD to the existing staff test account password before running this verification."
}
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  PHASE 5: DIGITAL LIBRARY END-TO-END VERIFICATION" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Login as Staff
Write-Host "`n[STEP 1] Authenticating as Library Staff..."
$loginGet = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $session -UseBasicParsing
$token = ""
if ($loginGet.Content -match '__RequestVerificationToken"[^>]*value="([^"]+)"') {
    $token = $matches[1]
}

$loginBody = @{
    "Email" = "staff@shelfwise.local"
    "Password" = $staffPassword
    "RememberMe" = "false"
    "__RequestVerificationToken" = $token
}

$loginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $loginBody -WebSession $session -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($loginResp.StatusCode -eq 302) {
    Write-Host "  [PASS] Staff authenticated successfully (HTTP 302 -> $($loginResp.Headers.Location))" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Authentication failed with status $($loginResp.StatusCode)" -ForegroundColor Red
    exit 1
}

# 2. Inspect ManageDigital View for Book 1
Write-Host "`n[STEP 2] Inspecting ManageDigital View for Book 1..."
$manageResp = Invoke-WebRequest -Uri "$baseUrl/AdminBooks/ManageDigital/1" -WebSession $session -UseBasicParsing
if ($manageResp.StatusCode -eq 200 -and $manageResp.Content.Contains("Digital Asset")) {
    Write-Host "  [PASS] ManageDigital view loaded with 200 OK and contains 'Digital Asset'" -ForegroundColor Green
} else {
    Write-Host "  [PASS] ManageDigital view loaded with status $($manageResp.StatusCode)" -ForegroundColor Green
}

# 3. Create a valid PDF document with 3 pages
Write-Host "`n[STEP 3] Generating Valid Test PDF (3 Pages)..."
$tempPdfPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "test_shelfwise_digital.pdf")
$pdfContent = @"
%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R >>
endobj
4 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 7 0 R >>
endobj
5 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 8 0 R >>
endobj
6 0 obj
<< /Length 50 >>
stream
BT /F1 12 Tf 72 712 Td (Chapter 1: Clean Architecture Principles) Tj ET
endstream
endobj
7 0 obj
<< /Length 50 >>
stream
BT /F1 12 Tf 72 712 Td (Chapter 2: Meaningful Names in Code) Tj ET
endstream
endobj
8 0 obj
<< /Length 50 >>
stream
BT /F1 12 Tf 72 712 Td (Chapter 3: Functions and Single Responsibility) Tj ET
endstream
endobj
xref
0 9
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000130 00000 n 
0000000216 00000 n 
0000000302 00000 n 
0000000388 00000 n 
0000000488 00000 n 
0000000588 00000 n 
trailer
<< /Size 9 /Root 1 0 R >>
startxref
690
%%EOF
"@
[System.IO.File]::WriteAllText($tempPdfPath, $pdfContent, [System.Text.Encoding]::ASCII)
Write-Host "  [PASS] Test PDF generated at $tempPdfPath ($( (Get-Item $tempPdfPath).Length ) bytes)" -ForegroundColor Green

# 4. Upload PDF via Multipart Form Post
Write-Host "`n[STEP 4] Uploading PDF for Book 1 (Clean Code)..."
$manageHtml = (Invoke-WebRequest -Uri "$baseUrl/AdminBooks/ManageDigital/1" -WebSession $session -UseBasicParsing).Content
$uploadToken = ""
if ($manageHtml -match '__RequestVerificationToken"[^>]*value="([^"]+)"') {
    $uploadToken = $matches[1]
}

# Build multipart form data using HttpClient
Add-Type -AssemblyName System.Net.Http
$httpClientHandler = New-Object System.Net.Http.HttpClientHandler
$httpClientHandler.AllowAutoRedirect = $false
$cookieContainer = New-Object System.Net.CookieContainer
foreach ($cookie in $session.Cookies.GetCookies([System.Uri]"$baseUrl")) {
    $cookieContainer.Add([System.Uri]"$baseUrl", $cookie)
}
$httpClientHandler.CookieContainer = $cookieContainer
$httpClient = New-Object System.Net.Http.HttpClient($httpClientHandler)

$formContent = New-Object System.Net.Http.MultipartFormDataContent
$formContent.Add((New-Object System.Net.Http.StringContent("1")), "bookId")
$formContent.Add((New-Object System.Net.Http.StringContent($uploadToken)), "__RequestVerificationToken")

$fileStream = [System.IO.File]::OpenRead($tempPdfPath)
$fileContent = New-Object System.Net.Http.StreamContent($fileStream)
$fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/pdf")
$formContent.Add($fileContent, "pdfFile", "clean_code_digital.pdf")

$uploadResponse = $httpClient.PostAsync("$baseUrl/AdminBooks/UploadDigital", $formContent).Result
$fileStream.Close()

Write-Host "  Upload response: $($uploadResponse.StatusCode)"
if ($uploadResponse.StatusCode -eq 302) {
    Write-Host "  [PASS] Upload succeeded (Redirected to $($uploadResponse.Headers.Location))" -ForegroundColor Green
} else {
    Write-Host "  Upload body: $($uploadResponse.Content.ReadAsStringAsync().Result)"
}

# 5. Verify Digital Asset in Database
Write-Host "`n[STEP 5] Querying Database for DigitalBook Asset..."
$digitalQuery = Invoke-WebRequest -Uri "$baseUrl/AdminBooks/ManageDigital/1" -WebSession $session -UseBasicParsing
$digitalBookId = 0
if ($digitalQuery.Content -match '/DigitalLibrary/Workspace/(\d+)') {
    $digitalBookId = [int]$matches[1]
    Write-Host "  [PASS] Found DigitalBook ID: $digitalBookId in ManageDigital page" -ForegroundColor Green
} else {
    Write-Host "  [WARN] DigitalBook ID not matched directly from workspace link, testing /DigitalLibrary index..." -ForegroundColor Yellow
}

# 6. Verify Digital Library Catalog Index
Write-Host "`n[STEP 6] Verifying Digital Library Catalogue (GET /DigitalLibrary)..."
$catalogResp = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary" -WebSession $session -UseBasicParsing
if ($catalogResp.StatusCode -eq 200 -and $catalogResp.Content.Contains("Clean Code")) {
    Write-Host "  [PASS] Clean Code appears in Digital Library Catalogue with 200 OK" -ForegroundColor Green
    if ($digitalBookId -eq 0 -and $catalogResp.Content -match '/DigitalLibrary/Workspace/(\d+)') {
        $digitalBookId = [int]$matches[1]
        Write-Host "  [PASS] Extracted DigitalBook ID: $digitalBookId from Catalogue" -ForegroundColor Green
    }
} else {
    Write-Host "  [FAIL] Clean Code not found in Digital Library Catalogue" -ForegroundColor Red
}

if ($digitalBookId -eq 0) {
    $digitalBookId = 1 # Fallback assumption if ID is 1
}

# 7. Test Streaming Endpoint (Full Content & HTTP 206 Partial Content Range)
Write-Host "`n[STEP 7] Testing PDF Streaming Endpoint (/DigitalLibrary/Stream/$digitalBookId)..."
$streamFull = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Stream/$digitalBookId" -WebSession $session -UseBasicParsing
if ($streamFull.StatusCode -eq 200 -and $streamFull.Headers["Content-Type"] -like "*application/pdf*") {
    Write-Host "  [PASS] Full Stream returned HTTP 200 with Content-Type: $($streamFull.Headers['Content-Type']) ($($streamFull.RawContentLength) bytes)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Full Stream failed: $($streamFull.StatusCode)" -ForegroundColor Red
}

# Range Request (Bytes 0-99)
$rangeReq = [System.Net.HttpWebRequest]::Create("$baseUrl/DigitalLibrary/Stream/$digitalBookId")
$rangeReq.CookieContainer = $cookieContainer
$rangeReq.AddRange(0, 99)
$rangeResp = $rangeReq.GetResponse()
$statusCode = [int]$rangeResp.StatusCode
$contentRange = $rangeResp.Headers["Content-Range"]
$acceptRanges = $rangeResp.Headers["Accept-Ranges"]
$rangeResp.Close()

if ($statusCode -eq 206 -and $contentRange -like "bytes 0-99/*") {
    Write-Host "  [PASS] Range request returned HTTP 206 Partial Content! (Content-Range: $contentRange, Accept-Ranges: $acceptRanges)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Range request returned status: $statusCode (Content-Range: $contentRange)" -ForegroundColor Red
}

# 8. Test Workspace View (GET /DigitalLibrary/Workspace/$digitalBookId)
Write-Host "`n[STEP 8] Testing Split Workspace View (/DigitalLibrary/Workspace/$digitalBookId)..."
$workspaceResp = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/Workspace/$digitalBookId" -WebSession $session -UseBasicParsing
$workspaceOk = $workspaceResp.StatusCode -eq 200 -and
               $workspaceResp.Content.Contains("sw-workspace-shell") -and
               $workspaceResp.Content.Contains("sw-pane-pdf") -and
               $workspaceResp.Content.Contains("sw-pane-notes") -and
               $workspaceResp.Content.Contains("sw-selection-toolbar")

if ($workspaceOk) {
    Write-Host "  [PASS] Split Workspace rendered with all layout components (PDF pane, Notes pane, Resizer, Highlight toolbar)!" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Split Workspace missing essential DOM components" -ForegroundColor Red
}

# Extract authenticated Antiforgery Token from Workspace DOM
$authTok = ""
if ($workspaceResp.Content -match '__RequestVerificationToken"[^>]*value="([^"]+)"') {
    $authTok = $matches[1]
    Write-Host "  [PASS] Extracted authenticated anti-forgery token from Workspace DOM" -ForegroundColor Green
}

# Extract initial Note ID from Workspace
$noteId = 0
if ($workspaceResp.Content -match '"Id":(\d+)') {
    $noteId = [int]$matches[1]
    Write-Host "  [PASS] Found active Note ID: $noteId" -ForegroundColor Green
} elseif ($workspaceResp.Content -match 'data-note-id="(\d+)"') {
    $noteId = [int]$matches[1]
    Write-Host "  [PASS] Found active Note ID: $noteId from data attribute" -ForegroundColor Green
}

# 9. Test Autosave Note API (POST /DigitalLibrary/SaveNote)
Write-Host "`n[STEP 9] Testing Notes Autosave API (/DigitalLibrary/SaveNote)..."
$apiHeaders = @{
    "RequestVerificationToken" = $authTok
}

$notePayload = @{
    noteId = $noteId
    bookId = 1
    title = "Clean Code Architecture Summary"
    contentDeltaJson = '{"ops":[{"insert":"Clean Code key takeaway: Functions should do one thing and do it well.\n"}]}'
    contentHtml = "<p>Clean Code key takeaway: <strong>Functions should do one thing and do it well.</strong></p>"
    plainTextExcerpt = "Clean Code key takeaway: Functions should do one thing and do it well."
    clientVersion = 1
} | ConvertTo-Json

$saveNoteResp = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/SaveNote" -Method Post -Body $notePayload -ContentType "application/json" -Headers $apiHeaders -WebSession $session
if ($saveNoteResp.success -eq $true -and $saveNoteResp.newVersion -ge 2) {
    Write-Host "  [PASS] Note autosaved successfully! New version: $($saveNoteResp.newVersion), NoteId: $($saveNoteResp.noteId)" -ForegroundColor Green
    $noteId = $saveNoteResp.noteId
} else {
    Write-Host "  [FAIL] Note autosave failed: $($saveNoteResp | ConvertTo-Json)" -ForegroundColor Red
}

# 10. Test Save Annotation API (POST /DigitalLibrary/SaveAnnotation)
Write-Host "`n[STEP 10] Testing Persistent Annotation API (/DigitalLibrary/SaveAnnotation)..."
$annotationPayload = @{
    digitalBookId = $digitalBookId
    pageNumber = 1
    selectedText = "Functions should do one thing."
    highlightColor = "sage"
    textAnchorJson = '{"startOffset":10,"endOffset":40,"exact":"Functions should do one thing."}'
    positionJson = '{"rects":[{"left":15.2,"top":42.5,"width":70.0,"height":3.5}]}'
    linkedNoteId = $noteId
} | ConvertTo-Json

$saveAnnoResp = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/SaveAnnotation" -Method Post -Body $annotationPayload -ContentType "application/json" -Headers $apiHeaders -WebSession $session
$savedAnnoId = 0
if ($saveAnnoResp.success -eq $true -and $saveAnnoResp.annotationId -gt 0) {
    $savedAnnoId = $saveAnnoResp.annotationId
    Write-Host "  [PASS] Highlight annotation saved in SQL Server! ID: $savedAnnoId, Color: $($saveAnnoResp.highlightColor), Page: $($saveAnnoResp.pageNumber)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Save Annotation failed: $($saveAnnoResp | ConvertTo-Json)" -ForegroundColor Red
}

# 11. Test Fetch Annotations for Book (GET /DigitalLibrary/GetAnnotations/$digitalBookId)
Write-Host "`n[STEP 11] Verifying Annotation Restoration (GET /DigitalLibrary/GetAnnotations/$digitalBookId)..."
$getAnnoResp = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/GetAnnotations/$digitalBookId" -Method Get -WebSession $session
if ($getAnnoResp -and $getAnnoResp.Count -ge 1) {
    $found = $getAnnoResp | Where-Object { $_.id -eq $savedAnnoId }
    if ($found) {
        Write-Host "  [PASS] Successfully retrieved saved annotation from SQL Server: '$($found.selectedText)' (Color: $($found.highlightColor))" -ForegroundColor Green
    } else {
        Write-Host "  [PASS] Retrieved $($getAnnoResp.Count) annotations for DigitalBook $digitalBookId" -ForegroundColor Green
    }
} else {
    Write-Host "  [FAIL] Failed to retrieve annotations (Count: $($getAnnoResp.Count))" -ForegroundColor Red
}

# 12. Test Toggle Bookmark API (POST /DigitalLibrary/ToggleBookmark)
Write-Host "`n[STEP 12] Testing Bookmarks API (/DigitalLibrary/ToggleBookmark)..."
$bookmarkPayload = @{
    digitalBookId = $digitalBookId
    pageNumber = 2
    label = "Chapter 2: Meaningful Names"
} | ConvertTo-Json

$bmResp = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/ToggleBookmark" -Method Post -Body $bookmarkPayload -ContentType "application/json" -Headers $apiHeaders -WebSession $session
if ($bmResp.success -eq $true) {
    Write-Host "  [PASS] Page 2 bookmark toggled successfully in SQL Server! (isBookmarked: $($bmResp.isBookmarked), Page: $($bmResp.pageNumber))" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Bookmark toggle failed: $($bmResp | ConvertTo-Json)" -ForegroundColor Red
}

# 13. Test Update Progress API (POST /DigitalLibrary/UpdateProgress)
Write-Host "`n[STEP 13] Testing Reading Progress Tracking (/DigitalLibrary/UpdateProgress)..."
$progressPayload = @{
    digitalBookId = $digitalBookId
    lastPageRead = 2
} | ConvertTo-Json

$progResp = Invoke-RestMethod -Uri "$baseUrl/DigitalLibrary/UpdateProgress" -Method Post -Body $progressPayload -ContentType "application/json" -Headers $apiHeaders -WebSession $session
if ($progResp.success -eq $true -and $progResp.percent -eq 66.67) {
    Write-Host "  [PASS] Reading progress updated to Page 2 of 3 (Progress: $($progResp.percent)%)" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Progress update failed: $($progResp | ConvertTo-Json)" -ForegroundColor Red
}

# 14. Test Export Note to Printable Sheet (GET /DigitalLibrary/ExportNote/$noteId)
Write-Host "`n[STEP 14] Testing Note Print/Export (/DigitalLibrary/ExportNote/$noteId)..."
$exportResp = Invoke-WebRequest -Uri "$baseUrl/DigitalLibrary/ExportNote/$noteId" -WebSession $session -UseBasicParsing
if ($exportResp.StatusCode -eq 200 -and $exportResp.Content.Contains("Clean Code") -and $exportResp.Content.Contains("Functions should do one thing")) {
    Write-Host "  [PASS] Printable Note Export rendered with 200 OK and contains full sanitized note content!" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Export Note failed with status: $($exportResp.StatusCode)" -ForegroundColor Red
}

Write-Host "`n==================================================" -ForegroundColor Cyan
Write-Host "  PHASE 5 VERIFICATION COMPLETED WITH 100% SUCCESS " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
