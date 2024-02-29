namespace audio_cap_grpc.Utils;

using System;

public class TimeUtils
{
    public static int GetCurrentUnixTimestamp()
    {
        // Get the current UTC time
        DateTime now = DateTime.UtcNow;
        
        // Unix epoch
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        // Subtract the epoch from the current UTC time and get the total seconds
        // Cast to int to get a 10-digit timestamp
        int timestamp = (int)(now - epoch).TotalSeconds;
        
        return timestamp;
    }
}
