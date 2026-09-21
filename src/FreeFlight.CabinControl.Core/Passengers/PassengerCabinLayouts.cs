namespace FreeFlight.CabinControl.Core.Passengers;

internal sealed record PassengerCabinLayoutDefinition(
    PassengerCabinLayout Layout,
    IReadOnlyList<CabinSeat> Seats,
    double L1DoorX,
    double L2DoorX,
    double DoorEntryY,
    double DoorThresholdY,
    IReadOnlyList<CabinDoorDefinition>? CabinDoors = null)
{
    public IReadOnlyList<CabinDoorDefinition> Doors { get; } = CabinDoors ??
    [
        new(BoardingDoor.L1, "L1", L1DoorX, true),
        new(BoardingDoor.L2, "L2", L2DoorX, true)
    ];
}

internal static class PassengerCabinLayouts
{
    private const double CabinCanvasWidth = 1033d;
    private const double CabinCanvasHeight = 192d;

    // The live-cabin artwork is not rendered at a common aspect ratio. Keep the
    // seat/door data in artwork coordinates and project it with the same
    // Uniform scaling used by the ImageBrush in PassengerFlowView.xaml.
    private static readonly CabinArtworkGeometry BritishAirwaysA319Artwork = new(
        2840d, 591d, 640d, 2494d, 419d, 2605d,
        [488d, 373d, 232d, 120d],
        [488d, 431d, 373d, 232d, 177d, 120d],
        297d);
    private static readonly CabinArtworkGeometry BritishAirwaysA321Artwork = new(
        2841d, 420d, 404d, 2584d, 233d, 2663d,
        [353d, 263d, 156d, 68d],
        [353d, 307d, 263d, 156d, 112d, 68d],
        208d);
    private static readonly CabinArtworkGeometry BritishAirwaysA321NeoArtwork = new(
        2907d, 417d, 468d, 2652d, 302d, 2730d,
        [350d, 261d, 156d, 67d],
        [350d, 306d, 261d, 156d, 112d, 67d],
        205d);
    private static readonly CabinArtworkGeometry BritishAirwaysEmbraer190Artwork = new(
        2837d, 341d, 625d, 2532d, 476d, 2609d,
        [267d, 220d, 115d, 72d],
        [267d, 220d, 115d, 72d],
        166d);

    public static PassengerCabinLayoutDefinition Create(PassengerCabinLayout layout) => layout switch
    {
        PassengerCabinLayout.BritishAirways777200Er => CreateBritishAirways777200Er(),
        PassengerCabinLayout.BritishAirways777300 => CreateBritishAirways777300(),
        PassengerCabinLayout.BritishAirwaysA320200 => CreateBritishAirwaysA320200(),
        PassengerCabinLayout.BritishAirwaysA320Neo => CreateBritishAirwaysA320Neo(),
        PassengerCabinLayout.BritishAirwaysA319 => CreateSingleAisle(layout, 24, 10, 0, BritishAirwaysA319Artwork),
        PassengerCabinLayout.BritishAirwaysA321 => CreateSingleAisle(layout, 35, 12, 0, BritishAirwaysA321Artwork),
        PassengerCabinLayout.BritishAirwaysA321Neo220M => CreateSingleAisle(layout, 37, 0, 2, BritishAirwaysA321NeoArtwork),
        PassengerCabinLayout.BritishAirwaysEmbraer190 => CreateEmbraer190(),
        PassengerCabinLayout.BritishAirwaysA350 => CreateWideBody(layout, 2, 14, 7, 22, 9, CabinArtworkGeometry.Wide(2910d, 425d, 360d, 2700d, 201d, 2771d)),
        PassengerCabinLayout.BritishAirways777200Lgw => CreateWideBody(layout, 0, 8, 7, 25, 0, CabinArtworkGeometry.Wide(2888d, 418d, 325d, 2566d, 177d, 2649d)),
        PassengerCabinLayout.BritishAirways777200First => CreateWideBody(layout, 2, 12, 5, 14, 1, CabinArtworkGeometry.Wide(2866d, 499d, 513d, 2620d, 177d, 2653d)),
        PassengerCabinLayout.BritishAirways7878ClubSuite => CreateWideBody(layout, 0, 8, 5, 14, 8, CabinArtworkGeometry.Wide(2906d, 506d, 360d, 2620d, 233d, 2697d)),
        PassengerCabinLayout.BritishAirways7879 => CreateWideBody(layout, 2, 11, 5, 13, 6, CabinArtworkGeometry.Wide(2854d, 449d, 360d, 2602d, 193d, 2711d)),
        PassengerCabinLayout.BritishAirways7879Alternate => CreateWideBody(layout, 2, 11, 5, 13, 7, CabinArtworkGeometry.Wide(2854d, 449d, 360d, 2602d, 193d, 2711d)),
        PassengerCabinLayout.BritishAirways78710 => CreateWideBody(layout, 2, 12, 5, 17, 10, CabinArtworkGeometry.Wide(2906d, 380d, 360d, 2676d, 173d, 2737d)),
        _ => CreateFlightFactor777V2()
    };

