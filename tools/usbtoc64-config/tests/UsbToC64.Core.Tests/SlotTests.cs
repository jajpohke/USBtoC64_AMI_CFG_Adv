using UsbToC64.Core.Device;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Model;
using Xunit;

namespace UsbToC64.Core.Tests;

/// <summary>Profile drawers of firmware 1.3.0 (8 labelled profiles, one per pad).</summary>
public class SlotTests
{
    private static JmProfile Pad(ushort vid, ushort pid, byte fireMask = 0x40)
    {
        var p = new JmProfile { Vid = vid, Pid = pid, DpadIndex = 5 };
        p.Rules.Add(new JmRule(5, 0, JmOp.Hat, JmFunc.Up));
        p.Rules.Add(new JmRule(5, fireMask, JmOp.BitAny, JmFunc.Fire1));
        return p;
    }

    private static AdapterWriter Writer(FakeAdapter a) => new(a, _ => { }, _ => { }) { BootDelay = TimeSpan.Zero };

    [Fact]
    public void Write_WithLabel_GoesToAFreeDrawer_AndIsVerifiedWithTheLabel()
    {
        var a = new FakeAdapter { Version = "1.3.0" };
        var r = Writer(a).WriteAndVerify(Pad(0x0810, 0x0001), label: "PS3 generico");
        Assert.True(r.Verified, r.Message + string.Join("\n", r.Differences));
        Assert.Equal(0, r.Slot);
        Assert.Equal("PS3 generico", r.Label);
        Assert.Equal("PS3 generico", a.Drawers[0].Label);
    }

    [Fact]
    public void Write_SamePadAgain_ReplacesItsDrawer()
    {
        var a = new FakeAdapter { Version = "1.3.0" };
        a.PutDrawer(0, Pad(0x045E, 0x02EA), "Xbox One");
        a.PutDrawer(3, Pad(0x0810, 0x0001), "PS3 generico");
        var r = Writer(a).WriteAndVerify(Pad(0x0810, 0x0001, 0x20));
        Assert.True(r.Verified, r.Message);
        Assert.Equal(3, r.Slot);
        Assert.Equal("PS3 generico", a.Drawers[3].Label); // no label sent: the pad keeps its label
        Assert.Equal(2, a.Drawers.Count);
    }

    [Fact]
    public void Write_ToAChosenDrawer_MovesThePadThere()
    {
        var a = new FakeAdapter { Version = "1.3.0" };
        a.PutDrawer(1, Pad(0x0810, 0x0001), "Vecchio");
        var r = Writer(a).WriteAndVerify(Pad(0x0810, 0x0001), slot: 5, label: "Nuovo");
        Assert.True(r.Verified, r.Message);
        Assert.Equal(5, r.Slot);
        Assert.False(a.Drawers.ContainsKey(1)); // one drawer per pad
        Assert.Equal("Nuovo", a.Drawers[5].Label);
    }

    [Fact]
    public void Write_On123_IgnoresDrawers()
    {
        var a = new FakeAdapter { Version = "1.2.3" };
        var r = Writer(a).WriteAndVerify(Pad(0x0810, 0x0001), slot: 2, label: "x");
        Assert.True(r.Verified, r.Message);
        Assert.Null(r.Slot);
        Assert.Empty(a.Drawers);
    }

    [Fact]
    public void ListRenameDelete()
    {
        var a = new FakeAdapter { Version = "1.3.0" };
        a.PutDrawer(2, Pad(0x0810, 0x0001), "");
        a.PutDrawer(0, Pad(0x045E, 0x028E), "Xbox 360");
        using var conn = AdapterConnection.Connect(a, null, _ => { }, bootDelay: TimeSpan.Zero);
        var list = conn.Session.ListSlots();
        Assert.Equal(new[] { 0, 2 }, list.Select(s => s.Slot));
        Assert.Equal(SlotLabels.WebConfigured, list[1].DisplayLabel);
        Assert.Equal("0810:0001", list[1].Ids);

        conn.Session.RenameSlot(2, "  PS3 nero  ");
        Assert.Equal("PS3 nero", conn.Session.ReadSlot(2)!.Info.Label);

        conn.Session.DeleteSlot(0);
        Assert.Single(conn.Session.ListSlots());
        Assert.Null(conn.Session.ReadSlot(0));
        Assert.Throws<InvalidOperationException>(() => conn.Session.DeleteSlot(0));
    }

    [Fact]
    public void ReadAllSlots_ReturnsRules()
    {
        var a = new FakeAdapter { Version = "1.3.0" };
        a.PutDrawer(1, Pad(0x0810, 0x0001, 0x10), "PS3");
        using var conn = AdapterConnection.Connect(a, null, _ => { }, bootDelay: TimeSpan.Zero);
        var all = conn.Session.ReadAllSlots();
        var d = Assert.Single(all);
        Assert.Empty(ProfileComparer.Compare(Pad(0x0810, 0x0001, 0x10), d.Profile));
    }

    [Theory]
    [InlineData("Controller (Xbox One For Windows)", "Xbox One For Windows")]
    [InlineData("USB Gamepad", "USB Gamepad")]
    [InlineData("", "Pad 0810:0001")]
    [InlineData("A very long name for a game controller", "A very long name for a g")]
    public void AutoLabel(string name, string expected) =>
        Assert.Equal(expected, SlotLabels.Auto(name, 0x0810, 0x0001));

