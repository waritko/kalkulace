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

Po vytvoření balíčku skript přes SCP zkopíruje obsah rozbalené složky přímo do `waritko@mrazitko.varak.net:/home/waritko/kalkulace`. Soubory `appsettings*` se přes SCP nepřenášejí, ani z podsložek; v lokálním ZIPu a rozbaleném balíčku zůstávají. Vyžaduje klienta OpenSSH (`scp` v `PATH`) a SSH přístup s právem zápisu do cílového adresáře. Používá běžnou konfiguraci SSH; port, klíč a cíl lze změnit parametry. Při chybě přenosu skript skončí chybou, lokální ZIP i rozbalená složka zůstanou k dispozici. SCP přepíše stejnojmenné soubory, ale neodstraní staré soubory na serveru ani nerestartuje aplikaci.

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

## Sklad fošen

V nabídce **Sklad fošen** se každý kus přijímá samostatně zadáním dřeviny, šířky, tloušťky a libovolné kladné délky v celých milimetrech. Pole **Délka (mm)** nabízí hodnoty 3000, 4000 a 5000 mm (3/4/5 m); lze také napsat vlastní délku, například 2755 mm. Dřevinu lze vybrat z návrhů ceníku nebo zadat vlastní. Tloušťku dostupného kusu lze upravit přímo v tabulce; hodnota se uloží po opuštění pole. U starších kusů je tloušťka neznámá a je nutné ji doplnit před použitím v nářezovém plánu. Rozřezané kusy předávají tloušťku svým potomkům. Sklad se ukládá do databáze; při spuštění se doplní potřebná tabulka a sloupce i do existující databáze.

Tlačítko **Rozřezat** nejprve umožní zkrácení na délku. Zkrácené kusy lze dále krátit nebo rozřezat na šířku; po řezu na šířku lze pokračovat na šířku. Zadává se rozměr prvního kusu a prořez pily (výchozí 3 mm, lze zadat i 0). Před potvrzením se zobrazí oba výsledné rozměry. Původní kus se označí jako rozřezaný a oba nové kusy přibudou do skladu. Volba **Zobrazit i rozřezané** ukáže historii s čísly původních a vzniklých kusů. Sklad je samostatná evidence, jeho řezy neovlivňují cenové kalkulace zakázek.

## Nářezový plán

V záložce **Výpočet dřeva** zvolte **Vytvořit nářezový plán**. Nejmenší délka lamely je výchozí 350 mm, nejmenší šířka 50 mm a největší šířka 110 mm před srovnáním. Úběr šířky při srovnávání je výchozí 4 mm celkem na každou lamelu; lze zadat i 0 mm, musí však být menší než nejmenší šířka lamely. Hodnoty lze změnit u zakázky a uložit tlačítkem **Uložit**. Ve výchozím nastavení se používají pro nové zakázky; změna výchozích hodnot nepřepisuje uložené zakázky. Největší šířka spárovky pro spojování více dílců je výchozí 900 mm a ukládá se také se zakázkou a do výchozích nastavení. Starší zakázky dostanou hodnoty 350/50/110 mm, úběr 4 mm a šířku spárovky 900 mm.

Plán vychází přímo ze seznamu dílců a ignoruje procentní prořez i procentní rezervu dřeva. Delší rozměr dílce používá jako délku, kratší jako šířku; přičítá přídavek k délce a rozměry zaokrouhluje nahoru na celé milimetry. Krátké dílce stejné dřeviny a nákupní tloušťky řadí za sebe, dokud součet délek včetně mezer pro řez nedosáhne minimální délky. Šířku takového sloupce určuje nejširší dílec; nedokončený sloupec doplní na minimální délku. Sloupce podobné délky (v nastavené toleranci sloučení délek) spojuje vedle sebe, pokud se vejdou do největší šířky spárovky včetně mezer pro řez. Samostatný dílec širší než limit ponechá samostatně a upozorní na něj. Necelý počet dílců pro plán zaokrouhlí nahoru. Výsledek obsahuje rozměry desek, seznam dílců s jejich polohou a přiřazení lamel k deskám; rozměry dílců ve výsledku zahrnují přídavek.

Použije jen dostupné kusy odpovídající dřeviny a nákupní tloušťky (32 nebo 50 mm); fošny bez zadané tloušťky vynechá s upozorněním. Pro každý kus uvádí jednotlivé lamely, jejich délku, šířku pro řezání, šířku po srovnání a polohu od začátku kusu. Navrhuje nejprve zkrácení na délku, potom podélné řezy; využívá i zbytky. Již podélně rozřezané skladové kusy zachovávají svou délku. Požadovanou šířku pokrývá součet šířek lamel po odečtení úběru při srovnávání. Fyzický prořez pily je 3 mm na řez, včetně oddělení dílců z hotové spárovky. Procentní prořez se nadále používá v nákupním seznamu a cenové kalkulaci.

Pokud se celý požadavek nepodaří pokrýt, zobrazí částečný plán a chybějící součet šířek podle dřeviny, tloušťky a délky. Plánování postupuje od nejdelších lamel a přednostně vybírá nejkratší vhodné kusy; nevyhledává všechny možné kombinace a nezaručuje nejmenší odpad. Při neúplném plánu lze doplnit sklad nebo upravit parametry. Limit je 20 000 dílců a 20 000 lamel na jeden plán.

Vytvoření plánu sklad nemění a materiál nerezervuje. Před řezáním plán znovu vytvořte podle aktuálního skladu a zkontrolujte stav dřeva. Změna zadání ruší zobrazený plán; parametry zůstávají uloženy se zakázkou, samotný seznam se vždy vytváří z aktuálního skladu. Nářezové parametry nemění cenu kalkulace.

Po vytvoření plánu tlačítko **Tisk nářezového plánu** otevře tiskový dialog prohlížeče pro tisk nebo uložení PDF. Sestava nejprve uvádí rozřezání na délku podle čísel skladových fošen: každý úsek má označení, polohu od začátku fošny, délku a počet lamel. Úseky stejné délky na různých místech fošny jsou uvedeny samostatně. Druhá část začíná na nové stránce a pro každý označený úsek uvádí lamely v pořadí na šířku, šířku pro řezání i po srovnání a číslo výsledné desky. Všechny rozměry jsou v milimetrech; polohy zahrnují mezery pro prořez. Tisk obsahuje také upozornění a chybějící materiál při neúplném plánu.

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

Agent musí běžet na Windows a mít v `PATH` PowerShell 5.1, .NET SDK 9 nebo novější (včetně .NET 10) a Node.js 20 nebo novější (včetně `npm.cmd`). Skript spustí `npm ci`, sestaví frontend a backend, spustí API na volném lokálním portu se samostatnou SQLite databází a provede všech pět smoke testů, včetně příjmu a rozřezání fošen a nářezového plánu. Výsledky jsou vidět jako testy v TeamCity; logy API se ukládají jako artefakty `api-logs`. Databáze a proces API se po běhu odstraní.

Stejný postup lze spustit lokálně z kořene repozitáře:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/ci.ps1
```