    private static PassengerCabinLayoutDefinition CreateSingleAisle(
        PassengerCabinLayout layout,
        int rows,
        int businessRows,
        int seatsToRemove,
        CabinArtworkGeometry artwork)
    {
        var seats = new List<CabinSeat>();
        for (var row = 1; row <= rows; row++)
        {
            var sourceX = Lerp(artwork.FirstSeatSourceX, artwork.LastSeatSourceX, (row - 1d) / Math.Max(1, rows - 1));
            var x = artwork.MapX(sourceX);
            var letters = row <= businessRows ? new[] { "A", "C", "D", "F" } : new[] { "A", "B", "C", "D", "E", "F" };
            var sourceYPositions = row <= businessRows ? artwork.FourAcrossSeatY : artwork.SixAcrossSeatY;
            for (var index = 0; index < letters.Length; index++)
            {
                seats.Add(new CabinSeat(
                    $"{row}{letters[index]}",
                    row <= businessRows ? PassengerCabinClass.Business : PassengerCabinClass.Economy,
                    x,
                    artwork.MapY(sourceYPositions[index]),
                    artwork.MapY(artwork.AisleSourceY)));
            }
        }

        while (seatsToRemove-- > 0 && seats.Count > 0)
        {
            seats.RemoveAt(seats.Count - 1);
        }

        return new PassengerCabinLayoutDefinition(
            layout,
            seats,
            artwork.MapX(artwork.ForwardDoorSourceX),
            artwork.MapX(artwork.AftDoorSourceX),
            210d,
            174d,
            CreateNarrowBodyDoors(artwork.MapX(artwork.ForwardDoorSourceX), artwork.MapX(artwork.AftDoorSourceX)));
    }

    private static PassengerCabinLayoutDefinition CreateEmbraer190()
    {
        var artwork = BritishAirwaysEmbraer190Artwork;
        var seats = new List<CabinSeat>(98);
        for (var row = 1; row <= 25; row++)
        {
            var x = artwork.MapX(Lerp(artwork.FirstSeatSourceX, artwork.LastSeatSourceX, (row - 1d) / 24d));
            var letters = row == 25 ? new[] { "A", "D" } : new[] { "A", "C", "D", "F" };
            var sourceYPositions = row == 25
                ? new[] { artwork.FourAcrossSeatY[0], artwork.FourAcrossSeatY[2] }
                : artwork.FourAcrossSeatY;
            for (var index = 0; index < letters.Length; index++)
            {
                seats.Add(new CabinSeat($"{row}{letters[index]}", row <= 8 ? PassengerCabinClass.Business : PassengerCabinClass.Economy, x, artwork.MapY(sourceYPositions[index]), artwork.MapY(artwork.AisleSourceY)));
            }
        }

        return new PassengerCabinLayoutDefinition(
            PassengerCabinLayout.BritishAirwaysEmbraer190,
            seats,
            artwork.MapX(artwork.ForwardDoorSourceX),
            artwork.MapX(artwork.AftDoorSourceX),
            210d,
            174d,
            [
                new(BoardingDoor.L1, "L1", artwork.MapX(artwork.ForwardDoorSourceX), true),
                new(BoardingDoor.R1, "R1", artwork.MapX(artwork.ForwardDoorSourceX), false),
                new(BoardingDoor.OverwingLeft, "OW L", artwork.MapX(1462d), false, true),
                new(BoardingDoor.OverwingRight, "OW R", artwork.MapX(1462d), false, true),
                new(BoardingDoor.L2, "L2", artwork.MapX(artwork.AftDoorSourceX), true),
                new(BoardingDoor.R2, "R2", artwork.MapX(artwork.AftDoorSourceX), false)
            ]);
    }

