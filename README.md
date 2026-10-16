# Moshtar Rental

Verhuurplatform voor springkastelen en spellen, opgezet om later als multi-tenant SaaS
aan andere verhuurbedrijven aangeboden te worden.

## Wat zit er al in

- **Reservatiewebsite** (`/`): de klant kiest eerst zijn huurperiode ("Kies je datum", `?van=…&tot=…` in de URL) en
  ziet dan wat vrij is, op de home en op de pagina van elke categorie (`/springkastelen`, slug per taal). De pagina van
  een artikel of pakket toont beschikbaarheid en totaalprijs voor die huurperiode, en pas dan het reservatieformulier.
  Voor zoekmachines en AI-bots heeft elke pagina titel, beschrijving, Open Graph en JSON-LD (LocalBusiness, Product,
  FAQPage, BreadcrumbList), en elke verhuurder een eigen `/robots.txt`, `/sitemap.xml` en `/llms.txt`.
  Eén modern sjabloon, ingekleurd met de huisstijl van de verhuurder (kleur, en een geüpload logo en sfeerfoto, in te
  stellen op `/admin/huisstijl`). De publieke pagina's zijn statisch, zonder JavaScript en zonder MudBlazor (stijl in `wwwroot/site.css`).
  De footer en de contactpagina (`/contact`) tonen de bedrijfsgegevens en het werkgebied, in te stellen op
  `/admin/bedrijfsgegevens`. Titel en ondertitel van de home en de veelgestelde vragen (`/veelgestelde-vragen`) past de
  beheerder aan op `/admin/websiteteksten`; anders gelden de standaardteksten van Moshtar. Categorieën maakt hij aan op
  `/admin/categorieen`, met naam, slug en inleiding per taal en een volgorde die hij versleept; een lege categorie kan
  hij er verwijderen. Een gewijzigde slug stuurt het oude adres met een 301 door naar het nieuwe, per taal (tabel
  `OldSlugs`, ook voor artikelen en pakketten).
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
- **Back office** (`/admin`): opent op de **Planning** van vandaag (`/admin/planning?dag=…`): per dag de leverronde
  (reservaties die die dag beginnen) en de ophaalronde (die die dag eindigen), op postcode gesorteerd, met klant,
  telefoon, adres, artikelen (pakketten opengeklapt), opmerkingen en status, een knop "Geleverd" of "Teruggebracht" en
  "Navigeer" (Waze). Wie zelf komt afhalen of terugbrengt, staat apart. Werkt ook op een gsm. Verder reservaties bekijken, van status veranderen en annuleren, met de historiek per reservatie
  (wie wat deed en wanneer). Zelf een reservatie invoeren, bv. voor een klant die belt (`/admin/reservaties/nieuw`),
  met dezelfde voorraadcontrole als de reservatiewebsite. Categorieën beheren. Artikelen aanmaken en wijzigen
  (`/admin/artikelen`): naam en beschrijving per taal (alleen de standaardtaal verplicht; het overzicht toont welke
  vertalingen ontbreken), categorie, kenmerken, voorraad, prijzen en slug, met een volgorde binnen de categorie die de
  beheerder versleept. Een artikel kan uitgezet worden, en verwijderd zolang geen reservatie, pakket of winkelkar ernaar
  verwijst; zit het (nooit gereserveerd) in een pakket, dan zegt het back office uit welk pakket het eerst moet. Een
  voorraad onder wat al gereserveerd is, mag, met een waarschuwing per dag die het bewaren een tekort gaf of het tekort
  groter maakte (`ShortageCalculator.NewOrWorse`); hetzelfde na het bewaren van een blokkering.
  Pakketten aanmaken en wijzigen (`/admin/pakketten`): naam en beschrijving per taal, artikelen met aantallen, eigen
  prijs, slug (gedeeld met de artikelen) en optioneel tot 10 eigen foto's; zonder eigen foto's toont de
  reservatiewebsite die van de artikelen. Een pakket staat niet op de reservatiewebsite zolang een artikel erin
  uitgezet is; het back office zegt welk. Uitzetten en verwijderen (zolang geen reservatie of winkelkar ernaar
  verwijst) zoals bij artikelen.
  Artikelen en pakketten uitlichten met de ster in hun overzicht; de lijst Uitgelicht (`/admin/uitgelicht`) toont ze
  samen in een volgorde die de beheerder versleept. Zonder gekozen huurperiode toont de home ze in die volgorde in
  plaats van "Populair" (uitgezette artikelen en verborgen pakketten niet); is niets uitgelicht, dan de pakketten en het
  eerste artikel van elke categorie. Met een huurperiode staan de uitgelichte bovenaan, met hun beschikbaarheid, en
  daaronder het hele aanbod.
  Prijsregels (`/admin/prijsregels`, alleen Beheerder), met eerst de combinatiekortingen: bij een minimum aantal stuks
  uit een categorie of uit gekozen artikelen en pakketten krijgt het goedkoopste stuk van elke volledige groep een
  kortingspercentage, met een naam per taal, aan/uit en een optionele einddatum. Eén prijsberekening
  (`PriceCalculator`) voor de winkelkar, reserveren (website en back office), Open winkelkarren, de herinneringsmail en
  GA4; een reservatie bewaart elke korting als eigen lijn met de naam van de regel, en houdt die als de regel later
  wijzigt. De artikelpagina noemt de combinatiekorting waarin het artikel valt.
  Periodeprijzen (tabblad op dezelfde pagina, `?tab=periodeprijzen`): elke dag van de huurperiode in bepaalde data en/of
  op bepaalde weekdagen wordt −90% tot +200% duurder of goedkoper, voor alles of gekozen artikelen, pakketten en
  categorieën; op één dag tellen de percentages op en kortingen rekenen daarna. Met een gekozen datum tonen kaarten,
  artikelpagina, winkelkar, structured data en GA4 de prijs mét periodeprijs; zonder datum de gewone prijs.
  Acties (`?tab=acties`): 1–100% korting voor reservaties gemaakt tussen een start- en einddatum, optioneel enkel voor
  huurperiodes binnen bepaalde data, op alles of gekozen artikelen, pakketten en categorieën, met een naam per taal.
  Elk stuk krijgt hoogstens één korting: de voordeligste van acties en combinatiekortingen (van twee acties de
  hoogste); een stuk met een betere actie telt mee voor de groep van een combinatiekorting, maar de korting gaat naar
  het volgende goedkoopste stuk. Kaart en artikelpagina tonen de oude prijs doorstreept met "Actie −10%"; zonder datum
  alleen een actie die niet aan huurperiodes gebonden is. Structured data krijgt de actieprijs.
  De lijst Tekorten (`/admin/tekorten`, voor Beheerder en Medewerker, met een teller in het menu) toont per artikel de
  dagen vanaf vandaag met een tekort en elke getroffen reservatie, ook als het artikel in een gereserveerd pakket zit.
  Per getroffen reservatie staan er de alternatieven (artikelen uit dezelfde categorie, aan en vrij in de volledige
  huurperiode) met het prijsverschil als hulp; de aangevinkte gaan met één klik en een optionele meerprijs naar de klant,
  in zijn taal (mailsjabloon "Alternatieven bij een tekort"). Wanneer en door wie komt per artikel bij de reservatie en in
  de historiek, de meerprijzen worden bewaard voor het vervangen (`IReservationAlternatives`).
  Heeft de klant gekozen, dan vervangt "Vervangen" (in Tekorten, of "Artikel vervangen" bij de reservatie) het artikel
  door het alternatief, met de meerprijs uit de mail voorgevuld. Een losse lijn krijgt het andere artikel aan dezelfde
  prijs; in een pakket wordt enkel het artikel binnen het pakket vervangen, als notitie op de pakketlijn ("Jungle
  vervangen door Piraat") in het back office en de mails, en de pakketprijs blijft. Een meerprijs wordt een aparte lijn.
  Het komt in de historiek en de klant krijgt een bijgewerkte bevestiging (`IReservationService.ReplaceItemAsync`).
  Het **klantenbestand** (`/admin/klanten`, voor Beheerder en Medewerker): zoeken op naam, bedrijfsnaam, e-mail,
  telefoon of gemeente, filteren op particulier of zakelijk, met per klant het aantal reservaties en de laatste
  huurperiode (zonder geannuleerde). De klantfiche toont de gegevens, alle reservaties (ook geannuleerde), of de klant
  marketingcontact is (alleen tonen) en of hij afgemeld is voor reviewvragen, en wanneer en door wie (een gebruiker of
  "via website") hij aangemaakt en laatst gewijzigd werd. Klanten manueel toevoegen en wijzigen (`/admin/klanten/nieuw`):
  particulier of zakelijk (bedrijfsnaam verplicht, btw-nummer optioneel), een apart factuuradres als "Factuuradres is
  hetzelfde" uit staat, en een interne notitie. Het e-mailadres is uniek per verhuurder (`ICustomerAdministration`);
  reserveren werkt de klant bij, maar nooit de notitie. Alleen de Beheerder verwijdert op de klantfiche een klant
  zonder reservaties, of **anonimiseert** een klant met reservaties die geen meer lopen: naam, bedrijfsnaam, btw-nummer,
  e-mail (een uniek adres op `geanonimiseerd.invalid`), telefoon, adressen en notitie worden gewist, net als de straat
  en de opmerking van zijn reservaties en zijn marketingcontact; de reservaties en een afmelding voor reviewvragen
  blijven, en een geanonimiseerde klant krijgt geen reviewvraag meer.
  Bij het reserveren (stap 2 van de winkelkar, zonder JavaScript: "Bijwerken" toont de juiste velden) kiest de klant
  "Particulier" of "Bedrijf of organisatie" en kan hij een apart factuuradres opgeven; een btw-nummer krijgt een
  formaatcontrole per EU-land (`VatNumberFormat`). In het back office zoekt Nieuwe reservatie een bestaande klant, of
  start je vanaf de klantfiche (`/admin/reservaties/nieuw?klant=…`); het leveradres is standaard het adres van de klant.
  Mailsjablonen kennen `{customerCompanyName}`, `{customerVatNumber}` en `{billingAddress}` (valt terug op het adres).
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
  worden rond elke verhuur gelegd, niet rond blokkeringen. Een artikel dat voor een reservatie binnen een pakket
  vervangen is, telt als het vervangende artikel (`ReservationLine.Replacements`). Een blokkering zonder einddatum telt voor alle dagen vanaf
  haar begindatum (`Blockout.Period`). Zie `AvailabilityCalculator`. Tekorten (dagen vanaf vandaag met meer gereserveerd
  dan verhuurbaar) worden op dezelfde manier berekend, niet bewaard. Zie `ShortageCalculator` en `ShortageQuery`.
- **Geen dubbele reservaties**: reservaties per tenant worden binnen een transactie met een
  PostgreSQL advisory lock na elkaar afgehandeld, net als een artikel vervangen. Zie `ReservationService`.
- **Prijzen**: eerste dag aan dagprijs, elke extra dag aan de prijs per extra dag (incl. btw).
- **E-mail**: mails vertrekken via Azure Communication Services vanaf het eigen domein van de verhuurder
  (`SenderEmail`, bv. noreply@hopsakee.fun), met zijn contactadres als antwoordadres (zie
  `docs/adr/0002-verhuurder-mailt-vanaf-eigen-domein.md`). Zonder `Mail:AzureCommunicationServicesConnectionString`
  wordt niets verstuurd: mails worden dan enkel gelogd, zoals lokaal en in de tests. Lokaal vind je zo
  ook de link uit een uitnodiging of herstelmail terug in de console.
- **Kaart**: adressen en de kaart in de planning komen van Azure Maps (`docs/adr/0006-azure-maps-voor-kaart-en-routes.md`), met de sleutel uit
  `Maps:AzureMapsKey` (adressen en routes, enkel op de server). Zonder sleutel zoekt een lokale vervanger de adressen
  op en berekent hij routes: die vindt elk adres met een Belgische postcode en een huisnummer (zonder huisnummer:
  "Adres niet gevonden") en rijdt in vogelvlucht aan 50 km/u. In de tests is het altijd de vervanger. De kaart in de
  browser gebruikt een aparte sleutel, `Maps:AzureMapsBrowserKey`, die in Azure beperkt is tot kaarttegels; zonder die
  sleutel toont de planning geen kaart.
- **Rondes**: "Route voorstellen" bewaart een ronde (`Round`) met de snelste volgorde; de uren en verwachte
  tijdsvensters rekent `RoundTimes` uit, een nieuwe stop komt via `RoundInsertion` op de goedkoopste plek. Zie
  `IRoundPlanner`.
- **Afbeeldingen**: logo, sfeerfoto en foto's (tot 10 per artikel, de eerste is de hoofdfoto) uploadt de beheerder; Moshtar bewaart ze zelf, per verhuurder
  gescheiden, verkleind naar vaste breedtes (400, 800 en 1600 px) als WebP, en bewaart enkel de sleutel; het publieke
  adres wordt eruit afgeleid (zie `docs/adr/0005-verhuurder-uploadt-afbeeldingen-naar-moshtar.md` en `IImageStore`).
  Aanvaard: JPG, PNG, WebP of GIF tot 10 MB. In productie in Azure Blob Storage
  (`Images:AzureBlobStorageConnectionString`, container `Images:Container`, standaard `afbeeldingen`, met publieke
  leestoegang; optioneel een CDN in `Images:PublicBaseUrl`). Zonder connection string, zoals lokaal en in de tests,
  staan ze in een map van de app (`Images:LocalPath`, standaard `App_Data/afbeeldingen`) en serveert de app ze zelf
  onder `/afbeeldingen`, op het hoofddomein van de verhuurder of op `Images:PublicBaseUrl` (lokaal `http://localhost:5016/afbeeldingen`).
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
