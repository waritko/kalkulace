# Kalkulace pro truhlářské zakázky

Webová aplikace podle `kalkulace.xlsx`. Backend je ASP.NET Core 9, frontend React + TypeScript a výchozí databáze SQLite. Přihlášení aplikace neřeší; nasazení předpokládá ověření na HTTP proxy.

## Spuštění

Požadavky: .NET SDK 9 nebo novější (včetně .NET 10) a Node.js 20 nebo novější. Z kořene projektu spusťte:

```powershell
.\run-local.ps1
```

Skript nainstaluje chybějící balíčky frontendu, sestaví frontend a spustí aplikaci na `http://localhost:5080`. Ukončíte ji pomocí `Ctrl+C`.

Stejné kroky lze provést ručně:

```powershell
cd frontend
npm install
npm run build
cd ..
dotnet run --project backend/Kalkulace.Api.csproj --urls http://localhost:5080
```

Otevřete `http://localhost:5080`. Backend podává sestavený frontend z `frontend/dist`. Pro samostatné nasazení použijte níže uvedený release balíček.

Pro vývoj frontendu spusťte v druhém terminálu `cd frontend; npm run dev` a otevřete `http://localhost:5173`. Vite předává `/api` na backend na portu 5080.

## Release balíček a nasazení

Z kořene projektu spusťte `./build.ps1` (Windows PowerShell 5.1 nebo PowerShell 7; na Linuxu `pwsh ./build.ps1`). Vyžaduje .NET SDK 9 nebo novější (včetně .NET 10) a Node.js 20 nebo novější s npm.

Skript provede `npm ci`, sestaví frontend, publikuje backend v konfiguraci **Release** a vytvoří `artifacts/kalkulace-release.zip` i rozbalenou složku `artifacts/kalkulace-release` se stejnými soubory. ZIP obsahuje frontend ve `wwwroot`, spouštěcí skripty a `appsettings.Production.json` s konfigurací MySQL. Lokální databáze a `appsettings.Development.json` se nebalí. Úspěšný build nahradí předchozí ZIP i rozbalenou složku.

Po vytvoření balíčku skript přes SCP zkopíruje obsah rozbalené složky přímo do `waritko@mrazitko.varak.net:/home/waritko/kalkulace`. Vyžaduje klienta OpenSSH (`scp` v `PATH`) a SSH přístup s právem zápisu do cílového adresáře. Používá běžnou konfiguraci SSH; port, klíč a cíl lze změnit parametry. Při chybě přenosu skript skončí chybou, lokální ZIP i rozbalená složka zůstanou k dispozici. SCP přepíše stejnojmenné soubory, ale neodstraní staré soubory na serveru ani nerestartuje aplikaci.

```powershell
# Pouze lokální build bez SCP
./build.ps1 -SkipScp

# Vlastní cíl, SSH port a privátní klíč
./build.ps1 -ScpDestination 'user@server:/srv/kalkulace' -ScpPort 2222 -ScpIdentityFile "$HOME/.ssh/id_ed25519"
```

Balíček je společný pro Windows a Linux a vyžaduje nainstalovaný **ASP.NET Core Runtime 9 nebo 10** pro daný systém a architekturu. Node.js ani SDK nejsou na cílovém stroji potřeba. Použijte složku `artifacts/kalkulace-release` nebo rozbalte ZIP do adresáře s právem zápisu a spusťte:

- Windows: `run.bat`
- Linux: `sh ./run.sh` (případně `chmod +x run.sh` a `./run.sh`)

Aplikace běží na `http://localhost:5080`; ukončíte ji pomocí `Ctrl+C`. Adresu lze změnit proměnnou `ASPNETCORE_URLS` nebo argumentem, například `sh ./run.sh --urls http://localhost:8080` či `run.bat --urls http://localhost:8080`. Skripty fungují i při spuštění z jiného pracovního adresáře. Balíček ve výchozím prostředí `Production` používá MySQL na `localhost`, databázi `kalkulace`, uživatele `kalkulace` a heslo `fillMeIn`. Před spuštěním vytvořte databázi a uživatele s právy k této databázi. Připojení lze upravit v přibaleném `appsettings.Production.json` nebo proměnnou `ConnectionStrings__MySql`. Schéma vytvoří aplikace při prvním spuštění.

## Databáze

SQLite používá soubor `backend/Data/kalkulace.db` a při prvním spuštění vytvoří schéma. Pro MySQL nastavte `Database__Provider=MySql` a `ConnectionStrings__MySql` na platný connection string, například přes proměnné prostředí nebo vlastní `appsettings.Production.json`. Databázi je nutné vytvořit předem. Schéma vytvoří aplikace při prvním spuštění. Změna provideru automaticky nepřenáší záznamy z původní databáze.

