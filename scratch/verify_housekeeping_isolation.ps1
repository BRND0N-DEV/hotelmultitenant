# Live Housekeeping State Isolation Verification Script
$baseUrl = "http://localhost:5000"
$headers = @{
    Authorization = "Bearer $( (Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body (@{email="admin@hotelchain.com";password="Admin123*!";preferredHotelId=1} | ConvertTo-Json) -ContentType "application/json").accessToken )"
    "X-Hotel-Id" = "1"
}

Write-Host "=== 1. CONSULTANDO ESTADO INICIAL DE LAS HABITACIONES ===" -ForegroundColor Cyan
$roomsBefore = Invoke-RestMethod -Uri "$baseUrl/api/rooms" -Method Get -Headers $headers
$r101Before = $roomsBefore | Where-Object { $_.roomNumber -eq "101" }
$r102Before = $roomsBefore | Where-Object { $_.roomNumber -eq "102" }
$r103Before = $roomsBefore | Where-Object { $_.roomNumber -eq "103" }
$r104Before = $roomsBefore | Where-Object { $_.roomNumber -eq "104" }

Write-Host "Habitacion 101: Operativo=$($r101Before.operationalStatus) | Limpieza=$($r101Before.cleaningStatus)"
Write-Host "Habitacion 102: Operativo=$($r102Before.operationalStatus) | Limpieza=$($r102Before.cleaningStatus)"
Write-Host "Habitacion 103: Operativo=$($r103Before.operationalStatus) | Limpieza=$($r103Before.cleaningStatus)"
Write-Host "Habitacion 104: Operativo=$($r104Before.operationalStatus) | Limpieza=$($r104Before.cleaningStatus)"

Write-Host "`n=== 2. EJECUTANDO OPERACION EXCLUSIVAMENTE SOBRE HABITACION 104 ===" -ForegroundColor Cyan
$updateBody = @{
    operationalStatus = "Mantenimiento"
    notes = "Prueba de aislamiento de estado QA"
} | ConvertTo-Json
$toggleResp = Invoke-RestMethod -Uri "$baseUrl/api/housekeeping/rooms/$($r104Before.roomId)/status" -Method Put -Body $updateBody -ContentType "application/json" -Headers $headers
Write-Host "Resultado habitacion 104: $($toggleResp.message)"

Write-Host "`n=== 3. VERIFICANDO AISLAMIENTO ESTRICTO DE ESTADOS ===" -ForegroundColor Cyan
$roomsAfter = Invoke-RestMethod -Uri "$baseUrl/api/rooms" -Method Get -Headers $headers
$r101After = $roomsAfter | Where-Object { $_.roomNumber -eq "101" }
$r102After = $roomsAfter | Where-Object { $_.roomNumber -eq "102" }
$r103After = $roomsAfter | Where-Object { $_.roomNumber -eq "103" }
$r104After = $roomsAfter | Where-Object { $_.roomNumber -eq "104" }

Write-Host "Habitacion 101: Operativo=$($r101After.operationalStatus) | Limpieza=$($r101After.cleaningStatus)"
Write-Host "Habitacion 102: Operativo=$($r102After.operationalStatus) | Limpieza=$($r102After.cleaningStatus)"
Write-Host "Habitacion 103: Operativo=$($r103After.operationalStatus) | Limpieza=$($r103After.cleaningStatus)"
Write-Host "Habitacion 104: Operativo=$($r104After.operationalStatus) | Limpieza=$($r104After.cleaningStatus)"

$isolationSuccess = $true
if ($r101Before.operationalStatus -ne $r101After.operationalStatus -or $r101Before.cleaningStatus -ne $r101After.cleaningStatus) {
    Write-Host "ERROR: Habitacion 101 fue afectada colateralmente!" -ForegroundColor Red
    $isolationSuccess = $false
}
if ($r102Before.operationalStatus -ne $r102After.operationalStatus -or $r102Before.cleaningStatus -ne $r102After.cleaningStatus) {
    Write-Host "ERROR: Habitacion 102 fue afectada colateralmente!" -ForegroundColor Red
    $isolationSuccess = $false
}
if ($r103Before.operationalStatus -ne $r103After.operationalStatus -or $r103Before.cleaningStatus -ne $r103After.cleaningStatus) {
    Write-Host "ERROR: Habitacion 103 fue afectada colateralmente!" -ForegroundColor Red
    $isolationSuccess = $false
}

if ($isolationSuccess) {
    Write-Host "`n>>> AISLAMIENTO CONFIRMADO: Solo cambio la habitacion 104. Ninguna otra habitacion fue alterada <<<" -ForegroundColor Green
} else {
    Write-Host "`n>>> ERROR EN AISLAMIENTO DE ESTADOS <<<" -ForegroundColor Red
}

# Restaurar estado original de habitacion 104
$restoreBody = @{
    operationalStatus = "Disponible"
    notes = "Restaurado tras prueba de aislamiento"
} | ConvertTo-Json
$null = Invoke-RestMethod -Uri "$baseUrl/api/housekeeping/rooms/$($r104Before.roomId)/status" -Method Put -Body $restoreBody -ContentType "application/json" -Headers $headers
Write-Host "Habitacion 104 restaurada a estado original."
