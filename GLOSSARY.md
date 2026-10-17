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
Het adres, e-mailadres, telefoonnummer, WhatsApp-nummer, btw-nummer, de sociale media en het Google-bedrijfsprofiel van een verhuurder, zoals klanten ze op de reservatiewebsite zien. Werkgebied en rondes stelt hij in bij Planning, zijn reviewlink bij Reviewvragen en het meet-ID van zijn Google Analytics bij Huisstijl.
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
Een persoon of organisatie die bij een verhuurder reserveerde, ook als die reservatie geannuleerd werd, of die de verhuurder zelf toevoegde. Een klant wordt herkend aan zijn e-mailadres en is particulier of zakelijk. Een bezoeker die alleen een winkelkar heeft of alleen marketingcontact is, is nog geen klant. Een klant is nooit een gebruiker.
_Avoid_: huurder, gebruiker, client, contact

**Particuliere klant** (`CustomerKind.Private`):
Een klant die als privépersoon huurt.
_Avoid_: privéklant, consument

**Zakelijke klant** (`CustomerKind.Business`):
Een klant die huurt namens een bedrijf of organisatie, zoals een school, vereniging of gemeente, met een bedrijfsnaam, eventueel een btw-nummer, en een contactpersoon.
_Avoid_: bedrijfsklant, B2B-klant, organisatie

**Klantenbestand** (`Customers`):
Alle klanten van een verhuurder.
_Avoid_: klantenlijst, adresboek, CRM

**Klantfiche**:
De gegevens van één klant in het back office, met zijn reservaties en een interne notitie die de klant nooit ziet.
_Avoid_: klantprofiel, klantdetail, klantkaart

**Factuuradres** (`BillingAddress`):
Het adres van een klant waarop hij gefactureerd wordt. Standaard is het hetzelfde als zijn adres; alleen als het verschilt, staat het apart op zijn klantfiche.
_Avoid_: facturatieadres, betaaladres

**Btw-databank** (`VatRegistry`):
De databank van de Europese Unie (VIES) die zegt of een btw-nummer uit een EU-land bestaat, en voor veel landen op welke naam en welk adres. Een EU-btw-nummer dat de btw-databank na controle niet kent, is ongeldig. Is ze onbereikbaar, dan is het nummer nog niet gecontroleerd. Een btw-nummer van buiten de EU is niet te controleren.
_Avoid_: VIES (in gesprekken), btw-register, KBO

**Anonimiseren** (`Anonymize`):
De persoonsgegevens van een klant wissen, bijvoorbeeld op zijn vraag, terwijl zijn reservaties blijven bestaan zonder te tonen van wie ze waren. Een klant zonder reservaties wordt verwijderd in plaats van geanonimiseerd.
_Avoid_: wissen, vergeten, archiveren

## Back office

**Back office**:
Het beheergedeelte van Moshtar waar de gebruikers van een verhuurder hun verhuur beheren. Het menu is ingedeeld in onderdelen; elke pagina hoort bij precies één onderdeel.
_Avoid_: admin, beheerpagina's, dashboard

**Onderdeel**:
Een groep pagina's in het menu van het back office. Er zijn er zes, in deze volgorde: Planning, Reservaties, Klantenbeheer, Artikelbeheer, Website en mails, Instellingen. Een gebruiker ziet alleen de onderdelen met pagina's die hij mag openen. Het onderdeel Planning bevat de pagina Planning.
_Avoid_: module, sectie, groep, tab

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

**Uitgezet** (`IsActive = false`):
Een artikel of pakket dat de verhuurder bewust uitzette: het staat niet meer op de reservatiewebsite en kan niet meer gereserveerd worden, maar blijft bestaan in oude reservaties. Een pakket dat zelf aan staat maar verborgen is omdat een van zijn artikelen uitgezet is, is niet uitgezet. Een artikel of pakket dat al eens gereserveerd werd, kan alleen uitgezet worden, niet verwijderd.
_Avoid_: gearchiveerd, inactief, verborgen

**Uitgelicht** (`IsFeatured`):
Een artikel of pakket dat de verhuurder bovenaan de home van zijn reservatiewebsite toont, in een volgorde die hij zelf kiest.
_Avoid_: populair, aanbevolen, in de kijker

**Kenmerken**:
De vaste eigenschappen van een artikel die de klant helpen kiezen: afmetingen, leeftijd, maximum aantal personen en of er stroom nodig is. Ze zijn allemaal optioneel.
_Avoid_: specificaties, eigenschappen, specs

