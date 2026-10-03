param([string]$BaseUrl = 'http://127.0.0.1:5080')
$ErrorActionPreference = 'Stop'
$base = "$BaseUrl/api"
$project = @{
  name = 'Test knihovna'; customerName = 'Test zákazník'; budgetLimit = 1000; nonVatPayer = $false
  woodReservePercent = 0; materialOverheadPercent = 47; materialMarginPercent = 5
  laborMarginPercent = 0; serviceMarginPercent = 15; financeMarginPercent = 15; discountPercent = 0
  woodParts = @()
  lines = @(
    @{ name = 'Lepidlo'; category = 'material'; materialType = 'fastener'; unit = 'akce'; quantity = 1; unitPrice = 50; vatRate = 21 },
    @{ name = 'Fakturace'; category = 'finance'; unit = 'hod'; quantity = 0.5; unitPrice = 600; vatRate = 21 }
  )
}
$body = $project | ConvertTo-Json -Depth 10
$catalog = Invoke-RestMethod "$base/catalog"
if (($catalog.items | Where-Object name -eq 'Osmo').materialType -ne 'finish' -or ($catalog.items | Where-Object name -eq 'Brusný pás').materialType -ne 'abrasive') { throw 'Material catalog types are missing' }
$calculation = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
if ($calculation.totalWithVat -ne 509.41) { throw "Workbook example mismatch: $($calculation.totalWithVat)" }
$project.woodParts = @(@{ name = 'Police'; woodType = 'Dub'; widthMm = 500; lengthMm = 1000; thicknessMm = 25; quantity = 2; pricePerM3 = 21900; finish = '' })
$wood = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($project | ConvertTo-Json -Depth 10)))
if ($wood.woodVolumeM3 -ne 0.032 -or $wood.woodCost -ne 700.8) { throw "Wood calculation mismatch: $($wood.woodVolumeM3) m3, $($wood.woodCost) CZK" }
if ($wood.lines.Count -ne 3 -or $wood.lines[0].quantity -ne 0.032) { throw 'Wood volume must come from parts without a duplicate cost line' }
$project.woodParts = @()
$created = Invoke-RestMethod "$base/projects" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
try {
  $loaded = Invoke-RestMethod "$base/projects/$($created.id)"
  if ($loaded.input.name -ne $project.name -or $loaded.input.lines.Count -ne 2 -or $loaded.input.lines[0].materialType -ne 'fastener' -or $loaded.input.lines[1].quantity -ne 0.5) { throw 'Project did not persist' }
  $listed = @(Invoke-RestMethod "$base/projects")
  if ($created.id -notin $listed.id) { throw 'Project is missing from project list' }
  $invoiceBody = @{
    number = "TEST-$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
    issuedOn = '2026-10-03'; dueOn = '2026-10-17'; customerName = 'Test zákazník'; customerAddress = 'Praha'
    supplierName = 'Test truhlář'; supplierAddress = 'Brno'; supplierIco = '12345678'; supplierDic = ''; bankAccount = '123/0100'; note = ''
  } | ConvertTo-Json
  $invoice = Invoke-RestMethod "$base/projects/$($created.id)/invoices" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($invoiceBody))
  if ($invoice.result.totalWithVat -ne 509.41) { throw 'Invoice snapshot total mismatch' }
  $project.lines[0].unitPrice = 100
  $null = Invoke-RestMethod "$base/projects/$($created.id)" -Method Put -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($project | ConvertTo-Json -Depth 10)))
  $snapshot = Invoke-RestMethod "$base/invoices/$($invoice.id)"
  if ($snapshot.result.totalWithVat -ne 509.41) { throw 'Invoice snapshot changed after editing project' }
  Write-Output 'API smoke test passed: calculation, project persistence, invoice snapshot.'
} finally {
  Invoke-RestMethod "$base/projects/$($created.id)" -Method Delete | Out-Null
}