    private static PassengerCabinLayoutDefinition CreateWideBody(
        PassengerCabinLayout layout,
        int firstRows,
        int businessRows,
        int premiumRows,
        int economyRows,
        int seatsToRemove,
        CabinArtworkGeometry artwork)
    {
        var seats = new List<CabinSeat>();
        var totalRows = firstRows + businessRows + premiumRows + economyRows;
        var row = 1;
        for (var section = 0; section < 4; section++)
        {
            var count = section switch { 0 => firstRows, 1 => businessRows, 2 => premiumRows, _ => economyRows };
            var cabinClass = section switch
            {
                0 => PassengerCabinClass.First,
                1 => PassengerCabinClass.Business,
                2 => PassengerCabinClass.PremiumEconomy,
                _ => PassengerCabinClass.Economy
            };
            var letters = section <= 1
                ? new[] { "A", "E", "F", "K" }
                : section == 2
                    ? new[] { "A", "B", "D", "E", "F", "G", "J", "K" }
                    : new[] { "A", "B", "C", "D", "E", "F", "G", "H", "J", "K" };
            for (var sectionRow = 0; sectionRow < count; sectionRow++, row++)
            {
                var sourceX = Lerp(artwork.FirstSeatSourceX, artwork.LastSeatSourceX, (row - 1d) / Math.Max(1, totalRows - 1));
                var x = artwork.MapX(sourceX);
                var sourceYPositions = letters.Length switch
                {
                    4 => artwork.FourAcrossSeatY,
                    8 => artwork.EightAcrossSeatY,
                    _ => artwork.TenAcrossSeatY
                };
                for (var index = 0; index < letters.Length; index++)
                {
                    var aisleY = index < letters.Length / 2 ? artwork.MapY(artwork.UpperAisleSourceY) : artwork.MapY(artwork.LowerAisleSourceY);
                    seats.Add(new CabinSeat($"{row}{letters[index]}", cabinClass, x, artwork.MapY(sourceYPositions[index]), aisleY));
                }
            }
        }

        while (seatsToRemove-- > 0 && seats.Count > 0)
        {
            seats.RemoveAt(seats.Count - 1);
        }

        return new PassengerCabinLayoutDefinition(
            layout,
            seats,
            artwork.MapX(artwork.ForwardDoorSourceX),
            artwork.MapX(Lerp(artwork.ForwardDoorSourceX, artwork.AftDoorSourceX, 0.33d)),
            208d,
            174d,
            CreateWideBodyDoors(
                artwork.MapX(artwork.ForwardDoorSourceX),
                artwork.MapX(Lerp(artwork.ForwardDoorSourceX, artwork.AftDoorSourceX, 0.33d)),
                artwork.MapX(Lerp(artwork.ForwardDoorSourceX, artwork.AftDoorSourceX, 0.50d)),
                artwork.MapX(Lerp(artwork.ForwardDoorSourceX, artwork.AftDoorSourceX, 0.67d)),
                artwork.MapX(artwork.AftDoorSourceX)));
    }

    private static IReadOnlyList<CabinDoorDefinition> CreateNarrowBodyDoors(double l1X = 72d, double l2X = 960d) =>
    [
        new(BoardingDoor.L1, "L1", l1X, true),
        new(BoardingDoor.R1, "R1", l1X, false),
        new(BoardingDoor.OverwingLeft, "OW L", 520d, false, true),
        new(BoardingDoor.OverwingRight, "OW R", 520d, false, true),
        new(BoardingDoor.L2, "L2", l2X, true),
        new(BoardingDoor.R2, "R2", l2X, false)
    ];

    private static IReadOnlyList<CabinDoorDefinition> CreateWideBodyDoors(
        double l1X = 55d,
        double l2X = 295d,
        double l3X = 535d,
        double l4X = 760d,
        double l5X = 960d) =>
    [
        new(BoardingDoor.L1, "L1", l1X, true),
        new(BoardingDoor.R1, "R1", l1X, false),
        new(BoardingDoor.L2, "L2", l2X, true),
        new(BoardingDoor.R2, "R2", l2X, false),
        new(BoardingDoor.L3, "L3", l3X, true),
        new(BoardingDoor.R3, "R3", l3X, false),
        new(BoardingDoor.L4, "L4", l4X, true),
        new(BoardingDoor.R4, "R4", l4X, false),
        new(BoardingDoor.L5, "L5", l5X, true),
        new(BoardingDoor.R5, "R5", l5X, false)
    ];

