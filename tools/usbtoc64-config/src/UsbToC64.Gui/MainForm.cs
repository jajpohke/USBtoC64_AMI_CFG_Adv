using System.Text;
using UsbToC64.Core.Device;
using UsbToC64.Core.Emulation;
using UsbToC64.Core.Formats;
using UsbToC64.Core.Hid;
using UsbToC64.Core.Mapping;
using UsbToC64.Core.Model;
using UsbToC64.Core.Validation;
using UsbToC64.Device;
using UsbToC64.Input;
using PadControl = UsbToC64.Core.Hid.Control;

namespace UsbToC64.Gui;

internal sealed class MainForm : Form
{
    // --- UI
    private readonly ToolStripComboBox _cbPads = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 360 };
    private readonly DataGridView _grid = new();
    private readonly C64Panel _c64 = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly TextBox _txtIssues = ReadOnlyBox();
    private readonly TextBox _txtRules = ReadOnlyBox();
    private readonly TextBox _txtJson = ReadOnlyBox();
    private readonly TextBox _txtAdapter = ReadOnlyBox();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _tabIssues = new("Avvisi");
    private readonly TabPage _tabAdapter = new("Adattatore");
    private readonly ToolStripComboBox _cbPorts = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 300 };
    private readonly List<ToolStripItem> _busyDisabled = new();
    private readonly Panel _fwBanner = new() { Dock = DockStyle.Top, Height = 40, Visible = false, BackColor = Color.FromArgb(255, 243, 205), Padding = new Padding(10, 6, 10, 6) };
    private readonly Label _fwLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button _fwButton = new() { Dock = DockStyle.Right, Width = 190, Text = "Aggiorna firmware…" };
    private FirmwareManifest? _manifest;
    private string? _bannerPort;
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 40 };

    private const int ColName = 0, ColKind = 1, ColByte = 2, ColBits = 3, ColAction = 4, ColState = 5, ColNote = 6;

    // --- state
    private List<PadDevice> _devices = new();
    private PadDevice? _device;
    private PadDescription? _pad;
    private PadMapping? _mapping;
    private JmProfile? _profile;
    private ImportedHeader? _viewHeader;            // profile opened read-only (no pad, or not matching the pad)
    private bool _viewOnly;                          // the editor is suspended, _profile is the imported one
    private List<AdapterPortInfo> _ports = new();
    private bool _busy;
    private readonly List<string> _extraNotes = new(); // e.g. rules not matched on import

    // --- live reading (reader thread -> UI timer)
    private readonly object _sync = new();
    private Thread? _readerThread;
    private CancellationTokenSource? _cts;
    private DynamicEngine? _engine;
    private byte[]? _lastReport;
    private C64State? _lastState;
    private string? _readerError;
    private byte[]? _shownReport;
    private bool _populating;

    private static readonly Dictionary<string, PadAction> LabelToAction =
        Enum.GetValues<PadAction>().ToDictionary(PadActions.Label, a => a);

    public MainForm()
    {
        Text = "USBtoC64 Config";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildLayout();
        _uiTimer.Tick += (_, _) => RefreshLive();
        Load += async (_, _) =>
        {
            RefreshDevices();
            RefreshPorts();
            _uiTimer.Start();
            try { _manifest = FirmwareManifest.FindNextTo(AppContext.BaseDirectory); }
            catch (Exception ex) { Log($"firmware.json non leggibile: {ex.Message}"); }
            await CheckFirmware(silent: true);
        };
        FormClosing += (_, _) => { _uiTimer.Stop(); StopReader(); };
    }

    // ================================================================ layout

    private void BuildLayout()
    {
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        var btnRefresh = new ToolStripButton("Aggiorna elenco");
        var btnPropose = new ToolStripButton("Proposta automatica");
        var btnImport = new ToolStripButton("Importa profilo…");
        var btnSave = new ToolStripButton("Salva JSON…");
        var btnCopy = new ToolStripButton("Copia JSON");
        tools.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Pad:"), _cbPads, btnRefresh, new ToolStripSeparator(),
            btnPropose, btnImport, new ToolStripSeparator(), btnSave, btnCopy,
        });
        btnRefresh.Click += (_, _) => { RefreshDevices(); RefreshPorts(); };
        btnPropose.Click += (_, _) => ProposeAgain();
        btnImport.Click += (_, _) => ImportProfile();
        btnSave.Click += (_, _) => SaveJson();
        btnCopy.Click += (_, _) => CopyJson();
        _cbPads.SelectedIndexChanged += (_, _) => OpenSelectedDevice();

        var tools2 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        var btnWrite = new ToolStripButton("Scrivi sull'adattatore e verifica") { Font = new Font(Font, FontStyle.Bold) };
        var btnRead = new ToolStripButton("Leggi dall'adattatore");
        var btnPing = new ToolStripButton("Test connessione");
        var btnFirmware = new ToolStripButton("Firmware…");
        tools2.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Adattatore:"), _cbPorts, btnPing, new ToolStripSeparator(), btnWrite, btnRead,
            new ToolStripSeparator(), btnFirmware,
        });
        btnFirmware.Click += async (_, _) => await CheckFirmware(silent: false);
        _fwBanner.Controls.Add(_fwLabel);
        _fwBanner.Controls.Add(_fwButton);
        _fwButton.Click += async (_, _) => { if (_bannerPort != null) await UpdateFirmware(_bannerPort); };
        btnPing.Click += async (_, _) => await PingAdapter();
        btnWrite.Click += async (_, _) => await WriteToAdapter();
        btnRead.Click += async (_, _) => await ReadFromAdapter();
        _busyDisabled.AddRange(new ToolStripItem[] { btnWrite, btnRead, btnPing, btnFirmware, btnRefresh, btnImport, btnPropose, _cbPads, _cbPorts });

        SetupGrid();

        var tabs = _tabs;
        _tabIssues.Controls.Add(_txtIssues);
        _tabAdapter.Controls.Add(_txtAdapter);
        var tabRules = new TabPage("Regole per il firmware");
        tabRules.Controls.Add(_txtRules);
        var tabJson = new TabPage("JSON save_jm");
        tabJson.Controls.Add(_txtJson);
        tabs.TabPages.AddRange(new[] { _tabIssues, tabRules, tabJson, _tabAdapter });

        var top = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
        top.Panel1.Controls.Add(_grid);
        top.Panel2.Controls.Add(_c64);

        var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
        main.Panel1.Controls.Add(top);
        main.Panel2.Controls.Add(tabs);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        Controls.Add(main);
        Controls.Add(_fwBanner);
        Controls.Add(tools2);
        Controls.Add(tools);
        Controls.Add(statusStrip);

        Shown += (_, _) =>
        {
            // Splitter distances need the final size.
            try
            {
                top.SplitterDistance = Math.Max(300, top.Width - 290);
                main.SplitterDistance = Math.Max(200, main.Height - 200);
            }
            catch (InvalidOperationException) { /* window too small: keep the defaults */ }
        };
    }

    private void SetupGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        _grid.Columns.Add(TextCol("Controllo", 170));
        _grid.Columns.Add(TextCol("Tipo", 80));
        _grid.Columns.Add(TextCol("Byte", 45));
        _grid.Columns.Add(TextCol("Bit / valori", 85));
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Azione sul C64", Width = 230, FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
        });
        _grid.Columns.Add(TextCol("Stato", 70));
        var note = TextCol("Note", 300);
        note.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns.Add(note);

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell?.ColumnIndex == ColAction)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnActionChanged;
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    private static DataGridViewTextBoxColumn TextCol(string header, int width) =>
        new() { HeaderText = header, Width = width, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };

    private static TextBox ReadOnlyBox() => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Font = new Font("Consolas", 9.5f), BackColor = SystemColors.Window,
    };

    // ================================================================ devices

    private void RefreshDevices()
    {
        StopReader();
        try
        {
            _devices = PadDevices.List();
        }
        catch (Exception ex)
        {
            _devices = new();
            SetStatus($"Errore nell'elenco dei pad: {ex.Message}");
        }

        _cbPads.Items.Clear();
        if (_devices.Count == 0)
        {
            _cbPads.Items.Add("(nessun gamepad trovato: collegalo e premi Aggiorna)");
            _cbPads.SelectedIndex = 0;
            ClearDevice();
            SetStatus("Nessun gamepad trovato. Puoi comunque importare un profilo .h/.json per controllarlo.");
            return;
        }
        foreach (var d in _devices) _cbPads.Items.Add(d.ToString());
        _cbPads.SelectedIndex = 0; // triggers OpenSelectedDevice
    }

    private void OpenSelectedDevice()
    {
        StopReader();
        int i = _cbPads.SelectedIndex;
        if (i < 0 || i >= _devices.Count) { ClearDevice(); return; }

        _device = _devices[i];
        _viewHeader = null;
        _viewOnly = false;
        _grid.Enabled = true;
        _extraNotes.Clear();
        try
        {
            _pad = _device.Describe();
        }
        catch (Exception ex)
        {
            ClearDevice();
            SetStatus($"{_device}: {ex.Message}");
            return;
        }
        _mapping = MappingProposer.Propose(_pad);
        PopulateGrid();
        RebuildProfile();
        StartReader();

        string layout = _pad.IsXInput ? "layout XInput nativo (quello che riceve l'adattatore)"
                      : _pad.UsesReportIds ? "il report inizia con il report ID (byte 0)"
                      : "senza report ID";
        SetStatus($"{_device} — {_pad.Controls.Count} controlli, {layout}. Premi i tasti: la riga corrispondente si evidenzia.");
    }

    private void ClearDevice()
    {
        _device = null; _pad = null; _mapping = null;
        _grid.Rows.Clear();
        if (_viewHeader == null) { _profile = null; ShowProfile(); }
        _c64.Live = false;
    }

    // ================================================================ grid

    private void PopulateGrid()
    {
        _populating = true;
        try { FillRows(); }
        finally { _populating = false; }
    }

    private void FillRows()
    {
        _grid.Rows.Clear();
        if (_pad == null || _mapping == null) return;

        foreach (var c in _pad.Controls)
        {
            int r = _grid.Rows.Add(c.Name, KindLabel(c.Kind), c.ByteIndex, BitsLabel(c), null, "", c.Note ?? "");
            var row = _grid.Rows[r];
            row.Tag = c;
            if (c.Supported)
            {
                var cell = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                foreach (var a in PadActions.AllowedFor(c.Kind)) cell.Items.Add(PadActions.Label(a));
                row.Cells[ColAction] = cell;
                cell.Value = PadActions.Label(_mapping.Get(c));
            }
            else
            {
                row.Cells[ColAction] = new DataGridViewTextBoxCell { Value = "non supportato dal firmware" };
                row.Cells[ColAction].ReadOnly = true;
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }
        }
    }

    private void OnActionChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_populating || e.RowIndex < 0 || e.ColumnIndex != ColAction || _mapping == null) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not PadControl c || !c.Supported) return;
        if (row.Cells[ColAction].Value is string label && LabelToAction.TryGetValue(label, out var action))
        {
            _mapping.Set(c, action);
            RebuildProfile();
        }
    }

    private static string KindLabel(ControlKind k) => k switch
    {
        ControlKind.Button => "pulsante",
        ControlKind.DpadButton => "D-pad",
        ControlKind.Hat => "hat",
        ControlKind.Axis => "asse",
        _ => k.ToString(),
    };

    private static string BitsLabel(PadControl c) => c.Kind switch
    {
        ControlKind.Hat => $"{c.HatBase}–{c.HatBase + c.HatPositions - 1}",
        ControlKind.Axis => "0–255",
        _ => $"0x{c.Mask:X2}",
    };

    // ================================================================ profile

    private void ProposeAgain()
    {
        if (_pad == null) { SetStatus("Collega e seleziona un pad per la proposta automatica."); return; }
        _mapping = MappingProposer.Propose(_pad);
        _extraNotes.Clear();
        _viewOnly = false;
        _viewHeader = null;
        _grid.Enabled = true;
        PopulateGrid();
        RebuildProfile();
    }

    private void RebuildProfile()
    {
        if (!_viewOnly && _mapping != null && _device != null)
            _profile = ProfileBuilder.Build(_mapping, _device.Vid, _device.Pid, _device.Name);
        lock (_sync)
        {
            // In view-only mode the preview shows how the imported profile behaves on this pad.
            _engine = _profile != null && _device != null ? new DynamicEngine(_profile) : null;
            _lastState = null;
        }
        ShowProfile();
    }

    private void ShowProfile()
    {
        if (_profile == null)
        {
            _txtIssues.Text = "";
            _txtRules.Text = "";
            _txtJson.Text = "";
            _tabIssues.Text = "Avvisi";
            return;
        }

        var issues = ProfileValidator.Validate(_profile, _viewHeader);
        var sb = new StringBuilder();
        foreach (var i in issues.OrderByDescending(i => i.Severity))
        {
            string icon = i.Severity switch { Severity.Error => "✖ ERRORE", Severity.Warning => "⚠ AVVISO", _ => "ℹ INFO" };
            sb.AppendLine($"{icon}  {i.Message}");
        }
        foreach (var n in _extraNotes) sb.AppendLine($"⚠ AVVISO  {n}");
        if (_pad != null) foreach (var n in _pad.Notes) sb.AppendLine($"ℹ NOTA  {n}");
        if (_viewHeader != null) foreach (var n in _viewHeader.Notes) sb.AppendLine($"ℹ NOTA  {n}");
        if (sb.Length == 0) sb.AppendLine("✔ Nessun problema rilevato.");
        _txtIssues.Text = sb.ToString();

        int errors = issues.Count(i => i.Severity == Severity.Error);
        int warnings = issues.Count(i => i.Severity == Severity.Warning) + _extraNotes.Count;
        _tabIssues.Text = errors + warnings == 0 ? "Avvisi ✔" : $"Avvisi ({errors} errori, {warnings} avvisi)";

        var rules = new StringBuilder();
        rules.AppendLine($"VID {_profile.Vid:X4}  PID {_profile.Pid:X4}  {_profile.DeviceName}");
        rules.AppendLine($"Regole: {_profile.Rules.Count}/{FirmwareLimits.MaxRules}");
        for (int i = 0; i < _profile.Rules.Count; i++) rules.AppendLine($"  {i + 1,2}. {Names.Rule(_profile.Rules[i])}");
        if (_profile.UseAnalog)
            rules.AppendLine($"Assi analogici: X [{string.Join(", ", _profile.AnalogX)}]   Y [{string.Join(", ", _profile.AnalogY)}]");
        _txtRules.Text = rules.ToString();

        _txtJson.Text = SaveJmJson.Serialize(_profile);
    }

    private void ImportProfile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Importa un profilo",
            Filter = "Profili (*.h;*.json)|*.h;*.json|Tutti i file (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        JmProfile imported;
        ImportedHeader? header = null;
        try
        {
            var text = File.ReadAllText(dlg.FileName);
            if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('{'))
                imported = SaveJmJson.Parse(text.Trim());
            else
            {
                header = HeaderImporter.Import(text);
                imported = header.Profile;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Importazione non riuscita", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_pad == null || _device == null)
        {
            // No pad: show the profile and its problems, read only.
            _viewHeader = header ?? new ImportedHeader { Profile = imported, Generator = HeaderGenerator.Unknown };
            _profile = imported;
            _extraNotes.Clear();
            ShowProfile();
            SetStatus($"Profilo aperto in sola lettura: {Path.GetFileName(dlg.FileName)}. Collega il pad per modificarlo.");
            return;
        }

        var res = MappingImporter.FromProfile(_pad, imported);
        int total = imported.Rules.Count + imported.AnalogX.Count + imported.AnalogY.Count;
        int unmatched = res.UnmatchedRules.Count + res.UnmatchedAxes.Count;
        bool sameDevice = imported.Vid == _device.Vid && imported.Pid == _device.Pid;
        bool poorMatch = total == 0 || unmatched * 2 > total;

        if (!sameDevice || poorMatch)
        {
            var msg = new StringBuilder();
            if (!sameDevice)
                msg.AppendLine($"Il profilo è per VID {imported.Vid:X4} PID {imported.Pid:X4}, il pad collegato è {_device.Vid:X4}:{_device.Pid:X4}.");
            msg.AppendLine(total == 0
                ? "Il profilo è vuoto."
                : $"Solo {total - unmatched} elementi su {total} corrispondono ai controlli di questo pad.");
            msg.AppendLine();
            msg.AppendLine("Sì = apri in sola lettura (vedi regole e problemi del profilo così com'è)");
            msg.AppendLine("No = applica ai controlli di questo pad (gli elementi senza corrispondenza vengono scartati)");
            var answer = MessageBox.Show(this, msg.ToString(), "Il profilo non corrisponde a questo pad",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                poorMatch ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes)
            {
                _viewOnly = true;
                _viewHeader = header ?? new ImportedHeader { Profile = imported, Generator = HeaderGenerator.Unknown };
                _profile = imported;
                _extraNotes.Clear();
                _grid.Enabled = false;
                RebuildProfile();
                SetStatus($"{Path.GetFileName(dlg.FileName)} aperto in sola lettura: l'anteprima mostra come si comporterebbe su questo pad. " +
                          "\"Proposta automatica\" torna all'editor.");
                return;
            }
        }

        _viewOnly = false;
        _viewHeader = null;
        _grid.Enabled = true;
        _mapping = res.Mapping;
        _extraNotes.Clear();
        foreach (var r in res.UnmatchedRules)
            _extraNotes.Add($"Regola importata senza un controllo corrispondente, scartata: {Names.Rule(r)}");
        foreach (var a in res.UnmatchedAxes)
            _extraNotes.Add($"Asse importato (byte {a}) senza un asse corrispondente, scartato.");
        PopulateGrid();
        RebuildProfile();

        SetStatus($"Importato {Path.GetFileName(dlg.FileName)}: " +
                  (res.UnmatchedRules.Count + res.UnmatchedAxes.Count == 0
                      ? "tutte le regole sono state associate ai controlli."
                      : $"{res.UnmatchedRules.Count + res.UnmatchedAxes.Count} elementi non associati (vedi Avvisi)."));
    }

    private bool ConfirmIfErrors(string action)
    {
        if (_profile == null) { SetStatus("Nessun profilo da esportare."); return false; }
        var errors = ProfileValidator.Validate(_profile, _viewHeader).Where(i => i.Severity == Severity.Error).ToList();
        if (errors.Count == 0) return true;
        var msg = $"Il profilo ha {errors.Count} errori:\n\n" + string.Join("\n", errors.Select(e => "• " + e.Message)) +
                  $"\n\n{action} comunque?";
        return MessageBox.Show(this, msg, "Profilo con errori", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void SaveJson()
    {
        if (!ConfirmIfErrors("Salvare")) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Salva il comando save_jm",
            Filter = "JSON (*.json)|*.json",
            FileName = $"profilo_{_profile!.Vid:X4}_{_profile.Pid:X4}.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, SaveJmJson.Serialize(_profile) + "\n");
        SetStatus($"Salvato: {dlg.FileName}");
    }

    private void CopyJson()
    {
        if (!ConfirmIfErrors("Copiare")) return;
        Clipboard.SetText(SaveJmJson.Serialize(_profile!));
        SetStatus("JSON copiato negli appunti: si può incollare in un terminale seriale (115200, fine riga LF).");
    }

    // ================================================================ adapter (serial)

    private const string AutoPort = "Automatica (ESP32)";

    private void RefreshPorts()
    {
        string? previous = _cbPorts.SelectedIndex > 0 ? _ports.ElementAtOrDefault(_cbPorts.SelectedIndex - 1)?.PortName : null;
        try { _ports = AdapterPorts.List(); }
        catch (Exception) { _ports = new(); }
        _cbPorts.Items.Clear();
        _cbPorts.Items.Add(AutoPort);
        foreach (var p in _ports) _cbPorts.Items.Add(p.ToString());
        int keep = previous == null ? -1 : _ports.FindIndex(p => p.PortName == previous);
        _cbPorts.SelectedIndex = keep >= 0 ? keep + 1 : 0;
    }

    private string? RequestedPort() =>
        _cbPorts.SelectedIndex > 0 ? _ports.ElementAtOrDefault(_cbPorts.SelectedIndex - 1)?.PortName : null;

    private void Log(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(line)); return; }
        _txtAdapter.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var item in _busyDisabled) item.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private async Task WriteToAdapter()
    {
        if (_busy) return;
        if (!ConfirmIfErrors("Scrivere sull'adattatore")) return;
        var profile = _profile!;

        AdapterPortInfo port;
        try { port = AdapterPorts.Find(RequestedPort()); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Adattatore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_device != null && (profile.Vid != _device.Vid || profile.Pid != _device.Pid))
        {
            if (MessageBox.Show(this,
                    $"Il profilo è per VID {profile.Vid:X4} PID {profile.Pid:X4}, diverso dal pad collegato al PC. Scriverlo comunque?",
                    "Adattatore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        if (MessageBox.Show(this,
                $"Scrivere il profilo ({profile.Rules.Count} regole) sull'adattatore su {port.PortName}?\n\n" +
                "Sostituisce il profilo pad salvato. L'adattatore si riavvia e il profilo viene riletto per verifica.",
                "Scrivi sull'adattatore", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        _tabs.SelectedTab = _tabAdapter;
        Log($"--- Scrittura su {port}");
        SetBusy(true);
        SetStatus($"Scrittura in corso su {port.PortName}…");
        WriteResult result;
        try
        {
            var writer = new AdapterWriter(new SerialAdapterPort(port.PortName), Log);
            result = await Task.Run(() => writer.WriteAndVerify(profile));
        }
        catch (Exception ex)
        {
            result = new WriteResult(false, false, null, new(), ex.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshPorts();
        }

        Log(result.Message);
        foreach (var d in result.Differences) Log("  ≠ " + d);
        SetStatus(result.Message);
        var icon = result.Verified ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
        var text = result.Message + (result.Differences.Count > 0 ? "\n\n" + string.Join("\n", result.Differences.Select(d => "• " + d)) : "");
        MessageBox.Show(this, text, result.Verified ? "Profilo scritto e verificato" : "Scrittura sull'adattatore", MessageBoxButtons.OK, icon);
    }

    private async Task PingAdapter()
    {
        if (_busy) return;
        AdapterPortInfo port;
        try { port = AdapterPorts.Find(RequestedPort()); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Test connessione", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _tabs.SelectedTab = _tabAdapter;
        Log($"--- Test connessione su {port}");
        SetBusy(true);
        string message;
        bool ok = false;
        try
        {
            var info = await Task.Run(() =>
            {
                using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), Log);
                return conn.Info;
            });
            ok = true;
            message = $"L'adattatore su {port.PortName} risponde: {info}.";
        }
        catch (Exception ex) { message = ex.Message; }
        finally { SetBusy(false); }
        Log(message);
        SetStatus(message);
        MessageBox.Show(this, message, "Test connessione", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    // ================================================================ firmware

    private async Task CheckFirmware(bool silent)
    {
        if (_busy) return;
        AdapterPortInfo port;
        try { port = AdapterPorts.Find(RequestedPort()); }
        catch (InvalidOperationException ex)
        {
            if (!silent) MessageBox.Show(this, ex.Message, "Firmware", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Log($"--- Controllo firmware su {port}");
        SetBusy(true);
        AdapterFirmware? fw = null;
        string? error = null;
        try
        {
            fw = await Task.Run(() =>
            {
                using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), Log);
                return conn.Session.GetFirmware();
            });
        }
        catch (Exception ex) { error = ex.Message; }
        finally { SetBusy(false); }

        if (error != null)
        {
            Log(error);
            if (!silent) MessageBox.Show(this, error, "Firmware", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Log($"Sull'adattatore: {fw}");

        if (_manifest == null)
        {
            SetStatus($"Adattatore: {fw}. Questo programma non include un firmware da installare.");
            if (!silent) MessageBox.Show(this, $"Sull'adattatore c'è il {fw}.\n\nQuesto programma non include un firmware (manca firmware.json).",
                "Firmware", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var status = FirmwareCheck.Compare(fw!, _manifest);
        string available = $"{_manifest.Version}" + (string.IsNullOrEmpty(_manifest.Build) ? "" : $" (build {_manifest.Build})");
        if (status == FirmwareStatus.UpdateAvailable)
        {
            _bannerPort = port.PortName;
            _fwLabel.Text = $"⚠  Sull'adattatore ({port.PortName}) c'è il {fw}. Con il programma è incluso il firmware {available}.";
            _fwBanner.Visible = true;
            if (!silent) await UpdateFirmware(port.PortName);
        }
        else
        {
            _fwBanner.Visible = false;
            string msg = status == FirmwareStatus.UpToDate
                ? $"Il firmware dell'adattatore è aggiornato ({fw})."
                : $"Sull'adattatore c'è un firmware più recente ({fw}) di quello incluso ({available}).";
            SetStatus(msg);
            if (!silent) MessageBox.Show(this, msg, "Firmware", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task UpdateFirmware(string portName)
    {
        if (_busy || _manifest == null) return;
        var answer = MessageBox.Show(this,
            $"Installare il firmware {_manifest.Version} sull'adattatore su {portName}?\n\n" +
            "⚠ L'aggiornamento cancella TUTTE le impostazioni salvate sull'adattatore e lo riporta ai valori predefiniti, " +
            "come \"erase all\" dalla pagina web: profilo pad, impostazioni mouse e LED, Developer Mode, inversione dello switch.\n\n" +
            "Durante l'aggiornamento non scollegare l'adattatore. Deve essere collegato solo al PC, non al C64/Amiga.",
            "Aggiorna firmware", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) return;

        _tabs.SelectedTab = _tabAdapter;
        Log($"--- Aggiornamento firmware su {portName}");
        SetBusy(true);
        SetStatus("Aggiornamento del firmware in corso: non scollegare l'adattatore…");
        FlashResult result;
        try
        {
            var manifest = _manifest;
            result = await Task.Run(() => new FirmwareUpdater(Log).Update(portName, manifest));
        }
        catch (Exception ex) { result = new FlashResult(false, ex.Message); }
        finally
        {
            SetBusy(false);
            RefreshPorts();
        }

        Log(result.Message);
        SetStatus(result.Message);
        if (result.Success) _fwBanner.Visible = false;
        MessageBox.Show(this, result.Message, result.Success ? "Firmware aggiornato" : "Aggiornamento firmware",
            MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }

    private async Task ReadFromAdapter()
    {
        if (_busy) return;
        AdapterPortInfo port;
        try { port = AdapterPorts.Find(RequestedPort()); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Adattatore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _tabs.SelectedTab = _tabAdapter;
        Log($"--- Lettura da {port}");
        SetBusy(true);
        StoredProfile? stored = null;
        string? error = null;
        try
        {
            stored = await Task.Run(() =>
            {
                using var conn = AdapterConnection.Connect(new SerialAdapterPort(port.PortName), Log);
                return conn.Session.ReadStored();
            });
        }
        catch (Exception ex) { error = ex.Message; }
        finally { SetBusy(false); }

        if (error != null) { Log("Errore: " + error); SetStatus("Lettura non riuscita: " + error); return; }
        var p = stored!.Profile;
        string via = stored.Method == ReadMethod.GetJm ? "get_jm" : "dumppad";
        if (p == null)
        {
            Log($"Nessun profilo HTML (TIER 0) salvato sull'adattatore ({via}).");
            SetStatus("Sull'adattatore non c'è un profilo pad salvato con il configuratore.");
            return;
        }
        Log($"Profilo sull'adattatore ({via}): VID {p.Vid:X4} PID {p.Pid:X4}, {p.Rules.Count} regole");
        for (int i = 0; i < p.Rules.Count; i++) Log($"  {i + 1,2}. {Names.Rule(p.Rules[i])}");
        if (p.UseAnalog) Log($"  Assi X [{string.Join(", ", p.AnalogX)}]  Y [{string.Join(", ", p.AnalogY)}]");

        if (_profile != null)
        {
            var diff = ProfileComparer.Compare(_profile, p);
            if (diff.Count == 0) Log("✔ Identico al profilo mostrato nel programma.");
            else { Log("Differenze rispetto al profilo mostrato nel programma:"); foreach (var d in diff) Log("  ≠ " + d); }
            SetStatus(diff.Count == 0 ? "L'adattatore contiene lo stesso profilo mostrato nel programma."
                                      : $"L'adattatore contiene un profilo diverso ({diff.Count} differenze, vedi scheda Adattatore).");
        }
        foreach (var i in ProfileValidator.Validate(p)) Log("  " + i);
    }

    // ================================================================ live reading

    private void StartReader()
    {
        if (_device == null || _pad == null) return;
        var device = _device;
        var pad = _pad;
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
            catch (Exception ex)
            {
                lock (_sync) _readerError = ex.Message;
            }
            finally
            {
                reader?.Dispose();
            }
        })
        { IsBackground = true, Name = "PadReader" };
        _readerThread.Start();
        _c64.Live = true;
    }

    private void StopReader()
    {
        _cts?.Cancel();
        _readerThread?.Join(500);
        _readerThread = null;
        _cts?.Dispose();
        _cts = null;
        _shownReport = null;
        _c64.Live = false;
    }

    private void RefreshLive()
    {
        byte[]? report;
        C64State? state;
        string? error;
        lock (_sync)
        {
            report = _lastReport;
            state = _lastState;
            error = _readerError;
            _readerError = null;
        }

        if (error != null)
        {
            _c64.Live = false;
            SetStatus($"Lettura del pad interrotta: {error}. Ricollega il pad e premi Aggiorna elenco.");
        }

        _c64.State = state;
        if (report == null || ReferenceEquals(report, _shownReport) || _pad == null) return;
        _shownReport = report;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not PadControl c) continue;
            bool active = c.Supported && c.IsActive(report);
            var back = active ? Color.FromArgb(200, 240, 205) : SystemColors.Window;
            if (row.DefaultCellStyle.BackColor != back) row.DefaultCellStyle.BackColor = back;

            string value = c.Kind switch
            {
                ControlKind.Button or ControlKind.DpadButton => c.ReadValue(report) != 0 ? "premuto" : "",
                ControlKind.Hat => c.IsActive(report) ? HatLabel(c.ReadValue(report) - c.HatBase, c.HatPositions) : "",
                _ => c.ReadValue(report).ToString(),
            };
            if (!Equals(row.Cells[ColState].Value, value)) row.Cells[ColState].Value = value;
        }
    }

    private static string HatLabel(int pos, int positions)
    {
        string[] eight = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        string[] four = { "N", "E", "S", "O" };
        var names = positions == 4 ? four : eight;
        return pos >= 0 && pos < names.Length ? names[pos] : "";
    }

    private void SetStatus(string text) => _status.Text = text;
}
