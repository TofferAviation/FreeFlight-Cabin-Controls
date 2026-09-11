namespace FreeFlight.CabinControl.App.ViewModels;

internal static class BritishAirwaysStationCatalog
{
    // Bundled from the BA route-network catalogue. Codes are kept local so iPort works offline.
    public static IReadOnlyList<GateStationOption> All { get; } = Parse(
        "ABZ:Aberdeen|BHD:Belfast City|BEB:Benbecula|DUB:Dublin|EDI:Edinburgh|GCI:Guernsey|GLA:Glasgow|INV:Inverness|IOM:Isle of Man|ILY:Islay|JER:Jersey|KOI:Kirkwall|LBA:Leeds Bradford|LCY:London City|LGW:London Gatwick|LHR:London Heathrow|LSI:Sumburgh|MAN:Manchester|NCL:Newcastle|NQY:Newquay|NWI:Norwich|SNN:Shannon|STN:London Stansted|SYY:Stornoway|WIC:Wick John O'Groats|" +
        "TIA:Tirana|GRZ:Graz|INN:Innsbruck|SZG:Salzburg|VIE:Vienna|BRU:Brussels|SOF:Sofia|DBV:Dubrovnik|PUY:Pula|SPU:Split|ZAG:Zagreb|LCA:Larnaca|PFO:Paphos|PRG:Prague|BLL:Billund|CPH:Copenhagen|TLL:Tallinn|HEL:Helsinki|BIQ:Biarritz|BOD:Bordeaux|CDG:Paris Charles de Gaulle|LYS:Lyon|MRS:Marseille|MPL:Montpellier|NCE:Nice|TLS:Toulouse|BER:Berlin|CGN:Cologne|DUS:Düsseldorf|FRA:Frankfurt|HAM:Hamburg|HAJ:Hannover|MUC:Munich|NUE:Nuremberg|STR:Stuttgart|GIB:Gibraltar|ATH:Athens|CFU:Corfu|CHQ:Chania|EFL:Kefalonia|HER:Heraklion|JMK:Mykonos|JTR:Santorini|KGS:Kos|KLX:Kalamata|RHO:Rhodes|SKG:Thessaloniki|ZTH:Zakynthos|BUD:Budapest|KEF:Reykjavik|TLV:Tel Aviv|BLQ:Bologna|BRI:Bari|CAG:Cagliari|CTA:Catania|FLR:Florence|LIN:Milan Linate|MXP:Milan Malpensa|NAP:Naples|OLB:Olbia|PMO:Palermo|PSA:Pisa|FCO:Rome|TRN:Turin|VCE:Venice|VRN:Verona|RIX:Riga|VNO:Vilnius|LUX:Luxembourg|MLA:Malta|TGD:Podgorica|TIV:Tivat|AMS:Amsterdam|BGO:Bergen|OSL:Oslo|TOS:Tromsø|GDN:Gdańsk|KRK:Kraków|WAW:Warsaw|WRO:Wrocław|FAO:Faro|FNC:Funchal|LIS:Lisbon|OPO:Porto|OTP:Bucharest|LJU:Ljubljana|ALC:Alicante|BCN:Barcelona|BIO:Bilbao|IBZ:Ibiza|MAD:Madrid|AGP:Málaga|MAH:Menorca|PMI:Palma de Mallorca|SVQ:Seville|TFS:Tenerife South|VLC:Valencia|GOT:Gothenburg|ARN:Stockholm|BSL:Basel|GVA:Geneva|ZRH:Zurich|AYT:Antalya|BJV:Bodrum|DLM:Dalaman|IST:Istanbul|TBS:Tbilisi|" +
        "ATL:Atlanta|AUS:Austin|BOS:Boston|BWI:Baltimore|DFW:Dallas Fort Worth|DEN:Denver|IAH:Houston|LAS:Las Vegas|LAX:Los Angeles|MIA:Miami|MSY:New Orleans|JFK:New York JFK|EWR:Newark|MCO:Orlando|PHL:Philadelphia|PHX:Phoenix|PIT:Pittsburgh|PDX:Portland|SAN:San Diego|SFO:San Francisco|SEA:Seattle|TPA:Tampa|IAD:Washington Dulles|BNA:Nashville|CVG:Cincinnati|YUL:Montréal|YYZ:Toronto|YVR:Vancouver|" +
        "ANU:Antigua|BGI:Barbados|BDA:Bermuda|GCM:Grand Cayman|GND:Grenada|POS:Port of Spain|PLS:Turks and Caicos|PUJ:Punta Cana|NAS:Nassau|UVF:Saint Lucia|SKB:Saint Kitts|MBJ:Montego Bay|SJU:San Juan|STT:St Thomas|EZE:Buenos Aires|GIG:Rio de Janeiro|GRU:São Paulo|SCL:Santiago|BOG:Bogotá|SJO:San José|MEX:Mexico City|CUN:Cancún|PTY:Panama City|LIM:Lima|GEO:Georgetown|" +
        "ALG:Algiers|CAI:Cairo|SSH:Sharm El Sheikh|ACC:Accra|NBO:Nairobi|MRU:Mauritius|AGA:Agadir|RAK:Marrakech|CMN:Casablanca|ABV:Abuja|LOS:Lagos|CPT:Cape Town|JNB:Johannesburg|DAR:Dar es Salaam|ZNZ:Zanzibar|LUN:Lusaka|HRE:Harare|VFA:Victoria Falls|" +
        "BAH:Bahrain|BLR:Bengaluru|MAA:Chennai|DEL:Delhi|HYD:Hyderabad|BOM:Mumbai|AMM:Amman|KWI:Kuwait|MLE:Malé|MCT:Muscat|DOH:Doha|JED:Jeddah|RUH:Riyadh|DMM:Dammam|DXB:Dubai|AUH:Abu Dhabi|ISB:Islamabad|LHE:Lahore|PKX:Beijing Daxing|PVG:Shanghai|HKG:Hong Kong|HND:Tokyo Haneda|NRT:Tokyo Narita|SIN:Singapore|KUL:Kuala Lumpur|BKK:Bangkok|SYD:Sydney");

    private static IReadOnlyList<GateStationOption> Parse(string value) => value
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(entry => entry.Split(':', 2))
        .Where(parts => parts.Length == 2)
        .Select(parts => new GateStationOption(parts[0], $"{parts[1]} ({parts[0]})"))
        .OrderBy(station => station.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();
}
