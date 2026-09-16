using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RomM.Tests
{
    /// <summary>
    /// The version this client reports to RomM comes from the assembly, which is built from Version
    /// in RomM.csproj, while the addon manifests declare their own. Nothing in the compiler or the
    /// packaging step ties the two together, so a release that bumped one and forgot the other would
    /// ship a plugin identifying itself as the wrong version, silently. These tests are that tie.
    /// </summary>
    public class PluginVersionTests
    {
        [Fact]
        public void Project_version_matches_the_extension_manifest()
        {
            Assert.Equal(ExtensionVersion(), ProjectVersion());
        }

        [Fact]
        public void Installer_manifest_carries_a_package_for_the_current_version()
        {
            var packaged = Regex.Matches(ReadDeclaration("installer.yaml"), @"(?m)^\s*-\s*Version:\s*(\S+)\s*$")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();

            Assert.Contains(ExtensionVersion(), packaged);
        }

        [Theory]
        [InlineData("0.9.0.0", "0.9.0")]
        [InlineData("1.2.3.4", "1.2.3")]
        [InlineData("0.9", "0.9.0")] // Build is -1 when only two parts were declared.
        public void Format_drops_the_revision_the_plugin_never_sets(string assemblyVersion, string expected)
        {
            Assert.Equal(expected, PluginVersion.Format(new Version(assemblyVersion)));
        }

        [Fact]
        public void Format_survives_an_assembly_with_no_version()
        {
            Assert.Equal("0.0.0", PluginVersion.Format(null));
        }

        private static string ProjectVersion() =>
            Declaration("RomM.csproj.txt", @"<Version>\s*([^<\s]+)\s*</Version>");

        private static string ExtensionVersion() =>
            Declaration("extension.yaml", @"(?m)^﻿?Version:\s*(\S+)\s*$");

        private static string Declaration(string file, string pattern)
        {
            var match = Regex.Match(ReadDeclaration(file), pattern);
            Assert.True(match.Success, $"No version declaration found in {file}.");
            return match.Groups[1].Value;
        }

        // RomM.Tests.csproj copies these three next to the test assembly; the project file keeps a
        // .txt suffix there so nothing mistakes the copy for a project it should build.
        private static string ReadDeclaration(string file) =>
            File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Declarations", file));
    }
}
