using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using ScanQa.Core.Qa;

namespace ScanQa.Revit.UI
{
    /// <summary>Modeless QA window. All Revit work is posted to <see cref="RevitActionQueue"/>.</summary>
    internal sealed class QaWindow : Form
    {
        private readonly RevitActionQueue _actions;
        private readonly QaDisplayServer _display;

        private readonly ComboBox _cloud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly Button _refresh = new Button { Text = "Refresh", Width = 80 };
        private readonly CheckedListBox _categories = new CheckedListBox { CheckOnClick = true, Height = 150, Width = 300, MultiColumn = false };
        private readonly CheckBox _checkLinks = new CheckBox { Text = "Check elements in linked models", AutoSize = true, Checked = true };
        private readonly ComboBox _discipline = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly CheckBox _selectionOnly = new CheckBox { Text = "Selected elements only", AutoSize = true };
        private readonly CheckBox _insulation = new CheckBox { Text = "Insulation is the pipe/duct surface", AutoSize = true, Checked = true };
        private readonly CheckBox _links = new CheckBox { Text = "Linked models explain scan points", AutoSize = true, Checked = true };
        private readonly NumericUpDown _tolerance = Num(1, 500, 20, 1);
        private readonly NumericUpDown _threshold = Num(5, 2000, 100, 1);
        private readonly NumericUpDown _spacing = Num(1, 100, 5, 1);
        private readonly NumericUpDown _maxPoints = Num(10_000, 20_000_000, 2_000_000, 0, 100_000);
        private readonly NumericUpDown _minPoints = Num(1, 100_000, 30, 0);
        private readonly NumericUpDown _minCoverage = Num(0, 100, 25, 0);
        private readonly NumericUpDown _minCluster = Num(1, 1_000_000, 30, 0);
        private readonly CheckBox _showHeat = new CheckBox { Text = "Show heatmap", AutoSize = true, Checked = true };
        private readonly CheckBox _showMissing = new CheckBox { Text = "Show Suspected Missing", AutoSize = true, Checked = true };
        private readonly FlowLayoutPanel _legend = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly Button _run = new Button { Text = "Run Check", Width = 120, Height = 32 };
        private readonly Button _export = new Button { Text = "Export CSV…", Width = 120, Enabled = false };
        private readonly ComboBox _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly DataGridView _grid = Grid();
        private readonly DataGridView _clusters = Grid();
        private readonly Label _summary = new Label { AutoSize = true };
        private readonly TextBox _notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 70 };

        private QaOutcome _outcome;