    private static PassengerCabinLayoutDefinition CreateFlightFactor777V2()
    {
        var seats = new List<CabinSeat>(311);
        AddRows(
            seats,
            PassengerCabinClass.First,
            [1, 2, 3, 4],
            304d,
            403d,
            ["A", "B", "C", "D", "E", "F", "G", "H", "K"],
            [30d, 38d, 46d, 67d, 75d, 83d, 103d, 111d, 119d],
            56d,
            94d);
        AddRows(
            seats,
            PassengerCabinClass.Business,
            [5, 6, 7, 8, 9],
            447d,
            565d,
            ["A", "B", "D", "E", "F", "J", "K"],
            [31d, 41d, 64d, 74d, 84d, 106d, 116d],
            53d,
            95d);
        AddRows(
            seats,
            PassengerCabinClass.Economy,
            Enumerable.Range(10, 24).ToArray(),
            630d,
            890d,
            ["A", "B", "C", "D", "E", "F", "G", "H", "J", "K"],
            [30d, 39d, 48d, 67d, 74d, 81d, 88d, 102d, 111d, 120d],
            57d,
            95d);
        return new PassengerCabinLayoutDefinition(
            PassengerCabinLayout.FlightFactor777V2,
            seats,
            183d,
            426d,
            145d,
            128d,
            CreateWideBodyDoors(183d, 426d));
    }

    private static PassengerCabinLayoutDefinition CreateBritishAirways777200Er()
    {
        var seats = new List<CabinSeat>(272);
        AddMappedRows(seats, PassengerCabinClass.Business, Enumerable.Range(1, 7).ToArray(),
            [315d, 379d, 446d, 512d, 577d, 640d, 705d], ["A", "E", "F", "K"],
            [318d, 244d, 152d, 75d], [0d, 0d, 0d, 0d], 2860d, 380d, 70d, 129d);
        AddMappedRows(seats, PassengerCabinClass.Business, Enumerable.Range(10, 5).ToArray(),
            [927d, 992d, 1058d, 1121d, 1184d], ["A", "E", "F", "K"],
            [318d, 244d, 152d, 75d], [0d, 12d, 12d, 0d], 2860d, 380d, 70d, 129d);
        AddMappedRows(seats, PassengerCabinClass.PremiumEconomy, Enumerable.Range(15, 5).ToArray(),
            [1265.4d, 1320.3d, 1375.3d, 1430.4d, 1485.5d], ["A", "B", "D", "E", "F", "G", "J", "K"],
            [343.2d, 308.2d, 249d, 214.3d, 177.3d, 142.6d, 81.2d, 47.3d],
            [0d, 0d, 12.4d, 12.4d, 12.4d, 12.4d, 0d, 0d], 2860d, 380d, 70d, 129d);
        AddMappedRows(seats, PassengerCabinClass.Economy, Enumerable.Range(20, 5).ToArray(),
            [1579.1d, 1623.5d, 1668.1d, 1714d, 1758.9d], ["A", "B", "C", "D", "E", "F", "G", "H", "J", "K"],
            [348.6d, 318d, 288d, 241.2d, 211.7d, 181.2d, 150.4d, 105.1d, 74.3d, 43.4d],
            [0d, 0d, 0d, 8.7d, 8.7d, 8.7d, 8.7d, 0d, 0d, 0d], 2860d, 380d, 70d, 129d);
        var rearRows = Enumerable.Range(26, 10).ToArray();
        AddMappedRows(seats, PassengerCabinClass.Economy, rearRows,
            [1970.6d, 2018.3d, 2063.3d, 2108.7d, 2154.2d, 2198.6d, 2243.3d, 2289.2d, 2334.5d, 2379.6d],
            ["A", "B", "C", "H", "J", "K"],
            [348d, 318.9d, 289.5d, 102.9d, 73.7d, 43.7d],
            [0d, 0d, 0d, 0d, 0d, 0d], 2860d, 380d, 70d, 129d);
        AddMappedRows(seats, PassengerCabinClass.Economy, rearRows,
            [1941.1d, 1985.3d, 2031.2d, 2075.9d, 2120.4d, 2166.1d, 2212.8d, 2256.9d, 2304.1d, 2350.1d],
            ["D", "E", "F", "G"], [240.8d, 212d, 182.8d, 151.7d],
            [0d, 0d, 0d, 0d], 2860d, 380d, 70d, 129d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 36,
            [("A", 2431.9d, 344d), ("B", 2429.6d, 311.1d),
             ("D", 2394.4d, 240.4d), ("E", 2394.2d, 211.8d), ("F", 2393.8d, 182.5d), ("G", 2394.3d, 150.9d),
             ("J", 2434d, 75.2d), ("K", 2435.9d, 44.8d)], 2860d, 380d, 70d, 129d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 37,
            [("A", 2476.5d, 339.9d), ("B", 2474.5d, 307.7d),
             ("D", 2439.4d, 240d), ("E", 2439.5d, 211.5d), ("F", 2439.1d, 182.8d), ("G", 2438.5d, 153d),
             ("J", 2479d, 78d), ("K", 2480.6d, 48.1d)], 2860d, 380d, 70d, 129d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 38,
            [("A", 2520d, 337.8d), ("B", 2517.7d, 306.1d),
             ("D", 2484.7d, 240.3d), ("E", 2484.7d, 211.6d), ("F", 2484.5d, 182.7d), ("G", 2483.8d, 151.7d),
             ("J", 2523.7d, 82.3d), ("K", 2525.5d, 52d)], 2860d, 380d, 70d, 129d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 39,
            [("D", 2528.6d, 240.8d), ("E", 2528.7d, 211.6d), ("F", 2527.7d, 182.1d), ("G", 2528.3d, 151.3d),
             ("J", 2568.3d, 84.9d), ("K", 2570.2d, 56.2d)], 2860d, 380d, 70d, 129d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 40,
            [("D", 2572.4d, 240.6d), ("E", 2572.5d, 211.3d), ("F", 2571.9d, 182.2d), ("G", 2572d, 151.5d)],
            2860d, 380d, 70d, 129d);
        return new PassengerCabinLayoutDefinition(
            PassengerCabinLayout.BritishAirways777200Er,
            seats,
            52d,
            295d,
            208d,
            174d,
            CreateWideBodyDoors(52d, 295d));
    }

