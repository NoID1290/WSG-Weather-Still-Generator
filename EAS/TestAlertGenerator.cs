using System;
using System.Collections.Generic;

namespace EAS
{
    /// <summary>
    /// Generates fake CAP-CP alerts for testing NAAD/Alert Ready integration
    /// </summary>
    public static class TestAlertGenerator
    {
        /// <summary>
        /// Generate a test AMBER alert (child abduction)
        /// </summary>
        public static string GenerateAmberAlert(string language = "fr-CA")
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var expires = DateTimeOffset.UtcNow.AddHours(12).ToString("yyyy-MM-ddTHH:mm:sszzz");

            if (language == "fr-CA")
            {
                return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>cap@alerts.pelmorex.com</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>Test System</source>
    <scope>Public</scope>
    <code>profile:CAP-CP:0.4</code>
    <info>
        <language>fr-CA</language>
        <category>Safety</category>
        <event>Alerte AMBER</event>
        <responseType>Monitor</responseType>
        <urgency>Immediate</urgency>
        <severity>Severe</severity>
        <certainty>Observed</certainty>
        <expires>{expires}</expires>
        <senderName>Sûreté du Québec</senderName>
        <headline>Alerte AMBER - Enfant disparu</headline>
        <description>Un enfant de 7 ans a été enlevé à Montréal. Garçon aux cheveux bruns, portant un chandail bleu. Véhicule suspect: Honda Civic grise, plaque Québec ABC 123. Dernière fois vu rue Sainte-Catherine Est à 14h30.</description>
        <instruction>Si vous apercevez l'enfant ou le véhicule, composez le 911 immédiatement. Ne tentez pas d'intervenir vous-même.</instruction>
        <parameter>
            <valueName>layer:SOREM:1.0:Broadcast_Immediately</valueName>
            <value>Yes</value>
        </parameter>
        <area>
            <areaDesc>Région métropolitaine de Montréal</areaDesc>
            <geocode>
                <valueName>profile:CAP-CP:Location:0.3</valueName>
                <value>2466</value>
            </geocode>
        </area>
    </info>
</alert>";
            }
            else
            {
                return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>cap@alerts.pelmorex.com</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>Test System</source>
    <scope>Public</scope>
    <code>profile:CAP-CP:0.4</code>
    <info>
        <language>en-CA</language>
        <category>Safety</category>
        <event>AMBER Alert</event>
        <responseType>Monitor</responseType>
        <urgency>Immediate</urgency>
        <severity>Severe</severity>
        <certainty>Observed</certainty>
        <expires>{expires}</expires>
        <senderName>Ontario Provincial Police</senderName>
        <headline>AMBER Alert - Missing Child</headline>
        <description>A 7-year-old child has been abducted in Toronto. Boy with brown hair, wearing a blue shirt. Suspect vehicle: grey Honda Civic, Ontario plate ABC 123. Last seen on Queen Street East at 2:30 PM.</description>
        <instruction>If you see the child or vehicle, call 911 immediately. Do not attempt to intervene yourself.</instruction>
        <parameter>
            <valueName>layer:SOREM:1.0:Broadcast_Immediately</valueName>
            <value>Yes</value>
        </parameter>
        <area>
            <areaDesc>Greater Toronto Area</areaDesc>
            <geocode>
                <valueName>profile:CAP-CP:Location:0.3</valueName>
                <value>3520</value>
            </geocode>
        </area>
    </info>
</alert>";
            }
        }

        /// <summary>
        /// Generate a test civil emergency alert
        /// </summary>
        public static string GenerateCivilEmergencyAlert(string language = "fr-CA")
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var expires = DateTimeOffset.UtcNow.AddHours(24).ToString("yyyy-MM-ddTHH:mm:sszzz");

