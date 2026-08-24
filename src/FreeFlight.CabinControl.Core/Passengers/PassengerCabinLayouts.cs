namespace FreeFlight.CabinControl.Core.Passengers;

internal sealed record PassengerCabinLayoutDefinition(
    PassengerCabinLayout Layout,
    IReadOnlyList<CabinSeat> Seats,
    double L1DoorX,
    double L2DoorX);

internal static class PassengerCabinLayouts
{
    public static PassengerCabinLayoutDefinition Create(PassengerCabinLayout layout) => layout switch
    {
        PassengerCabinLayout.BritishAirways777200Er => CreateBritishAirways777200Er(),
        PassengerCabinLayout.BritishAirways777300 => CreateBritishAirways777300(),
        _ => CreateFlightFactor777V2()
    };

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
        return new PassengerCabinLayoutDefinition(PassengerCabinLayout.FlightFactor777V2, seats, 183d, 426d);
    }

    private static PassengerCabinLayoutDefinition CreateBritishAirways777200Er()
    {
        var seats = new List<CabinSeat>(280);
        AddClubRows(seats, Enumerable.Range(1, 7).ToArray(), 72d, 264d);
        AddClubRows(seats, Enumerable.Range(10, 5).ToArray(), 330d, 460d);
        AddTwoFourTwoRows(seats, PassengerCabinClass.PremiumEconomy, Enumerable.Range(15, 5).ToArray(), 515d, 605d);
        AddThreeFourThreeRows(seats, Enumerable.Range(20, 5).ToArray(), 650d, 724d);
        AddThreeFourThreeRows(seats, Enumerable.Range(26, 11).ToArray(), 766d, 916d);
        AddTwoFourTwoRows(seats, PassengerCabinClass.Economy, Enumerable.Range(37, 4).ToArray(), 934d, 985d);
        return new PassengerCabinLayoutDefinition(PassengerCabinLayout.BritishAirways777200Er, seats, 52d, 295d);
    }

    private static PassengerCabinLayoutDefinition CreateBritishAirways777300()
    {
        var seats = new List<CabinSeat>(266);
        AddClubRows(seats, [1, 2], 76d, 112d, PassengerCabinClass.First);
        AddClubRows(seats, [5, 6, 7], 146d, 205d);
        AddClubRows(seats, Enumerable.Range(8, 10).ToArray(), 276d, 510d);
        AddClubRows(seats, Enumerable.Range(19, 6).ToArray(), 560d, 695d);
        AddTwoFourTwoRows(seats, PassengerCabinClass.PremiumEconomy, Enumerable.Range(25, 5).ToArray(), 735d, 807d);
        AddThreeFourThreeRows(seats, Enumerable.Range(30, 11).ToArray(), 842d, 934d);
        AddTwoFourTwoRows(seats, PassengerCabinClass.Economy, Enumerable.Range(41, 4).ToArray(), 948d, 992d);
        return new PassengerCabinLayoutDefinition(PassengerCabinLayout.BritishAirways777300, seats, 50d, 228d);
    }

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
