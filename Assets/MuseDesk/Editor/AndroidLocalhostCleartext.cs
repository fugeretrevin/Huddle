#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace MuseDesk.EditorTools
{
    /// <summary>
    /// Permits cleartext HTTP/WS ONLY to 127.0.0.1 / localhost (the adb-reverse target used
    /// for USB-tethered live mode). Scoped to localhost, so it is safe to keep in the
    /// competition build — it never allows cleartext to the public internet.
    /// </summary>
    public class AndroidLocalhostCleartext : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        const string NscXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<network-security-config>\n" +
            "  <base-config cleartextTrafficPermitted=\"false\" />\n" +
            "  <domain-config cleartextTrafficPermitted=\"true\">\n" +
            "    <domain includeSubdomains=\"false\">127.0.0.1</domain>\n" +
            "    <domain includeSubdomains=\"false\">localhost</domain>\n" +
            "  </domain-config>\n" +
            "</network-security-config>\n";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // write res/xml/network_security_config.xml
            var xmlDir = Path.Combine(path, "src", "main", "res", "xml");
            Directory.CreateDirectory(xmlDir);
            File.WriteAllText(Path.Combine(xmlDir, "network_security_config.xml"), NscXml);

            // reference it from <application>
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[MuseDesk] manifest not found at {manifestPath}; cleartext not wired.");
                return;
            }
            const string androidNs = "http://schemas.android.com/apk/res/android";
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var app = doc.SelectSingleNode("/manifest/application") as XmlElement;
            if (app == null)
            {
                Debug.LogWarning("[MuseDesk] <application> not found; cleartext not wired.");
                return;
            }
            app.SetAttribute("networkSecurityConfig", androidNs, "@xml/network_security_config");
            doc.Save(manifestPath);
            Debug.Log("[MuseDesk] Android localhost cleartext enabled (127.0.0.1/localhost only).");
        }
    }
}
#endif
