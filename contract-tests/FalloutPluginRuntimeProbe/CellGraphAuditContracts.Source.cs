using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void SourceCampaigns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-audit-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string Installation(string name, params string[] files)
            {
                var root = Path.Combine(directory, name);
                var data = Path.Combine(root, "dAtA");
                Directory.CreateDirectory(data);
                foreach (var file in files) File.WriteAllBytes(Path.Combine(data, file), []);
                return root;
            }

            var newVegas = Installation("NewVegas", "fAlLoUtNv.EsM");
            var fallout3 = Installation("Fallout3", "Fallout3.esm");
            var ttw = Installation("TTW", "FalloutNV.esm", "Fallout3.esm", "TaleOfTwoWastelands.esm");
            Require(DevelopmentLabSource.Campaign(newVegas) == RuntimeLiveContentSource.FalloutNewVegasGame &&
                DevelopmentLabSource.Campaign(fallout3) == RuntimeLiveContentSource.Fallout3Game &&
                DevelopmentLabSource.Campaign(ttw) == RuntimeLiveContentSource.FalloutNewVegasGame &&
                DevelopmentLabSource.Campaign(Path.Combine(fallout3, "dAtA")) == RuntimeLiveContentSource.Fallout3Game,
                "The audit source did not preserve detected standalone or TTW engine identity.");
            Reject(() => DevelopmentLabSource.Campaign(Path.Combine(directory, "missing")), "does not exist");
            Reject(() => DevelopmentLabSource.Campaign(Installation("unrecognized")), "not a recognized");
            foreach (var classic in new[] { "Fallout1", "Fallout2" })
            {
                var root = Installation(classic);
                File.WriteAllBytes(Path.Combine(root, "master.dat"), []);
                File.WriteAllBytes(Path.Combine(root, "critter.dat"), []);
                if (classic == "Fallout2") File.WriteAllBytes(Path.Combine(root, "patch000.dat"), []);
                Reject(() => DevelopmentLabSource.Campaign(root), "requires a Fallout 3 or Fallout: New Vegas");
            }
            Console.WriteLine("OPENNV_AUDIT_SOURCE_CAMPAIGN_CONTRACT_PASS newVegas=true fallout3=true ttwUsesNewVegas=true dataRoot=true invalidRootRefused=true classicRefused=true");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
