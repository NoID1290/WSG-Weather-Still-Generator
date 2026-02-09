using System;
using System.Collections.Generic;

namespace EAS.NWS
{
    /// <summary>
    /// Generates fake CAP (Common Alerting Protocol) alerts for testing NWS feed integration
    /// </summary>
    public static class TestNwsAlerts
    {
        /// <summary>
        /// Generate a test tornado warning alert
        /// </summary>
        public static string GenerateTornadoWarning()
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");
            var expires = DateTimeOffset.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>w-nws.webmaster@noaa.gov</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>NWS</source>
    <scope>Public</scope>
    <code>profile:CAP-1.2</code>
    <references/>
    <info>
        <language>en-US</language>
        <category>Met</category>
        <event>Tornado Warning</event>
        <responseType>Evacuate</responseType>
        <urgency>Immediate</urgency>
        <severity>Extreme</severity>
        <certainty>Observed</certainty>
        <onset>{sent}</onset>
        <expires>{expires}</expires>
        <senderName>NWS Chicago</senderName>
        <headline>Tornado Warning issued March 14 at 2:23 PM CDT until 3:30 PM CDT by NWS Chicago</headline>
        <description>A tornado warning means that a tornado has been sighted or indicated by weather radar, and there is a grave threat to life and property from the intensifying tornado. TAKE COVER NOW in the interior part of a sturdy building on a low floor, away from windows.</description>
        <instruction>TAKE COVER NOW in the interior part of a sturdy building on a low floor, away from windows. MOVE TO AN INTERIOR ROOM ON THE LOWEST FLOOR OF A STURDY BUILDING. AVOID WINDOWS. DO NOT ATTEMPT TO OUTRUN A TORNADO IN YOUR VEHICLE. If in a mobile home...EVACUATE NOW to a sturdy building nearby. Persons on the top floor of an apartment or office building near the center of the structure in an interior room should be reasonably safe.</instruction>
        <contact>For more information, contact NWS Chicago</contact>
        <web>https://weather.gov/lot/</web>
        <parameter>
            <valueName>NWSheader</valueName>
            <value>WRNWUS23 KLOT 142323 REC</value>
        </parameter>
        <parameter>
            <valueName>SAME</valueName>
            <value>061741+0140-3612265-WRNWUS23 KLOT 142323 REC</value>
        </parameter>
        <parameter>
            <valueName>Profile</valueName>
            <value>Profile SAME and NWS fmt_TO VTEC /O.NEW.KLOT.TO.W.0054.140223T1923Z-140223T2030Z/</value>
        </parameter>
        <area>
            <areaDesc>Parts of Cook County; LaGrange; Hinsdale; Western suburbs of Chicago</areaDesc>
            <polygon>41.8,-87.9 41.8,-87.8 41.7,-87.8 41.7,-87.9 41.8,-87.9</polygon>
            <geocode>
                <valueName>FIPS6</valueName>
                <value>017031</value>
            </geocode>
            <geocode>
                <valueName>UGC</valueName>
                <value>ILC031</value>
            </geocode>
        </area>
    </info>
</alert>";
        }

        /// <summary>
        /// Generate a test severe thunderstorm warning
        /// </summary>
        public static string GenerateSevereThunderstormWarning()
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");
            var expires = DateTimeOffset.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>w-nws.webmaster@noaa.gov</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>NWS</source>
    <scope>Public</scope>
    <code>profile:CAP-1.2</code>
    <info>
        <language>en-US</language>
        <category>Met</category>
        <event>Severe Thunderstorm Warning</event>
        <responseType>Shelter</responseType>
        <urgency>Expected</urgency>
        <severity>Severe</severity>
        <certainty>Likely</certainty>
        <onset>{sent}</onset>
        <expires>{expires}</expires>
        <senderName>NWS St. Louis</senderName>
        <headline>Severe Thunderstorm Warning issued for parts of St. Louis County</headline>
        <description>A severe thunderstorm capable of producing damaging winds, large hail, and dangerous lightning is moving through the area.</description>
        <instruction>TAKE COVER. Go to an interior room on a lower floor away from windows. Take cover in a basement or small interior room. STAY AWAY FROM WINDOWS.</instruction>
        <parameter>
            <valueName>SAME</valueName>
            <value>061903+0140-3814695-WSSVLS SVX 131903 REC</value>
        </parameter>
        <area>
            <areaDesc>Parts of St. Louis City and St. Louis County</areaDesc>
            <geocode>
                <valueName>UGC</valueName>
                <value>MOC510</value>
            </geocode>
        </area>
    </info>
</alert>";
        }

        /// <summary>
        /// Generate a test winter weather advisory
        /// </summary>
        public static string GenerateWinterWeatherAdvisory()
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");
            var expires = DateTimeOffset.UtcNow.AddHours(12).ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>w-nws.webmaster@noaa.gov</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>NWS</source>
    <scope>Public</scope>
    <code>profile:CAP-1.2</code>
    <info>
        <language>en-US</language>
        <category>Met</category>
        <event>Winter Weather Advisory</event>
        <responseType>Prepare</responseType>
        <urgency>Expected</urgency>
        <severity>Moderate</severity>
        <certainty>Likely</certainty>
        <onset>{sent}</onset>
        <expires>{expires}</expires>
        <senderName>NWS Denver</senderName>
        <headline>Winter Weather Advisory issued for Colorado</headline>
        <description>Heavy snow expected. Plan for slippery road conditions. Please use caution while traveling.</description>
        <instruction>Motorists should be prepared for snow-covered roadways and possible travel delays. Consider postponing travel until roads are treated and passable. The latest road conditions may be obtained by calling 511 in Colorado.</instruction>
        <area>
            <areaDesc>Colorado Front Range</areaDesc>
            <geocode>
                <valueName>UGC</valueName>
                <value>COZ095</value>
            </geocode>
        </area>
    </info>
