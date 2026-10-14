# Moshtar Rental

Verhuurplatform voor springkastelen en spellen, opgezet om later als multi-tenant SaaS
aan andere verhuurbedrijven aangeboden te worden.

## Wat zit er al in

- **Reservatiewebsite** (`/`): de klant kiest eerst zijn huurperiode ("Kies je datum", `?van=…&tot=…` in de URL) en
  ziet dan wat vrij is, op de home en op de pagina van elke categorie (`/springkastelen`, slug per taal). De pagina van
  een artikel of pakket toont beschikbaarheid en totaalprijs voor die huurperiode, en pas dan het reservatieformulier.
  Voor zoekmachines en AI-bots heeft elke pagina titel, beschrijving, Open Graph en JSON-LD (LocalBusiness, Product,
  FAQPage, BreadcrumbList), en elke verhuurder een eigen `/robots.txt`, `/sitemap.xml` en `/llms.txt`.
  Eén modern sjabloon, ingekleurd met de huisstijl van de verhuurder (logo, kleur en sfeerfoto, in te stellen op
  `/admin/huisstijl`). De publieke pagina's zijn statisch, zonder JavaScript en zonder MudBlazor (stijl in `wwwroot/site.css`).
  De footer en de contactpagina (`/contact`) tonen de bedrijfsgegevens en het werkgebied, in te stellen op
  `/admin/bedrijfsgegevens`. Titel en ondertitel van de home en de veelgestelde vragen (`/veelgestelde-vragen`) past de
  beheerder aan op `/admin/websiteteksten`; anders gelden de standaardteksten van Moshtar. Categorieën maakt hij aan op
  `/admin/categorieen`, met naam, slug en inleiding per taal en een volgorde die hij versleept; een lege categorie kan
  hij er verwijderen. Een gewijzigde slug stuurt het oude adres met een 301 door naar het nieuwe, per taal (tabel
  `OldSlugs`, ook bedoeld voor artikelen en pakketten).
  Met een GA4-meet-ID (op `/admin/bedrijfsgegevens`) vraagt een toestemmingsbalk of Google Analytics mag; het script
  laadt pas na "Accepteren" en meet dan ook gecontroleerde beschikbaarheid en bevestigde reservaties, zonder
  persoonsgegevens. De keuze staat in de cookie `moshtar-analytics` (`POST /toestemming`, wijzigen via "Cookie-instellingen"
  in de footer). Elke verhuurder heeft een privacyverklaring per taal (`/privacy`, `/fr/confidentialite`, `/en/privacy`).
  Na "Kies je datum" legt de klant artikelen en pakketten in zijn **winkelkar** (`/winkelkar`, `/fr/panier`, `/en/cart`),
  voor één huurperiode; daar wijzigt hij aantallen, verwijdert hij lijnen of kiest hij een andere datum. De winkelkar staat
  op de server, de klant heeft alleen de sleutel in de functionele cookie `moshtar-winkelkar` (7 dagen). Hij houdt geen
  voorraad vast: wat intussen niet meer vrij is, staat aangeduid.
  Een reservatie wordt meteen bevestigd als de voorraad het toelaat. De klant krijgt dan een bevestigingsmail in
  zijn taal (NL, FR of EN), ook als de reservatie in het back office ingevoerd werd.
- **Back office** (`/admin`): reservaties bekijken, van status veranderen en annuleren, met de historiek per reservatie
  (wie wat deed en wanneer). Zelf een reservatie invoeren, bv. voor een klant die belt (`/admin/reservaties/nieuw`),
  met dezelfde voorraadcontrole als de reservatiewebsite. Voorraad en prijzen van artikelen aanpassen, categorieën beheren.
  Gebruikers loggen in met e-mail en wachtwoord op het domein van hun verhuurder (`/admin/inloggen`).
  Wie zijn wachtwoord vergeten is, vraagt een herstelmail aan (`/admin/wachtwoord-vergeten`).
  Een Beheerder nodigt gebruikers uit, wijzigt hun rol en schakelt ze uit of weer in (`/admin/gebruikers`).
- **Meertalig**: NL (standaard), FR en EN. De taal staat in de URL: de standaardtaal van de verhuurder zonder voorvoegsel,
  de andere met `/fr/` of `/en/` (bv. `/fr/louer/springkasteel-jungle`), met hreflang-links tussen de versies (zie
  `docs/adr/0004-taal-staat-in-de-url-van-de-reservatiewebsite.md`). Elke verhuurder heeft één hoofddomein; zijn
  andere domeinen sturen daarheen door.
