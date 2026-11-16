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
        try { Pad = Device.Describe(); }
        catch (Exception ex)
        {
            var msg = $"{Device}: {ex.Message}";
            Clear();
            return msg;
        }
        Mapping = MappingProposer.Propose(Pad);
        Rebuild();
        StartReader();
        return null;
    }

    private void Clear()
    {
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
        if (!ViewOnly && Mapping != null && Device != null)
            Profile = ProfileBuilder.Build(Mapping, Device.Vid, Device.Pid, Device.Name);
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
