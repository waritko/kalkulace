function New-TestSession([string]$BaseUrl) {
    $username = if ($env:KALKULACE_TEST_USERNAME) { $env:KALKULACE_TEST_USERNAME } else { 'waritko' }
    $password = if ($env:KALKULACE_TEST_PASSWORD) { $env:KALKULACE_TEST_PASSWORD } else { 'blade666' }
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $session.Headers['X-Kalkulace-Request'] = '1'
    if ($env:KALKULACE_TEST_COOKIE) {
        foreach ($item in $env:KALKULACE_TEST_COOKIE.Split(';')) {
            $pair = $item.Trim().Split('=', 2)
            $session.Cookies.Add([Uri]$BaseUrl, (New-Object System.Net.Cookie($pair[0], $pair[1], '/')))
        }
        return $session
    }
    $null = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -WebSession $session -ContentType 'application/json' -Body (@{ username = $username; password = $password } | ConvertTo-Json)
    return $session
}
