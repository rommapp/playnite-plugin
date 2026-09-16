using System;

namespace RomM
{
    /// <summary>
    /// The plugin's own version, as reported to RomM whenever this client registers itself (QR
    /// pairing, save sync device registration). Read from the assembly -- built from Version in
    /// RomM.csproj -- so the registration paths cannot report different versions from one another;
    /// PluginVersionTests ties that version to the one the addon manifests declare.
    /// </summary>
    internal static class PluginVersion
    {
        /// <summary>
        /// Major.minor.patch, matching the Version in extension.yaml. Assemblies always carry a
        /// four-part version ("0.9.0.0"), and the revision is never set here, so it is dropped.
        /// </summary>
        public static string Current { get; } = Format(typeof(PluginVersion).Assembly.GetName().Version);

        internal static string Format(Version version)
        {
            if (version == null)
                return "0.0.0";

            // Build is -1, not 0, when the version was declared with two parts.
            return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }
    }
}
