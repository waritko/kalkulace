param([string]$BaseUrl = 'http://127.0.0.1:5081')
$ErrorActionPreference = 'Stop'
$base = "$BaseUrl/api"
$project = @{
  name = 'Denní záznamy'; customerName = ''; budgetLimit = 0; nonVatPayer = $false
  woodReservePercent = 0; materialOverheadPercent = 0; materialMarginPercent = 0
  laborMarginPercent = 0; serviceMarginPercent = 0; financeMarginPercent = 0; discountPercent = 0
  woodParts = @()
  lines = @(
    @{ name = 'Práce truhláře'; category = 'labor'; unit = 'hod'; quantity = 2; unitPrice = 500; vatRate = 21; workDate = '2026-10-01' },
    @{ name = 'Práce truhláře'; category = 'labor'; unit = 'hod'; quantity = 3; unitPrice = 500; vatRate = 21; workDate = '2026-10-02' },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 2; unitPrice = 30; vatRate = 21; workDate = '2026-10-01'; usesExtraction = $true },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 3; unitPrice = 30; vatRate = 21; workDate = '2026-10-02'; usesExtraction = $true }
  )
}
$json = [Text.Encoding]::UTF8.GetBytes(($project | ConvertTo-Json -Depth 10))
$created = Invoke-RestMethod "$base/projects" -Method Post -ContentType 'application/json; charset=utf-8' -Body $json
try {
  $loaded = Invoke-RestMethod "$base/projects/$($created.id)"
  $labor = @($loaded.input.lines | Where-Object category -eq 'labor')
  $extraction = @($loaded.input.lines | Where-Object { $_.automaticMachineryCharge } | Sort-Object workDate)
  if ($labor.Count -ne 2 -or $labor[0].workDate -ne '2026-10-01' -or $labor[1].workDate -ne '2026-10-02') { throw 'Denní práce se neuložila.' }
  if ($extraction.Count -ne 2 -or $extraction[0].quantity -ne 2 -or $extraction[1].quantity -ne 3 -or $extraction[0].workDate -ne '2026-10-01' -or $extraction[1].workDate -ne '2026-10-02') { throw 'Odsávání není rozdělené podle dnů.' }
  if ($loaded.result.laborCost -ne 2500 -or $loaded.result.serviceCost -ne 150 -or $loaded.result.totalWithVat -ne 3206.5) { throw "Denní položky se nesčítají správně: práce $($loaded.result.laborCost), služby $($loaded.result.serviceCost), celkem $($loaded.result.totalWithVat)." }
  $fractional = $project.Clone()
  $fractional.lines = @(
    @{ name = 'Práce truhláře'; category = 'labor'; unit = 'hod'; quantity = 0.1; unitPrice = 500; vatRate = 21; workDate = '2026-10-01' },
    @{ name = 'Práce truhláře'; category = 'labor'; unit = 'hod'; quantity = 0.1; unitPrice = 500; vatRate = 21; workDate = '2026-10-01' },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.2; unitPrice = 30; vatRate = 21; workDate = '2026-10-01' },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.2; unitPrice = 30; vatRate = 21; workDate = '2026-10-01' }
  )
  $fractionalResult = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($fractional | ConvertTo-Json -Depth 10)))
  if ($fractionalResult.laborCost -ne 125 -or $fractionalResult.serviceCost -ne 15) { throw 'Součet času se nezaokrouhlil nahoru po čtvrthodinách.' }
  $acrossDays = $project.Clone()
  $acrossDays.lines = @(
    @{ name = 'Práce truhláře'; category = 'labor'; unit = 'hod'; quantity = 0.1; unitPrice = 500; vatRate = 21; workDate = '2026-10-01' },
    @{ name = 'Práce truhláře'; category = 'labor'; unit = 'hod'; quantity = 0.1; unitPrice = 500; vatRate = 21; workDate = '2026-10-02' },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.1; unitPrice = 30; vatRate = 21; workDate = '2026-10-01'; usesExtraction = $true; usesVacuum = $true },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.1; unitPrice = 30; vatRate = 21; workDate = '2026-10-02'; usesExtraction = $true; usesVacuum = $true }
  )
  $acrossDaysJson = [Text.Encoding]::UTF8.GetBytes(($acrossDays | ConvertTo-Json -Depth 10))
  $acrossDaysResult = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body $acrossDaysJson
  if ($acrossDaysResult.laborCost -ne 250 -or $acrossDaysResult.serviceCost -ne 7.5) { throw 'Mechanizace se musí zaokrouhlit až po součtu všech dní; práce zůstává zaokrouhlená po dnech.' }
  foreach ($name in @('Pokosová pila', 'Odsávání', 'Vysavač')) {
    $timedRows = @($acrossDaysResult.lines | Where-Object name -eq $name)
    if ($timedRows.Count -ne 2 -or $timedRows[0].quantity -ne 0.1 -or $timedRows[1].quantity -ne 0.15) { throw "Zaokrouhlení napříč dny neplatí pro položku $name." }
  }
  $separateRates = $project.Clone()
  $separateRates.lines = @(
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.1; unitPrice = 30; vatRate = 21; workDate = '2026-10-01' },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.1; unitPrice = 40; vatRate = 21; workDate = '2026-10-02' },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.1; unitPrice = 30; vatRate = 12; workDate = '2026-10-02' },
    @{ name = 'Hoblovka'; category = 'service'; serviceCategory = 'machinery'; unit = 'hod'; quantity = 0.1; unitPrice = 30; vatRate = 21; workDate = '2026-10-02' },
    @{ name = 'Pokosová pila'; category = 'service'; serviceCategory = 'machinery'; unit = 'ks'; quantity = 0.1; unitPrice = 30; vatRate = 21; workDate = '2026-10-02' }
  )
  $separateResult = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($separateRates | ConvertTo-Json -Depth 10)))
  if ($separateResult.serviceCost -ne 35.5) { throw 'Různé stroje, ceny a DPH se nesmí slučovat; jiné jednotky se nezaokrouhlují.' }
  $updated = Invoke-RestMethod "$base/projects/$($created.id)" -Method Put -ContentType 'application/json; charset=utf-8' -Body $acrossDaysJson
  if (@($updated.input.lines | Where-Object quantity -ne 0.1).Count -ne 0 -or $updated.result.serviceCost -ne 7.5) { throw 'Uložení musí zachovat zadaný čas a kalkulovat zaokrouhlený součet dní.' }
  $invoiceRequest = @{
    number = "DAILY-$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"; issuedOn = '2026-10-03'; dueOn = '2026-10-17'
    customerName = 'Test'; supplierName = 'Test'
  } | ConvertTo-Json
  $invoice = Invoke-RestMethod "$base/projects/$($created.id)/invoices" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($invoiceRequest))
  $snapshot = Invoke-RestMethod "$base/invoices/$($invoice.id)"
  $snapshotLabor = @($snapshot.result.lines | Where-Object category -eq 'labor')
  if ($snapshotLabor.Count -ne 2 -or $snapshotLabor[0].workDate -ne '2026-10-01' -or $snapshotLabor[1].workDate -ne '2026-10-02') { throw 'Faktura nezachovala denní záznamy.' }
  $snapshotMachinery = @($snapshot.result.lines | Where-Object name -eq 'Pokosová pila')
  if ($snapshot.result.serviceCost -ne 7.5 -or $snapshotMachinery.Count -ne 2 -or $snapshotMachinery[0].quantity -ne 0.1 -or $snapshotMachinery[1].quantity -ne 0.15 -or $snapshotMachinery[0].workDate -ne '2026-10-01' -or $snapshotMachinery[1].workDate -ne '2026-10-02') { throw 'Faktura musí použít mechanizaci zaokrouhlenou až po součtu dní.' }
  Write-Output 'Denní práce a mechanizace: uložení, příplatky, výpočet a faktura OK.'
} finally {
  Invoke-RestMethod "$base/projects/$($created.id)" -Method Delete | Out-Null
}
