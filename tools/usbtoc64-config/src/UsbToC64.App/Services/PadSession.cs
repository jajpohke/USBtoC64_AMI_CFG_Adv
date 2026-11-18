using UsbToC64.Core.Device;
using UsbToC64.Core.Emulation;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.Text;
using UsbToC64.Core.Validation;
using UsbToC64.Input;
using PadControl = UsbToC64.Core.Hid.Control;

namespace UsbToC64.App.Services;

/// <summary>
/// The pad connected to the PC: description, mapping, resulting profile and live reading.
/// UI-independent: pages observe <see cref="Changed"/> and poll <see cref="Snapshot"/>.
/// </summary>
public sealed class PadSession : IDisposable
{
    private readonly object _sync = new();
    private Thread? _readerThread;
    private CancellationTokenSource? _cts;
    private DynamicEngine? _engine;
    private byte[]? _lastReport;
    private C64State? _lastState;
    private string? _readerError;

    public List<PadDevice> Devices { get; private set; } = new();
    public PadDevice? Device { get; private set; }
    public PadDescription? Pad { get; private set; }
    public PadMapping? Mapping { get; private set; }
    public JmProfile? Profile { get; private set; }

    /// <summary>Profile shown read-only (imported without a pad, or not matching the pad).</summary>
    public bool ViewOnly { get; private set; }
    public ImportedHeader? ViewHeader { get; private set; }
    public List<string> ExtraNotes { get; } = new();
    /// <summary>Recipes of <see cref="ExtraNotes"/>, so the notes can be rewritten in a new language.</summary>
    private readonly List<Func<string>> _extraNoteMakers = new();
    public List<Issue> Issues { get; private set; } = new();