</alert>";
        }

        /// <summary>
        /// Generate a test flood warning
        /// </summary>
        public static string GenerateFloodWarning()
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");
            var expires = DateTimeOffset.UtcNow.AddHours(6).ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>w-nws.webmaster@noaa.gov</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>NWS</source>
    <scope>Public</scope>
    <code>profile:CAP-1.2</code>
    <info>
        <language>en-US</language>
        <category>Met</category>
        <event>Flash Flood Warning</event>
        <responseType>Evacuate</responseType>
        <urgency>Immediate</urgency>
        <severity>Extreme</severity>
        <certainty>Observed</certainty>
        <onset>{sent}</onset>
        <expires>{expires}</expires>
        <senderName>NWS Houston</senderName>
        <headline>Flash Flood Warning issued for Harris County, Texas</headline>
        <description>Heavy rain is causing life-threatening flash flooding. Many roads have become impassable. MOVE TO HIGHER GROUND NOW. Flooding is imminent or already occurring.</description>
        <instruction>MOVE TO HIGHER GROUND NOW. Most flood deaths occur in automobiles. Never drive through flooded roadways. MOVE AWAY FROM POOLS OF WATER.</instruction>
        <parameter>
            <valueName>SAME</valueName>
            <value>041846+0200-3014825-WFSHOU HGX 131846 REC</value>
        </parameter>
        <area>
            <areaDesc>Harris County and Surrounding Areas along Brazos Creek</areaDesc>
            <geocode>
                <valueName>UGC</valueName>
                <value>TXC201</value>
            </geocode>
        </area>
    </info>
</alert>";
        }

        /// <summary>
        /// Generate a test heat advisory
        /// </summary>
        public static string GenerateHeatAdvisory()
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");
            var expires = DateTimeOffset.UtcNow.AddHours(24).ToString("yyyy-MM-ddTHH:mm:ssZ").Replace("+00:00", "Z");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>w-nws.webmaster@noaa.gov</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>NWS</source>
    <scope>Public</scope>
    <code>profile:CAP-1.2</code>
    <info>
        <language>en-US</language>
        <category>Met</category>
        <event>Heat Advisory</event>
        <responseType>Prepare</responseType>
        <urgency>Expected</urgency>
        <severity>Moderate</severity>
        <certainty>Likely</certainty>
        <onset>{sent}</onset>
        <expires>{expires}</expires>
        <senderName>NWS Phoenix</senderName>
        <headline>Heat Advisory issued for the Phoenix metropolitan area</headline>
        <description>Dangerously hot conditions will continue through the afternoon. Heat index values expected to reach 125 to 130 degrees Fahrenheit.</description>
        <instruction>Drink plenty of fluids, stay in an air-conditioned room, and check up on relatives and neighbors. Take extra precautions if you work or spend time outside. When possible, reschedule strenuous activities to early morning or evening. Know the signs and symptoms of heat exhaustion and heat cramps.</instruction>
        <area>
            <areaDesc>Phoenix metropolitan area and surrounding desert regions</areaDesc>
            <geocode>
                <valueName>UGC</valueName>
                <value>AZZ085</value>
            </geocode>
        </area>
    </info>
</alert>";
        }

        /// <summary>
        /// Get all test alert types
        /// </summary>
        public static Dictionary<string, Func<string>> GetAllTestAlerts()
        {
            return new Dictionary<string, Func<string>>
            {
                { "Tornado Warning", GenerateTornadoWarning },
                { "Severe Thunderstorm Warning", GenerateSevereThunderstormWarning },
                { "Winter Weather Advisory", GenerateWinterWeatherAdvisory },
                { "Flash Flood Warning", GenerateFloodWarning },
                { "Heat Advisory", GenerateHeatAdvisory }
            };
        }
    }
}