    [Fact]
    public void Clean_KeepsAtMost24Bytes_WithoutSplittingCharacters()
    {
        var s = SlotLabels.Clean("àààààààààààààààààààà"); // 2 bytes each
        Assert.Equal(12, s.Length);
        Assert.True(SlotLabels.ByteCount(s) <= FirmwareLimits.LabelMaxBytes);
        Assert.Equal("ab", SlotLabels.Clean("a\u0001b\n"));
    }

    [Fact]
    public void Choose_OwnThenFreeThenOldest()
    {
        var used = Enumerable.Range(0, 8).Select(i => new SlotInfo(i, (ushort)i, 1, "", 1, false, (uint)(10 - i))).ToList();
        Assert.Equal(3, SlotLabels.Choose(used, 3, 1));   // own
        Assert.Equal(7, SlotLabels.Choose(used, 99, 1));  // full: oldest (lowest seq)
        Assert.Equal(0, SlotLabels.Choose(used.Skip(1).ToList(), 99, 1)); // first free
    }

    [Fact]
    public void SaveJm_WithSlotAndLabel_IsStillParsable()
    {
        var json = SaveJmJson.Serialize(Pad(1, 2), 4, "Pad \"rosso\"");
        Assert.Contains("\"slot\":4", json);
        var back = SaveJmJson.Parse(json);
        Assert.Equal(2, back.Rules.Count);
    }
}

/// <summary>Editing a drawer without the pad: controls rebuilt from the rules.</summary>
public class OfflinePadTests
{
    [Theory]
    [InlineData("JoystickMapping_25.h")]
    [InlineData("JoystickMapping_24.h")]
    [InlineData("JoystickMapping_2_1.h")]
    [InlineData("JoystickMapping_3_1.h")]
    public void FromRules_EveryRuleFindsItsControl(string fixture)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var profile = UsbToC64.Core.Formats.HeaderImporter.Import(text).Profile;
        var pad = UsbToC64.Core.Hid.OfflinePad.FromRules(profile);
        var res = UsbToC64.Core.Mapping.MappingImporter.FromProfile(pad, profile);
        // Only "==" rules on several bits and AUTO L/R cannot become controls.
        Assert.All(res.UnmatchedRules, r => Assert.True(
            (r.Op == JmOp.Eq && (r.Value & (r.Value - 1)) != 0) || r.Func >= JmFunc.AutoLeftRightOn
            || res.Mapping.Pad.Controls.Any(c => c.ByteIndex == r.Index), r.ToString()));
        Assert.Empty(res.UnmatchedAxes);
    }

    [Fact]
    public void FromRules_ThenRebuild_KeepsButtonsAndAxes()
    {
        var p = new JmProfile { Vid = 1, Pid = 2 };
        p.Rules.Add(new JmRule(5, 0x20, JmOp.BitAny, JmFunc.Fire1));
        p.Rules.Add(new JmRule(6, 0x01, JmOp.Eq, JmFunc.Fire2));
        p.Rules.Add(new JmRule(14, unchecked((byte)(sbyte)-51), JmOp.SignedLess, JmFunc.Left));
        p.Rules.Add(new JmRule(14, 51, JmOp.SignedGreater, JmFunc.Right));
        p.AnalogY.Add(4);
        var pad = UsbToC64.Core.Hid.OfflinePad.FromRules(p);
        var m = UsbToC64.Core.Mapping.MappingImporter.FromProfile(pad, p);
        Assert.Empty(m.UnmatchedRules);
        var rebuilt = UsbToC64.Core.Mapping.ProfileBuilder.Build(m.Mapping, 1, 2);
        Assert.Contains(rebuilt.Rules, r => r.Index == 5 && r.Value == 0x20 && r.Func == JmFunc.Fire1);
        Assert.Contains(rebuilt.Rules, r => r.Index == 6 && r.Value == 0x01 && r.Func == JmFunc.Fire2);
        Assert.Contains(rebuilt.Rules, r => r.Index == 14 && r.Op == JmOp.SignedGreater && r.Func == JmFunc.Right);
        Assert.Equal(new byte[] { 4 }, rebuilt.AnalogY);
    }

    [Fact]
    public void Describe_UsesTheCardWhenThePadWasSeen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "usbtoc64-cards-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new UsbToC64.Core.Hid.PadCatalog(dir);
            catalog.Save(new UsbToC64.Core.Hid.PadCard(0x0810, 0x0001, "USB Gamepad", null,
                Convert.ToHexString(Descriptors.Ps3Clone0810), DateTime.UtcNow));
            var card = catalog.Find(0x0810, 0x0001);
            Assert.NotNull(card);
            var p = new JmProfile { Vid = 0x0810, Pid = 0x0001 };
            p.Rules.Add(new JmRule(5, 0x20, JmOp.BitAny, JmFunc.Fire1));
            var (pad, src) = UsbToC64.Core.Hid.OfflinePad.Describe(p, card);
            Assert.Equal(UsbToC64.Core.Hid.OfflineSource.Card, src);
            Assert.True(pad.Controls.Count > 4);
            Assert.Equal(UsbToC64.Core.Hid.OfflineSource.Rules, UsbToC64.Core.Hid.OfflinePad.Describe(p, null).Source);
            Assert.Equal(UsbToC64.Core.Hid.OfflineSource.KnownXbox,
                UsbToC64.Core.Hid.OfflinePad.Describe(new JmProfile { Vid = 0x045E, Pid = 0x02EA }, null).Source);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
