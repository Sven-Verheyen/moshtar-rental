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

**Bedrijfsgegevens** (`BusinessDetails`):
Het adres, telefoonnummer, WhatsApp-nummer, btw-nummer, de sociale media en het Google-bedrijfsprofiel van een verhuurder, zoals klanten ze op de reservatiewebsite zien, en het meet-ID van zijn Google Analytics.
_Avoid_: profiel, contactgegevens

**Toestemming** (`AnalyticsConsent`):
De keuze van een bezoeker van de reservatiewebsite of de verhuurder hem mag meten met Google Analytics. Zonder toestemming laadt Google Analytics niet; de bezoeker kan zijn keuze altijd wijzigen.
_Avoid_: cookie-akkoord, consent

**Privacyverklaring**:
De pagina van de reservatiewebsite die per taal uitlegt welke gegevens een verhuurder bewaart, waarom, welke cookies hij gebruikt en wat de rechten van de bezoeker zijn.
_Avoid_: privacybeleid, cookiebeleid

**Werkgebied** (`ServiceArea`):
De lijst van gemeenten waar een verhuurder levert.
_Avoid_: regio, leveringszone

**Hoofddomein** (`PrimaryHost`):
Het ene domein waarop de reservatiewebsite van een verhuurder getoond wordt. Zijn andere domeinen sturen door naar het hoofddomein.
_Avoid_: hoofdsite, standaarddomein

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

**Categorie** (`Category`):
Een groep artikelen van dezelfde soort, zoals springkastelen of spellen, met een eigen pagina op de reservatiewebsite.
_Avoid_: soort, type, rubriek

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
De hele dagen waarvoor gehuurd wordt, begin- en einddag inbegrepen. Op de reservatiewebsite kiest de klant ze met
"Kies je datum"; ze staat dan in de URL (`?van=…&tot=…`) en gaat mee naar elke pagina.
_Avoid_: periode, verhuurdagen

**Winkelkar** (`Cart`):
De artikelen en pakketten die een klant op de reservatiewebsite verzamelt om ze samen in één reservatie te reserveren, voor één huurperiode. Iets in de winkelkar leggen houdt geen voorraad vast; pas reserveren doet dat. Een winkelkar die 7 dagen niet gewijzigd en niet gereserveerd werd, verdwijnt.
_Avoid_: winkelmandje, mandje, selectie, bestelling

**Marketingcontact** (`MarketingContact`):
Een e-mailadres van een klant of bezoeker die uitdrukkelijk toestemming gaf om nieuws en acties van een verhuurder te ontvangen, met zijn taal en het tijdstip van die toestemming.
_Avoid_: nieuwsbriefabonnee, lead, prospect

**Blokkering** (`Blockout`):
Een periode waarin exemplaren van een artikel niet verhuurbaar zijn, bijvoorbeeld voor onderhoud.
_Avoid_: onderhoud, uitsluiting

**Bufferdag** (`BufferDays`):
Een dag voor of na een huurperiode waarop het exemplaar nog niet vrij is, voor levering, opbouw of poetsen.
_Avoid_: marge, wachttijd

## Planning en leveringen

**Planning** (`Schedule`):
Het overzicht per dag, week of maand van de leveringen en ophalingen van een verhuurder.
_Avoid_: kalender, agenda, rooster

**Levering** (`Delivery`):
Het brengen en opbouwen van de artikelen van een reservatie op het leveradres, op de begindag van de huurperiode.
_Avoid_: vertrek, afzetten

**Ophaling** (`Collection`):
Het afbouwen en terughalen van de artikelen van een reservatie, op de einddag van de huurperiode.
_Avoid_: terugkomst, afhalen, retour

**Afhalen** (`Pickup`):
Een klant die de artikelen zelf komt halen en terugbrengt, als de verhuurder dat aanbiedt. Afhalen is nooit een ophaling.
_Avoid_: ophalen, zelf ophalen

**Ronde** (`Round`):
De leveringen of de ophalingen van één dag die één bestelwagen na elkaar rijdt, vanaf en terug naar het adres van de verhuurder, met een vertrekuur. Elke dag heeft een leverronde en een ophaalronde.
_Avoid_: route, rit, toer

**Verwacht tijdsvenster** (`ExpectedWindow`):
Het tijdsvak waarin de verhuurder verwacht bij een klant aan te komen voor een levering of ophaling, zoals "tussen 9u30 en 10u30", zoals hij het de klant meedeelt.
_Avoid_: leveruur, aankomsttijd, ETA

**Leverlijst** (`DeliverySheet`):
Het printblad van één ronde: de leveringen of ophalingen in volgorde, met hun verwacht uur, adres, klant en artikelen, en bovenaan hoeveel van elk artikel mee moet.
_Avoid_: rittenblad, routeblad, pakbon

## Communicatie

**Mailsjabloon** (`MailTemplate`):
De tekst en het onderwerp van een soort mail die een verhuurder aan zijn klanten stuurt, per taal, met plaatshouders voor de gegevens van de reservatie. Zolang de verhuurder niets aanpast, geldt de standaardtekst van Moshtar.
_Avoid_: template, e-mailtekst, mailtemplate

**Websitetekst** (`WebsiteText`):
Een tekst van de reservatiewebsite die een verhuurder per taal kan aanpassen: titel en ondertitel van de home, naam, slug en inleiding van een categorie, en de veelgestelde vragen. Zolang de verhuurder niets aanpast, geldt de standaardtekst van Moshtar, met zijn naam en werkgebied ingevuld.
_Avoid_: content, pagina-inhoud, copy

**Huisstijl** (`Branding`):
Het logo, de kleur en de sfeerfoto van een verhuurder. Logo en kleur komen automatisch in zijn klantmails en op zijn reservatiewebsite, de sfeerfoto bovenaan zijn reservatiewebsite. Logo en sfeerfoto zijn adressen van afbeeldingen op de eigen website van de verhuurder.
_Avoid_: branding, thema, look
