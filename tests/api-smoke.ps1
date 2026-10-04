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
if ($wood.woodPurchase.Count -ne 1 -or $wood.woodPurchase[0].woodType -ne 'Dub' -or $wood.woodPurchase[0].boardThicknessMm -ne 32 -or $wood.woodPurchase[0].areaM2 -ne 1.155 -or $wood.woodPurchase[0].volumeM3 -ne 0.03696 -or $wood.woodPurchase[0].width3mCm -ne 40 -or $wood.woodPurchase[0].width4mCm -ne 30) { throw 'Wood purchase list mismatch' }
$project.woodParts += @{ name = 'Druhá police'; woodType = 'Dub'; widthMm = 200; lengthMm = 1000; thicknessMm = 25; quantity = 1; pricePerM3 = 21900; finish = '' }
$project.woodParts += @{ name = 'Silnější díl'; woodType = 'Dub'; widthMm = 500; lengthMm = 1000; thicknessMm = 30; quantity = 1; pricePerM3 = 29900; finish = '' }
$project.woodParts += @{ name = 'Bukový díl'; woodType = 'Buk'; widthMm = 100; lengthMm = 1000; thicknessMm = 25; quantity = 1; pricePerM3 = 11000; finish = '' }
$grouped = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($project | ConvertTo-Json -Depth 10)))
$oak32 = @($grouped.woodPurchase | Where-Object { $_.woodType -eq 'Dub' -and $_.boardThicknessMm -eq 32 })
if ($grouped.woodPurchase.Count -ne 3 -or $oak32.Count -ne 1 -or $oak32[0].areaM2 -ne 1.386 -or $oak32[0].volumeM3 -ne 0.044352 -or $oak32[0].width3mCm -ne 50 -or $oak32[0].width4mCm -ne 40) { throw 'Wood purchase grouping or rounding mismatch' }
$project.lamellaLengthExtraMm = 50
$project.lamellaMergeToleranceMm = 50
$project.glueBoardWastePercent = 10
$project.woodParts = @(
  @{ name = 'Dlouhý'; woodType = 'Dub'; widthMm = 400; lengthMm = 1000; thicknessMm = 25; quantity = 2; pricePerM3 = 21900; finish = '' },
  @{ name = 'Blízký'; woodType = 'Dub'; widthMm = 300; lengthMm = 950; thicknessMm = 25; quantity = 1; pricePerM3 = 21900; finish = '' },
  @{ name = 'Další'; woodType = 'Dub'; widthMm = 200; lengthMm = 900; thicknessMm = 25; quantity = 1; pricePerM3 = 21900; finish = '' },
  @{ name = 'Otočený'; woodType = 'Buk'; widthMm = 1200; lengthMm = 150; thicknessMm = 30; quantity = 1; pricePerM3 = 11000; finish = '' }
)
$glue = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($project | ConvertTo-Json -Depth 10)))
$oakGlue = @($glue.glueBoardPurchase | Where-Object woodType -eq 'Dub')
$beechGlue = @($glue.glueBoardPurchase | Where-Object woodType -eq 'Buk')
if ($oakGlue.Count -ne 2 -or $oakGlue[0].lamellaLengthMm -ne 1050 -or $oakGlue[0].totalWidthMm -ne 1210 -or $oakGlue[1].lamellaLengthMm -ne 950 -or $oakGlue[1].totalWidthMm -ne 220 -or $beechGlue[0].lamellaLengthMm -ne 1250 -or $beechGlue[0].totalWidthMm -ne 165) { throw 'Glue board grouping, orientation or waste mismatch' }
$oakBoards = @($glue.woodPurchase | Where-Object { $_.woodType -eq 'Dub' -and $_.boardThicknessMm -eq 32 })
if ($oakBoards.Count -ne 1 -or $oakBoards[0].areaM2 -ne 1.4795 -or $oakBoards[0].volumeM3 -ne 0.047344 -or $oakBoards[0].width3mCm -ne 50 -or $oakBoards[0].width4mCm -ne 40) { throw 'Wood purchase must use grouped lamellas including waste' }
$project.lamellaMergeToleranceMm = 0
$unmerged = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($project | ConvertTo-Json -Depth 10)))
if (@($unmerged.glueBoardPurchase | Where-Object woodType -eq 'Dub').Count -ne 3) { throw 'Glue board tolerance was ignored' }
$project.woodParts = @(@{ name = 'Police'; woodType = 'Dub'; widthMm = 500; lengthMm = 1000; thicknessMm = 25; quantity = 2; pricePerM3 = 21900; finish = '' })
$project.woodParts[0].applyFinish = $true
$finished = Invoke-RestMethod "$base/calculate" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($project | ConvertTo-Json -Depth 10)))
$finishLine = @($finished.lines | Where-Object name -eq 'Osmo')
if ($finishLine.Count -ne 1 -or $finishLine[0].quantity -ne 2.2 -or $finishLine[0].cost -ne 82.1) { throw 'Automatic finish quantity or cost mismatch' }
$project.woodParts[0].applyFinish = $false
$project.woodParts = @()
$project.lamellaLengthExtraMm = 70
$project.lamellaMergeToleranceMm = 25
$project.glueBoardWastePercent = 12
$body = $project | ConvertTo-Json -Depth 10
$created = Invoke-RestMethod "$base/projects" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
try {
  $loaded = Invoke-RestMethod "$base/projects/$($created.id)"
  if ($loaded.input.name -ne $project.name -or $loaded.input.lines.Count -ne 2 -or $loaded.input.lines[0].materialType -ne 'fastener' -or $loaded.input.lines[1].quantity -ne 0.5) { throw 'Project did not persist' }
  if ($loaded.input.lamellaLengthExtraMm -ne 70 -or $loaded.input.lamellaMergeToleranceMm -ne 25 -or $loaded.input.glueBoardWastePercent -ne 12) { throw 'Glue board settings did not persist' }
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
