using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using Xunit;

namespace UsbToC64.Core.Tests;

public class MappingImporterTests
{
    [Fact]
    public void RoundTrip_ProposalSurvivesExportAndImport()
    {
        var pad = ControlClassifier.Classify(Descriptors.Ps3Clone0810);
        var original = MappingProposer.Propose(pad);
        var profile = ProfileBuilder.Build(original, 0x0810, 1);

        var r = MappingImporter.FromProfile(pad, profile);
        Assert.Empty(r.UnmatchedRules);
        Assert.Empty(r.UnmatchedAxes);
        foreach (var c in pad.Controls) Assert.Equal(original.Get(c), r.Mapping.Get(c));
    }

    [Fact]
    public void EqSingleBitRules_FromWebSave_AreRecognisedAsButtons()
    {
        var pad = ControlClassifier.Classify(Descriptors.Ps3Clone0810);
        var p = new JmProfile { Vid = 0x0810, Pid = 1 };
        p.Rules.Add(new JmRule(5, 64, JmOp.Eq, JmFunc.Fire1));   // web SAVE style
        p.Rules.Add(new JmRule(9, 1, JmOp.BitAny, JmFunc.Fire2)); // no such control
        var r = MappingImporter.FromProfile(pad, p);
        Assert.Equal(PadAction.Fire1, r.Mapping.Get(pad.Controls.Single(c => c.Id == "btn3")));
        Assert.Single(r.UnmatchedRules);
    }
}
