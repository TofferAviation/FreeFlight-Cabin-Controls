namespace FreeFlight.CabinControl.App.Services;

public sealed record VirtualPassportAirport(
    string Iata,
    string AirportName,
    string City,
    string Country,
    string WindowsTimeZoneId,
    string? Icao = null);

/// <summary>
/// The small, deliberately local catalogue keeps the Passport feature offline.
/// Unknown destinations still receive a clearly-labelled virtual arrival record.
/// </summary>
public static class VirtualPassportAirportCatalog
{
    private static readonly IReadOnlyDictionary<string, VirtualPassportAirport> Airports =
        new Dictionary<string, VirtualPassportAirport>(StringComparer.OrdinalIgnoreCase)
        {
            ["LHR"] = A("LHR", "Heathrow", "London", "United Kingdom", "GMT Standard Time", "EGLL"),
            ["LGW"] = A("LGW", "Gatwick", "London", "United Kingdom", "GMT Standard Time", "EGKK"),
            ["LCY"] = A("LCY", "London City", "London", "United Kingdom", "GMT Standard Time", "EGLC"),
            ["MAN"] = A("MAN", "Manchester", "Manchester", "United Kingdom", "GMT Standard Time", "EGCC"),
            ["EDI"] = A("EDI", "Edinburgh", "Edinburgh", "United Kingdom", "GMT Standard Time", "EGPH"),
            ["DUB"] = A("DUB", "Dublin", "Dublin", "Ireland", "GMT Standard Time", "EIDW"),
            ["CDG"] = A("CDG", "Charles de Gaulle", "Paris", "France", "Romance Standard Time", "LFPG"),
            ["ORY"] = A("ORY", "Orly", "Paris", "France", "Romance Standard Time", "LFPO"),
            ["AMS"] = A("AMS", "Schiphol", "Amsterdam", "Netherlands", "W. Europe Standard Time", "EHAM"),
            ["BRU"] = A("BRU", "Brussels", "Brussels", "Belgium", "Romance Standard Time", "EBBR"),
            ["FRA"] = A("FRA", "Frankfurt", "Frankfurt", "Germany", "W. Europe Standard Time", "EDDF"),
            ["MUC"] = A("MUC", "Munich", "Munich", "Germany", "W. Europe Standard Time", "EDDM"),
            ["BER"] = A("BER", "Berlin Brandenburg", "Berlin", "Germany", "W. Europe Standard Time", "EDDB"),
            ["ZRH"] = A("ZRH", "Zurich", "Zurich", "Switzerland", "W. Europe Standard Time", "LSZH"),
            ["GVA"] = A("GVA", "Geneva", "Geneva", "Switzerland", "W. Europe Standard Time", "LSGG"),
            ["VIE"] = A("VIE", "Vienna", "Vienna", "Austria", "W. Europe Standard Time", "LOWW"),
            ["CPH"] = A("CPH", "Copenhagen", "Copenhagen", "Denmark", "Romance Standard Time", "EKCH"),
            ["OSL"] = A("OSL", "Oslo", "Norway", "W. Europe Standard Time", "ENGM"),
            ["ARN"] = A("ARN", "Arlanda", "Stockholm", "Sweden", "W. Europe Standard Time", "ESSA"),
            ["HEL"] = A("HEL", "Helsinki", "Finland", "FLE Standard Time", "EFHK"),
            ["KEF"] = A("KEF", "Keflavik", "Reykjanesbær", "Iceland", "Greenwich Standard Time", "BIKF"),
            ["MAD"] = A("MAD", "Adolfo Suárez Madrid–Barajas", "Madrid", "Spain", "Romance Standard Time", "LEMD"),
            ["BCN"] = A("BCN", "Barcelona–El Prat", "Barcelona", "Spain", "Romance Standard Time", "LEBL"),
            ["LIS"] = A("LIS", "Humberto Delgado", "Lisbon", "Portugal", "GMT Standard Time", "LPPT"),
            ["FCO"] = A("FCO", "Leonardo da Vinci–Fiumicino", "Rome", "Italy", "W. Europe Standard Time", "LIRF"),
            ["MXP"] = A("MXP", "Malpensa", "Milan", "Italy", "W. Europe Standard Time", "LIMC"),
            ["ATH"] = A("ATH", "Athens", "Athens", "Greece", "GTB Standard Time", "LGAV"),
            ["IST"] = A("IST", "Istanbul", "Istanbul", "Türkiye", "Turkey Standard Time", "LTFM"),
            ["WAW"] = A("WAW", "Chopin", "Warsaw", "Poland", "Central European Standard Time", "EPWA"),
            ["PRG"] = A("PRG", "Václav Havel", "Prague", "Czechia", "Central Europe Standard Time", "LKPR"),
            ["BUD"] = A("BUD", "Ferenc Liszt", "Budapest", "Hungary", "Central Europe Standard Time", "LHBP"),
            ["JFK"] = A("JFK", "John F. Kennedy", "New York", "United States", "Eastern Standard Time", "KJFK"),
            ["EWR"] = A("EWR", "Newark Liberty", "Newark", "United States", "Eastern Standard Time", "KEWR"),
            ["BOS"] = A("BOS", "Logan", "Boston", "United States", "Eastern Standard Time", "KBOS"),
            ["IAD"] = A("IAD", "Washington Dulles", "Washington", "United States", "Eastern Standard Time", "KIAD"),
            ["MIA"] = A("MIA", "Miami", "Miami", "United States", "Eastern Standard Time", "KMIA"),
            ["ORD"] = A("ORD", "O'Hare", "Chicago", "United States", "Central Standard Time", "KORD"),
            ["DFW"] = A("DFW", "Dallas/Fort Worth", "Dallas", "United States", "Central Standard Time", "KDFW"),
            ["DEN"] = A("DEN", "Denver", "Denver", "United States", "Mountain Standard Time", "KDEN"),
            ["LAX"] = A("LAX", "Los Angeles", "Los Angeles", "United States", "Pacific Standard Time", "KLAX"),
            ["SFO"] = A("SFO", "San Francisco", "San Francisco", "United States", "Pacific Standard Time", "KSFO"),
            ["SEA"] = A("SEA", "Seattle–Tacoma", "Seattle", "United States", "Pacific Standard Time", "KSEA"),
            ["YYZ"] = A("YYZ", "Toronto Pearson", "Toronto", "Canada", "Eastern Standard Time", "CYYZ"),
            ["YUL"] = A("YUL", "Montréal–Trudeau", "Montréal", "Canada", "Eastern Standard Time", "CYUL"),
            ["YVR"] = A("YVR", "Vancouver", "Vancouver", "Canada", "Pacific Standard Time", "CYVR"),
            ["MEX"] = A("MEX", "Mexico City", "Mexico City", "Mexico", "Central Standard Time (Mexico)", "MMMX"),
            ["CUN"] = A("CUN", "Cancún", "Cancún", "Mexico", "Eastern Standard Time (Mexico)", "MMUN"),
            ["GRU"] = A("GRU", "São Paulo–Guarulhos", "São Paulo", "Brazil", "E. South America Standard Time", "SBGR"),
            ["GIG"] = A("GIG", "Rio de Janeiro–Galeão", "Rio de Janeiro", "Brazil", "E. South America Standard Time", "SBGL"),
            ["EZE"] = A("EZE", "Ezeiza", "Buenos Aires", "Argentina", "Argentina Standard Time", "SAEZ"),
            ["SCL"] = A("SCL", "Arturo Merino Benítez", "Santiago", "Chile", "Pacific SA Standard Time", "SCEL"),
            ["LIM"] = A("LIM", "Jorge Chávez", "Lima", "Peru", "SA Pacific Standard Time", "SPJC"),
            ["DXB"] = A("DXB", "Dubai", "Dubai", "United Arab Emirates", "Arabian Standard Time", "OMDB"),
            ["DOH"] = A("DOH", "Hamad", "Doha", "Qatar", "Arab Standard Time", "OTHH"),
            ["AUH"] = A("AUH", "Zayed", "Abu Dhabi", "United Arab Emirates", "Arabian Standard Time", "OMAA"),
            ["RUH"] = A("RUH", "King Khalid", "Riyadh", "Saudi Arabia", "Arab Standard Time", "OERK"),
            ["TLV"] = A("TLV", "Ben Gurion", "Tel Aviv", "Israel", "Israel Standard Time", "LLBG"),
            ["CAI"] = A("CAI", "Cairo", "Cairo", "Egypt", "Egypt Standard Time", "HECA"),
            ["JNB"] = A("JNB", "O. R. Tambo", "Johannesburg", "South Africa", "South Africa Standard Time", "FAOR"),
            ["CPT"] = A("CPT", "Cape Town", "Cape Town", "South Africa", "South Africa Standard Time", "FACT"),
            ["NBO"] = A("NBO", "Jomo Kenyatta", "Nairobi", "Kenya", "E. Africa Standard Time", "HKJK"),
            ["LOS"] = A("LOS", "Murtala Muhammed", "Lagos", "Nigeria", "W. Central Africa Standard Time", "DNMM"),
            ["BOM"] = A("BOM", "Chhatrapati Shivaji Maharaj", "Mumbai", "India", "India Standard Time", "VABB"),
            ["DEL"] = A("DEL", "Indira Gandhi", "Delhi", "India", "India Standard Time", "VIDP"),
            ["BLR"] = A("BLR", "Kempegowda", "Bengaluru", "India", "India Standard Time", "VOBL"),
            ["SIN"] = A("SIN", "Changi", "Singapore", "Singapore Standard Time", "WSSS"),
            ["HKG"] = A("HKG", "Hong Kong", "Hong Kong", "Hong Kong", "China Standard Time", "VHHH"),
            ["BKK"] = A("BKK", "Suvarnabhumi", "Bangkok", "Thailand", "SE Asia Standard Time", "VTBS"),
            ["KUL"] = A("KUL", "Kuala Lumpur", "Kuala Lumpur", "Malaysia", "Singapore Standard Time", "WMKK"),
            ["CGK"] = A("CGK", "Soekarno–Hatta", "Jakarta", "Indonesia", "SE Asia Standard Time", "WIII"),
            ["NRT"] = A("NRT", "Narita", "Tokyo", "Japan", "Tokyo Standard Time", "RJAA"),
            ["HND"] = A("HND", "Haneda", "Tokyo", "Japan", "Tokyo Standard Time", "RJTT"),
            ["ICN"] = A("ICN", "Incheon", "Seoul", "South Korea", "Korea Standard Time", "RKSI"),
            ["PEK"] = A("PEK", "Beijing Capital", "Beijing", "China", "China Standard Time", "ZBAA"),
            ["PVG"] = A("PVG", "Shanghai Pudong", "Shanghai", "China", "China Standard Time", "ZSPD"),
            ["SYD"] = A("SYD", "Kingsford Smith", "Sydney", "Australia", "AUS Eastern Standard Time", "YSSY"),
            ["MEL"] = A("MEL", "Melbourne", "Melbourne", "Australia", "AUS Eastern Standard Time", "YMML"),
            ["PER"] = A("PER", "Perth", "Perth", "Australia", "W. Australia Standard Time", "YPPH"),
            ["AKL"] = A("AKL", "Auckland", "Auckland", "New Zealand", "New Zealand Standard Time", "NZAA")
        };

    public static VirtualPassportAirport Resolve(string? airportCode)
    {
        var normalized = (airportCode ?? string.Empty).Trim().ToUpperInvariant();
        if (Airports.TryGetValue(normalized, out var airport)) return airport;

        airport = Airports.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Icao, normalized, StringComparison.OrdinalIgnoreCase));
        if (airport is not null) return airport;

        return new VirtualPassportAirport(
            normalized.Length == 0 ? "---" : normalized,
            "Destination airport",
            "Destination",
            "Destination country",
            TimeZoneInfo.Local.Id);
    }

    private static VirtualPassportAirport A(
        string iata,
        string airportName,
        string city,
        string country,
        string timeZone,
        params string[] aliases)
    {
        return new VirtualPassportAirport(iata, airportName, city, country, timeZone, aliases.FirstOrDefault());
    }
}
