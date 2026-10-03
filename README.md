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

## Výpočet

- Plocha dílu je šířka × délka × počet v m². Pro tloušťku dílu pod 29 mm se používá fošna 32 mm, jinak 50 mm. Objem × cena/m³ určuje cenu dřeva; volitelná rezerva se přidává k ceně.
- Materiál, práce, služby a režijní činnosti používají množství × jednotkovou cenu. Režie z materiálu a marže kategorií mají samostatně nastavitelné sazby. Sleva se uplatní na konečnou cenu před DPH.
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