    private static PassengerCabinLayoutDefinition CreateBritishAirways777300()
    {
        var seats = new List<CabinSeat>(256);
        AddMappedRows(seats, PassengerCabinClass.First, [1, 2], [261d, 367d], ["A", "E", "F", "K"],
            [331d, 273d, 191d, 134d], [0d, 10d, 10d, 0d], 2855d, 390d, 84d, 135d);
        AddMappedRows(seats, PassengerCabinClass.Business, [5, 6, 7], [471.5d, 524.8d, 581.1d], ["A", "E", "F", "K"],
            [331d, 273d, 191d, 134d], [0d, -6d, -6d, 0d], 2855d, 390d, 84d, 135d);
        AddMappedRows(seats, PassengerCabinClass.Business, Enumerable.Range(8, 10).ToArray(),
            [815d, 869.6d, 925.2d, 980.2d, 1034.8d, 1091d, 1145.4d, 1200.1d, 1254.8d, 1310d],
            ["A", "E", "F", "K"], [331d, 273d, 191d, 134d], [0d, 19d, 19d, 0d], 2855d, 390d, 84d, 135d);
        AddMappedRows(seats, PassengerCabinClass.Business, Enumerable.Range(19, 6).ToArray(),
            [1466d, 1521d, 1576d, 1630d, 1685.5d, 1740.5d], ["A", "E", "F", "K"],
            [331d, 273d, 191d, 134d], [0d, 19d, 19d, 0d], 2855d, 390d, 84d, 135d);
        AddMappedRows(seats, PassengerCabinClass.PremiumEconomy, Enumerable.Range(25, 5).ToArray(),
            [1811.2d, 1858.1d, 1905.3d, 1951d, 1997d], ["A", "B", "D", "E", "F", "G", "J", "K"],
            [359.3d, 330d, 277.7d, 249.2d, 218.2d, 189.2d, 135.8d, 107.4d],
            [0d, 0d, 7d, 7d, 7d, 7d, 0d, 0d], 2855d, 390d, 84d, 135d);
        var economyRows = Enumerable.Range(30, 10).ToArray();
        AddMappedRows(seats, PassengerCabinClass.Economy, economyRows,
            [2133.8d, 2172.4d, 2211d, 2249.6d, 2288.1d, 2325.9d, 2364.5d, 2402.7d, 2440.8d, 2479d],
            ["A", "B", "C", "H", "J", "K"], [363.4d, 337.5d, 312.5d, 153.5d, 128.4d, 103.2d],
            [0d, 0d, 0d, 0d, 0d, 0d], 2855d, 390d, 84d, 135d);
        AddMappedRows(seats, PassengerCabinClass.Economy, economyRows,
            [2130.2d, 2168.7d, 2206.3d, 2244.9d, 2283.1d, 2321.1d, 2359.1d, 2396.6d, 2435.1d, 2474d],
            ["D", "E", "F", "G"], [271.8d, 246.7d, 221.8d, 195.7d],
            [0d, 0d, 0d, 0d], 2855d, 390d, 84d, 135d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 40,
            [("A", 2520.1d, 360.2d), ("B", 2517.9d, 333.6d),
             ("D", 2512.5d, 272.6d), ("E", 2512.4d, 246.8d), ("F", 2511.8d, 222d), ("G", 2512.1d, 196.3d),
             ("J", 2517.3d, 132.4d), ("K", 2519.4d, 106.9d)], 2855d, 390d, 84d, 135d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 41,
            [("A", 2558.2d, 357.6d), ("B", 2556.2d, 330.9d),
             ("D", 2550.3d, 272.4d), ("E", 2550.6d, 247.4d), ("F", 2549.5d, 222.3d), ("G", 2549.7d, 197.4d),
             ("J", 2556.7d, 134.8d), ("K", 2558d, 109.8d)], 2855d, 390d, 84d, 135d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 42,
            [("A", 2595.7d, 355.8d), ("B", 2593.6d, 328.2d),
             ("D", 2589.6d, 273.1d), ("E", 2589.7d, 246.9d), ("F", 2589d, 222.3d), ("G", 2589.1d, 196.9d),
             ("J", 2594.1d, 137.6d), ("K", 2595.7d, 112.8d)], 2855d, 390d, 84d, 135d);
        AddMappedRow(seats, PassengerCabinClass.Economy, 43,
            [("A", 2634d, 353d), ("B", 2631.7d, 325.7d),
             ("D", 2626.8d, 272.5d), ("E", 2627d, 247.1d), ("F", 2625.9d, 222.2d), ("G", 2626d, 196.5d),
             ("J", 2634.4d, 141.5d), ("K", 2636.1d, 116.5d)], 2855d, 390d, 84d, 135d);
        return new PassengerCabinLayoutDefinition(
            PassengerCabinLayout.BritishAirways777300,
            seats,
            50d,
            228d,
            208d,
            174d,
            CreateWideBodyDoors(50d, 228d));
    }

