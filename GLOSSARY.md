# Moshtar

Verhuurplatform waarop verhuurbedrijven hun springkastelen en spellen laten reserveren en hun verhuur beheren.

## Verhuurders en gebruikers

**Moshtar**:
Het platform zelf, dat aan verhuurders wordt aangeboden. Moshtar is nooit zelf een verhuurder.
_Avoid_: Moshtar Rental, de software

**Verhuurder** (`Tenant`):
Een bedrijf dat Moshtar gebruikt om zijn aanbod te verhuren. Hopsakee.fun is de eerste verhuurder.
_Avoid_: tenant (in gesprekken), klant, bedrijf

**Gebruiker** (`User`):
Een persoon die namens precies één verhuurder in het back office werkt.
_Avoid_: account, admin, medewerker (als algemene term)

**Beheerder** (`Administrator`):
Een gebruiker die alles mag binnen zijn verhuurder, ook prijzen, voorraad, instellingen en gebruikers.
_Avoid_: admin, eigenaar

**Medewerker** (`Staff`):
Een gebruiker die reservaties opvolgt, maar geen prijzen, voorraad, instellingen of gebruikers beheert.
_Avoid_: personeel, operator

**Klant** (`Customer`):
Een persoon of organisatie die bij een verhuurder huurt via de reservatiewebsite. Een klant is nooit een gebruiker.
_Avoid_: huurder, gebruiker, client

## Aanbod

**Artikel** (`RentalItem`):
Iets wat verhuurd wordt, zoals een springkasteel of een spel, met een eigen voorraad.
_Avoid_: product, item

**Voorraad** (`Stock`):
Het aantal exemplaren van een artikel dat een verhuurder bezit.
_Avoid_: stock, aantal

**Pakket** (`Bundle`):
Een vaste combinatie van artikelen met een eigen prijs, die als één geheel gereserveerd wordt.
_Avoid_: bundel, combo, set

## Verhuur

**Reservatie** (`Reservation`):
De afspraak dat een klant artikelen of pakketten huurt voor een huurperiode. Een reservatie is bevestigd zodra ze bestaat.
_Avoid_: boeking, bestelling, order

**Reservatiewebsite**:
De publieke website van een verhuurder waarop klanten het aanbod bekijken en reserveren.
_Avoid_: boekingswebsite, front office, webshop

**Reserveren** (`Reserve`):
Een reservatie aanmaken, door een klant op de website of door een gebruiker in het back office.
_Avoid_: boeken, bestellen

**Historiek** (`History`):
De lijst van wat er met een reservatie gebeurde: aangemaakt, status gewijzigd of geannuleerd, telkens met tijdstip en wie het deed. Een gebruiker, of "via website" als de klant het zelf deed.
_Avoid_: audit log, logboek, geschiedenis

**Huurperiode** (`DateRange`):
De hele dagen waarvoor gehuurd wordt, begin- en einddag inbegrepen.
_Avoid_: periode, verhuurdagen

**Blokkering** (`Blockout`):
Een periode waarin exemplaren van een artikel niet verhuurbaar zijn, bijvoorbeeld voor onderhoud.
_Avoid_: onderhoud, uitsluiting

**Bufferdag** (`BufferDays`):
Een dag voor of na een huurperiode waarop het exemplaar nog niet vrij is, voor levering, opbouw of poetsen.
_Avoid_: marge, wachttijd

## Communicatie

**Mailsjabloon** (`MailTemplate`):
De tekst en het onderwerp van een soort mail die een verhuurder aan zijn klanten stuurt, per taal, met plaatshouders voor de gegevens van de reservatie. Zolang de verhuurder niets aanpast, geldt de standaardtekst van Moshtar.
_Avoid_: template, e-mailtekst, mailtemplate

**Huisstijl** (`Branding`):
Het logo en de kleur van een verhuurder, die automatisch bovenaan zijn klantmails komen. Het logo is een adres van een afbeelding op de eigen website van de verhuurder.
_Avoid_: branding, thema, look
