# Taal staat in de URL van de reservatiewebsite, niet in een cookie

Op de reservatiewebsite staat de standaardtaal van de verhuurder zonder voorvoegsel (`/springkastelen`) en elke andere taal met een eigen voorvoegsel (`/fr/...`, `/en/...`), met hreflang-links tussen de versies. Voordien koos een klant de taal met een cookie op dezelfde URL, waardoor zoekmachines en AI-bots alleen de standaardtaal zagen. We kozen URL-voorvoegsels boven een domein per taal (meer domeinen en certificaten per verhuurder) en boven de cookie (onvindbaar in FR en EN). Een artikel houdt één slug in alle talen; vertaalde slugs kunnen later, maar elke wijziging aan een URL vraagt dan een 301-doorverwijzing omdat de oude URL al geïndexeerd is.

Het back office valt hier buiten: dat is Nederlandstalig en niet geïndexeerd.