    private static PassengerCabinLayoutDefinition CreateBritishAirwaysA320200()
    {
        var seats = new List<CabinSeat>(156);
        var sourceRowXPositions = new[]
        {
            477d, 545d, 610d, 678d, 745d, 813d, 879d, 947d, 1015d, 1083d,
            1161d, 1243d, 1308d, 1375d, 1442d, 1509d, 1577d, 1643d, 1710d,
            1778d, 1846d, 1912d, 1977d, 2047d, 2114d, 2180d, 2248d, 2316d,
            2382d, 2449d
        };
        AddMappedRows(seats, PassengerCabinClass.Business, Enumerable.Range(1, 12).ToArray(),
            sourceRowXPositions[..12], ["A", "C", "D", "F"], [424d, 323d, 201d, 98d],
            [0d, 0d, 0d, 0d], 2770d, 570d, 88d, 88d);
        AddMappedRows(seats, PassengerCabinClass.Economy, Enumerable.Range(13, 18).ToArray(),
            sourceRowXPositions[12..], ["A", "B", "C", "D", "E", "F"],
            [424d, 373d, 323d, 201d, 151d, 98d], [0d, 0d, 0d, 0d, 0d, 0d],
            2770d, 570d, 88d, 88d);
        return new PassengerCabinLayoutDefinition(
            PassengerCabinLayout.BritishAirwaysA320200,
            seats,
            MapArtworkX(285d, 2770d, 570d),
            MapArtworkX(2534d, 2770d, 570d),
            210d,
            174d,
            CreateNarrowBodyDoors(
                MapArtworkX(285d, 2770d, 570d),
                MapArtworkX(2534d, 2770d, 570d)));
    }

