namespace FreeFlight.CabinControl.Core.Integration;

public static class XPlaneFlightPhaseClassifier
{
    public static string Classify(
        bool onGround,
        double groundSpeedMetresPerSecond,
        double altitudeAboveGroundFeet,
        double verticalSpeedFeetPerMinute,
        bool anyEngineRunning)
    {
        if (onGround)
        {
            if (groundSpeedMetresPerSecond >= 1.5d)
            {
                return "Taxi";
            }

            return anyEngineRunning ? "On stand · engines running" : "On stand";
        }

        if (altitudeAboveGroundFeet <= 2_500d && verticalSpeedFeetPerMinute < -150d)
        {
            return "Approach";
        }

        if (verticalSpeedFeetPerMinute >= 300d)
        {
            return "Climb";
        }

        if (verticalSpeedFeetPerMinute <= -300d)
        {
            return "Descent";
        }

        return "Cruise";
    }
}
