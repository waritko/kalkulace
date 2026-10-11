# Pokyny pro práci na projektu

Tyto pokyny platí pro celý repozitář.

## Projekt

Kalkulace je česká webová aplikace pro truhlářské zakázky vycházející ze sešitu `kalkulace.xlsx`. Eviduje zakázky, dřevěné dílce, materiál, práci a mechanizaci po dnech, služby, ceník, výchozí sazby a faktury. Počítá spotřebu a nákup dřeva, lamely pro spárovku, režii, marže, slevu a DPH. Obsahuje samostatný sklad jednotlivých fošen s příjmem, rozřezáváním a historií řezů. Faktury a dílenské seznamy lze tisknout přes prohlížeč.

- Backend: ASP.NET Core 9 (`net9.0`), C#, Minimal API a Entity Framework Core 9. Podporuje SQLite a MySQL přes Pomelo; při spuštění vytváří a aktualizuje databázové schéma.
- Frontend: React 19, TypeScript 5.9, Vite 6 a CSS; ikony z `lucide-react`.
- Lokální databáze: SQLite v `backend/Data/kalkulace.db`. Produkční konfigurace používá MySQL. Provider a připojení se nastavují v `appsettings*.json` nebo proměnnými `Database__Provider` a `ConnectionStrings__MySql`.
- Aplikace nemá vlastní přihlašování; nasazení předpokládá ověřování na HTTP proxy.
- Podrobné uživatelské a provozní informace jsou v `README.md`.

## Mapa repozitáře

- `backend/Program.cs`: HTTP API pod `/api`, konfigurace databáze a podávání frontendu.
- `backend/Models.cs`: datové kontrakty, validace, cenové výpočty, výchozí katalog a automatické položky.
- `backend/AppDb.cs`, `backend/EntityMapping.cs`: relační entity, databázový kontext a převody mezi entitami a API.
- `backend/DatabaseUpgrade.cs`: inicializace a aktualizace schématu včetně převodu starších dat.
- `backend/BoardStock.cs`: sklad fošen a pravidla rozřezávání.
- `frontend/src/App.tsx`: hlavní uživatelské rozhraní; další komponenty řeší ceník, denní záznamy, nákup dřeva a sklad fošen. `types.ts` obsahuje frontendové kontrakty, `workTime.ts` práci s časem.
- `tests/`: smoke testy API, denních záznamů, povrchové úpravy a skladu; `ci.ps1` sestaví aplikaci a spustí testy s dočasnou SQLite databází.
- `.teamcity/settings.kts`: konfigurace CI v Kotlin DSL pro Windows agenta.
- `run-local.ps1`: sestavení frontendu a lokální spuštění aplikace.
- `build.ps1`: release ZIP a rozbalený balíček v `artifacts/`, standardně také přenos přes SCP. Pro pouze lokální sestavení použij `-SkipScp`.
- `run.bat`, `run.sh`: spuštění release balíčku na Windows a Linuxu.
- `kalkulace.xlsx`: původní podklad pro výpočty a ceník.

## Spuštění a ověření

Vyžaduje .NET SDK 9 nebo novější (včetně 10) a Node.js 20 nebo novější s npm.

- Z kořene spusť `./run-local.ps1`; aplikace běží na `http://localhost:5080`.
- Pro samostatný frontend spusť v `frontend/` příkaz `npm run dev`; Vite běží na portu 5173 a předává `/api` backendu na portu 5080.
- Backend sestav příkazem `dotnet build backend/Kalkulace.Api.csproj`.
- Frontend sestav příkazem `npm run build` v `frontend/`; zahrnuje kontrolu TypeScriptu.
- Kompletní existující ověření na Windows: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/ci.ps1`.
- `tests/api-smoke.ps1` lze spustit proti běžícímu backendu; ověřuje výpočet, uložení zakázky a neměnnost faktury.
- Rozsah ověření přizpůsob změně. U samotné dokumentace zkontroluj správnost obsahu a `git diff --check`; sestavení aplikace není nutné.

## Pravidla změn

1. Po každém vyřešeném požadavku proveď `git commit`. Před commitem dokonči přiměřené ověření a zkontroluj diff. Commituj jen soubory související s požadavkem, včetně aktualizace tohoto dokumentu; nepřidávej cizí rozpracované změny. Zpráva commitu má stručně popsat výsledek. Pokud commit zablokují oprávnění nebo chyba, uveď překážku uživateli a nepovažuj změnu za commitnutou.
2. Relevantní změny projektu průběžně ukládej do `AGENTS.md` ještě před commitem: zejména změny funkcí, architektury, datového modelu, výpočtů, konfigurace, spuštění, testů a nasazení. Aktualizuj příslušnou část tak, aby popisovala aktuální stav; neveď zde deník každé drobné úpravy a neukládej tajné údaje. Při změně uživatelských nebo provozních postupů aktualizuj také `README.md`.
3. Při změně API udržuj soulad backendových kontraktů a `frontend/src/types.ts`. Výpočty a validace musí být správné také na backendu.
4. Zachovej neměnnost uloženého snímku faktury: pozdější úprava zakázky nesmí měnit vystavenou fakturu. Změny ceníku a výchozích sazeb nesmí samovolně přepisovat hodnoty již uložených zakázek.
5. Při změně ukládání dat řeš aktualizaci existujícího schématu v `DatabaseUpgrade.cs` a podporu SQLite i MySQL. Skladové řezy jsou samostatná evidence a neovlivňují cenovou kalkulaci zakázek.
6. Udržuj české uživatelské texty a dokumentaci v UTF-8. Změny cenových výpočtů, DPH a zaokrouhlování ověř příslušnými smoke testy; popis stávajících pravidel je v `README.md`.
7. Necommituj lokální databáze, závislosti ani generované výstupy (`node_modules`, `dist`, `bin`, `obj`, `artifacts`, `tests/.output`). Respektuj `.gitignore`.
