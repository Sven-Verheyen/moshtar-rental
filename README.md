# Moshtar Rental

Verhuurplatform voor springkastelen en spellen, opgezet om later als multi-tenant SaaS
aan andere verhuurbedrijven aangeboden te worden.

## Wat zit er al in

- **Reservatiewebsite** (`/`): catalogus met artikelen en pakketten, detailpagina met reservatieformulier.
  Een reservatie wordt meteen bevestigd als de voorraad het toelaat.
- **Back office** (`/admin`): reservaties bekijken en annuleren, voorraad en prijzen van artikelen aanpassen.
  Er is nog geen login, dus buiten de ontwikkelomgeving geeft `/admin` voorlopig een 404.
- **Meertalig**: NL (standaard), FR en EN, via een taalkeuze bovenaan de site.
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
```

### Belangrijke keuzes

- **Beschikbaarheid**: per dag wordt de voorraad vergeleken met bevestigde reservaties en blokkeringen.
  Pakketten worden opengeklapt naar hun artikelen, en de bufferdagen van de tenant (leveren, poetsen)
  worden rond elke verhuur gelegd. Zie `AvailabilityCalculator`.
- **Geen dubbele reservaties**: reservaties per tenant worden binnen een transactie met een
  PostgreSQL advisory lock na elkaar afgehandeld. Zie `ReservationService`.
- **Prijzen**: eerste dag aan dagprijs, elke extra dag aan de prijs per extra dag (incl. btw).
- **Vertalingen**: UI-teksten in `Resources/SharedResource.*.resx`, inhoud (namen, beschrijvingen) als JSON in de database.

## Lokaal draaien

Vereisten: .NET 10 SDK en Docker.

```bash
docker compose up -d                          # PostgreSQL op localhost:5432
dotnet run --project src/Moshtar.Web          # migreert de database en laadt demo-data
```

Open daarna http://localhost:5016 (reservatiewebsite) of http://localhost:5016/admin (back office).

Tests: `dotnet test`

Nieuwe migratie:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Naam> --project src/Moshtar.Infrastructure --output-dir Persistence/Migrations
```

## Volgende stappen

- Login voor het back office (ASP.NET Core Identity)
- Winkelmandje met meerdere artikelen en pakketten per reservatie
- Beheer van artikelen, pakketten, categorieën en foto's (aanmaken en vertalen)
- Bevestigingsmails naar klant en verhuurder
- Kalenderweergave van reservaties en blokkeringen
- Hosting op Azure (App Service + Azure Database for PostgreSQL) met deploy via GitHub Actions