- **Multi-tenant**: elke tabel heeft een `TenantId`, de verhuurder wordt herkend aan de domeinnaam.
  Moshtar is het platform; Hopsakee.fun (hopsakee.fun) is de eerste verhuurder en de demo-data in de lokale omgeving.
  Zie `GLOSSARY.md` voor de afgesproken termen.

## Structuur

```
src/
  Moshtar.Domain          entiteiten en bedrijfsregels (beschikbaarheid, prijzen)
  Moshtar.Application     contracten voor use cases (reserveren, catalogus, tenant)
  Moshtar.Infrastructure  EF Core + PostgreSQL, reservatieservice, tenantopzoeking, migraties
  Moshtar.Web             Blazor Web App: reservatiewebsite (static SSR) en back office (MudBlazor, interactive server)
tests/
  Moshtar.Domain.Tests    unit tests voor beschikbaarheid en prijzen
  Moshtar.Web.Tests       webapp via HTTP tegen een echte PostgreSQL (inloggen, rechten)
```

### Belangrijke keuzes

- **Beschikbaarheid**: per dag wordt de voorraad vergeleken met bevestigde reservaties en blokkeringen.
  Pakketten worden opengeklapt naar hun artikelen, en de bufferdagen van de tenant (leveren, poetsen)
  worden rond elke verhuur gelegd. Zie `AvailabilityCalculator`.
- **Geen dubbele reservaties**: reservaties per tenant worden binnen een transactie met een
  PostgreSQL advisory lock na elkaar afgehandeld. Zie `ReservationService`.
- **Prijzen**: eerste dag aan dagprijs, elke extra dag aan de prijs per extra dag (incl. btw).
- **E-mail**: mails vertrekken via Azure Communication Services vanaf het eigen domein van de verhuurder
  (`SenderEmail`, bv. noreply@hopsakee.fun), met zijn contactadres als antwoordadres (zie
  `docs/adr/0002-verhuurder-mailt-vanaf-eigen-domein.md`). Zonder `Mail:AzureCommunicationServicesConnectionString`
  wordt niets verstuurd: mails worden dan enkel gelogd, zoals lokaal en in de tests. Lokaal vind je zo
  ook de link uit een uitnodiging of herstelmail terug in de console.
- **Vertalingen**: UI-teksten in `Resources/SharedResource.*.resx`, inhoud (namen, beschrijvingen) als JSON in de database.

## Lokaal draaien

Vereisten: .NET 10 SDK en Docker.

```bash
docker compose up -d                          # PostgreSQL op localhost:5432
dotnet run --project src/Moshtar.Web          # migreert de database en laadt demo-data
```

Open daarna http://localhost:5016 (reservatiewebsite) of http://localhost:5016/admin (back office).
Lokaal log je in als `beheerder@hopsakee.fun` (Beheerder) of `medewerker@hopsakee.fun` (Medewerker),
beide met wachtwoord `hopsakee-demo-wachtwoord`. Een Medewerker volgt reservaties op, maar beheert geen
artikelen, prijzen, voorraad, instellingen of gebruikers.

De eerste Beheerder van een verhuurder maak je bij de installatie aan (er is geen registratiepagina):

```bash
MOSHTAR_WACHTWOORD='...' dotnet run --project src/Moshtar.Web -- beheerder-aanmaken hopsakee sven@hopsakee.fun
```

Zonder `MOSHTAR_WACHTWOORD` vraagt het commando het wachtwoord (minstens 12 tekens).

Tests: `dotnet test`. De webtests maken per testklasse een eigen database aan op de PostgreSQL uit
`MOSHTAR_TEST_DB` (standaard de lokale server uit `docker compose`).

Nieuwe migratie:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Naam> --project src/Moshtar.Infrastructure --output-dir Persistence/Migrations
```

## Volgende stappen

- Winkelmandje met meerdere artikelen en pakketten per reservatie
- Beheer van artikelen, pakketten, categorieën en foto's (aanmaken en vertalen)
- Bevestigingsmails naar klant en verhuurder
- Kalenderweergave van reservaties en blokkeringen
- Hosting op Azure (App Service + Azure Database for PostgreSQL) met deploy via GitHub Actions
