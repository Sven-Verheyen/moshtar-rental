# Een gebruiker hoort bij precies één verhuurder

Elke gebruiker van het back office hoort bij precies één verhuurder, en logt in op het domein van die verhuurder. Iemand die voor twee verhuurders werkt, krijgt twee aparte accounts. We kozen dit boven gebruikers die over verhuurders heen gedeeld worden, omdat de tenantscheiding dan eenvoudig en waterdicht blijft (elke gebruiker valt onder dezelfde tenantfilter als de rest van de data) en het inloggen per domein geen verhuurderkeuze nodig heeft. Beheer van het platform zelf (over alle verhuurders heen) is een aparte rol die later komt.