**Slug**:
Het stuk van het webadres dat een artikel, pakket of categorie aanduidt, zoals `springkasteel-jungle`. Wordt het gewijzigd, dan stuurt het oude adres door naar het nieuwe.
_Avoid_: url, link, permalink

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
De lijst van wat er met een reservatie gebeurde: aangemaakt, status gewijzigd, geannuleerd, alternatieven gemaild, een artikel vervangen, reviewvraag gemaild of reviewvraag via WhatsApp, telkens met tijdstip en wie het deed: een gebruiker, "via website" als de klant het zelf deed, of "automatisch" als Moshtar het zelf deed, zoals de reviewvraag mailen.
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
Een periode waarin exemplaren van een artikel niet verhuurbaar zijn, bijvoorbeeld omdat ze kapot zijn of in onderhoud. Een blokkering zonder einddatum loopt tot de verhuurder er een invult. Is een exemplaar definitief weg, dan verlaagt de verhuurder de voorraad in plaats van te blokkeren.
_Avoid_: onderhoud, uitsluiting, buiten dienst

**Tekort** (`Shortage`):
Een dag, vanaf vandaag, waarop van een artikel meer exemplaren gereserveerd zijn dan er verhuurbaar zijn, door een blokkering of een lagere voorraad. Een tekort verdwijnt zodra er niet meer gereserveerd is dan verhuurbaar. De reservaties die het artikel op zo'n dag gebruiken, los of in een gereserveerd pakket, zijn de getroffen reservaties.
_Avoid_: overboeking, conflict, dubbele reservatie

**Alternatief** (`Alternative`):
Een ander artikel uit dezelfde categorie dat vrij is in de huurperiode van een reservatie met een tekort, en dat de verhuurder de klant voorstelt in plaats van het artikel dat niet verhuurbaar is. Kiest de klant het, dan vervangt de verhuurder het artikel in de reservatie: op een losse lijn aan dezelfde prijs, in een pakket enkel binnen het pakket voor die reservatie.
_Avoid_: vervanger, suggestie

**Meerprijs** (`Surcharge`):
Wat een klant extra betaalt als hij een bepaald alternatief kiest. De verhuurder beslist per alternatief of er een meerprijs is en hoeveel; standaard is er geen. De meerprijs komt bovenop de reservatie, de prijs van een pakket blijft dezelfde.
_Avoid_: supplement, toeslag, prijsverschil

**Bufferdag** (`BufferDays`):
Een dag voor of na een huurperiode waarop het exemplaar nog niet vrij is, voor levering, opbouw of poetsen.
_Avoid_: marge, wachttijd

## Prijzen

**Prijsregel** (`PricingRule`):
Een regel van een verhuurder die de prijs van artikelen of pakketten in een reservatie verandert: een combinatiekorting, een periodeprijs of een actie. Een reservatie houdt de prijs van het moment van reserveren; een prijsregel die later wijzigt, verandert ze niet.
_Avoid_: prijszetting, promotie, kortingsregel

**Combinatiekorting** (`ComboDiscount`):
Een korting in procent op het goedkoopste stuk van elke volledige groep van een minimum aantal stuks uit dezelfde categorie of dezelfde gekozen artikelen in één reservatie, zoals "bij 2 springkastelen 40% korting op het goedkoopste".
_Avoid_: mengkorting, volumekorting, bundelkorting

**Periodeprijs** (`PeriodPrice`):
Een percentage waarmee de prijs stijgt of daalt voor elke dag van de huurperiode die in bepaalde data of op bepaalde weekdagen valt, zoals "zaterdag 20% duurder". Een periodeprijs is geen korting en geen meerprijs: hij hoort bij de gewone prijs van die dag.
_Avoid_: seizoensprijs, toeslag, meerprijs

**Actie** (`Promotion`):
Een tijdelijke korting in procent voor reservaties die gemaakt worden tussen een start- en einddatum, met een eigen naam per taal, zoals "Reserveer in november: 10% korting".
_Avoid_: promo, aanbieding, solden

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

**Reviewvraag** (`ReviewRequest`):
Het bericht dat een verhuurder een klant de dag na de einddag van elke reservatie stuurt, per mail en via WhatsApp, met de vraag een Google-review te plaatsen. Elke klant krijgt dezelfde vraag, zonder beloning; wie zich afmeldt, krijgt er geen meer.
_Avoid_: reviewverzoek, feedbackmail, enquête

**Reviewlink** (`ReviewLink`):
De link van Google waarmee een klant meteen een review over de verhuurder schrijft, ook als QR-code. De verhuurder stelt hem in bij Reviewvragen; hij is iets anders dan de link naar het Google-bedrijfsprofiel.
_Avoid_: Google-link, reviewpagina

**Afgemeld voor reviewvragen** (`ReviewOptOut`):
Een e-mailadres dat via de link onderaan een reviewvraag liet weten geen reviewvragen meer te willen. Het krijgt bij die verhuurder geen reviewvraag meer, per mail noch via WhatsApp; reservatiemails en een toestemming als marketingcontact blijven ongewijzigd.
_Avoid_: uitgeschreven, opt-out, blacklist

**Huisstijl** (`Branding`):
Het logo, de kleur en de sfeerfoto van een verhuurder. Logo en kleur komen automatisch in zijn klantmails en op zijn reservatiewebsite, de sfeerfoto bovenaan zijn reservatiewebsite. Logo en sfeerfoto uploadt de verhuurder in het back office.
_Avoid_: branding, thema, look