        public QaWindow(RevitActionQueue actions, QaDisplayServer display)
        {
            _actions = actions;
            _display = display;
            Text = "Scan-to-BIM QA";
            Width = 1180;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            foreach (var c in CategoryOption.All) _categories.Items.Add(c, c.DefaultOn);
            _filter.Items.AddRange(new object[] { "Problems (Fail + Insufficient)", "All", "Fail", "Insufficient Scan Data", "Pass" });
            _filter.SelectedIndex = 0;
            _discipline.Items.Add("All disciplines");
            _discipline.Items.AddRange(Discipline.All);
            _discipline.SelectedIndex = 0;
            _run.Font = new Font(Font, FontStyle.Bold);

            BuildLayout();
            BuildLegend(20);

            _run.Click += (s, e) => RunCheck();
            _refresh.Click += (s, e) => _actions.Post(RefreshInputs);
            _export.Click += (s, e) => ExportCsv();
            _filter.SelectedIndexChanged += (s, e) => FillElements();
            _discipline.SelectedIndexChanged += (s, e) => FillElements();
            _showHeat.CheckedChanged += (s, e) => ToggleLayers();
            _showMissing.CheckedChanged += (s, e) => ToggleLayers();
            _tolerance.ValueChanged += (s, e) => BuildLegend((double)_tolerance.Value);
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ZoomElement(true); };
            _clusters.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ZoomCluster(); };
            FormClosed += (s, e) => _actions.Post(app =>
            {
                _display.SetResult(null, null, null);
                app.ActiveUIDocument?.RefreshActiveView();
            });
        }

        private static NumericUpDown Num(decimal min, decimal max, decimal value, int decimals, decimal step = 1) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Increment = step, Width = 100, ThousandsSeparator = true };

        private static DataGridView Grid() => new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            BackgroundColor = SystemColors.Window,
        };

        private void BuildLayout()
        {
            var inputs = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8) };
            for (int i = 0; i < 4; i++) inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var cloudRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            cloudRow.Controls.AddRange(new Control[] { _cloud, _refresh });
            var presets = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            foreach (var (label, disc) in new[] { ("ARC", Discipline.Architecture), ("STR", Discipline.Structure), ("MEP", Discipline.Mep), ("All", "*"), ("None", "") })
            {
                var b = new Button { Text = label, Width = 52, Height = 24 };
                b.Click += (s, e) => ApplyPreset(disc);
                presets.Controls.Add(b);
            }
            var col1 = Stack(Caption("Point cloud"), cloudRow, Caption("Categories to check"), presets, _categories,
                _selectionOnly, _checkLinks, _insulation, _links);
            var thresholds = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            AddRow(thresholds, "Tolerance (mm)", _tolerance);
            AddRow(thresholds, "Missing threshold (mm)", _threshold);
            AddRow(thresholds, "Point spacing (mm)", _spacing);
            AddRow(thresholds, "Max points", _maxPoints);
            AddRow(thresholds, "Min points per element", _minPoints);
            AddRow(thresholds, "Min surface coverage (%)", _minCoverage);
            AddRow(thresholds, "Min points per missing cluster", _minCluster);
            var col2 = Stack(Caption("Thresholds"), thresholds);
            var col3 = Stack(Caption("Display in 3D view"), _showHeat, _showMissing, Caption("Legend"), _legend);
            var col4 = Stack(_run, _export, _summary);
            inputs.Controls.Add(col1, 0, 0);
            inputs.Controls.Add(col2, 1, 0);
            inputs.Controls.Add(col3, 2, 0);
            inputs.Controls.Add(col4, 3, 0);

            var elementButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            var select = new Button { Text = "Select", Width = 90 };
            var zoom = new Button { Text = "Zoom to element", Width = 130 };
            select.Click += (s, e) => ZoomElement(false);
            zoom.Click += (s, e) => ZoomElement(true);
            elementButtons.Controls.AddRange(new Control[] { Caption("Show"), _filter, _discipline, select, zoom });

            var clusterButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            var zoomCluster = new Button { Text = "Zoom to location", Width = 130 };
            zoomCluster.Click += (s, e) => ZoomCluster();
            clusterButtons.Controls.Add(zoomCluster);
            clusterButtons.Controls.Add(Caption("Scan points without a model surface within the missing threshold. Confirm each one in the model."));

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tElements = new TabPage("Elements");
            tElements.Controls.Add(_grid);
            tElements.Controls.Add(elementButtons);
            var tClusters = new TabPage("Suspected Missing");
            tClusters.Controls.Add(_clusters);
            tClusters.Controls.Add(clusterButtons);
            tabs.TabPages.Add(tElements);
            tabs.TabPages.Add(tClusters);

            var notes = new GroupBox { Text = "Run log", Dock = DockStyle.Bottom, Height = 100 };
            notes.Controls.Add(_notes);

            Controls.Add(tabs);
            Controls.Add(notes);
            Controls.Add(inputs);

            _grid.Columns.Add("model", "Model");
            _grid.Columns.Add("disc", "Discipline");
            _grid.Columns.Add("id", "Element ID");
            _grid.Columns.Add("cat", "Category");
            _grid.Columns.Add("name", "Type");
            _grid.Columns.Add("pts", "Points");
            _grid.Columns.Add("within", "% within tol.");
            _grid.Columns.Add("p95", "P95 |dev| (mm)");
            _grid.Columns.Add("max", "Max |dev| (mm)");
            _grid.Columns.Add("cov", "Coverage");
            _grid.Columns.Add("status", "Status");
            _grid.Columns.Add("reason", "Reason");

            _clusters.Columns.Add("id", "Cluster");
            _clusters.Columns.Add("pts", "Points");
            _clusters.Columns.Add("ext", "Extent (mm)");
            _clusters.Columns.Add("near", "Near open end of");
            _clusters.Columns.Add("note", "Note");
        }

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 2) };

        private static FlowLayoutPanel Stack(params Control[] controls)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(4, 0, 16, 0) };
            p.Controls.AddRange(controls);
            return p;
        }

        private static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            int r = t.RowCount++;
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }, 0, r);
            t.Controls.Add(c, 1, r);
        }

        private void BuildLegend(double tol)
        {
            _legend.Controls.Clear();
            foreach (var entry in Heatmap.Legend(tol))
            {
                var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
                row.Controls.Add(new Panel { Width = 16, Height = 12, BackColor = Color.FromArgb(entry.Color.R, entry.Color.G, entry.Color.B), Margin = new Padding(3, 4, 3, 0) });
                row.Controls.Add(new Label { Text = entry.Label, AutoSize = true });
                _legend.Controls.Add(row);
            }
        }

        /// <summary>Called on the Revit thread (command / external event).</summary>
        public void RefreshInputs(UIApplication app)
        {
            var previous = (_cloud.SelectedItem as CloudOption)?.Id;
            _cloud.Items.Clear();
            _cloud.Items.Add(new CloudOption { Id = 0, Name = "All point clouds visible in the view" });
            foreach (var c in QaSession.VisibleClouds(app)) _cloud.Items.Add(c);
            int idx = 0;
            for (int i = 0; i < _cloud.Items.Count; i++)
                if (((CloudOption)_cloud.Items[i]).Id == previous) idx = i;
            _cloud.SelectedIndex = idx;
            QaSession.ActiveCheckView(app, out string problem);
            if (problem != null) _summary.Text = problem;
            else if (_outcome == null) _summary.Text = "Ready. Adjust the section box, then Run Check.";
        }

        private QaRequest BuildRequest()
        {
            var req = new QaRequest
            {
                SelectionOnly = _selectionOnly.Checked,
                InsulationAsSurface = _insulation.Checked,
                LinkedModelsAsContext = _links.Checked,
                CheckLinkedElements = _checkLinks.Checked,
                SpacingMm = (double)_spacing.Value,
                MaxPoints = (int)_maxPoints.Value,
                Settings = new QaSettings
                {
                    ToleranceMm = (double)_tolerance.Value,
                    MissingThresholdMm = (double)_threshold.Value,
                    MinPoints = (int)_minPoints.Value,
                    MinCoverage = (double)_minCoverage.Value / 100.0,
                    MinClusterPoints = (int)_minCluster.Value,
                },
            };
            if (_cloud.SelectedItem is CloudOption co && co.Id != 0) req.CloudIds.Add(co.Id);
            foreach (CategoryOption c in _categories.CheckedItems) req.Categories.Add(c.Category);
            return req;
        }

        private void RunCheck()
        {
            QaRequest req;
            try
            {
                req = BuildRequest();
                req.Settings.Validate();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _run.Enabled = false;
            _summary.Text = "Running…";
            _actions.Post(app =>
            {
                try
                {
                    var outcome = QaSession.Run(app, req);
                    var uidoc = app.ActiveUIDocument;
                    _display.ShowHeatmap = _showHeat.Checked;
                    _display.ShowMissing = _showMissing.Checked;
                    _display.SetResult(uidoc.Document, uidoc.ActiveView, outcome.Result);
                    uidoc.RefreshActiveView();
                    ShowOutcome(outcome);
                }
                catch (Exception ex)
                {
                    _summary.Text = ex.Message;
                    _notes.Text = ex.ToString();
                }
                finally
                {
                    _run.Enabled = true;
                }
            });
        }

        private void ShowOutcome(QaOutcome outcome)
        {
            _outcome = outcome;
            var r = outcome.Result;
            int pass = r.Elements.Count(e => e.Status == QaStatus.Pass);
            int fail = r.Elements.Count(e => e.Status == QaStatus.Fail);
            int ins = r.Elements.Count(e => e.Status == QaStatus.InsufficientScanData);
            _summary.Text = $"View: {outcome.ViewName}\n{r.Elements.Count} elements: {pass} Pass, {fail} Fail, {ins} Insufficient\n{r.Clusters.Count} Suspected Missing clusters";
            _notes.Text = string.Join(Environment.NewLine, outcome.Notes);
            _export.Enabled = true;
            FillElements();
            FillClusters();
        }

        private StatusFilter CurrentFilter()
        {
            switch (_filter.SelectedIndex)
            {
                case 1: return StatusFilter.All;
                case 2: return StatusFilter.Fail;
                case 3: return StatusFilter.Insufficient;
                case 4: return StatusFilter.Pass;
                default: return StatusFilter.Problems;
            }
        }

        private static string Mm(double v) => double.IsNaN(v) ? "—" : v.ToString("0.0", CultureInfo.CurrentCulture);

        private void FillElements()
        {
            _grid.Rows.Clear();
            if (_outcome == null) return;
            string disc = _discipline.SelectedIndex <= 0 ? null : (string)_discipline.SelectedItem;
            var rows = ResultFilter.ByDiscipline(ResultFilter.Apply(_outcome.Result.Elements, CurrentFilter()), disc);
            foreach (var e in ResultFilter.DefaultOrder(rows))
            {
                int i = _grid.Rows.Add(e.Source, e.Discipline, e.ElementId, e.Category, e.Name, e.PointCount,
                    double.IsNaN(e.PercentWithinTolerance) ? "—" : e.PercentWithinTolerance.ToString("0.0", CultureInfo.CurrentCulture),
                    Mm(e.P95Mm), Mm(e.MaxMm), e.Coverage.ToString("P0", CultureInfo.CurrentCulture),
                    ResultFilter.StatusText(e.Status), e.Reason);
                var row = _grid.Rows[i];
                row.Tag = e;
                row.Cells["status"].Style.BackColor = e.Status == QaStatus.Pass ? Color.FromArgb(214, 245, 214)
                    : e.Status == QaStatus.Fail ? Color.FromArgb(255, 214, 214) : Color.FromArgb(255, 240, 200);
            }
        }

        private void FillClusters()
        {
            _clusters.Rows.Clear();
            if (_outcome == null) return;
            foreach (var c in _outcome.Result.Clusters)
            {
                int i = _clusters.Rows.Add(c.Id, c.PointCount, c.ExtentMm.ToString("0", CultureInfo.CurrentCulture),
                    c.NearOpenEndOfElementId?.ToString(CultureInfo.CurrentCulture) ?? "", c.Note);
                _clusters.Rows[i].Tag = c;
            }
        }

        private void ZoomElement(bool zoom)
        {
            if (_outcome == null || !(_grid.CurrentRow?.Tag is ElementResult e)) return;
            long viewId = _outcome.ViewId;
            var frame = ZoomTargets.ForElement(e);
            _actions.Post(app =>
            {
                if (zoom) QaSession.ZoomTo(app, viewId, frame, e.ElementId, e.LinkInstanceId);
                else QaSession.Select(app.ActiveUIDocument, e.ElementId, e.LinkInstanceId);
            });
        }

        private void ZoomCluster()
        {
            if (_outcome == null || !(_clusters.CurrentRow?.Tag is MissingCluster c)) return;
            long viewId = _outcome.ViewId;
            var frame = ZoomTargets.ForCluster(c);
            _actions.Post(app => QaSession.ZoomTo(app, viewId, frame, c.NearOpenEndOfElementId));
        }

        private void ApplyPreset(string discipline)
        {
            for (int i = 0; i < _categories.Items.Count; i++)
            {
                var c = (CategoryOption)_categories.Items[i];
                bool on = discipline == "*" || (discipline != "" && c.Discipline == discipline && c.DefaultOn);
                if (discipline == "*") on = c.DefaultOn;
                _categories.SetItemChecked(i, on);
            }
        }

        private void ToggleLayers()
        {
            _display.ShowHeatmap = _showHeat.Checked;
            _display.ShowMissing = _showMissing.Checked;
            _actions.Post(app => app.ActiveUIDocument?.RefreshActiveView());
        }

        private void ExportCsv()
        {
            if (_outcome == null) return;
            using (var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "ScanQA_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, CsvExport.Elements(_outcome.Result.Elements));
                var clusterPath = Path.Combine(Path.GetDirectoryName(dlg.FileName) ?? "", Path.GetFileNameWithoutExtension(dlg.FileName) + "_missing.csv");
                File.WriteAllText(clusterPath, CsvExport.Clusters(_outcome.Result.Clusters));
                _summary.Text = "Exported " + Path.GetFileName(dlg.FileName) + " and " + Path.GetFileName(clusterPath);
            }
        }
    }
}
