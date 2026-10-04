# Kalkulace pro truhlářské zakázky

Webová aplikace podle `kalkulace.xlsx`. Backend je ASP.NET Core 9, frontend React + TypeScript a výchozí databáze SQLite. Přihlášení aplikace neřeší; nasazení předpokládá ověření na HTTP proxy.

## Spuštění

Požadavky: .NET SDK 9 nebo novější a Node.js 20 nebo novější. Z kořene projektu spusťte:

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

Otevřete `http://localhost:5080`. Backend podává sestavený frontend z `frontend/dist`. Při samostatném nasazení zkopírujte obsah `frontend/dist` do `backend/wwwroot` před publikováním backendu.

Pro vývoj frontendu spusťte v druhém terminálu `cd frontend; npm run dev` a otevřete `http://localhost:5173`. Vite předává `/api` na backend na portu 5080.

## Databáze

SQLite používá soubor `backend/Data/kalkulace.db` a při prvním spuštění vytvoří schéma. Pro MySQL nastavte `Database__Provider=MySql` a `ConnectionStrings__MySql` na platný connection string, například přes proměnné prostředí nebo vlastní `appsettings.Production.json`. Databázi je nutné vytvořit předem. Schéma vytvoří aplikace při prvním spuštění. Změna provideru automaticky nepřenáší záznamy z původní databáze.

Zakázky, položky, ceník a snímky faktur jsou uloženy v relačních tabulkách s cizími klíči. Při spuštění nad starší databází aplikace převede obsah sloupců `Projects.Payload`, `Invoices.Snapshot` a `CatalogSettings.Payload` do nových tabulek a staré sloupce odstraní. Před prvním spuštěním nové verze si vytvořte zálohu databáze.

## Výpočet

- Množství dřeva v kalkulaci vychází z plochy nákupních lamel pro spárovku včetně přídavku k délce, sloučení délek a prořezu (výchozí 10 %). Pro tloušťku dílu pod 29 mm se používá fošna 32 mm, jinak 50 mm. Objem × cena/m³ určuje cenu dřeva; volitelná rezerva se přidává k ceně.
- Ve Výpočtu dřeva jsou nákupní seznam fošen a seznam lamel pro průběžnou spárovku. Délka lamely je delší strana dílu plus nastavitelný přídavek (výchozí 50 mm). Délky v nastavitelném rozmezí (výchozí 50 mm) se sčítají pod nejdelší nákupní délkou. Pro každou dřevinu, tloušťku fošny a délku se sčítají šířky kratších stran násobené počtem kusů; výsledek se navýší o nastavitelný prořez (výchozí 10 %) a zaokrouhlí nahoru na celý mm. Plocha a objem nákupních fošen se počítají z takto připravených lamel; potřebná šířka fošny při délce 3 m nebo 4 m se zaokrouhlí nahoru na 10 cm. Nastavení se ukládá se zakázkou a ovlivňuje i cenovou kalkulaci dřeva. Tlačítko **Tisk lamel a dílců** otevře tiskový dialog s dílenským seznamem bez cen.
- Materiál, práce, služby a režijní činnosti používají množství × jednotkovou cenu. Režie z materiálu a marže kategorií mají samostatně nastavitelné sazby. Sleva se uplatní na konečnou cenu před DPH.
- V nabídce **Výchozí nastavení ceny** lze uložit rezervu dřeva, režii materiálu, marže a slevu pro nové zakázky. Výchozí sazby se ukládají do databáze. Již vytvořené zakázky si zachovají vlastní hodnoty.
- Práce a mechanizace se po dnech zadávají v hodinách a minutách. Zaznamenaný čas se ukládá bez zaokrouhlení; součet stejné položky se stejnou cenou a DPH v jednom dni se pro kalkulaci a fakturu zaokrouhlí nahoru na 15 minut.
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

Agent musí běžet na Windows a mít v `PATH` PowerShell 5.1, .NET SDK 9 a Node.js 20 nebo novější (včetně `npm.cmd`). Skript spustí `npm ci`, sestaví frontend a backend, spustí API na volném lokálním portu se samostatnou SQLite databází a provede všechny tři smoke testy. Výsledky jsou vidět jako testy v TeamCity; logy API se ukládají jako artefakty `api-logs`. Databáze a proces API se po běhu odstraní.

Stejný postup lze spustit lokálně z kořene repozitáře:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/ci.ps1
```
