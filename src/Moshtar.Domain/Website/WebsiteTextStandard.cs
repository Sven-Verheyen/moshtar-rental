namespace Moshtar.Domain.Website;

/// <summary>
/// De standaardteksten van Moshtar voor de reservatiewebsite, in het Nederlands, Frans en Engels. De naam van de
/// verhuurder en zijn werkgebied worden ingevuld, zodat een nieuwe verhuurder meteen een volledige website heeft.
/// </summary>
public static class WebsiteTextStandard
{
    /// <summary>Zoveel gemeenten noemt een standaardtekst; bij meer volgt "en omstreken".</summary>
    private const int MunicipalitiesNamed = 3;

    public static string HomeTitle(string culture, string tenantName) => Pick(culture) switch
    {
        "fr" => $"Location de châteaux gonflables et de jeux chez {tenantName}",
        "en" => $"Rent bouncy castles and party games from {tenantName}",
        _ => $"Springkastelen en feestspellen huren bij {tenantName}",
    };

    public static string HomeSubtitle(string culture, IReadOnlyList<string> serviceArea) =>
        (Pick(culture), Area(culture, serviceArea)) switch
        {
            ("fr", null) => "Réservez en ligne, nous livrons et installons tout.",
            ("fr", var area) => $"Réservez en ligne, nous livrons et installons tout à {area}.",
            ("en", null) => "Reserve online, we deliver and set everything up.",
            ("en", var area) => $"Reserve online, we deliver and set everything up in {area}.",
            (_, null) => "Reserveer online, wij leveren en zetten alles op.",
            (_, var area) => $"Reserveer online, wij leveren en zetten alles op in {area}.",
        };

    /// <summary>De standaardset veelgestelde vragen: levering, regen, stroom, annuleren en opbouw.</summary>
    public static IReadOnlyList<FaqEntry> Faq(string culture, IReadOnlyList<string> serviceArea)
    {
        var area = Area(culture, serviceArea);
        return Pick(culture) switch
        {
            "fr" =>
            [
                new("Livrez-vous à domicile ?", area is null
                    ? "Oui, nous livrons et venons tout reprendre après la fête. Contactez-nous pour savoir si nous livrons chez vous."
                    : $"Oui, nous livrons à {area}. Vous habitez ailleurs ? Contactez-nous et nous verrons ce qui est possible."),
                new("Et s'il pleut ?", "Un château gonflable ne peut pas être utilisé sous la pluie ou par grand vent : il devient glissant et dangereux. Si le mauvais temps menace, contactez-nous et nous chercherons ensemble une solution."),
                new("Faut-il de l'électricité ?", "Un château gonflable a une soufflerie qui tourne en continu et a besoin d'une prise normale (230 V) à moins de 25 mètres. La fiche de chaque article indique s'il faut de l'électricité."),
                new("Puis-je annuler ma réservation ?", "Oui. Contactez-nous le plus vite possible avec votre numéro de réservation et nous nous occupons du reste."),
                new("Qui installe le château gonflable ?", "Quand nous livrons, nous installons tout et nous démontons après la fête. Prévoyez un sol plat, sans objets pointus, avec assez d'espace autour."),
            ],
            "en" =>
            [
                new("Do you deliver to my home?", area is null
                    ? "Yes, we deliver and pick everything up again after the party. Contact us to ask whether we deliver to you."
                    : $"Yes, we deliver in {area}. Live somewhere else? Contact us and we'll see what's possible."),
                new("What if it rains?", "A bouncy castle can't be used in rain or strong wind, because it gets slippery and unsafe. If bad weather is on the way, contact us and we'll find a solution together."),
                new("Do I need electricity?", "A bouncy castle has a blower that runs the whole time and needs a regular socket (230 V) within 25 metres. Each item's page says whether it needs electricity."),
                new("Can I cancel my reservation?", "Yes. Contact us as soon as possible with your reservation number and we'll take care of the rest."),
                new("Who sets up the bouncy castle?", "When we deliver, we set everything up and take it down again after the party. Make sure there's a flat surface without sharp objects and enough space around it."),
            ],
            _ =>
            [
                new("Leveren jullie bij mij thuis?", area is null
                    ? "Ja, we leveren en halen alles na het feest weer op. Neem contact met ons op om te vragen of we ook bij jou leveren."
                    : $"Ja, we leveren in {area}. Woon je ergens anders? Neem contact met ons op, dan bekijken we wat kan."),
                new("Wat als het regent?", "Een springkasteel mag niet gebruikt worden bij regen of veel wind, omdat het dan glad en onveilig wordt. Dreigt er slecht weer, neem dan contact met ons op, dan zoeken we samen een oplossing."),
                new("Heb ik stroom nodig?", "Een springkasteel heeft een blazer die de hele tijd draait en een gewoon stopcontact (230 V) nodig heeft, binnen 25 meter. Bij elk artikel staat of het stroom nodig heeft."),
                new("Kan ik mijn reservatie annuleren?", "Ja. Neem zo snel mogelijk contact met ons op met je reservatienummer, dan regelen we de rest."),
                new("Wie zet het springkasteel op?", "Als we leveren, zetten wij alles op en breken we het na het feest weer af. Zorg voor een vlakke ondergrond zonder scherpe voorwerpen, met genoeg ruimte rondom."),
            ],
        };
    }

    /// <summary>Het werkgebied als stukje zin, bv. "Antwerpen, Mortsel en Edegem", of null als er geen is.</summary>
    public static string? Area(string culture, IReadOnlyList<string> serviceArea)
    {
        if (serviceArea.Count == 0) return null;
        var (and, surroundings) = Pick(culture) switch
        {
            "fr" => ("et", "environs"),
            "en" => ("and", "the surrounding area"),
            _ => ("en", "omstreken"),
        };
        if (serviceArea.Count > MunicipalitiesNamed)
            return $"{string.Join(", ", serviceArea.Take(MunicipalitiesNamed))} {and} {surroundings}";
        return serviceArea.Count == 1
            ? serviceArea[0]
            : $"{string.Join(", ", serviceArea.SkipLast(1))} {and} {serviceArea[^1]}";
    }

    private static string Pick(string culture) => culture is "fr" or "en" ? culture : "nl";
}
