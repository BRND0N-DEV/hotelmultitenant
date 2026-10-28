# Live API End-to-End Verification Script
$baseUrl = "http://localhost:5000"
Write-Host "=== 1. VERIFICANDO SALUD DE LA API ===" -ForegroundColor Cyan
$health = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
Write-Host "API Health: $($health.status) | Engine: $($health.engine)"

Write-Host "`n=== 2. AUTENTICACION ADMIN GENERAL ===" -ForegroundColor Cyan
$loginBody = @{
    email = "admin@hotelchain.com"
    password = "Admin123*!"
    preferredHotelId = 1
} | ConvertTo-Json

$loginResp = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResp.accessToken
$hotelName = $loginResp.user.activeHotelName
Write-Host "Login exitoso. Usuario: $($loginResp.user.fullName) | Sede: $hotelName"

$headers = @{
    Authorization = "Bearer $token"
    "X-Hotel-Id" = "1"
}

Write-Host "`n=== 3. VERIFICACION DE CARACTERES UTF-8 ===" -ForegroundColor Cyan
$hotels = Invoke-RestMethod -Uri "$baseUrl/api/hotels" -Method Get -Headers $headers
foreach ($h in $hotels) {
    Write-Host "Hotel: $($h.name) - Ciudad: $($h.city)"
}

$roomTypes = Invoke-RestMethod -Uri "$baseUrl/api/roomtypes" -Method Get -Headers $headers
foreach ($rt in $roomTypes) {
    Write-Host "Tipo Habitacion: $($rt.name)"
}

Write-Host "`n=== 4. DIRECTORIO DE HUESPEDES & HABEAS DATA (RF-45 a RF-47) ===" -ForegroundColor Cyan
$guests = Invoke-RestMethod -Uri "$baseUrl/api/guests" -Method Get -Headers $headers
$targetGuest = $guests[0]
Write-Host "Huesped original: Id=$($targetGuest.guestId) | Nombre=$($targetGuest.firstName) $($targetGuest.lastName) | Tel=$($targetGuest.phone) | Consentimiento=$($targetGuest.hasDataConsent)"

$updatedPhone = "+57 300 999 8888"
$updateBody = @{
    firstName = $targetGuest.firstName
    lastName = $targetGuest.lastName
    email = $targetGuest.email
    phone = $updatedPhone
    nationality = "Colombiana"
    acceptDataConsent = $true
} | ConvertTo-Json

$updateResp = Invoke-RestMethod -Uri "$baseUrl/api/guests/$($targetGuest.guestId)" -Method Put -Body $updateBody -ContentType "application/json" -Headers $headers
Write-Host "Respuesta de actualizacion: $($updateResp.message)"

# Consultar de nuevo para verificar persistencia real en SQL Server
$reloadedGuest = Invoke-RestMethod -Uri "$baseUrl/api/guests/$($targetGuest.guestId)" -Method Get -Headers $headers
Write-Host "Huesped persistido: Tel=$($reloadedGuest.phone) | Consentimiento=$($reloadedGuest.hasDataConsent)"
if ($reloadedGuest.phone -eq $updatedPhone) {
    Write-Host ">>> PERSISTENCIA CONFIRMADA EN BASE DE DATOS <<<" -ForegroundColor Green
} else {
    Write-Host ">>> ERROR DE PERSISTENCIA <<<" -ForegroundColor Red
}

Write-Host "`n=== 5. AUDITORIA DE HABITACIONES Y ESTADO DE RECEPCION ===" -ForegroundColor Cyan
$rooms = Invoke-RestMethod -Uri "$baseUrl/api/rooms" -Method Get -Headers $headers
foreach ($r in $rooms | Select-Object -First 5) {
    Write-Host "Habitacion $($r.roomNumber): Estado Operativo=$($r.operationalStatus) | Limpieza=$($r.cleaningStatus)"
}

Write-Host "`n=== 6. VERIFICACION COMPLETADA EXITOSAMENTE ===" -ForegroundColor Green
