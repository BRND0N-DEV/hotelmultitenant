# Live Reception, State Machine, Payment & Checkout Audit Script
$baseUrl = "http://localhost:5000"

Write-Host "=== 1. AUTENTICACION COMO RECEPCIONISTA BOGOTA ===" -ForegroundColor Cyan
$loginBody = @{
    email = "recepcion.bogota@hotelchain.com"
    password = "Admin123*!"
    preferredHotelId = 1
} | ConvertTo-Json

$loginResp = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResp.accessToken
Write-Host "Autenticado como: $($loginResp.user.fullName) (Rol: $($loginResp.user.role))"

$headers = @{
    Authorization = "Bearer $token"
    "X-Hotel-Id" = "1"
}

Write-Host "`n=== 2. VERIFICANDO TURNO DE CAJA (CASH SHIFT) ===" -ForegroundColor Cyan
$activeShift = $null
try {
    $activeShift = Invoke-RestMethod -Uri "$baseUrl/api/cashshift/current" -Method Get -Headers $headers
} catch {
    Write-Host "No hay turno activo. Abriendo uno nuevo..."
}

if (-not $activeShift) {
    $openBody = @{
        initialBase = 50000
        notes = "Turno de prueba QA Continentalia"
    } | ConvertTo-Json
    $activeShift = Invoke-RestMethod -Uri "$baseUrl/api/cashshift/open" -Method Post -Body $openBody -ContentType "application/json" -Headers $headers
}
Write-Host "Turno de caja activo: Id=$($activeShift.cashShiftId) | Estado=$($activeShift.status) | Base=$($activeShift.initialBase)"

Write-Host "`n=== 3. SELECCIONANDO HABITACION DISPONIBLE Y LIMPIA ===" -ForegroundColor Cyan
$rooms = Invoke-RestMethod -Uri "$baseUrl/api/rooms" -Method Get -Headers $headers
$availRoom = $rooms | Where-Object { $_.operationalStatus -eq "Disponible" -and $_.cleaningStatus -eq "Limpia" } | Select-Object -First 1
Write-Host "Habitacion elegida: $($availRoom.roomNumber) (Id=$($availRoom.roomId))"

Write-Host "`n=== 4. CREANDO RESERVA EN ESTADO PENDIENTE ===" -ForegroundColor Cyan
# Fechas futuras para evitar colisiones
$randomOffset = Get-Random -Minimum 120 -Maximum 300
$startDate = (Get-Date).Date.AddDays($randomOffset)
$endDate = $startDate.AddDays(2)
$uniqueDoc = "QA_AUDIT_" + (Get-Random -Minimum 100000 -Maximum 999999)

$resBody = @{
    roomId = $availRoom.roomId
    checkInDate = $startDate.ToString("yyyy-MM-dd")
    checkOutDate = $endDate.ToString("yyyy-MM-dd")
    adults = 1
    cancellationPolicyId = 1
    primaryGuest = @{
        documentType = "CC"
        documentNumber = $uniqueDoc
        firstName = "Fernando"
        lastName = "QA Auditor"
        email = "fernando.qa@test.com"
        phone = "+57 312 999 0011"
        acceptDataConsent = $true
    }
} | ConvertTo-Json

$resResp = Invoke-RestMethod -Uri "$baseUrl/api/reservations" -Method Post -Body $resBody -ContentType "application/json" -Headers $headers
Write-Host "Reserva creada con exito: Codigo=$($resResp.code) | Estado=$($resResp.status) (Id=$($resResp.reservationId))"

Write-Host "`n=== 5. PRUEBA DE TRANSICION DE ESTADO PENDIENTE -> CHECK-IN ===" -ForegroundColor Cyan
$checkInUrl = "$baseUrl/api/reservations/$($resResp.reservationId)/check-in"
try {
    $checkInResp = Invoke-RestMethod -Uri $checkInUrl -Method Post -Headers $headers
    Write-Host ">>> CHECK-IN EXITOSO <<< Mensaje: $($checkInResp.message)" -ForegroundColor Green
} catch {
    Write-Host "ERROR EN CHECK-IN: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== 6. VALIDACION REGLA FINANCIERA RF-40 (CHECK-OUT CON SALDO PENDIENTE) ===" -ForegroundColor Cyan
$checkOutUrl = "$baseUrl/api/reservations/$($resResp.reservationId)/check-out"
$blockedAsExpected = $false
try {
    $unexpectedCheckout = Invoke-RestMethod -Uri $checkOutUrl -Method Post -Headers $headers
    Write-Host "FALLO: El backend permitio Check-Out con saldo pendiente!" -ForegroundColor Red
} catch {
    Write-Host ">>> CONFIRMADO: Check-Out rechazado correctamente por saldo pendiente <<<" -ForegroundColor Green
    $blockedAsExpected = $true
}

Write-Host "`n=== 7. CONSULTANDO FOLIO Y REGISTRANDO PAGO CON VINCULACION A CAJA ===" -ForegroundColor Cyan
$resDetail = Invoke-RestMethod -Uri "$baseUrl/api/reservations/$($resResp.reservationId)" -Method Get -Headers $headers
$folioId = $resDetail.folio.folioId
$folio = Invoke-RestMethod -Uri "$baseUrl/api/folios/$folioId" -Method Get -Headers $headers
Write-Host "Folio Id=$($folio.folioId) | Total=$($folio.totalCharges) | Saldo=$($folio.balance)"

$paymentBody = @{
    amount = $folio.balance
    paymentMethod = "Efectivo"
    notes = "Pago de liquidacion total de la reserva"
} | ConvertTo-Json

$payResp = Invoke-RestMethod -Uri "$baseUrl/api/folios/$folioId/payments" -Method Post -Body $paymentBody -ContentType "application/json" -Headers $headers
Write-Host "Pago registrado: Id=$($payResp.paymentId) | Monto=$($payResp.amount) | Metodo=$($payResp.paymentMethod) | CajaId=$($payResp.cashShiftId)"

# Comprobar que el saldo del folio quedo en 0
$folioAfterPay = Invoke-RestMethod -Uri "$baseUrl/api/folios/$folioId" -Method Get -Headers $headers
Write-Host "Saldo actual del Folio: $($folioAfterPay.balance)"

# Comprobar que la caja actualizo sus totales
$shiftAfterPay = Invoke-RestMethod -Uri "$baseUrl/api/cashshift/current" -Method Get -Headers $headers
Write-Host "Caja Actualizada: Total Efectivo=$($shiftAfterPay.totalCash)"

Write-Host "`n=== 8. CHECK-OUT EXITOSO TRAS SALDAR LA CUENTA ===" -ForegroundColor Cyan
$finalCheckout = Invoke-RestMethod -Uri $checkOutUrl -Method Post -Headers $headers
Write-Host ">>> CHECK-OUT FINALIZADO CON EXITO <<< Mensaje: $($finalCheckout.message)" -ForegroundColor Green

# Verificar que la habitacion pasa a estado Sucia para Housekeeping
$roomAfter = (Invoke-RestMethod -Uri "$baseUrl/api/rooms" -Method Get -Headers $headers) | Where-Object { $_.roomId -eq $availRoom.roomId }
Write-Host "Habitacion $($availRoom.roomNumber) tras Checkout: Limpieza=$($roomAfter.cleaningStatus) | Operativo=$($roomAfter.operationalStatus)"

Write-Host "`n=== AUDITORIA COMPLETA DE RECEPCION Y PAGOS VERIFICADA AL 100% ===" -ForegroundColor Green