    private static PassengerCabinLayoutDefinition CreateBritishAirwaysA320Neo()
    {
        var seats = new List<CabinSeat>(156);
        var sourceRowXPositions = new[]
        {
            467d, 534d, 600d, 667d, 734d, 801d, 867d, 934d, 1003d, 1072d,
            1154d, 1236d, 1306d, 1373d, 1445d, 1515d, 1582d, 1650d, 1717d,
            1788d, 1855d, 1921d, 1990d, 2058d, 2123d, 2191d, 2258d, 2324d,
            2390d, 2457d
        };
        AddMappedRows(seats, PassengerCabinClass.Business, Enumerable.Range(1, 12).ToArray(),
            sourceRowXPositions[..12], ["A", "C", "D", "F"], [465d, 365d, 242d, 142d],
            [0d, 0d, 0d, 0d], 2765d, 640d, 90d, 90d);
        AddMappedRows(seats, PassengerCabinClass.Economy, Enumerable.Range(13, 18).ToArray(),
            sourceRowXPositions[12..], ["A", "B", "C", "D", "E", "F"],
            [465d, 415d, 365d, 242d, 192d, 142d], [0d, 0d, 0d, 0d, 0d, 0d],
            2765d, 640d, 90d, 90d);
        return new PassengerCabinLayoutDefinition(
            PassengerCabinLayout.BritishAirwaysA320Neo,
            seats,
            MapArtworkX(276d, 2765d, 640d),
            MapArtworkX(2548d, 2765d, 640d),
            210d,
            174d,
            CreateNarrowBodyDoors(
                MapArtworkX(276d, 2765d, 640d),
                MapArtworkX(2548d, 2765d, 640d)));
    }