    /// <summary>Cards of the pads seen by this PC (%APPDATA%\USBtoC64Config\pads): controls for offline editing.</summary>
    public PadCatalog Catalog { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "USBtoC64Config", "pads"));

    /// <summary>Drawer being edited (opened from the drawers page); null = normal editing of the connected pad.</summary>
    public SlotInfo? EditSlot { get; private set; }
    /// <summary>Editing a drawer without its pad: the controls come from <see cref="OfflineFrom"/>.</summary>
    public bool Offline { get; private set; }
    public OfflineSource? OfflineFrom { get; private set; }
    /// <summary>Rules of the drawer that match no control: kept as they are when saving.</summary>
    private List<JmRule> _keptRules = new();
    private List<byte> _keptAxesX = new(), _keptAxesY = new();
    private ushort _offlineVid, _offlinePid;

    /// <summary>VID/PID of the profile being edited (the pad, or the drawer when offline).</summary>
    public ushort? TargetVid => Device?.Vid ?? (Offline ? _offlineVid : null);
    public ushort? TargetPid => Device?.Pid ?? (Offline ? _offlinePid : null);

    /// <summary>Structure changed (pad, mapping, profile): rebuild the views.</summary>
    public event Action? Changed;

    public PadSession()
    {
        L.Changed += OnLanguageChanged;
    }

    /// <summary>
    /// Language changed: rewrite the texts held here (validator messages, import notes) and tell the views.
    /// Mapping, profile and live reading are left untouched.
    /// </summary>
    private void OnLanguageChanged()
    {
        ExtraNotes.Clear();
        foreach (var make in _extraNoteMakers) ExtraNotes.Add(make());
        Validate();
        Changed?.Invoke();
    }

    private void Validate() =>
        Issues = Profile == null ? new() : ProfileValidator.Validate(Profile, ViewHeader);

    private void ClearNotes()
    {
        ExtraNotes.Clear();
        _extraNoteMakers.Clear();
    }

    private void AddNote(Func<string> make)
    {
        _extraNoteMakers.Add(make);
        ExtraNotes.Add(make());
    }

    // ------------------------------------------------------------------ devices

    public string? RefreshDevices()
    {
        StopReader();
        string? error = null;
        try { Devices = PadDevices.List(); }
        catch (Exception ex) { Devices = new(); error = ex.Message; }
        if (Offline && EditSlot != null)
        {
            // Editing a drawer without its pad: if the pad is now connected, go on with it (edits kept).
            int i = Devices.FindIndex(d => d.Vid == _offlineVid && d.Pid == _offlinePid);
            if (i >= 0 && Profile != null) OpenWithDevice(i, Profile, EditSlot);
            else Changed?.Invoke();
            return error;
        }
        if (Devices.Count == 0) { Clear(); return error; }
        Select(0);
        return error;
    }

    public string? Select(int index)
    {
        StopReader();
        if (index < 0 || index >= Devices.Count) { Clear(); return null; }
        Device = Devices[index];
        ViewOnly = false; ViewHeader = null; ClearNotes();
        EditSlot = null; Offline = false; OfflineFrom = null; _keptRules = new(); _keptAxesX = new(); _keptAxesY = new();
        try { Pad = Device.Describe(); }
        catch (Exception ex)
        {
            var msg = $"{Device}: {ex.Message}";
            Clear();
            return msg;
        }
        RememberCard(Device);
        Mapping = MappingProposer.Propose(Pad);
        Rebuild();
        StartReader();
        return null;
    }

    private void RememberCard(PadDevice d) =>
        Catalog.Save(new PadCard(d.Vid, d.Pid, d.Name, d.IsXInput ? d.XInputKind : null,
            d.IsXInput || d.RawDescriptor == null ? null : Convert.ToHexString(d.RawDescriptor), DateTime.UtcNow));

    // ------------------------------------------------------------------ drawers

    /// <summary>
    /// Opens a drawer for editing: with its pad when connected (live preview), otherwise from the card of the pad,
    /// a known Xbox layout or the rules themselves.
    /// </summary>
    public void OpenSlot(SlotContent drawer)
    {
        int i = Devices.FindIndex(d => d.Vid == drawer.Info.Vid && d.Pid == drawer.Info.Pid);
        if (i >= 0) { OpenWithDevice(i, drawer.Profile, drawer.Info); return; }

        StopReader();
        Device = null; ViewOnly = false; ViewHeader = null; ClearNotes();
        var (pad, source) = OfflinePad.Describe(drawer.Profile, Catalog.Find(drawer.Info.Vid, drawer.Info.Pid));
        Pad = pad;
        Offline = true; OfflineFrom = source; EditSlot = drawer.Info;
        _offlineVid = drawer.Info.Vid; _offlinePid = drawer.Info.Pid;
        ApplyImported(MappingImporter.FromProfile(pad, drawer.Profile), drawer.Profile);
    }

    private void OpenWithDevice(int index, JmProfile profile, SlotInfo slot)
    {
        var err = Select(index);
        if (err != null || Pad == null) return;
        EditSlot = slot;
        ApplyImported(MappingImporter.FromProfile(Pad, profile), profile);
    }

    /// <summary>Mapping from the drawer; rules matching no control are kept unchanged and listed as notes.</summary>
    private void ApplyImported(MappingImporter.Result res, JmProfile source)
    {
        Mapping = res.Mapping;
        _keptRules = res.UnmatchedRules.ToList();
        _keptAxesX = source.AnalogX.Where(a => res.UnmatchedAxes.Contains(a)).ToList();
        _keptAxesY = source.AnalogY.Where(a => res.UnmatchedAxes.Contains(a)).ToList();
        ClearNotes();
        foreach (var r in _keptRules) AddNote(() => L.F("pad.note.kept", Names.Rule(r)));
        foreach (var a in res.UnmatchedAxes) AddNote(() => L.F("pad.note.axis", a));
        Rebuild();
    }

    /// <summary>Back to the connected pad (leaves the drawer editing).</summary>
    public void CloseSlot()
    {
        EditSlot = null;
        Offline = false; OfflineFrom = null;
        _keptRules = new(); _keptAxesX = new(); _keptAxesY = new();
        if (Devices.Count > 0) Select(Math.Max(0, Device == null ? 0 : Devices.IndexOf(Device)));
        else { Pad = null; Mapping = null; Profile = null; Rebuild(); }
    }

    /// <summary>After a save into a drawer: the editing continues on the saved drawer.</summary>
    public void SlotSaved(SlotInfo info)
    {
        if (EditSlot != null) { EditSlot = info; Changed?.Invoke(); }
    }

    private void Clear()
    {
        if (Offline) return; // editing a drawer without its pad
        Device = null; Pad = null; Mapping = null;
        if (!ViewOnly) Profile = null;
        Rebuild();
    }

    // ------------------------------------------------------------------ mapping / profile

    public void Propose()
    {
        if (Pad == null) return;
        Mapping = MappingProposer.Propose(Pad);
        ViewOnly = false; ViewHeader = null; ClearNotes();
        _keptRules = new(); _keptAxesX = new(); _keptAxesY = new();
        Rebuild();
    }

    public void SetAction(PadControl c, PadAction a)
    {
        if (Mapping == null || ViewOnly) return;
        Mapping.Set(c, a);
        Rebuild();
    }

    private void Rebuild()
    {
        if (!ViewOnly && Mapping != null && (Device != null || Offline))
        {
            var p = ProfileBuilder.Build(Mapping, TargetVid!.Value, TargetPid!.Value, Device?.Name ?? EditSlot?.DisplayLabel);
            foreach (var r in _keptRules) if (p.Rules.Count < FirmwareLimits.MaxRules) p.Rules.Add(r);
            foreach (var a in _keptAxesX) if (!p.AnalogX.Contains(a)) p.AnalogX.Add(a);
            foreach (var a in _keptAxesY) if (!p.AnalogY.Contains(a)) p.AnalogY.Add(a);
            Profile = p;
        }
        lock (_sync)
        {
            _engine = Profile != null && Device != null ? new DynamicEngine(Profile) : null;
            _lastState = null;
        }
        Validate();
        Changed?.Invoke();
    }

    public sealed record ImportOutcome(bool NeedsChoice, string Question, Action ApplyToPad, Action OpenReadOnly, string Summary);

    /// <summary>
    /// Reads a .h / .json. Without a pad it is opened read-only. With a pad: if it matches, it is applied;
    /// otherwise the caller asks the user (apply or read-only).
    /// </summary>
    public ImportOutcome Import(string path)
    {
        var text = File.ReadAllText(path);
        JmProfile imported;
        ImportedHeader? header = null;
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('{'))
            imported = SaveJmJson.Parse(text.Trim());
        else
        {
            header = HeaderImporter.Import(text);
            imported = header.Profile;
        }
        string name = Path.GetFileName(path);

        void ReadOnly()
        {
            ViewOnly = true;
            ViewHeader = header ?? new ImportedHeader { Profile = imported, Generator = HeaderGenerator.Unknown };
            Profile = imported;
            ClearNotes();
            Rebuild();
        }

        if (Pad == null || Device == null)
            return new ImportOutcome(false, "", ReadOnly, ReadOnly, L.F("pad.import.readOnlyNoPad", name));

        var res = MappingImporter.FromProfile(Pad, imported);
        int total = imported.Rules.Count + imported.AnalogX.Count + imported.AnalogY.Count;
        int unmatched = res.UnmatchedRules.Count + res.UnmatchedAxes.Count;
        bool sameDevice = imported.Vid == Device.Vid && imported.Pid == Device.Pid;
        bool poor = total == 0 || unmatched * 2 > total;

        void Apply()
        {
            ViewOnly = false; ViewHeader = null;
            Mapping = res.Mapping;
            ClearNotes();
            foreach (var r in res.UnmatchedRules)
                AddNote(() => L.F("pad.note.rule", Names.Rule(r)));
            foreach (var a in res.UnmatchedAxes)
                AddNote(() => L.F("pad.note.axis", a));
            Rebuild();
        }

        string q = (sameDevice ? "" : L.F("pad.q.otherDevice", imported.Vid, imported.Pid, Device.Vid, Device.Pid)) +
                   (total == 0 ? L.T("pad.q.empty") : L.F("pad.q.matches", total - unmatched, total)) +
                   L.T("pad.q.choice");
        string summary = unmatched == 0 ? L.F("pad.sum.all", name) : L.F("pad.sum.unmatched", name, unmatched);
        return new ImportOutcome(!sameDevice || poor, q, Apply, ReadOnly, summary);
    }

    // ------------------------------------------------------------------ live reading

    public sealed record LiveSnapshot(byte[]? Report, C64State? State, string? Error, bool Live);

    public LiveSnapshot Snapshot()
    {
        lock (_sync)
        {
            var s = new LiveSnapshot(_lastReport, _lastState, _readerError, _readerThread != null);
            _readerError = null;
            return s;
        }
    }

    private void StartReader()
    {
        if (Device == null || Pad == null) return;
        var device = Device;
        var pad = Pad;
        int slot = device.IsXInput ? PadReader.ConnectedXInputSlots().DefaultIfEmpty(0).First() : 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        lock (_sync) { _lastReport = null; _lastState = null; _readerError = null; }
        _readerThread = new Thread(() =>
        {
            PadReader? reader = null;
            try
            {
                reader = new PadReader(device, pad, slot);
                while (!token.IsCancellationRequested)
                {
                    var report = reader.Read();
                    if (report == null) continue;
                    lock (_sync)
                    {
                        _lastReport = report;
                        if (_engine?.Process(report) is { } st) _lastState = st;
                    }
                }
            }
            catch (Exception ex) { lock (_sync) _readerError = ex.Message; }
            finally { reader?.Dispose(); }
        })
        { IsBackground = true, Name = "PadReader" };
        _readerThread.Start();
    }

    private void StopReader()
    {
        _cts?.Cancel();
        _readerThread?.Join(500);
        _readerThread = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => StopReader();
}