Zakázky, položky, ceník a snímky faktur jsou uloženy v relačních tabulkách s cizími klíči. Při spuštění nad starší databází aplikace převede obsah sloupců `Projects.Payload`, `Invoices.Snapshot` a `CatalogSettings.Payload` do nových tabulek a staré sloupce odstraní. Před prvním spuštěním nové verze si vytvořte zálohu databáze.

## Výpočet

- Množství dřeva v kalkulaci vychází z plochy nákupních lamel pro spárovku včetně přídavku k délce, sloučení délek a prořezu (výchozí 10 %). Pro tloušťku dílu pod 29 mm se používá fošna 32 mm, jinak 50 mm. Objem × cena/m³ určuje cenu dřeva; volitelná rezerva se přidává k ceně.
- Ve Výpočtu dřeva jsou nákupní seznam fošen a seznam lamel pro průběžnou spárovku. Délka lamely je delší strana dílu plus nastavitelný přídavek (výchozí 50 mm). Délky v nastavitelném rozmezí (výchozí 50 mm) se sčítají pod nejdelší nákupní délkou. Pro každou dřevinu, tloušťku fošny a délku se sčítají šířky kratších stran násobené počtem kusů; výsledek se navýší o nastavitelný prořez (výchozí 10 %) a zaokrouhlí nahoru na celý mm. Plocha a objem nákupních fošen se počítají z takto připravených lamel; potřebná šířka fošny při délce 3 m nebo 4 m se zaokrouhlí nahoru na 10 cm. Nastavení se ukládá se zakázkou a ovlivňuje i cenovou kalkulaci dřeva. Tlačítko **Tisk lamel a dílců** otevře tiskový dialog s dílenským seznamem bez cen.
- Materiál, práce, služby a režijní činnosti používají množství × jednotkovou cenu. Režie z materiálu a marže kategorií mají samostatně nastavitelné sazby. Sleva se uplatní na konečnou cenu před DPH.
- V nabídce **Výchozí nastavení ceny** lze uložit rezervu dřeva, režii materiálu, marže a slevu pro nové zakázky. Výchozí sazby se ukládají do databáze. Již vytvořené zakázky si zachovají vlastní hodnoty.
- Práce a mechanizace se po dnech zadávají v hodinách a minutách. Zaznamenaný čas se ukládá bez zaokrouhlení. Pro kalkulaci a fakturu se součet stejné položky se stejnou cenou a DPH zaokrouhlí nahoru na 15 minut: u práce v rámci jednoho dne, u mechanizace až po sečtení všech dní zakázky (včetně odsávání a vysavače). Rozdíl ze zaokrouhlení se započítá do posledního záznamu položky.
- Každá běžná položka může mít sazbu DPH 12 % nebo 21 %. Režie a marže se mezi sazby rozdělují podle hodnoty položek. Režim neplátce počítá nulovou DPH.
- Výchozí ceny katalogu pocházejí z listů ceníku v sešitu; každou cenu lze na zakázce upravit. Vzor v sešitu s materiálem 50 Kč a fakturací 0,5 hodiny po 600 Kč dává 509,41 Kč včetně DPH.
- V nabídce **Ceník** lze upravit ceny dřeva podle tloušťky fošny, mechanizace a ostatního materiálu. Změny se ukládají do databáze a použijí se při přidávání nových položek do zakázek. Dříve uložené ceny v zakázkách se samy nemění.
- Faktura ukládá okamžitý snímek výpočtu. Pozdější úprava zakázky její částku nezmění. Fakturu lze vytisknout nebo uložit jako PDF přes tiskový dialog prohlížeče.

## Ověření

```powershell
dotnet build backend/Kalkulace.Api.csproj
cd frontend; npm run build
```

Při běžícím backendu lze spustit `./tests/api-smoke.ps1`. Test ověřuje vzorový výpočet, uložení zakázky a neměnnost vystavené faktury.

## TeamCity

Konfigurace v `.teamcity/settings.kts` vytváří build **Build and smoke tests** se spouštěním při změně ve VCS. V TeamCity vytvořte projekt z tohoto Git repozitáře a zapněte **Versioned Settings** ve formátu Kotlin DSL s cestou `.teamcity`. Build používá stejný VCS root jako nastavení projektu. Verzi DSL v `settings.kts` upravte podle verze vašeho TeamCity serveru.

Agent musí běžet na Windows a mít v `PATH` PowerShell 5.1, .NET SDK 9 nebo novější (včetně .NET 10) a Node.js 20 nebo novější (včetně `npm.cmd`). Skript spustí `npm ci`, sestaví frontend a backend, spustí API na volném lokálním portu se samostatnou SQLite databází a provede všechny tři smoke testy. Výsledky jsou vidět jako testy v TeamCity; logy API se ukládají jako artefakty `api-logs`. Databáze a proces API se po běhu odstraní.

Stejný postup lze spustit lokálně z kořene repozitáře:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/ci.ps1
```
