# Verhuurder past klantmails aan met plaatshouders, niet met vrije HTML

Een beheerder past de mailsjablonen voor klantmails aan in een eenvoudige editor (vet, cursief, links, alinea's) met plaatshouders zoals {firstName}, en niet met vrije HTML. Het overzicht van de reservatie (artikelen, huurperiode, totaal, levering) is één vast blok, {reservationDetails}, dat de verhuurder kan verplaatsen maar niet aanpassen; logo en kleur van de verhuurder komen er automatisch bij. We kozen dit boven vrije HTML of volledig vrije sjablonen, omdat vrije HTML makkelijk breekt in mailprogramma's en een verhuurder zo nooit een bevestiging kan versturen zonder de gegevens die de klant nodig heeft. De prijs is minder vormvrijheid: wie een volledig eigen huisstijl wil, kan die niet in de mail kwijt.

Mails aan gebruikers (uitnodiging, wachtwoordherstel) zijn bewust niet aanpasbaar: ze zijn functioneel, en een fout erin sluit iemand buiten het back office.

## Gevolgen

- We bewaren alleen wat de verhuurder zelf aanpaste, per mailsjabloon en per taal. Een taal die hij niet aanpaste volgt de standaardtekst van Moshtar, ook als Moshtar die later verbetert.
- De plaatshouders zijn Engelstalig en dezelfde in elke taal, zodat er één lijst is om te kennen.