            if (language == "fr-CA")
            {
                return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>cap@alerts.pelmorex.com</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>Test System</source>
    <scope>Public</scope>
    <code>profile:CAP-CP:0.4</code>
    <info>
        <language>fr-CA</language>
        <category>Safety</category>
        <event>Urgence civile</event>
        <responseType>Evacuate</responseType>
        <urgency>Immediate</urgency>
        <severity>Extreme</severity>
        <certainty>Observed</certainty>
        <expires>{expires}</expires>
        <senderName>Ministère de la Sécurité publique du Québec</senderName>
        <headline>Ordre d'évacuation immédiate</headline>
        <description>Fuite de matières dangereuses dans le secteur industriel. Risque pour la santé publique. Évacuation obligatoire dans un rayon de 2 kilomètres de l'usine ChemTech sur le boulevard Industrial.</description>
        <instruction>Évacuez immédiatement vers le nord. Centre d'accueil temporaire: Aréna municipal, 500 rue Principale. Apportez médicaments essentiels et pièces d'identité. N'utilisez pas de téléphone cellulaire dans la zone d'évacuation.</instruction>
        <parameter>
            <valueName>layer:SOREM:1.0:Broadcast_Immediately</valueName>
            <value>Yes</value>
        </parameter>
        <area>
            <areaDesc>Secteur de Laval-des-Rapides, Laval</areaDesc>
            <geocode>
                <valueName>profile:CAP-CP:Location:0.3</valueName>
                <value>2465</value>
            </geocode>
        </area>
    </info>
</alert>";
            }
            else
            {
                return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>cap@alerts.pelmorex.com</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>Test System</source>
    <scope>Public</scope>
    <code>profile:CAP-CP:0.4</code>
    <info>
        <language>en-CA</language>
        <category>Safety</category>
        <event>Civil Emergency</event>
        <responseType>Evacuate</responseType>
        <urgency>Immediate</urgency>
        <severity>Extreme</severity>
        <certainty>Observed</certainty>
        <expires>{expires}</expires>
        <senderName>Emergency Management Ontario</senderName>
        <headline>Immediate Evacuation Order</headline>
        <description>Hazardous material spill in industrial area. Public health risk. Mandatory evacuation within 2 kilometers of ChemTech plant on Industrial Boulevard.</description>
        <instruction>Evacuate immediately north. Temporary shelter: Municipal Arena, 500 Main Street. Bring essential medications and identification. Do not use cell phones in evacuation zone.</instruction>
        <parameter>
            <valueName>layer:SOREM:1.0:Broadcast_Immediately</valueName>
            <value>Yes</value>
        </parameter>
        <area>
            <areaDesc>City of Mississauga</areaDesc>
            <geocode>
                <valueName>profile:CAP-CP:Location:0.3</valueName>
                <value>3521</value>
            </geocode>
        </area>
    </info>
</alert>";
            }
        }

        /// <summary>
        /// Generate a test public safety alert (moderate severity)
        /// </summary>
        public static string GeneratePublicSafetyAlert(string language = "fr-CA")
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var expires = DateTimeOffset.UtcNow.AddHours(6).ToString("yyyy-MM-ddTHH:mm:sszzz");

            if (language == "fr-CA")
            {
                return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>cap@alerts.pelmorex.com</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>Test System</source>
    <scope>Public</scope>
    <code>profile:CAP-CP:0.4</code>
    <info>
        <language>fr-CA</language>
        <category>Safety</category>
        <event>Avis de sécurité publique</event>
        <responseType>Monitor</responseType>
        <urgency>Expected</urgency>
        <severity>Moderate</severity>
        <certainty>Likely</certainty>
        <expires>{expires}</expires>
        <senderName>Service de police de Québec</senderName>
        <headline>Panne d'électricité majeure - Soyez vigilants</headline>
        <description>Panne électrique généralisée affectant plus de 50,000 foyers dans le secteur de Sainte-Foy. Les feux de circulation ne fonctionnent pas. Temps de rétablissement estimé: 4-6 heures.</description>
        <instruction>Utilisez les intersections avec prudence. Évitez les déplacements non essentiels. Vérifiez vos voisins vulnérables. Ne branchez pas de génératrices à l'intérieur.</instruction>
        <area>
            <areaDesc>Sainte-Foy, Québec</areaDesc>
            <geocode>
                <valueName>profile:CAP-CP:Location:0.3</valueName>
                <value>2423</value>
            </geocode>
        </area>
    </info>
</alert>";
            }
            else
            {
                return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>cap@alerts.pelmorex.com</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>Test System</source>
    <scope>Public</scope>
    <code>profile:CAP-CP:0.4</code>
    <info>
        <language>en-CA</language>
        <category>Safety</category>
        <event>Public Safety Advisory</event>
        <responseType>Monitor</responseType>
        <urgency>Expected</urgency>
        <severity>Moderate</severity>
        <certainty>Likely</certainty>
        <expires>{expires}</expires>
        <senderName>Ottawa Police Service</senderName>
        <headline>Major Power Outage - Exercise Caution</headline>
        <description>Widespread power outage affecting over 50,000 homes in the downtown area. Traffic lights are not functioning. Estimated restoration time: 4-6 hours.</description>
        <instruction>Use intersections with caution. Avoid non-essential travel. Check on vulnerable neighbors. Do not operate generators indoors.</instruction>
        <area>
            <areaDesc>Downtown Ottawa</areaDesc>
            <geocode>
                <valueName>profile:CAP-CP:Location:0.3</valueName>
                <value>3506</value>
            </geocode>
        </area>
    </info>
</alert>";
            }
        }

        /// <summary>
        /// Generate a test meteorological alert (to verify filtering works)
        /// </summary>
        public static string GenerateWeatherAlert(string language = "fr-CA")
        {
            var identifier = Guid.NewGuid().ToString("N").ToUpper();
            var sent = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var expires = DateTimeOffset.UtcNow.AddHours(8).ToString("yyyy-MM-ddTHH:mm:sszzz");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<alert xmlns=""urn:oasis:names:tc:emergency:cap:1.2"">
    <identifier>{identifier}</identifier>
    <sender>cap@alerts.pelmorex.com</sender>
    <sent>{sent}</sent>
    <status>Actual</status>
    <msgType>Alert</msgType>
    <source>Test System</source>
    <scope>Public</scope>
    <code>profile:CAP-CP:0.4</code>
    <info>
        <language>{language}</language>
        <category>Met</category>
        <event>Avertissement de tempête hivernale</event>
        <responseType>Prepare</responseType>
        <urgency>Expected</urgency>
        <severity>Severe</severity>
        <certainty>Likely</certainty>
        <expires>{expires}</expires>
        <senderName>Environnement et Changement climatique Canada</senderName>
        <headline>Avertissement de tempête hivernale</headline>
        <description>Tempête hivernale importante. 25 à 35 cm de neige attendus.</description>
        <instruction>Évitez les déplacements non essentiels.</instruction>
        <area>
            <areaDesc>Montréal</areaDesc>
            <geocode>
                <valueName>profile:CAP-CP:Location:0.3</valueName>
                <value>2466</value>
            </geocode>
        </area>
    </info>
</alert>";
        }

        /// <summary>
        /// Get all test alert types
        /// </summary>
        public static Dictionary<string, Func<string, string>> GetAllTestAlerts()
        {
            return new Dictionary<string, Func<string, string>>
            {
                { "AMBER Alert", GenerateAmberAlert },
                { "Civil Emergency", GenerateCivilEmergencyAlert },
                { "Public Safety", GeneratePublicSafetyAlert },
                { "Weather (should be filtered)", GenerateWeatherAlert }
            };
        }
    }
}