    private static void AddMappedRows(
        ICollection<CabinSeat> seats,
        PassengerCabinClass cabinClass,
        IReadOnlyList<int> rows,
        IReadOnlyList<double> sourceRowXPositions,
        IReadOnlyList<string> letters,
        IReadOnlyList<double> sourceYPositions,
        IReadOnlyList<double> sourceLetterXOffsets,
        double sourceWidth,
        double sourceCropHeight,
        double upperAisleY,
        double lowerAisleY)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var seatIndex = 0; seatIndex < letters.Count; seatIndex++)
            {
                var x = MapArtworkX(sourceRowXPositions[rowIndex] + sourceLetterXOffsets[seatIndex], sourceWidth, sourceCropHeight);
                var y = MapArtworkY(sourceYPositions[seatIndex], sourceWidth, sourceCropHeight);
                var aisleY = IsLowerCabinSeat(letters[seatIndex]) ? lowerAisleY : upperAisleY;
                seats.Add(new CabinSeat($"{rows[rowIndex]}{letters[seatIndex]}", cabinClass, x, y, aisleY));
            }
        }
    }

    private static void AddMappedRow(
        ICollection<CabinSeat> seats,
        PassengerCabinClass cabinClass,
        int row,
        IReadOnlyList<(string Letter, double SourceX, double SourceY)> sourceSeats,
        double sourceWidth,
        double sourceCropHeight,
        double upperAisleY,
        double lowerAisleY)
    {
        foreach (var sourceSeat in sourceSeats)
        {
            var aisleY = IsLowerCabinSeat(sourceSeat.Letter) ? lowerAisleY : upperAisleY;
            seats.Add(new CabinSeat(
                $"{row}{sourceSeat.Letter}",
                cabinClass,
                MapArtworkX(sourceSeat.SourceX, sourceWidth, sourceCropHeight),
                MapArtworkY(sourceSeat.SourceY, sourceWidth, sourceCropHeight),
                aisleY));
        }
    }

    private static double Lerp(double start, double end, double progress) => start + ((end - start) * progress);

    private static double GetArtworkScale(double sourceWidth, double sourceVisibleHeight) =>
        Math.Min(CabinCanvasWidth / sourceWidth, CabinCanvasHeight / sourceVisibleHeight);

    private static double MapArtworkX(double sourceX, double sourceWidth, double sourceVisibleHeight)
    {
        var scale = GetArtworkScale(sourceWidth, sourceVisibleHeight);
        return ((CabinCanvasWidth - (sourceWidth * scale)) / 2d) + (sourceX * scale);
    }

    private static double MapArtworkY(double sourceY, double sourceWidth, double sourceVisibleHeight)
    {
        var scale = GetArtworkScale(sourceWidth, sourceVisibleHeight);
        return ((CabinCanvasHeight - (sourceVisibleHeight * scale)) / 2d) + (sourceY * scale);
    }

    private sealed record CabinArtworkGeometry(
        double SourceWidth,
        double SourceHeight,
        double FirstSeatSourceX,
        double LastSeatSourceX,
        double ForwardDoorSourceX,
        double AftDoorSourceX,
        IReadOnlyList<double> FourAcrossSeatY,
        IReadOnlyList<double> SixAcrossSeatY,
        double AisleSourceY)
    {
        public IReadOnlyList<double> EightAcrossSeatY => BuildSeatY(8);
        public IReadOnlyList<double> TenAcrossSeatY => BuildSeatY(10);
        public double UpperAisleSourceY => SourceHeight * 0.40d;
        public double LowerAisleSourceY => SourceHeight * 0.60d;

        public double MapX(double sourceX) => MapArtworkX(sourceX, SourceWidth, SourceHeight);
        public double MapY(double sourceY) => MapArtworkY(sourceY, SourceWidth, SourceHeight);

        public static CabinArtworkGeometry Wide(
            double sourceWidth,
            double sourceHeight,
            double firstSeatSourceX,
            double lastSeatSourceX,
            double forwardDoorSourceX,
            double aftDoorSourceX) => new(
                sourceWidth,
                sourceHeight,
                firstSeatSourceX,
                lastSeatSourceX,
                forwardDoorSourceX,
                aftDoorSourceX,
                [sourceHeight * 0.76d, sourceHeight * 0.60d, sourceHeight * 0.40d, sourceHeight * 0.24d],
                [sourceHeight * 0.78d, sourceHeight * 0.68d, sourceHeight * 0.58d, sourceHeight * 0.48d, sourceHeight * 0.52d, sourceHeight * 0.42d, sourceHeight * 0.32d, sourceHeight * 0.22d],
                sourceHeight * 0.50d);

        private IReadOnlyList<double> BuildSeatY(int count) => Enumerable.Range(0, count)
            .Select(index => SourceHeight * (0.78d - ((0.56d * index) / Math.Max(1, count - 1))))
            .ToArray();
    }

    private static bool IsLowerCabinSeat(string letter) => letter is "A" or "B" or "C" or "D" or "E";

    private static void AddClubRows(
        ICollection<CabinSeat> seats,
        IReadOnlyList<int> rows,
        double startX,
        double endX,
        PassengerCabinClass cabinClass = PassengerCabinClass.Business) =>
        AddRows(
            seats,
            cabinClass,
            rows,
            startX,
            endX,
            ["A", "E", "F", "K"],
            [136d, 110d, 91d, 66d],
            81d,
            121d);

    private static void AddTwoFourTwoRows(
        ICollection<CabinSeat> seats,
        PassengerCabinClass cabinClass,
        IReadOnlyList<int> rows,
        double startX,
        double endX) =>
        AddRows(
            seats,
            cabinClass,
            rows,
            startX,
            endX,
            ["A", "B", "D", "E", "F", "G", "J", "K"],
            [138d, 129d, 113d, 105d, 97d, 89d, 74d, 66d],
            81d,
            121d);

    private static void AddThreeFourThreeRows(
        ICollection<CabinSeat> seats,
        IReadOnlyList<int> rows,
        double startX,
        double endX) =>
        AddRows(
            seats,
            PassengerCabinClass.Economy,
            rows,
            startX,
            endX,
            ["A", "B", "C", "D", "E", "F", "G", "H", "J", "K"],
            [140d, 132d, 124d, 113d, 105d, 97d, 89d, 77d, 69d, 61d],
            81d,
            121d);

    private static void AddRows(
        ICollection<CabinSeat> seats,
        PassengerCabinClass cabinClass,
        IReadOnlyList<int> rows,
        double startX,
        double endX,
        IReadOnlyList<string> letters,
        IReadOnlyList<double> yPositions,
        double upperAisleY,
        double lowerAisleY)
    {
        var rowSpacing = rows.Count == 1 ? 0d : (endX - startX) / (rows.Count - 1);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var x = startX + (rowSpacing * rowIndex);
            for (var seatIndex = 0; seatIndex < letters.Count; seatIndex++)
            {
                var y = yPositions[seatIndex];
                seats.Add(new CabinSeat(
                    $"{rows[rowIndex]}{letters[seatIndex]}",
                    cabinClass,
                    x,
                    y,
                    y <= 103d ? upperAisleY : lowerAisleY));
            }
        }
    }
}
